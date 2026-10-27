using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using TintaStudio.ViewModels;

namespace TintaStudio.Views;

/// <summary>
/// Halaman Bersihkan. Kanvas menampilkan gambar bersih halaman
/// (atau asli saat toggle "Asli" menyala). Overlay kotak blok
/// digambar seperti halaman Deteksi. Pointer di atas kanvas dipakai
/// alat manual: Penetes (ambil warna), Kuas &amp; Penghapus &amp;
/// Penghapus Latar (sapuan dicatat saat pointer dilepas, lalu
/// gambar dibangun ulang dari proyek — non-destruktif). Alat
/// berbentuk lingkaran menampilkan pratinjau cincin mengikuti
/// kursor, sebesar ukuran kuas terpilih.
/// </summary>
public partial class CleanView : UserControl
{
    private static readonly IBrush BlockBrush = new SolidColorBrush(Color.Parse("#0D9488"));
    private static readonly IBrush CleanedBrush = new SolidColorBrush(Color.Parse("#5EEAD4"));
    private static readonly IBrush BlockFill =
        new SolidColorBrush(Color.Parse("#0D9488"), 0.08);

    private MainWindowViewModel? _vm;
    private List<(int X, int Y)>? _strokePoints;
    private bool _strokeErased;
    private bool _strokeBgEraser;
    private Point? _lastPointer;

    public CleanView()
    {
        InitializeComponent();
        CleanPageButton.Click += async (_, _) =>
        {
            if (_vm is { } vm) await vm.CleanCurrentPageAsync();
        };
        CleanChapterButton.Click += async (_, _) =>
        {
            if (_vm is { } vm) await vm.CleanChapterAsync();
        };
        CancelCleanButton.Click += (_, _) => _vm?.CancelClean();
        EyedropperToggle.Click += (_, _) => SetToolFromToggle(CleanTool.Eyedropper, EyedropperToggle);
        BrushToggle.Click += (_, _) => SetToolFromToggle(CleanTool.Brush, BrushToggle);
        EraserToggle.Click += (_, _) => SetToolFromToggle(CleanTool.Eraser, EraserToggle);
        BackgroundEraserToggle.Click += (_, _) => SetToolFromToggle(CleanTool.BackgroundEraser, BackgroundEraserToggle);
        OverlayCanvas.SizeChanged += (_, _) => RebuildOverlay();
        StageGrid.PointerPressed += OnStagePointerPressed;
        StageGrid.PointerMoved += OnStagePointerMoved;
        StageGrid.PointerReleased += OnStagePointerReleased;
        StageGrid.PointerExited += (_, _) => BrushRing.IsVisible = false;
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm is not null)
        {
            _vm.PropertyChanged -= OnVmPropertyChanged;
            _vm.CleanBlocks.CollectionChanged -= OnCleanBlocksChanged;
        }
        _vm = DataContext as MainWindowViewModel;
        if (_vm is not null)
        {
            _vm.PropertyChanged += OnVmPropertyChanged;
            _vm.CleanBlocks.CollectionChanged += OnCleanBlocksChanged;
        }
        SyncToolToggles();
        RebuildOverlay();
    }

    private void OnVmPropertyChanged(object? sender,
        System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainWindowViewModel.CleanTool))
            Dispatcher.UIThread.Post(SyncToolToggles);
        if (e.PropertyName is nameof(MainWindowViewModel.BrushSize))
            Dispatcher.UIThread.Post(UpdateBrushRing);
        if (e.PropertyName is nameof(MainWindowViewModel.CleanCanvasBitmap)
            or nameof(MainWindowViewModel.ShowCleanOverlay)
            or nameof(MainWindowViewModel.SelectedPageIndex))
            Dispatcher.UIThread.Post(RebuildOverlay);
    }

    private void OnCleanBlocksChanged(object? sender,
        System.Collections.Specialized.NotifyCollectionChangedEventArgs e) =>
        Dispatcher.UIThread.Post(RebuildOverlay);

    // ─── Alat manual ─────────────────────────────────────────────────

    private void SetToolFromToggle(CleanTool tool, ToggleButton button)
    {
        if (_vm is null)
            return;
        _vm.CleanTool = button.IsChecked == true ? tool : CleanTool.None;
        SyncToolToggles();
    }

    private void SyncToolToggles()
    {
        var tool = _vm?.CleanTool ?? CleanTool.None;
        EyedropperToggle.IsChecked = tool == CleanTool.Eyedropper;
        BrushToggle.IsChecked = tool == CleanTool.Brush;
        EraserToggle.IsChecked = tool == CleanTool.Eraser;
        BackgroundEraserToggle.IsChecked = tool == CleanTool.BackgroundEraser;
        UpdateBrushRing();
    }

    private static bool IsCircleTool(CleanTool tool) =>
        tool is CleanTool.Brush or CleanTool.Eraser or CleanTool.BackgroundEraser;

    /// <summary>Pratinjau lingkaran kuas mengikuti kursor: cincin
    /// sebesar BrushSize (diameter piksel halaman) dipetakan ke
    /// koordinat kontrol, terpusat di posisi pointer terakhir.
    /// Tersembunyi bila alat aktif bukan alat lingkaran atau
    /// pointer di luar area gambar.</summary>
    private void UpdateBrushRing()
    {
        var vm = _vm;
        if (vm is null || !IsCircleTool(vm.CleanTool) || _lastPointer is null
            || !TryStageTransform(out var scale, out _, out _))
        {
            BrushRing.IsVisible = false;
            return;
        }
        var p = _lastPointer.Value;
        if (ToPagePoint(p) is null && _strokePoints is null)
        {
            BrushRing.IsVisible = false;
            return;
        }
        double d = Math.Max(4, vm.BrushSize * scale);
        BrushRing.Width = d;
        BrushRing.Height = d;
        Canvas.SetLeft(BrushRing, p.X - d / 2);
        Canvas.SetTop(BrushRing, p.Y - d / 2);
        BrushRing.IsVisible = true;
    }

    /// <summary>Skala + offset pemetaan halaman → kontrol
    /// (Stretch=Uniform, terpusat).</summary>
    private bool TryStageTransform(out double scale, out double ox, out double oy)
    {
        scale = 0; ox = 0; oy = 0;
        var vm = _vm;
        if (vm is null)
            return false;
        int pageW = vm.CurrentPageWidth, pageH = vm.CurrentPageHeight;
        double cw = StageGrid.Bounds.Width, ch = StageGrid.Bounds.Height;
        if (pageW <= 0 || pageH <= 0 || cw <= 0 || ch <= 0)
            return false;
        scale = Math.Min(cw / pageW, ch / pageH);
        ox = (cw - pageW * scale) / 2;
        oy = (ch - pageH * scale) / 2;
        return true;
    }

    /// <summary>Konversi titik kontrol → piksel halaman asli
    /// (Stretch=Uniform, terpusat — matematika yang sama dengan
    /// overlay halaman Deteksi). Null bila di luar gambar.</summary>
    private (int X, int Y)? ToPagePoint(Point p)
    {
        var vm = _vm;
        if (vm is null)
            return null;
        int pageW = vm.CurrentPageWidth, pageH = vm.CurrentPageHeight;
        double cw = StageGrid.Bounds.Width, ch = StageGrid.Bounds.Height;
        if (pageW <= 0 || pageH <= 0 || cw <= 0 || ch <= 0)
            return null;
        double scale = Math.Min(cw / pageW, ch / pageH);
        double ox = (cw - pageW * scale) / 2, oy = (ch - pageH * scale) / 2;
        int x = (int)((p.X - ox) / scale), y = (int)((p.Y - oy) / scale);
        if (x < 0 || y < 0 || x >= pageW || y >= pageH)
            return null;
        return (x, y);
    }

    private void OnStagePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var vm = _vm;
        if (vm is null || !e.GetCurrentPoint(StageGrid).Properties.IsLeftButtonPressed)
            return;
        _lastPointer = e.GetPosition(StageGrid);
        UpdateBrushRing();
        var pt = ToPagePoint(e.GetPosition(StageGrid));
        if (pt is null)
            return;
        switch (vm.CleanTool)
        {
            case CleanTool.Eyedropper:
                vm.PickColorAt(pt.Value.X, pt.Value.Y);
                e.Handled = true;
                break;
            case CleanTool.Brush:
            case CleanTool.Eraser:
            case CleanTool.BackgroundEraser:
                _strokeErased = vm.CleanTool == CleanTool.Eraser;
                _strokeBgEraser = vm.CleanTool == CleanTool.BackgroundEraser;
                _strokePoints = new List<(int X, int Y)> { pt.Value };
                e.Pointer.Capture(StageGrid);
                e.Handled = true;
                break;
        }
    }

    private void OnStagePointerMoved(object? sender, PointerEventArgs e)
    {
        _lastPointer = e.GetPosition(StageGrid);
        UpdateBrushRing();
        if (_strokePoints is null || _vm is null)
            return;
        var pt = ToPagePoint(e.GetPosition(StageGrid));
        if (pt is null)
            return;
        var last = _strokePoints[^1];
        int dx = pt.Value.X - last.X, dy = pt.Value.Y - last.Y;
        if (dx * dx + dy * dy >= 9)
            _strokePoints.Add(pt.Value);
    }

    private void OnStagePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_strokePoints is null || _vm is null)
            return;
        var points = _strokePoints;
        _strokePoints = null;
        if (_strokeBgEraser)
            _vm.AddBackgroundEraserStroke(points);
        else
            _vm.AddManualStroke(points, _strokeErased);
        _strokeBgEraser = false;
    }

    // ─── Overlay kotak blok ──────────────────────────────────────────

    private void RebuildOverlay()
    {
        OverlayCanvas.Children.Clear();
        var vm = _vm;
        if (vm is null || !vm.ShowCleanOverlay)
            return;
        int pageW = vm.CurrentPageWidth, pageH = vm.CurrentPageHeight;
        double cw = OverlayCanvas.Bounds.Width, ch = OverlayCanvas.Bounds.Height;
        if (pageW <= 0 || pageH <= 0 || cw <= 0 || ch <= 0)
            return;
        double scale = Math.Min(cw / pageW, ch / pageH);
        double ox = (cw - pageW * scale) / 2, oy = (ch - pageH * scale) / 2;

        foreach (var item in vm.CleanBlocks)
        {
            var block = item.Block;
            double x = ox + block.X * scale, y = oy + block.Y * scale;
            var rect = new Border
            {
                BorderBrush = item.IsCleaned ? CleanedBrush : BlockBrush,
                BorderThickness = new Thickness(2),
                Background = BlockFill,
                CornerRadius = new CornerRadius(3),
                Width = Math.Max(6, block.Width * scale),
                Height = Math.Max(6, block.Height * scale),
            };
            Canvas.SetLeft(rect, x);
            Canvas.SetTop(rect, y);
            OverlayCanvas.Children.Add(rect);

            var badge = new Border
            {
                Background = item.IsCleaned ? CleanedBrush : BlockBrush,
                CornerRadius = new CornerRadius(2),
                Padding = new Thickness(4, 1),
                Child = new TextBlock
                {
                    Text = item.IsCleaned ? "✓" : item.Number.ToString(),
                    Foreground = Brushes.Black,
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
