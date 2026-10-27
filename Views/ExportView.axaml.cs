using Avalonia.Controls;
using TintaStudio.ViewModels;

namespace TintaStudio.Views;

/// <summary>
/// Halaman Ekspor (Tahap 6): pilih bentuk keluaran (CBZ/PDF/Folder),
/// format gambar, kualitas, dan folder tujuan, lalu ekspor bab
/// terbuka menjadi halaman final lewat ExportService.
/// </summary>
public partial class ExportView : UserControl
{
    public ExportView()
    {
        InitializeComponent();
        ExportButton.Click += async (_, _) =>
        {
            if (DataContext is MainWindowViewModel vm)
                await vm.ExportCurrentChapterAsync();
        };
        PickFolderButton.Click += async (_, _) =>
        {
            var top = TopLevel.GetTopLevel(this);
            if (top is null)
                return;
            var path = await FilePickers.PickFolderAsync(top, "Pilih Folder Tujuan Ekspor");
            if (path is not null && DataContext is MainWindowViewModel vm)
                vm.ExportFolderPath = path;
        };
    }
}
