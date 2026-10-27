using Avalonia.Controls;
using TintaStudio.ViewModels;

namespace TintaStudio.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        MenuLibrary.Click += (_, _) => ViewModel?.ShowLibrary();
        MenuViewer.Click += (_, _) => ViewModel?.ShowViewer();
        MenuDetect.Click += (_, _) => ViewModel?.ShowDetect();
        MenuClean.Click += (_, _) => ViewModel?.ShowClean();
        MenuTranslate.Click += (_, _) => ViewModel?.ShowTranslate();
        MenuTypeset.Click += (_, _) => ViewModel?.ShowTypeset();
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
                if (args.PropertyName is nameof(MainWindowViewModel.IsLibraryActive)
                    or nameof(MainWindowViewModel.IsViewerActive)
                    or nameof(MainWindowViewModel.IsDetectActive)
                    or nameof(MainWindowViewModel.IsCleanActive)
                    or nameof(MainWindowViewModel.IsTranslateActive)
                    or nameof(MainWindowViewModel.IsTypesetActive))
                    UpdateMenuStyles();
            };

            // Alat uji & screenshot: TINTA_OPEN=<jalur> membuka bab otomatis,
            // TINTA_VIEW=library|viewer|detect|clean|translate|typeset memilih halaman awal.
            var autoOpen = Environment.GetEnvironmentVariable("TINTA_OPEN");
            if (!string.IsNullOrWhiteSpace(autoOpen))
                await vm.OpenChapterPathAsync(autoOpen);
            switch (Environment.GetEnvironmentVariable("TINTA_VIEW"))
            {
                case "detect": vm.ShowDetect(); break;
                case "clean": vm.ShowClean(); break;
                case "translate": vm.ShowTranslate(); break;
                case "typeset": vm.ShowTypeset(); break;
                case "viewer": vm.ShowViewer(); break;
                case "library": vm.ShowLibrary(); break;
            }
        }
    }

    private void UpdateMenuStyles()
    {
        SetActive(MenuLibrary, ViewModel?.IsLibraryActive ?? true);
        SetActive(MenuViewer, ViewModel?.IsViewerActive ?? false);
        SetActive(MenuDetect, ViewModel?.IsDetectActive ?? false);
        SetActive(MenuClean, ViewModel?.IsCleanActive ?? false);
        SetActive(MenuTranslate, ViewModel?.IsTranslateActive ?? false);
        SetActive(MenuTypeset, ViewModel?.IsTypesetActive ?? false);
    }

    private static void SetActive(Button button, bool active)
    {
        if (active)
        {
            if (!button.Classes.Contains("active"))
                button.Classes.Add("active");
        }
        else
        {
            button.Classes.Remove("active");
        }
    }
}
