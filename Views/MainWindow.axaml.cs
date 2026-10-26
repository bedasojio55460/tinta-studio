using Avalonia.Controls;
using Avalonia.Media;
using TintaStudio.ViewModels;

namespace TintaStudio.Views;

public partial class MainWindow : Window
{
    private static readonly IBrush ActiveBrush = new SolidColorBrush(Color.Parse("#0d9488"));
    private static readonly IBrush InactiveBrush = Brushes.Transparent;

    public MainWindow()
    {
        InitializeComponent();
        MenuLibrary.Click += (_, _) => ViewModel?.ShowLibrary();
        MenuViewer.Click += (_, _) => ViewModel?.ShowViewer();
        Opened += OnOpened;
    }

    private MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;

    private async void OnOpened(object? sender, EventArgs e)
    {
        UpdateMenuStyles();
        if (ViewModel is { } vm)
        {
            vm.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(MainWindowViewModel.IsLibraryActive))
                    UpdateMenuStyles();
            };

            // Alat uji & screenshot: TINTA_OPEN=<jalur> membuka bab otomatis.
            var autoOpen = Environment.GetEnvironmentVariable("TINTA_OPEN");
            if (!string.IsNullOrWhiteSpace(autoOpen))
                await vm.OpenChapterPathAsync(autoOpen);
        }
    }

    private void UpdateMenuStyles()
    {
        var libraryActive = ViewModel?.IsLibraryActive ?? true;
        MenuLibrary.Background = libraryActive ? ActiveBrush : InactiveBrush;
        MenuViewer.Background = libraryActive ? InactiveBrush : ActiveBrush;
    }
}
