using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using TintaStudio.Services;
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

    /// <summary>Penawaran lokasi data sekali-saja saat jalan
    /// pertama (v0.7.13): tampil hanya bila FirstRunOffer
    /// membolehkan. "Pilih Folder…" menulis penanda lokasi lewat
    /// mekanisme yang sama seperti seksi Lokasi Data &amp; Model
    /// (pada titik ini belum ada isi berarti untuk dipindah —
    /// cukup penanda + muat ulang pengaturan); batal memilih
    /// folder mengembalikan penawaran; pilihan lain (termasuk
    /// menutup jendela) mencatat flag "sudah ditawari".</summary>
    private async Task ShowFirstRunOfferAsync(MainWindowViewModel vm)
    {
        while (true)
        {
            var choice = await new FirstRunOfferWindow()
                .ShowDialog<FirstRunChoice>(this);
            if (choice == FirstRunChoice.PilihFolder)
            {
                var folder = await FilePickers.PickFolderAsync(
                    this, "Pilih Folder untuk Data & Model Tinta Studio");
                if (folder is null)
                    continue; // belum ada keputusan — tawarkan lagi
                var error = DataMover.ValidateDestination(folder, new[]
                {
                    DataLocation.EffectiveConfigDir,
                    DataLocation.EffectiveCacheRootDir,
                });
                if (error is not null)
                    continue; // folder tak sah — tawarkan lagi
                FirstRunOffer.MarkOffered();
                DataLocation.WriteMarker(folder);
                vm.ReloadSettings();
            }
            else
            {
                FirstRunOffer.MarkOffered();
            }
            break;
        }
    }

    /// <summary>Dialog konfirmasi deteksi ulang (v0.7.17): pola
    /// jendela konfirmasi yang sama seperti dialog lain di
    /// aplikasi (dibangun dari kode, Close(bool) sebagai hasil).
    /// true = pengguna memilih "Lanjutkan".</summary>
    private async Task<bool> ShowRedetectConfirmAsync(string message)
    {
        var confirm = new Window
        {
            Title = "Deteksi Ulang",
            Width = 520,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(Color.Parse("#101216")),
        };
        var panel = new StackPanel { Spacing = 10, Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock
        {
            Text = "Deteksi ulang halaman/bab ini?",
            FontWeight = FontWeight.SemiBold,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
        });
        panel.Children.Add(new TextBlock
        {
            Text = message,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
        });
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        var cancel = new Button { Content = "Batal" };
        cancel.Click += (_, _) => confirm.Close(false);
        var yes = new Button { Content = "Lanjutkan" };
        yes.Click += (_, _) => confirm.Close(true);
        row.Children.Add(cancel);
        row.Children.Add(yes);
        panel.Children.Add(row);
        confirm.Content = panel;
        return await confirm.ShowDialog<bool>(this);
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        UpdateMenuStyles();
        if (ViewModel is { } vm)
        {
            vm.ConfirmRedetectAsync = ShowRedetectConfirmAsync;
            // Penawaran lokasi jalan pertama (v0.7.13) — sebelum
            // alat uji lain agar dialog sambutan selalu didahulukan.
            if (await Task.Run(() => FirstRunOffer.ShouldOfferNow()))
                await ShowFirstRunOfferAsync(vm);

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
