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
        MenuExport.Click += (_, _) => ViewModel?.ShowExport();
        Opened += OnOpened;
    }

    private MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;

    /// <summary>Pembuka dialog pengaturan otomatis untuk alat uji
    /// (TINTA_SETTINGS=1): tunggu jendela tampil penuh dulu.</summary>
    private async Task OpenSettingsDialogDelayedAsync(MainWindowViewModel vm)
    {
        await Task.Delay(1500);
        var dialog = new TranslateSettingsWindow();
        var saved = await dialog.ShowDialog<bool>(this);
        if (saved)
            vm.ReloadSettings();
    }

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
                    or nameof(MainWindowViewModel.IsTypesetActive)
                    or nameof(MainWindowViewModel.IsExportActive))
                    UpdateMenuStyles();
            };

            // Alat uji & screenshot: TINTA_OPEN=<jalur> membuka bab otomatis,
            // TINTA_VIEW=library|viewer|detect|clean|translate|typeset|export memilih halaman awal.
            // TINTA_SETTINGS=1 (v0.7.11): buka dialog Pengaturan Key &
            // Model otomatis — untuk screenshot Instal Instan/Hapus Bersih.
            if (Environment.GetEnvironmentVariable("TINTA_SETTINGS") == "1")
            {
                // Tunda sampai jendela benar-benar tampil & pemilik
                // dialog siap (Post langsung di OnOpened terlalu dini).
                _ = OpenSettingsDialogDelayedAsync(vm);
            }
            var autoOpen = Environment.GetEnvironmentVariable("TINTA_OPEN");
            if (!string.IsNullOrWhiteSpace(autoOpen))
                await vm.OpenChapterPathAsync(autoOpen);
            switch (Environment.GetEnvironmentVariable("TINTA_VIEW"))
            {
                case "detect": vm.ShowDetect(); break;
                case "clean": vm.ShowClean(); break;
                case "translate": vm.ShowTranslate(); break;
                case "typeset": vm.ShowTypeset(); break;
                case "export": vm.ShowExport(); break;
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
        SetActive(MenuExport, ViewModel?.IsExportActive ?? false);
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
