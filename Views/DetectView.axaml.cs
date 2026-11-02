using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using TintaStudio.Services;
using TintaStudio.ViewModels;

namespace TintaStudio.Views;

/// <summary>
/// Halaman Deteksi &amp; OCR. Overlay kotak blok digambar di atas
/// halaman mengikuti transformasi PageViewport (v0.7.14: zoom roda
/// mouse berpusat di kursor, seret untuk geser, Pas Layar / 100%,
/// zoom-ke-blok dari daftar atau tombol Blok ‹ ›). Koordinat blok
/// tetap piksel halaman asli — viewport murni tampilan.
///
/// Sunting kotak di kanvas (v0.7.15): klik kotak memilih blok,
/// seret badan kotak memindahkannya, dan kotak terpilih punya 8
/// pegangan tarik (sudut + titik tengah tepi) untuk mengubah
/// ukuran — geometri baru (X/Y/Width/Height blok) disimpan ke
/// proyek saat pointer dilepas. Seret area kosong tetap menggeser
/// tampilan. Sebelumnya kotak di halaman ini tidak bisa disunting
/// sama sekali dari kanvas (keluhan pemilik).
/// </summary>
public partial class DetectView : UserControl
{
    private static readonly IBrush BlockBrush = new SolidColorBrush(Color.Parse("#0D9488"));
    private static readonly IBrush BlockFill =
        new SolidColorBrush(Color.Parse("#0D9488"), 0.10);
    private static readonly IBrush SelectedBrush = new SolidColorBrush(Color.Parse("#2DD4BF"));
    private static readonly IBrush SelectedFill =
        new SolidColorBrush(Color.Parse("#2DD4BF"), 0.22);

    private MainWindowViewModel? _vm;

    private PageViewport _vp = new();
    private bool _panning;
    private Point _panLast;
    private bool _listPointerDown;

    // Keadaan seret kotak blok (v0.7.15, piksel halaman): blok yang
    // diseret + pegangannya + rect awal + rect pratinjau.
    private Models.TextBlock? _dragBlock;
    private BoxHandle _dragHandle = BoxHandle.None;
    private int _dragStartX, _dragStartY;
    private int _dsX, _dsY, _dsW, _dsH;
    private int _dcX, _dcY, _dcW, _dcH;
    private bool _draggingBlock;

    /// <summary>Toleransi tangkap pegangan dalam piksel halaman
    /// (±8 px layar pada zoom saat ini).</summary>
    private double HandleTolPage() => _vp.Scale > 0 ? 8.0 / _vp.Scale : 8.0;

    public DetectView()
    {
        InitializeComponent();
        DetectPageButton.Click += async (_, _) =>
        {
            if (_vm is { } vm) await vm.DetectCurrentPageAsync();
        };
        DetectChapterButton.Click += async (_, _) =>
        {
            if (_vm is { } vm) await vm.DetectChapterAsync();
        };
        CancelDetectButton.Click += (_, _) => _vm?.CancelDetect();
        OverlayCanvas.SizeChanged += (_, _) => RebuildOverlay();
        StageGrid.SizeChanged += (_, _) => ApplyViewport();
        StageGrid.PointerWheelChanged += OnStagePointerWheel;
        StageGrid.PointerPressed += OnStagePointerPressed;
        StageGrid.PointerMoved += OnStagePointerMoved;
        StageGrid.PointerReleased += OnStagePointerReleased;
        ZoomFitButton.Click += (_, _) =>
        {
            if (StageDims() is { } d) { _vp.Fit(d.Cw, d.Ch, d.Pw, d.Ph); ApplyViewport(); }
        };
        Zoom100Button.Click += (_, _) =>
        {
            if (StageDims() is { } d) { _vp.SetActualSize(d.Cw, d.Ch, d.Pw, d.Ph); ApplyViewport(); }
        };
        ZoomBlockPrevButton.Click += (_, _) => StepBlock(-1);
        ZoomBlockNextButton.Click += (_, _) => StepBlock(+1);
        BlockList.AddHandler(PointerPressedEvent,
            (_, _) => _listPointerDown = true, handledEventsToo: true);
        BlockList.SelectionChanged += (_, _) =>
        {
            if (_listPointerDown)
            {
                _listPointerDown = false;
                ZoomToSelectedBlock();
            }
        };
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm is not null)
        {
            _vm.PropertyChanged -= OnVmPropertyChanged;
            _vm.Blocks.CollectionChanged -= OnBlocksChanged;
        }
        _vm = DataContext as MainWindowViewModel;
        if (_vm is not null)
        {
            _vm.PropertyChanged += OnVmPropertyChanged;
            _vm.Blocks.CollectionChanged += OnBlocksChanged;
        }
        _vp = new PageViewport();
        ApplyViewport();
    }

    private void OnVmPropertyChanged(object? sender,
        System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainWindowViewModel.SelectedPageIndex))
        {
            _vp = new PageViewport();
            Dispatcher.UIThread.Post(ApplyViewport);
        }
        else if (e.PropertyName is nameof(MainWindowViewModel.CurrentPageBitmap)
            or nameof(MainWindowViewModel.SelectedBlock)
            or nameof(MainWindowViewModel.ShowOverlay))
            Dispatcher.UIThread.Post(RebuildOverlay);
    }

    private void OnBlocksChanged(object? sender,
        System.Collections.Specialized.NotifyCollectionChangedEventArgs e) =>
        Dispatcher.UIThread.Post(RebuildOverlay);

    // ─── Viewport zoom/pan ───────────────────────────────────────────

    private (double Cw, double Ch, int Pw, int Ph)? StageDims()
    {
        var vm = _vm;
        if (vm is null)
            return null;
        int pw = vm.CurrentPageWidth, ph = vm.CurrentPageHeight;
        double cw = StageGrid.Bounds.Width, ch = StageGrid.Bounds.Height;
        if (pw <= 0 || ph <= 0 || cw <= 0 || ch <= 0)
            return null;
        return (cw, ch, pw, ph);
    }

    private void ApplyViewport()
    {
        if (StageDims() is not { } d)
            return;
        if (!_vp.HasLayout)
            _vp.Fit(d.Cw, d.Ch, d.Pw, d.Ph);
        else
            _vp.ClampOffsets(d.Cw, d.Ch, d.Pw, d.Ph);
        Canvas.SetLeft(PageImage, _vp.OffsetX);
        Canvas.SetTop(PageImage, _vp.OffsetY);
        PageImage.Width = d.Pw * _vp.Scale;
        PageImage.Height = d.Ph * _vp.Scale;
        ZoomPercentText.Text = _vp.PercentText;
        RebuildOverlay();
    }

    private void OnStagePointerWheel(object? sender, PointerWheelEventArgs e)
    {
        if (StageDims() is not { } d)
            return;
        var pos = e.GetPosition(StageGrid);
        _vp.ZoomAt(pos.X, pos.Y, e.Delta.Y > 0 ? 1.2 : 1.0 / 1.2,
            d.Cw, d.Ch, d.Pw, d.Ph);
        ApplyViewport();
        e.Handled = true;
    }

    /// <summary>Konversi titik layar → piksel halaman lewat
    /// viewport (sama seperti halaman Typesetting).</summary>
    private (int X, int Y)? ToPagePoint(Point p)
    {
        var vm = _vm;
        if (vm is null || !_vp.HasLayout)
            return null;
        int pageW = vm.CurrentPageWidth, pageH = vm.CurrentPageHeight;
        if (pageW <= 0 || pageH <= 0)
            return null;
        var page = _vp.ScreenToPage(p.X, p.Y);
        int x = (int)page.X, y = (int)page.Y;
        if (x < -2000 || y < -2000 || x > pageW + 2000 || y > pageH + 2000)
            return null;
        return (x, y);
    }

    private void OnStagePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var vm = _vm;
        if (vm is null || !e.GetCurrentPoint(StageGrid).Properties.IsLeftButtonPressed)
            return;
        var pos = e.GetPosition(StageGrid);
        var pt = ToPagePoint(pos);
        if (pt is null)
            return;
        // Pegangan kotak terpilih didahulukan: tarik = ubah ukuran.
        if (vm.SelectedBlock is { } sel)
        {
            var handle = BoxEdit.HitHandle(pt.Value.X, pt.Value.Y,
                sel.X, sel.Y, sel.Width, sel.Height, HandleTolPage());
            if (handle is not BoxHandle.None and not BoxHandle.Move)
            {
                BeginBlockDrag(sel, handle, pt.Value);
                e.Pointer.Capture(StageGrid);
                e.Handled = true;
                RebuildOverlay();
                return;
            }
        }
        // Badan kotak blok (teratas = tergambar terakhir) → pilih
        // blok itu dan seret untuk memindahkannya.
        for (int i = vm.Blocks.Count - 1; i >= 0; i--)
        {
            var b = vm.Blocks[i];
            if (pt.Value.X >= b.X && pt.Value.X < b.X + b.Width
                && pt.Value.Y >= b.Y && pt.Value.Y < b.Y + b.Height)
            {
                vm.SelectedBlock = b;
                BeginBlockDrag(b, BoxHandle.Move, pt.Value);
                e.Pointer.Capture(StageGrid);
                e.Handled = true;
                RebuildOverlay();
                return;
            }
        }
        // Area kosong: seret = geser tampilan (pan).
        _panning = true;
        _panLast = pos;
        e.Pointer.Capture(StageGrid);
    }

    private void BeginBlockDrag(Models.TextBlock block, BoxHandle handle, (int X, int Y) pt)
    {
        _draggingBlock = true;
        _dragBlock = block;
        _dragHandle = handle;
        _dragStartX = pt.X;
        _dragStartY = pt.Y;
        _dsX = _dcX = block.X;
        _dsY = _dcY = block.Y;
        _dsW = _dcW = block.Width;
        _dsH = _dcH = block.Height;
        StageGrid.Cursor = TypesetView.CursorFor(handle);
    }

    private void OnStagePointerMoved(object? sender, PointerEventArgs e)
    {
        if (_draggingBlock && _dragBlock is not null)
        {
            var pt = ToPagePoint(e.GetPosition(StageGrid));
            if (pt is null)
                return;
            var r = BoxEdit.ApplyDrag(_dragHandle,
                _dsX, _dsY, _dsW, _dsH,
                _dragStartX, _dragStartY, pt.Value.X, pt.Value.Y);
            _dcX = r.X; _dcY = r.Y; _dcW = r.W; _dcH = r.H;
            RebuildOverlay();
            return;
        }
        if (_panning)
        {
            if (StageDims() is not { } d)
                return;
            var pos = e.GetPosition(StageGrid);
            _vp.PanBy(pos.X - _panLast.X, pos.Y - _panLast.Y, d.Cw, d.Ch, d.Pw, d.Ph);
            _panLast = pos;
            ApplyViewport();
            return;
        }
        UpdateHoverCursor(e);
    }

    private void OnStagePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_draggingBlock && _dragBlock is not null && _vm is not null)
        {
            _draggingBlock = false;
            var block = _dragBlock;
            _dragBlock = null;
            StageGrid.Cursor = Cursor.Default;
            _vm.CommitBlockGeometry(block, _dcX, _dcY, _dcW, _dcH);
            RebuildOverlay();
        }
        _panning = false;
    }

    /// <summary>Kursor mengikuti pegangan/badan kotak terpilih
    /// saat pointer melayang (tanpa seret).</summary>
    private void UpdateHoverCursor(PointerEventArgs e)
    {
        var vm = _vm;
        if (vm?.SelectedBlock is not { } sel)
        {
            StageGrid.Cursor = Cursor.Default;
            return;
        }
        var pt = ToPagePoint(e.GetPosition(StageGrid));
        StageGrid.Cursor = pt is null
            ? Cursor.Default
            : TypesetView.CursorFor(BoxEdit.HitHandle(
                pt.Value.X, pt.Value.Y, sel.X, sel.Y, sel.Width, sel.Height,
                HandleTolPage()));
    }

    private void StepBlock(int delta)
    {
        var vm = _vm;
        if (vm is null || vm.Blocks.Count == 0)
            return;
        int idx = vm.SelectedBlock is null ? -1 : vm.Blocks.IndexOf(vm.SelectedBlock);
        int next = Math.Clamp(idx + delta, 0, vm.Blocks.Count - 1);
        vm.SelectedBlock = vm.Blocks[next];
        ZoomToSelectedBlock();
    }

    private void ZoomToSelectedBlock()
    {
        if (_vm?.SelectedBlock is not { } block || StageDims() is not { } d)
            return;
        _vp.ZoomToRect(block.X, block.Y, block.Width, block.Height,
            d.Cw, d.Ch, d.Pw, d.Ph);
        ApplyViewport();
    }

    private void RebuildOverlay()
    {
        OverlayCanvas.Children.Clear();
        var vm = _vm;
        if (vm is null || !vm.ShowOverlay || vm.CurrentPageBitmap is null || !_vp.HasLayout)
            return;
        int pageW = vm.CurrentPageWidth, pageH = vm.CurrentPageHeight;
        double cw = OverlayCanvas.Bounds.Width, ch = OverlayCanvas.Bounds.Height;
        if (pageW <= 0 || pageH <= 0 || cw <= 0 || ch <= 0)
            return;
        double scale = _vp.Scale;
        double ox = _vp.OffsetX, oy = _vp.OffsetY;

        int number = 0;
        foreach (var block in vm.Blocks)
        {
            number++;
            bool selected = ReferenceEquals(block, vm.SelectedBlock);
            // Selama seret, kotak yang diseret digambar pada rect
            // pratinjau hasil BoxEdit.ApplyDrag.
            int drawX = block.X, drawY = block.Y;
            int drawW = block.Width, drawH = block.Height;
            if (selected && _draggingBlock && ReferenceEquals(block, _dragBlock))
            {
                drawX = _dcX; drawY = _dcY; drawW = _dcW; drawH = _dcH;
            }
            double x = ox + drawX * scale, y = oy + drawY * scale;
            var rect = new Border
            {
                BorderBrush = selected ? SelectedBrush : BlockBrush,
                BorderThickness = new Thickness(selected ? 3 : 2),
                Background = selected ? SelectedFill : BlockFill,
                CornerRadius = new CornerRadius(3),
                Width = Math.Max(6, drawW * scale),
                Height = Math.Max(6, drawH * scale),
            };
            Canvas.SetLeft(rect, x);
            Canvas.SetTop(rect, y);
            OverlayCanvas.Children.Add(rect);

            if (selected)
            {
                // Pegangan tarik (v0.7.15): 8 kotak kecil di sudut +
                // titik tengah tepi kotak terpilih, ukuran layar
                // tetap ±8 px pada zoom berapa pun.
                foreach (var (fx, fy) in BoxEdit.HandleAnchors)
                {
                    var handleSq = new Border
                    {
                        Background = Brushes.White,
                        BorderBrush = SelectedBrush,
                        BorderThickness = new Thickness(1.5),
                        CornerRadius = new CornerRadius(2),
                        Width = 8,
                        Height = 8,
                    };
                    Canvas.SetLeft(handleSq,
                        ox + (drawX + fx * drawW) * scale - 4);
                    Canvas.SetTop(handleSq,
                        oy + (drawY + fy * drawH) * scale - 4);
                    OverlayCanvas.Children.Add(handleSq);
                }
            }

            var badge = new Border
            {
                Background = selected ? SelectedBrush : BlockBrush,
                CornerRadius = new CornerRadius(2),
                Padding = new Thickness(4, 1),
                Child = new TextBlock
                {
                    Text = number.ToString(),
                    Foreground = Brushes.White,
                    FontSize = 10,
                    FontWeight = FontWeight.Bold,
                },
            };
            Canvas.SetLeft(badge, Math.Max(0, x - 2));
            Canvas.SetTop(badge, Math.Max(0, y - 16));
            OverlayCanvas.Children.Add(badge);
        }
    }
}
