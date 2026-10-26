using Avalonia.Controls;
using Avalonia.Platform.Storage;
using TintaStudio.ViewModels;

namespace TintaStudio.Views;

public partial class LibraryView : UserControl
{
    public LibraryView()
    {
        InitializeComponent();
        OpenFileButton.Click += async (_, _) => await PickFileAsync();
        OpenFolderButton.Click += async (_, _) => await PickFolderAsync();
        OpenInViewerButton.Click += async (_, _) =>
        {
            if (ViewModel is { } vm) await vm.OpenFromLibraryAsync(vm.SelectedLibraryEntry);
        };
        RemoveButton.Click += (_, _) => ViewModel?.RemoveFromLibrary(ViewModel.SelectedLibraryEntry);
        ChapterList.DoubleTapped += async (_, _) =>
        {
            if (ViewModel is { } vm) await vm.OpenFromLibraryAsync(vm.SelectedLibraryEntry);
        };
    }

    private MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;

    private async Task PickFileAsync()
    {
        var top = TopLevel.GetTopLevel(this);
        if (top is null || ViewModel is not { } vm) return;
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Buka Berkas Komik",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Komik & gambar")
                {
                    Patterns = new[]
                    {
                        "*.cbz", "*.cbr", "*.cb7", "*.cbt", "*.zip", "*.rar", "*.7z", "*.tar",
                        "*.pdf", "*.epub", "*.png", "*.jpg", "*.jpeg", "*.webp", "*.gif",
                        "*.bmp", "*.tif", "*.tiff", "*.avif",
                    },
                },
                new FilePickerFileType("Semua berkas") { Patterns = new[] { "*" } },
            },
        });
        var path = files.Count > 0 ? files[0].TryGetLocalPath() : null;
        if (path is not null) await vm.OpenChapterPathAsync(path);
    }

    private async Task PickFolderAsync()
    {
        var top = TopLevel.GetTopLevel(this);
        if (top is null || ViewModel is not { } vm) return;
        var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Buka Folder Komik",
            AllowMultiple = false,
        });
        var path = folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
        if (path is not null) await vm.OpenChapterPathAsync(path);
    }
}
