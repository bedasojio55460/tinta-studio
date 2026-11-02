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
        StageGrid.PointerReleased += (_, _) => _panning = false;
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

    private void OnStagePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(StageGrid).Properties.IsLeftButtonPressed)
            return;
        _panning = true;
        _panLast = e.GetPosition(StageGrid);
        e.Pointer.Capture(StageGrid);
    }

    private void OnStagePointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_panning || StageDims() is not { } d)
            return;
        var pos = e.GetPosition(StageGrid);
        _vp.PanBy(pos.X - _panLast.X, pos.Y - _panLast.Y, d.Cw, d.Ch, d.Pw, d.Ph);
        _panLast = pos;
        ApplyViewport();
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
            double x = ox + block.X * scale, y = oy + block.Y * scale;
            var rect = new Border
            {
                BorderBrush = selected ? SelectedBrush : BlockBrush,
                BorderThickness = new Thickness(selected ? 3 : 2),
                Background = selected ? SelectedFill : BlockFill,
                CornerRadius = new CornerRadius(3),
                Width = Math.Max(6, block.Width * scale),
                Height = Math.Max(6, block.Height * scale),
            };
            Canvas.SetLeft(rect, x);
            Canvas.SetTop(rect, y);
            OverlayCanvas.Children.Add(rect);

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
