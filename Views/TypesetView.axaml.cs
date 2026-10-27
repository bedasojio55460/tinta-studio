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
/// </summary>
public partial class TypesetView : UserControl
{
    private static readonly IBrush FrameBrush = new SolidColorBrush(Color.Parse("#0D9488"));
    private static readonly IBrush SelectedBrush = new SolidColorBrush(Color.Parse("#2DD4BF"));
    private static readonly IBrush FaintFill =
        new SolidColorBrush(Color.Parse("#0D9488"), 0.05);

    private MainWindowViewModel? _vm;

    // Keadaan seret (dalam piksel halaman).
    private TypesetBlockItemViewModel? _dragItem;
    private int _dragStartX, _dragStartY;
    private int _dragCurX, _dragCurY;
    private int _dragStartDx, _dragStartDy;
    private bool _dragging;

    public TypesetView()
    {
        InitializeComponent();
        AutoPageButton.Click += (_, _) => _vm?.ApplyAutoTypesetCurrentPage();
        AutoChapterButton.Click += (_, _) => _vm?.ApplyAutoTypesetChapter();
        OverlayCanvas.SizeChanged += (_, _) => RebuildOverlay();
        StageGrid.PointerPressed += OnStagePointerPressed;
        StageGrid.PointerMoved += OnStagePointerMoved;
        StageGrid.PointerReleased += OnStagePointerReleased;
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
        RebuildOverlay();
    }

    private void OnVmPropertyChanged(object? sender,
        System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainWindowViewModel.SelectedTypesetBlock)
            or nameof(MainWindowViewModel.TypesetCanvasBitmap)
            or nameof(MainWindowViewModel.SelectedPageIndex))
            Dispatcher.UIThread.Post(RebuildOverlay);
    }

    private void OnBlocksChanged(object? sender,
        System.Collections.Specialized.NotifyCollectionChangedEventArgs e) =>
        Dispatcher.UIThread.Post(RebuildOverlay);

    // ─── Konversi koordinat (pola halaman Deteksi/Bersihkan) ────────

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
        if (x < -2000 || y < -2000 || x > pageW + 2000 || y > pageH + 2000)
            return null;
        return (x, y);
    }

    // ─── Pointer: pilih + seret ──────────────────────────────────────

    private void OnStagePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var vm = _vm;
        if (vm is null || !e.GetCurrentPoint(StageGrid).Properties.IsLeftButtonPressed)
            return;
        var pt = ToPagePoint(e.GetPosition(StageGrid));
        if (pt is null)
            return;
        var hit = vm.HitTestTypesetBlock(pt.Value.X, pt.Value.Y);
        if (hit is null)
            return;
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
        if (vm is null)
            return;
        int pageW = vm.CurrentPageWidth, pageH = vm.CurrentPageHeight;
        double cw = OverlayCanvas.Bounds.Width, ch = OverlayCanvas.Bounds.Height;
        if (pageW <= 0 || pageH <= 0 || cw <= 0 || ch <= 0)
            return;
        double scale = Math.Min(cw / pageW, ch / pageH);
        double ox = (cw - pageW * scale) / 2, oy = (ch - pageH * scale) / 2;

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
