using System.Text;
using Avalonia;
using TintaStudio.Services;

namespace TintaStudio;

internal static class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things
    // aren't initialized yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        // Mode CLI: --info <berkas/folder komik> — tanpa membuka UI.
        if (args.Length >= 2 && args[0] == "--info")
        {
            Environment.ExitCode = RunInfo(args[1]);
            return;
        }
        if (args.Length >= 1 && (args[0] == "--help" || args[0] == "-h"))
        {
            Console.WriteLine("Tinta Studio — penerjemah & penyunting komik");
            Console.WriteLine("Pemakaian:");
            Console.WriteLine("  TintaStudio                 buka antarmuka grafis");
            Console.WriteLine("  TintaStudio --info <path>   tampilkan info bab komik (format, halaman)");
            return;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    private static int RunInfo(string path)
    {
        var workDir = Path.Combine(
            Path.GetTempPath(), "tinta-studio-info-" + Guid.NewGuid().ToString("N"));
        try
        {
            var chapter = ComicImport.OpenChapter(path, workDir);
            Console.WriteLine($"Format    : {chapter.Format}");
            Console.WriteLine($"Judul     : {chapter.Title}");
            if (chapter.Series is not null)
                Console.WriteLine($"Seri      : {chapter.Series}");
            Console.WriteLine($"Sumber    : {chapter.SourcePath}");
            Console.WriteLine($"Halaman   : {chapter.Pages.Count}");
            Console.WriteLine($"Terlewati : {chapter.Skipped.Count}");
            Console.WriteLine("Halaman:");
            foreach (var page in chapter.Pages)
                Console.WriteLine(
                    $"  {page.Index + 1,3}. {page.SourceName} ({page.SourceFormat}, {page.Width}x{page.Height})");
            if (chapter.Skipped.Count > 0)
            {
                Console.WriteLine("Terlewati:");
                foreach (var s in chapter.Skipped)
                    Console.WriteLine($"  - {s.Name} — {s.Reason}");
            }
            return 0;
        }
        catch (ComicImportException ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
        finally
        {
            try
            {
                if (Directory.Exists(workDir)) Directory.Delete(workDir, recursive: true);
            }
            catch (Exception)
            {
                // berkas sementara gagal dihapus bukan masalah besar
            }
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
