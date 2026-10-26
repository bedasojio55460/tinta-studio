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
        // Mode CLI: --ocr <berkas/folder komik> [--lang ja|ko|ch] —
        // deteksi + OCR semua halaman, hasil disimpan ke proyek bab.
        if (args.Length >= 2 && args[0] == "--ocr")
        {
            Environment.ExitCode = RunOcr(args);
            return;
        }
        // Mode CLI: --download-models — unduh semua model Tahap 2.
        if (args.Length >= 1 && args[0] == "--download-models")
        {
            Environment.ExitCode = RunDownloadModels();
            return;
        }
        if (args.Length >= 1 && (args[0] == "--help" || args[0] == "-h"))
        {
            Console.WriteLine("Tinta Studio — penerjemah & penyunting komik");
            Console.WriteLine("Pemakaian:");
            Console.WriteLine("  TintaStudio                              buka antarmuka grafis");
            Console.WriteLine("  TintaStudio --info <path>                tampilkan info bab komik (format, halaman)");
            Console.WriteLine("  TintaStudio --ocr <path> [--lang ja|ko|ch]  deteksi + OCR, simpan ke proyek");
            Console.WriteLine("  TintaStudio --download-models            unduh model deteksi/OCR ke cache");
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

    private static int RunOcr(string[] args)
    {
        string path = args[1];
        var lang = Services.OcrLanguage.Japanese;
        for (int i = 2; i + 1 < args.Length; i++)
        {
            if (args[i] == "--lang")
            {
                try
                {
                    lang = Services.OcrLanguage.FromCode(args[i + 1]);
                }
                catch (ArgumentException ex)
                {
                    Console.Error.WriteLine($"Error: {ex.Message}");
                    return 2;
                }
            }
        }
        var workDir = Path.Combine(
            Path.GetTempPath(), "tinta-studio-ocr-" + Guid.NewGuid().ToString("N"));
        try
        {
            var chapter = ComicImport.OpenChapter(path, workDir);
            using var pipeline = new DetectOcrPipeline();
            var progress = new Progress<(int Done, int Total)>(p =>
                Console.Error.WriteLine($"  halaman {p.Done}/{p.Total}…"));
            var (project, projectChapter) = ProjectStore.GetOrCreate(chapter);
            pipeline.AnalyzeChapterAsync(
                chapter, projectChapter, lang, progress,
                CancellationToken.None).GetAwaiter().GetResult();
            ProjectStore.Save(project, chapter.SourcePath);
            Console.WriteLine(
                $"OCR selesai: {chapter.Title} — bahasa {lang.Code}, " +
                $"{chapter.Pages.Count} halaman");
            foreach (var page in projectChapter.Pages)
            {
                Console.WriteLine($"Halaman {page.Index + 1}: {page.Blocks.Count} blok");
                foreach (var b in page.Blocks)
                {
                    var dir = b.Direction == Models.TextDirection.Vertical
                        ? "vertikal" : "horizontal";
                    var text = b.Text.Replace("\n", " / ");
                    Console.WriteLine(
                        $"  [{b.X},{b.Y} {b.Width}x{b.Height}] {dir} " +
                        $"det={b.DetectionScore:F2} ocr={b.Confidence:F2} \"{text}\"");
                }
            }
            return 0;
        }
        catch (ModelMissingException ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 3;
        }
        catch (ComicImportException ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            if (Environment.GetEnvironmentVariable("TINTA_DEBUG") == "1")
                Console.Error.WriteLine(ex.ToString());
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

    private static int RunDownloadModels()
    {
        try
        {
            foreach (var entry in ModelStore.Catalog)
            {
                if (ModelStore.IsDownloaded(entry))
                {
                    Console.WriteLine($"Sudah ada : {entry.DisplayName}");
                    continue;
                }
                Console.WriteLine($"Mengunduh : {entry.DisplayName} …");
                var progress = new Progress<double>(p =>
                    Console.Error.Write($"\r  {p * 100:F0}%"));
                ModelStore.DownloadAsync(entry, progress, CancellationToken.None)
                    .GetAwaiter().GetResult();
                Console.Error.WriteLine();
                Console.WriteLine($"Selesai   : {entry.DisplayName}");
            }
            Console.WriteLine($"Model tersimpan di: {ModelStore.CacheDir}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
