using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using TintaStudio.ViewModels;

namespace TintaStudio.Views;

/// <summary>
/// Halaman Deteksi &amp; OCR. Overlay kotak blok digambar di atas
/// Canvas yang menutupi gambar halaman; skala dihitung dari ukuran
/// tampil gambar (Stretch=Uniform, terpusat) terhadap piksel asal.
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
        RebuildOverlay();
    }

    private void OnVmPropertyChanged(object? sender,
        System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainWindowViewModel.CurrentPageBitmap)
            or nameof(MainWindowViewModel.SelectedBlock)
            or nameof(MainWindowViewModel.ShowOverlay)
            or nameof(MainWindowViewModel.SelectedPageIndex))
            Dispatcher.UIThread.Post(RebuildOverlay);
    }

    private void OnBlocksChanged(object? sender,
        System.Collections.Specialized.NotifyCollectionChangedEventArgs e) =>
        Dispatcher.UIThread.Post(RebuildOverlay);

    private void RebuildOverlay()
    {
        OverlayCanvas.Children.Clear();
        var vm = _vm;
        if (vm is null || !vm.ShowOverlay || vm.CurrentPageBitmap is null)
            return;
        int pageW = vm.CurrentPageWidth, pageH = vm.CurrentPageHeight;
        double cw = OverlayCanvas.Bounds.Width, ch = OverlayCanvas.Bounds.Height;
        if (pageW <= 0 || pageH <= 0 || cw <= 0 || ch <= 0)
            return;
        double scale = Math.Min(cw / pageW, ch / pageH);
        double ox = (cw - pageW * scale) / 2, oy = (ch - pageH * scale) / 2;

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
