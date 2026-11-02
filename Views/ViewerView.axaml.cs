using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using TintaStudio.Services;
using TintaStudio.ViewModels;

namespace TintaStudio.Views;

/// <summary>
/// Penampil halaman. Zoom (v0.7.14) memakai PageViewport yang sama
/// dengan permukaan sunting: roda mouse berpusat di kursor, seret
/// untuk geser, Pas Layar / 100% + indikator persen. Di sini juga
/// tinggal aksi Potong Jadi Halaman Pendek / Sambung Lagi untuk
/// halaman panjang (aksi eksplisit pengguna).
/// </summary>
public partial class ViewerView : UserControl
{
    private PageViewport _vp = new();
    private bool _panning;
    private Point _panLast;
    private MainWindowViewModel? _vm;

    public ViewerView()
    {
        InitializeComponent();
        ViewerOpenFile.Click += async (_, _) =>
        {
            var top = TopLevel.GetTopLevel(this);
            if (top is null || ViewModel is not { } vm) return;
            var path = await FilePickers.PickComicFileAsync(top);
            if (path is not null) await vm.OpenChapterPathAsync(path);
        };
        ViewerOpenFolder.Click += async (_, _) =>
        {
            var top = TopLevel.GetTopLevel(this);
            if (top is null || ViewModel is not { } vm) return;
            var path = await FilePickers.PickComicFolderAsync(top);
            if (path is not null) await vm.OpenChapterPathAsync(path);
        };
        ViewerGoDetect.Click += (_, _) => ViewModel?.ShowDetect();
        ViewerSplitButton.Click += async (_, _) =>
        {
            if (ViewModel is { } vm) await vm.SplitCurrentPageAsync();
        };
        ViewerRejoinButton.Click += async (_, _) =>
        {
            if (ViewModel is { } vm) await vm.RejoinCurrentPageAsync();
        };
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
        DataContextChanged += (_, _) =>
        {
            if (_vm is not null)
                _vm.PropertyChanged -= OnVmPropertyChanged;
            _vm = ViewModel;
            if (_vm is not null)
                _vm.PropertyChanged += OnVmPropertyChanged;
            _vp = new PageViewport();
            ApplyViewport();
        };
    }

    private MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;

    private void OnVmPropertyChanged(object? sender,
        System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainWindowViewModel.SelectedPageIndex)
            or nameof(MainWindowViewModel.CurrentPageBitmap))
        {
            _vp = new PageViewport();
            Dispatcher.UIThread.Post(ApplyViewport);
        }
    }

    private (double Cw, double Ch, int Pw, int Ph)? StageDims()
    {
        var vm = ViewModel;
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
}
