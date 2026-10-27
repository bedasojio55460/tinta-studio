using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace TintaStudio.Views;

/// <summary>Dialog pemilih berkas/folder komik (dipakai Pustaka &amp; Penampil).</summary>
public static class FilePickers
{
    public static async Task<string?> PickComicFileAsync(TopLevel top)
    {
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
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public static async Task<string?> PickComicFolderAsync(TopLevel top)
    {
        var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Buka Folder Komik",
            AllowMultiple = false,
        });
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    /// <summary>Pemilih folder generik (mis. folder tujuan ekspor).</summary>
    public static async Task<string?> PickFolderAsync(TopLevel top, string title)
    {
        var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
        });
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }
}
