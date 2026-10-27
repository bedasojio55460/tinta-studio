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
        // Mode CLI: --clean <berkas/folder komik> [--mode solid|inpaint]
        // [--out <folder>] [--lang ja|ko|ch] — bersihkan bubble semua
        // halaman (deteksi dulu bila proyek belum punya blok), tulis
        // gambar bersih ke folder keluar; operasi tersimpan di proyek.
        if (args.Length >= 2 && args[0] == "--clean")
        {
            Environment.ExitCode = RunClean(args);
            return;
        }
        // Mode CLI: --download-models — unduh semua model katalog.
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
            Console.WriteLine("  TintaStudio --clean <path> [--mode solid|inpaint] [--out <folder>] [--lang ja|ko|ch]");
            Console.WriteLine("                                           bersihkan bubble, tulis gambar bersih");
            Console.WriteLine("  TintaStudio --download-models            unduh model deteksi/OCR/inpaint ke cache");
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

    private static int RunClean(string[] args)
    {
        string path = args[1];
        var mode = Models.CleanMode.Solid;
        var lang = Services.OcrLanguage.Japanese;
        string? outDir = null;
        for (int i = 2; i + 1 < args.Length; i++)
        {
            switch (args[i])
            {
                case "--mode":
                    mode = args[i + 1] switch
                    {
                        "solid" => Models.CleanMode.Solid,
                        "inpaint" => Models.CleanMode.Inpaint,
                        var other => throw new ArgumentException(
                            $"Mode tidak dikenal: '{other}' (pakai solid|inpaint)."),
                    };
                    break;
                case "--lang":
                    try
                    {
                        lang = Services.OcrLanguage.FromCode(args[i + 1]);
                    }
                    catch (ArgumentException ex)
                    {
                        Console.Error.WriteLine($"Error: {ex.Message}");
                        return 2;
                    }
                    break;
                case "--out":
                    outDir = args[i + 1];
                    break;
            }
        }
        outDir ??= Path.Combine(Directory.GetCurrentDirectory(), "tinta-cleaned");
        var workDir = Path.Combine(
            Path.GetTempPath(), "tinta-studio-clean-" + Guid.NewGuid().ToString("N"));
        try
        {
            var chapter = ComicImport.OpenChapter(path, workDir);
            var (project, projectChapter) = ProjectStore.GetOrCreate(chapter);

            // Proyek belum punya blok sama sekali → deteksi+OCR dulu
            // (bahasa --lang) untuk semua halaman.
            if (projectChapter.Pages.Sum(p => p.Blocks.Count) == 0)
            {
                using var pipeline = new DetectOcrPipeline();
                var progress = new Progress<(int Done, int Total)>(p =>
                    Console.Error.WriteLine($"  deteksi halaman {p.Done}/{p.Total}…"));
                pipeline.AnalyzeChapterAsync(
                    chapter, projectChapter, lang, progress,
                    CancellationToken.None).GetAwaiter().GetResult();
            }

            if (mode == Models.CleanMode.Inpaint && !ModelStore.InpaintReady)
                throw new ModelMissingException(
                    new[] { ModelStore.InpaintEntry });

            // Catat operasi untuk semua blok, bangun gambar bersih.
            using var clean = new CleanService();
            Directory.CreateDirectory(outDir);
            int totalCleaned = 0;
            for (int i = 0; i < chapter.Pages.Count; i++)
            {
                var page = projectChapter.PageAt(i);
                foreach (var block in page.Blocks)
                    CleanService.SetBlockOperation(page, block, mode, fillColor: null);
                var info = chapter.Pages[i];
                string outFile = Path.Combine(outDir, $"page-{i + 1:D3}.png");
                if (page.Blocks.Count == 0)
                {
                    File.Copy(info.WorkPath, outFile, overwrite: true);
                    Console.WriteLine(
                        $"Halaman {i + 1}: 0 blok — disalin apa adanya → {outFile}");
                    continue;
                }
                using var original = SkiaSharp.SKBitmap.Decode(info.WorkPath)
                    ?? throw new InvalidDataException(
                        $"Halaman kerja tidak terbaca: {info.SourceName}");
                using var cleaned = clean.BuildCleanedPage(
                    original, page, CancellationToken.None);
                using (var img = SkiaSharp.SKImage.FromBitmap(cleaned))
                using (var data = img.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100))
                using (var fs = File.Create(outFile))
                    data.SaveTo(fs);
                totalCleaned += page.Blocks.Count;
                Console.WriteLine(
                    $"Halaman {i + 1}: {page.Blocks.Count} blok dibersihkan " +
                    $"({mode.ToString().ToLowerInvariant()}) → {outFile}");
            }
            ProjectStore.Save(project, chapter.SourcePath);
            Console.WriteLine(
                $"Bersih selesai: {chapter.Title} — {totalCleaned} blok, " +
                $"mode {mode.ToString().ToLowerInvariant()}, keluaran di {outDir}");
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
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 2;
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
