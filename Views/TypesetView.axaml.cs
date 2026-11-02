using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using TintaStudio.Services;
using TintaStudio.ViewModels;

namespace TintaStudio.Views;

/// <summary>
/// Halaman Typesetting. Kanvas menampilkan halaman final (bersih +
/// ter-typeset; toggle Asli/Bersih untuk membandingkan). Klik
/// memilih blok (hit-test kotak render), seret memindahkan blok
/// terpilih — pratinjau seret hanya menggeser bingkai di overlay;
/// Dx/Dy baru disimpan ke proyek saat pointer dilepas.
///
/// Zoom (v0.7.14): roda mouse berpusat di kursor, seret area
/// kosong menggeser tampilan, tombol Pas Layar / 100% + indikator
/// persen, dan zoom-ke-blok (klik blok di daftar atau tombol
/// Blok ‹ ›). Semua transformasi lewat PageViewport (murni
/// tampilan — koordinat blok tetap koordinat halaman asli).
/// </summary>
public partial class TypesetView : UserControl
{
    private static readonly IBrush FrameBrush = new SolidColorBrush(Color.Parse("#0D9488"));
    private static readonly IBrush SelectedBrush = new SolidColorBrush(Color.Parse("#2DD4BF"));
    private static readonly IBrush FaintFill =
        new SolidColorBrush(Color.Parse("#0D9488"), 0.05);

    private MainWindowViewModel? _vm;

    // Keadaan seret blok (dalam piksel halaman).
    private TypesetBlockItemViewModel? _dragItem;
    private int _dragStartX, _dragStartY;
    private int _dragCurX, _dragCurY;
    private int _dragStartDx, _dragStartDy;
    private bool _dragging;

    // Keadaan zoom/pan (v0.7.14).
    private PageViewport _vp = new();
    private bool _panning;
    private Point _panLast;
    private bool _listPointerDown;

    public TypesetView()
    {
        InitializeComponent();
        AutoPageButton.Click += (_, _) => _vm?.ApplyAutoTypesetCurrentPage();
        AutoChapterButton.Click += (_, _) => _vm?.ApplyAutoTypesetChapter();
        OverlayCanvas.SizeChanged += (_, _) => RebuildOverlay();
        StageGrid.SizeChanged += (_, _) => ApplyViewport();
        StageGrid.PointerPressed += OnStagePointerPressed;
        StageGrid.PointerMoved += OnStagePointerMoved;
        StageGrid.PointerReleased += OnStagePointerReleased;
        StageGrid.PointerWheelChanged += OnStagePointerWheel;
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
            // Zoom hanya bila pilihan berubah dari klik daftar —
            // bukan dari klik kanvas / pergantian halaman.
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
            _vm.TypesetBlocks.CollectionChanged -= OnBlocksChanged;
        }
        _vm = DataContext as MainWindowViewModel;
        if (_vm is not null)
        {
            _vm.PropertyChanged += OnVmPropertyChanged;
            _vm.TypesetBlocks.CollectionChanged += OnBlocksChanged;
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
        else if (e.PropertyName is nameof(MainWindowViewModel.SelectedTypesetBlock)
            or nameof(MainWindowViewModel.TypesetCanvasBitmap))
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

    private void StepBlock(int delta)
    {
        var vm = _vm;
        if (vm is null || vm.TypesetBlocks.Count == 0)
            return;
        int idx = vm.SelectedTypesetBlock is null
            ? -1 : vm.TypesetBlocks.IndexOf(vm.SelectedTypesetBlock);
        int next = Math.Clamp(idx + delta, 0, vm.TypesetBlocks.Count - 1);
        vm.SelectedTypesetBlock = vm.TypesetBlocks[next];
        ZoomToSelectedBlock();
    }

    private void ZoomToSelectedBlock()
    {
        var vm = _vm;
        if (vm?.SelectedTypesetBlock is not { } item || StageDims() is not { } d)
            return;
        var box = TypesetService.RenderBox(
            item.Block, TypesetService.EffectiveStyle(item.Block));
        _vp.ZoomToRect(box.Left, box.Top, box.Width, box.Height,
            d.Cw, d.Ch, d.Pw, d.Ph);
        ApplyViewport();
    }

    // ─── Konversi koordinat lewat viewport ───────────────────────────

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

    // ─── Pointer: pilih + seret blok / geser tampilan ────────────────

    private void OnStagePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var vm = _vm;
        if (vm is null || !e.GetCurrentPoint(StageGrid).Properties.IsLeftButtonPressed)
            return;
        var pos = e.GetPosition(StageGrid);
        var pt = ToPagePoint(pos);
        if (pt is null)
            return;
        var hit = vm.HitTestTypesetBlock(pt.Value.X, pt.Value.Y);
        if (hit is null)
        {
            // Area kosong: seret = geser tampilan (pan).
            _panning = true;
            _panLast = pos;
            e.Pointer.Capture(StageGrid);
            e.Handled = true;
            return;
        }
        vm.SelectedTypesetBlock = hit;
        var style = TypesetService.EffectiveStyle(hit.Block);
        _dragItem = hit;
        _dragStartX = _dragCurX = pt.Value.X;
        _dragStartY = _dragCurY = pt.Value.Y;
        _dragStartDx = style.Dx;
        _dragStartDy = style.Dy;
        _dragging = true;
        e.Pointer.Capture(StageGrid);
        e.Handled = true;
        RebuildOverlay();
    }

    private void OnStagePointerMoved(object? sender, PointerEventArgs e)
    {
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
        if (!_dragging || _dragItem is null)
            return;
        var pt = ToPagePoint(e.GetPosition(StageGrid));
        if (pt is null)
            return;
        _dragCurX = pt.Value.X;
        _dragCurY = pt.Value.Y;
        RebuildOverlay();
    }

    private void OnStagePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_panning)
        {
            _panning = false;
            return;
        }
        if (!_dragging || _dragItem is null || _vm is null)
            return;
        _dragging = false;
        var item = _dragItem;
        _dragItem = null;
        int dx = _dragCurX - _dragStartX, dy = _dragCurY - _dragStartY;
        if (dx != 0 || dy != 0)
            _vm.CommitBlockDrag(item, _dragStartDx, _dragStartDy, dx, dy);
        RebuildOverlay();
    }

    // ─── Overlay bingkai blok ────────────────────────────────────────

    private void RebuildOverlay()
    {
        OverlayCanvas.Children.Clear();
        var vm = _vm;
        if (vm is null || !_vp.HasLayout)
            return;
        int pageW = vm.CurrentPageWidth, pageH = vm.CurrentPageHeight;
        double cw = OverlayCanvas.Bounds.Width, ch = OverlayCanvas.Bounds.Height;
        if (pageW <= 0 || pageH <= 0 || cw <= 0 || ch <= 0)
            return;
        double scale = _vp.Scale;
        double ox = _vp.OffsetX, oy = _vp.OffsetY;

        foreach (var item in vm.TypesetBlocks)
        {
            var box = TypesetService.RenderBox(
                item.Block, TypesetService.EffectiveStyle(item.Block));
            bool selected = ReferenceEquals(item, vm.SelectedTypesetBlock);
            int shiftX = 0, shiftY = 0;
            if (selected && _dragging && ReferenceEquals(item, _dragItem))
            {
                shiftX = _dragCurX - _dragStartX;
                shiftY = _dragCurY - _dragStartY;
            }
            var rect = new Border
            {
                BorderBrush = selected ? SelectedBrush : FrameBrush,
                BorderThickness = new Thickness(selected ? 2.5 : 1.25),
                Background = FaintFill,
                CornerRadius = new CornerRadius(3),
                Width = Math.Max(6, box.Width * scale),
                Height = Math.Max(6, box.Height * scale),
            };
            Canvas.SetLeft(rect, ox + (box.Left + shiftX) * scale);
            Canvas.SetTop(rect, oy + (box.Top + shiftY) * scale);
            OverlayCanvas.Children.Add(rect);

            if (selected)
            {
                var badge = new Border
                {
                    Background = SelectedBrush,
                    CornerRadius = new CornerRadius(2),
                    Padding = new Thickness(4, 1),
                    Child = new TextBlock
                    {
                        Text = $"#{item.Number}",
                        Foreground = Brushes.Black,
                        FontSize = 10,
                        FontWeight = FontWeight.Bold,
                    },
                };
                Canvas.SetLeft(badge, Math.Max(0, ox + (box.Left + shiftX) * scale - 2));
                Canvas.SetTop(badge, Math.Max(0, oy + (box.Top + shiftY) * scale - 16));
                OverlayCanvas.Children.Add(badge);
            }
        }
    }
}
