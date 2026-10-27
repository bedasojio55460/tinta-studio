using Avalonia.Controls;
using TintaStudio.ViewModels;

namespace TintaStudio.Views;

/// <summary>
/// Halaman Terjemahan (Tahap 4): toolbar penyedia/bahasa, dialog
/// Pengaturan Key &amp; Model, dan daftar blok dua kolom
/// (sumber → terjemahan). Semua logika ada di ViewModel.
/// </summary>
public partial class TranslateView : UserControl
{
    public TranslateView()
    {
        InitializeComponent();
        TranslatePageButton.Click += async (_, _) =>
        {
            if (DataContext is MainWindowViewModel vm)
                await vm.TranslateCurrentPageAsync();
        };
        TranslateChapterButton.Click += async (_, _) =>
        {
            if (DataContext is MainWindowViewModel vm)
                await vm.TranslateChapterAsync();
        };
        CancelTranslateButton.Click += (_, _) =>
            (DataContext as MainWindowViewModel)?.CancelTranslate();
        PolishPageButton.Click += async (_, _) =>
        {
            if (DataContext is MainWindowViewModel vm)
                await vm.PolishCurrentPageAsync();
        };
        PolishChapterButton.Click += async (_, _) =>
        {
            if (DataContext is MainWindowViewModel vm)
                await vm.PolishChapterAsync();
        };
        TranslateSettingsButton.Click += async (_, _) =>
        {
            if (DataContext is not MainWindowViewModel vm)
                return;
            var owner = TopLevel.GetTopLevel(this) as Window;
            if (owner is null)
                return;
            var dialog = new TranslateSettingsWindow();
            var saved = await dialog.ShowDialog<bool>(owner);
            if (saved)
                vm.ReloadSettings();
        };
    }
}
