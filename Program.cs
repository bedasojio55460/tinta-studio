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
        // Mode CLI: --translate <berkas/folder komik> [--provider <id>]
        // [--target id|en] — terjemahkan blok OCR tersimpan (deteksi
        // dulu bila belum ada blok); proyek disimpan sesudah SETIAP
        // halaman; blok yang sudah berterjemahan dilewati.
        if (args.Length >= 2 && args[0] == "--translate")
        {
            Environment.ExitCode = RunTranslate(args);
            return;
        }
        // Mode CLI: --render <berkas/folder komik> [--out <folder>] —
        // hasil akhir per halaman: halaman bersih (operasi Tahap 3,
        // atau asli bila belum dibersihkan) + teks ter-typeset
        // digambar; typeset bawaan diterapkan dulu ke blok
        // berterjemahan yang belum di-typeset, lalu disimpan.
        if (args.Length >= 2 && args[0] == "--render")
        {
            Environment.ExitCode = RunRender(args);
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
            Console.WriteLine("  TintaStudio --translate <path> [--provider gemini|groq|openrouter|ollama|lmstudio] [--target id|en]");
            Console.WriteLine("                                           terjemahkan blok OCR (key dari Pengaturan aplikasi)");
            Console.WriteLine("  TintaStudio --render <path> [--out <folder>]");
            Console.WriteLine("                                           gambar halaman final (bersih + typeset) ke folder");
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

    private static int RunTranslate(string[] args)
    {
        string path = args[1];
        string? providerId = null;
        string? target = null;
        for (int i = 2; i + 1 < args.Length; i++)
        {
            switch (args[i])
            {
                case "--provider": providerId = args[i + 1]; break;
                case "--target": target = args[i + 1]; break;
            }
        }

        var settings = AppConfig.LoadSettings();
        ProviderInfo provider;
        TargetLanguage targetLang;
        try
        {
            provider = TranslateService.ProviderById(providerId ?? settings.Provider);
            targetLang = TranslateService.TargetByCode(target ?? settings.TargetLanguage);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 2;
        }
        var ps = settings.For(provider.Id);
        if (provider.RequiresKey && AppSecrets.KeyCount(provider.Id) == 0)
        {
            Console.Error.WriteLine(
                $"Error: API key {provider.DisplayName} belum diisi. Isi dulu " +
                "lewat dialog Pengaturan Key & Model di aplikasi (halaman " +
                "Terjemahan) — key tidak pernah dibaca dari argumen/lingkungan.");
            return 2;
        }
        var options = new TranslateOptions(provider, ps.BaseUrl, ps.Model,
            targetLang.Code, TimeSpan.FromSeconds(settings.RequestDelaySeconds));

        var workDir = Path.Combine(
            Path.GetTempPath(), "tinta-studio-translate-" + Guid.NewGuid().ToString("N"));
        try
        {
            var chapter = ComicImport.OpenChapter(path, workDir);
            var (project, projectChapter) = ProjectStore.GetOrCreate(chapter);

            if (projectChapter.Pages.Sum(p => p.Blocks.Count) == 0)
            {
                using var pipeline = new DetectOcrPipeline();
                var ocrProgress = new Progress<(int Done, int Total)>(p =>
                    Console.Error.WriteLine($"  deteksi halaman {p.Done}/{p.Total}…"));
                pipeline.AnalyzeChapterAsync(
                    chapter, projectChapter, OcrLanguage.Japanese, ocrProgress,
                    CancellationToken.None).GetAwaiter().GetResult();
                ProjectStore.Save(project, chapter.SourcePath);
            }

            var service = new TranslateService { WaitForKeyRecovery = true };
            int lastWaitSecond = -1;
            service.WaitCallback = (remaining, keyNumber) =>
            {
                int s = (int)Math.Ceiling(remaining.TotalSeconds);
                if (s != lastWaitSecond)
                {
                    lastWaitSecond = s;
                    Console.WriteLine(
                        $"  Menunggu limit pulih… ±{s} dtk — key #{keyNumber}");
                }
            };
            Console.WriteLine(
                $"Menerjemahkan: {chapter.Title} — {provider.DisplayName} " +
                $"({(string.IsNullOrWhiteSpace(ps.Model) ? "model bawaan server" : ps.Model)}), " +
                $"tujuan {targetLang.DisplayName}");
            var progress = new Progress<(int Done, int Total)>(p =>
                Console.Error.WriteLine($"  halaman {p.Done}/{p.Total} selesai"));
            ChapterTranslationSummary summary;
            try
            {
                summary = service.TranslateChapterAsync(
                    projectChapter, options, progress,
                    _ => ProjectStore.Save(project, chapter.SourcePath),
                    CancellationToken.None).GetAwaiter().GetResult();
            }
            catch (AllKeysExhaustedException)
            {
                // Halaman yang selesai sudah disimpan per halaman;
                // ringkasan dicetak dari keadaan proyek saat ini.
                summary = CountSummary(projectChapter);
                Console.Error.WriteLine(
                    "Berhenti di tengah bab: semua key kena limit/tidak sah. " +
                    "Halaman yang sudah jadi tetap tersimpan.");
            }
            foreach (var page in projectChapter.Pages.OrderBy(p => p.Index))
            {
                int ok = page.Blocks.Count(b => !string.IsNullOrWhiteSpace(b.Translation));
                int fail = page.Blocks.Count(b => b.TranslationError is not null);
                Console.WriteLine(
                    $"Halaman {page.Index + 1}: {ok} blok diterjemahkan, {fail} gagal");
            }
            Console.WriteLine(
                $"Terjemahan selesai: {summary.Translated} blok diterjemahkan, " +
                $"{summary.Failed} gagal, {summary.Skipped} dilewati (sudah ada " +
                $"terjemahan), {summary.PagesProcessed} halaman.");
            return summary.Failed > 0 && summary.Translated == 0 ? 1 : 0;
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

    private static int RunRender(string[] args)
    {
        string path = args[1];
        string? outDir = null;
        for (int i = 2; i + 1 < args.Length; i++)
        {
            if (args[i] == "--out")
                outDir = args[i + 1];
        }
        outDir ??= Path.Combine(Directory.GetCurrentDirectory(), "tinta-final");
        var workDir = Path.Combine(
            Path.GetTempPath(), "tinta-studio-render-" + Guid.NewGuid().ToString("N"));
        try
        {
            var chapter = ComicImport.OpenChapter(path, workDir);
            var (project, projectChapter) = ProjectStore.GetOrCreate(chapter);

            // Proyek belum punya blok sama sekali → deteksi+OCR dulu
            // (bahasa ja) agar render berdiri sendiri seperti --clean.
            if (projectChapter.Pages.Sum(p => p.Blocks.Count) == 0)
            {
                using var pipeline = new DetectOcrPipeline();
                var progress = new Progress<(int Done, int Total)>(p =>
                    Console.Error.WriteLine($"  deteksi halaman {p.Done}/{p.Total}…"));
                pipeline.AnalyzeChapterAsync(
                    chapter, projectChapter, OcrLanguage.Japanese, progress,
                    CancellationToken.None).GetAwaiter().GetResult();
            }

            int autoApplied = TypesetService.AutoTypesetChapter(projectChapter);
            ProjectStore.Save(project, chapter.SourcePath);

            using var clean = new CleanService();
            Directory.CreateDirectory(outDir);
            int totalTypeset = 0;
            for (int i = 0; i < chapter.Pages.Count; i++)
            {
                var page = projectChapter.PageAt(i);
                int drawn = page.Blocks.Count(
                    b => !string.IsNullOrWhiteSpace(b.Translation));
                string outFile = Path.Combine(outDir, $"page-{i + 1:D3}.png");
                using var final = TypesetService.BuildFinalPageBitmap(
                    chapter, projectChapter, page, clean, CancellationToken.None);
                using (var img = SkiaSharp.SKImage.FromBitmap(final))
                using (var data = img.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100))
                using (var fs = File.Create(outFile))
                    data.SaveTo(fs);
                totalTypeset += drawn;
                Console.WriteLine(
                    $"Halaman {i + 1}: {drawn} blok digambar " +
                    $"({page.CleanOps.Count} operasi bersih) → {outFile}");
            }
            Console.WriteLine(
                $"Render selesai: {chapter.Title} — {totalTypeset} blok " +
                $"tergambar, {autoApplied} blok baru di-typeset otomatis, " +
                $"keluaran di {outDir}");
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

    private static ChapterTranslationSummary CountSummary(
        Models.ProjectChapter chapter)
    {
        int translated = 0, failed = 0, skipped = 0;
        foreach (var page in chapter.Pages)
        {
            translated += page.Blocks.Count(
                b => !string.IsNullOrWhiteSpace(b.Translation));
            failed += page.Blocks.Count(b => b.TranslationError is not null);
            skipped += page.Blocks.Count(
                b => !string.IsNullOrWhiteSpace(b.Translation));
        }
        return new ChapterTranslationSummary(
            chapter.Pages.Count, translated, failed, skipped);
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
