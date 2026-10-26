using Avalonia.Media.Imaging;
using TintaStudio.Services;

namespace TintaStudio.ViewModels;

/// <summary>Satu halaman pada daftar miniatur Penampil.</summary>
public sealed class PageItemViewModel
{
    public PageItemViewModel(ComicPage page)
    {
        Page = page;
        try
        {
            using var stream = File.OpenRead(page.WorkPath);
            Thumbnail = Bitmap.DecodeToWidth(stream, 150);
        }
        catch (Exception)
        {
            Thumbnail = null;
        }
    }

    public ComicPage Page { get; }
    public Bitmap? Thumbnail { get; }
    public string Label => $"{Page.Index + 1}";
    public string Detail => $"{Page.Width}×{Page.Height}";
}
