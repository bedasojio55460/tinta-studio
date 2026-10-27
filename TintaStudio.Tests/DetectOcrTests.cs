using System.Diagnostics;
using SkiaSharp;
using TintaStudio.Models;
using TintaStudio.Services;
using Xunit;
using Xunit.Abstractions;

namespace TintaStudio.Tests;

/// <summary>
/// Tes Tahap 2 — deteksi teks + OCR dengan model ASLI pada halaman
/// manga sintetis berteks diketahui. Tes yang butuh model di-skip
/// secara jujur bila model belum ada di cache
/// (~/.cache/tinta-studio/models — unduh: tools/fetch_models.sh).
/// </summary>
public sealed class DetectOcrTests
{
    private readonly ITestOutputHelper _out;

    public DetectOcrTests(ITestOutputHelper output) => _out = output;

    private static void RequireModels(OcrLanguage lang)
    {
        var missing = ModelStore.MissingFor(lang);
        Skip.If(missing.Count > 0,
            "Model belum diunduh: " + string.Join(", ", missing.Select(m => m.Id)));
    }

    private static TextBlock? BlockNear(IEnumerable<TextBlock> blocks, SKRectI rect)
    {
        var inflated = SKRectI.Inflate(rect, 40, 40);
        return blocks.FirstOrDefault(b =>
            inflated.Contains((int)(b.X + b.Width / 2), (int)(b.Y + b.Height / 2)));
    }

    [SkippableFact]
    public void Pipeline_JapanesePage_DetectsAndReadsBlocks()
    {
        RequireModels(OcrLanguage.Japanese);
        using var page = MangaFixture.JapanesePage();
        using var pipeline = new DetectOcrPipeline();
        var blocks = pipeline.AnalyzePage(page, OcrLanguage.Japanese);
        foreach (var b in blocks)
            _out.WriteLine($"[{b.X},{b.Y} {b.Width}x{b.Height}] {b.Direction} " +
                           $"ocr={b.Confidence:F2} \"{b.Text}\"");

        Assert.True(blocks.Count >= 2,
            $"Diharapkan ≥2 blok, dapat {blocks.Count}.");

        var horiz = BlockNear(blocks, MangaFixture.BubbleJaH);
        Assert.NotNull(horiz);
        var simH = MangaFixture.Similarity(horiz!.Text, "こんにちは");
        _out.WriteLine($"JA horizontal: \"{horiz.Text}\" kemiripan={simH:F2}");
        Assert.True(simH >= 0.7,
            $"OCR horizontal \"{horiz.Text}\" terlalu jauh dari \"こんにちは\" ({simH:F2}).");

        var vert = BlockNear(blocks, MangaFixture.BubbleJaV);
        Assert.NotNull(vert);
        Assert.Equal(TextDirection.Vertical, vert!.Direction);
        var simV = MangaFixture.Similarity(vert.Text.Replace("\n", ""), "ありがとう");
        _out.WriteLine($"JA vertikal: \"{vert.Text}\" kemiripan={simV:F2}");
        Assert.True(simV >= 0.7,
            $"OCR vertikal \"{vert.Text}\" terlalu jauh dari \"ありがとう\" ({simV:F2}).");
    }

    [SkippableFact]
    public void Pipeline_KoreanPage_ReadsHangul()
    {
        RequireModels(OcrLanguage.Korean);
        using var page = MangaFixture.KoreanPage();
        using var pipeline = new DetectOcrPipeline();
        var blocks = pipeline.AnalyzePage(page, OcrLanguage.Korean);
        foreach (var b in blocks)
            _out.WriteLine($"[{b.X},{b.Y}] ocr={b.Confidence:F2} \"{b.Text}\"");
        var block = BlockNear(blocks, MangaFixture.BubbleKo);
        Assert.NotNull(block);
        var sim = MangaFixture.Similarity(block!.Text, "안녕하세요");
        _out.WriteLine($"KO: \"{block.Text}\" kemiripan={sim:F2}");
        Assert.True(sim >= 0.7,
            $"OCR Korea \"{block.Text}\" terlalu jauh dari \"안녕하세요\" ({sim:F2}).");
    }

    [SkippableFact]
    public void Pipeline_ChinesePage_ReadsHanzi()
    {
        RequireModels(OcrLanguage.Chinese);
        using var page = MangaFixture.ChinesePage();
        using var pipeline = new DetectOcrPipeline();
        var blocks = pipeline.AnalyzePage(page, OcrLanguage.Chinese);
        foreach (var b in blocks)
            _out.WriteLine($"[{b.X},{b.Y}] ocr={b.Confidence:F2} \"{b.Text}\"");
        var block = BlockNear(blocks, MangaFixture.BubbleCh);
        Assert.NotNull(block);
        var sim = MangaFixture.Similarity(block!.Text, "谢谢你");
        _out.WriteLine($"CH: \"{block.Text}\" kemiripan={sim:F2}");
        Assert.True(sim >= 0.7,
            $"OCR Cina \"{block.Text}\" terlalu jauh dari \"谢谢你\" ({sim:F2}).");
    }

    [SkippableFact]
    public void Pipeline_EnglishPage_ReadsLatin()
    {
        RequireModels(OcrLanguage.English);
        using var page = MangaFixture.EnglishPage();
        using var pipeline = new DetectOcrPipeline();
        var blocks = pipeline.AnalyzePage(page, OcrLanguage.English);
        foreach (var b in blocks)
            _out.WriteLine($"[{b.X},{b.Y}] {b.Direction} ocr={b.Confidence:F2} \"{b.Text}\"");
        var block = BlockNear(blocks, MangaFixture.BubbleEn);
        Assert.NotNull(block);
        Assert.Equal(TextDirection.Horizontal, block!.Direction);
        var sim = MangaFixture.Similarity(block.Text, "Hello world");
        _out.WriteLine($"EN: \"{block.Text}\" kemiripan={sim:F2}");
        Assert.True(sim >= 0.7,
            $"OCR Inggris \"{block.Text}\" terlalu jauh dari \"Hello world\" ({sim:F2}).");
    }

    private static void RequireAutoCandidates()
    {
        Skip.If(ModelStore.MissingFor(OcrLanguage.Auto).Count > 0,
            "Detektor belum diunduh.");
        Skip.If(ModelStore.InstalledLanguages().Count < 2,
            "Mode Otomatis butuh ≥2 model rec terpasang untuk tes ini.");
    }

    [SkippableFact]
    public void Pipeline_Auto_JapanesePage_GuessesJapanese()
    {
        RequireAutoCandidates();
        RequireModels(OcrLanguage.Japanese);
        using var page = MangaFixture.JapanesePage();
        using var pipeline = new DetectOcrPipeline();
        var blocks = pipeline.AnalyzePage(page, OcrLanguage.Auto);
        foreach (var b in blocks)
            _out.WriteLine($"[{b.X},{b.Y}] {b.Language} ocr={b.Confidence:F2} \"{b.Text}\"");
        Assert.True(pipeline.DetectedLanguages.TryGetValue(0, out var guessed),
            "Bahasa terdeteksi halaman tidak tercatat.");
        Assert.Equal("ja", guessed!.Code);
        var horiz = BlockNear(blocks, MangaFixture.BubbleJaH);
        Assert.NotNull(horiz);
        Assert.Equal("ja", horiz!.Language);
        Assert.True(MangaFixture.Similarity(horiz.Text, "こんにちは") >= 0.7,
            $"Blok horizontal terbaca \"{horiz.Text}\".");
        var vert = BlockNear(blocks, MangaFixture.BubbleJaV);
        Assert.NotNull(vert);
        Assert.Equal("ja", vert!.Language);
    }

    [SkippableFact]
    public void Pipeline_Auto_EnglishPage_GuessesEnglish()
    {
        RequireAutoCandidates();
        RequireModels(OcrLanguage.English);
        using var page = MangaFixture.EnglishPage();
        using var pipeline = new DetectOcrPipeline();
        var blocks = pipeline.AnalyzePage(page, OcrLanguage.Auto);
        foreach (var b in blocks)
            _out.WriteLine($"[{b.X},{b.Y}] {b.Language} ocr={b.Confidence:F2} \"{b.Text}\"");
        Assert.True(pipeline.DetectedLanguages.TryGetValue(0, out var guessed),
            "Bahasa terdeteksi halaman tidak tercatat.");
        Assert.Equal("en", guessed!.Code);
        var block = BlockNear(blocks, MangaFixture.BubbleEn);
        Assert.NotNull(block);
        Assert.Equal("en", block!.Language);
        Assert.True(MangaFixture.Similarity(block.Text, "Hello world") >= 0.7,
            $"Blok Inggris terbaca \"{block.Text}\".");
    }

    [SkippableFact]
    public void Pipeline_Auto_MixedPage_PerBlockLanguagesCorrect()
    {
        RequireAutoCandidates();
        RequireModels(OcrLanguage.Japanese);
        RequireModels(OcrLanguage.English);
        using var page = MangaFixture.MixedPage();
        using var pipeline = new DetectOcrPipeline();
        var blocks = pipeline.AnalyzePage(page, OcrLanguage.Auto);
        foreach (var b in blocks)
            _out.WriteLine($"[{b.X},{b.Y}] {b.Language} ocr={b.Confidence:F2} \"{b.Text}\"");
        var ja = BlockNear(blocks, MangaFixture.BubbleMixJa);
        Assert.NotNull(ja);
        Assert.Equal("ja", ja!.Language);
        Assert.True(MangaFixture.Similarity(ja.Text, "こんにちは") >= 0.7,
            $"Blok campur Jepang terbaca \"{ja.Text}\" ({ja.Language}).");
        var en = BlockNear(blocks, MangaFixture.BubbleMixEn);
        Assert.NotNull(en);
        Assert.Equal("en", en!.Language);
        Assert.True(MangaFixture.Similarity(en.Text, "Hello world") >= 0.7,
            $"Blok campur Inggris terbaca \"{en.Text}\" ({en.Language}).");
    }

    [Fact]
    public void LanguageGuess_ScriptBonus_RulesHold()
    {
        Assert.True(LanguageGuess.HasKana("こんにちは"));
        Assert.True(LanguageGuess.HasHangul("안녕하세요"));
        Assert.True(LanguageGuess.HasLatin("Hello world"));
        Assert.Equal(LanguageGuess.ScriptBonus,
            LanguageGuess.ScriptBonusFor(OcrLanguage.Japanese, "こんにちは"));
        Assert.Equal(LanguageGuess.ScriptBonus,
            LanguageGuess.ScriptBonusFor(OcrLanguage.Korean, "안녕하세요"));
        Assert.Equal(LanguageGuess.ScriptBonus,
            LanguageGuess.ScriptBonusFor(OcrLanguage.English, "Hello world"));
        // Han murni netral untuk semua; Latin tanpa spasi tanpa bonus.
        Assert.Equal(0, LanguageGuess.ScriptBonusFor(OcrLanguage.Chinese, "谢谢你"));
        Assert.Equal(0, LanguageGuess.ScriptBonusFor(OcrLanguage.Japanese, "謝謝"));
        Assert.Equal(0, LanguageGuess.ScriptBonusFor(OcrLanguage.English, "Hello"));
        Assert.Equal("auto", OcrLanguage.Auto.Code);
        Assert.Contains(OcrLanguage.Auto, OcrLanguage.All);
        Assert.Equal(OcrLanguage.Auto, OcrLanguage.FromCode("auto"));
        Assert.DoesNotContain(OcrLanguage.Auto, OcrLanguage.Concrete);
    }

    /// <summary>
    /// Jalankan CLI aplikasi sebagai proses anak dengan folder model
    /// panggung (TINTA_MODEL_DIR) — mengisolasi variabel lingkungan
    /// dari proses tes. Mengembalikan (exit, stdout, stderr).
    /// </summary>
    private (int Exit, string StdOut, string StdErr) RunCliWithModelDir(
        string modelDir, params string[] args)
    {
        var testBin = Path.GetDirectoryName(typeof(DetectOcrTests).Assembly.Location)!;
        var config = new DirectoryInfo(testBin).Parent?.Name ?? "Debug";
        var root = Path.GetFullPath(Path.Combine(testBin, "..", "..", "..", ".."));
        var appDll = Path.Combine(root, "bin", config, "net10.0", "TintaStudio.dll");
        Skip.If(!File.Exists(appDll), $"DLL aplikasi belum dibangun: {appDll}");
        var psi = new ProcessStartInfo("dotnet",
            string.Join(" ", new[] { $"\"{appDll}\"" }.Concat(args.Select(a => $"\"{a}\""))))
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.Environment["TINTA_MODEL_DIR"] = modelDir;
        psi.Environment["TINTA_CONFIG_DIR"] =
            Path.Combine(Path.GetTempPath(), "tinta-cli-auto-" + Guid.NewGuid().ToString("N"));
        using var proc = Process.Start(psi)!;
        var stdout = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit(300_000);
        return (proc.ExitCode, stdout, stderr);
    }

    private static void StageModelDir(string dir, params string[] entryIds)
    {
        Directory.CreateDirectory(dir);
        foreach (var id in entryIds)
            foreach (var f in ModelStore.Entry(id).Files)
            {
                var src = ModelStore.PathOf(f);
                Skip.If(!File.Exists(src), $"Model cache tidak ada: {src}");
                File.Copy(src, Path.Combine(dir, f.CacheName));
            }
    }

    [SkippableFact]
    public void Cli_Auto_SingleInstalledModel_UsesIt()
    {
        // Hanya detektor + rec-en terpasang di folder panggung:
        // mode Otomatis harus memakai Inggris tanpa tebakan.
        var dir = Path.Combine(Path.GetTempPath(),
            "tinta-auto-one-" + Guid.NewGuid().ToString("N"));
        var modelDir = Path.Combine(dir, "models");
        StageModelDir(modelDir, ModelStore.DetectorId, "rec-en");
        using var page = MangaFixture.EnglishPage();
        var cbz = MangaFixture.MakeCbz(page, dir);
        var (exit, stdout, stderr) = RunCliWithModelDir(
            modelDir, "--ocr", cbz, "--lang", "auto");
        _out.WriteLine(stdout);
        _out.WriteLine(stderr);
        Assert.Equal(0, exit);
        Assert.Contains("Bahasa terdeteksi: Inggris", stdout);
        Assert.Contains("Hello", stdout);
        Directory.Delete(dir, recursive: true);
    }

    [SkippableFact]
    public void Cli_Auto_NoModels_HonestError()
    {
        // Folder model kosong: galat jujur (exit 3) menyebut model
        // OCR yang perlu diunduh, bukan tebakan buta.
        var dir = Path.Combine(Path.GetTempPath(),
            "tinta-auto-none-" + Guid.NewGuid().ToString("N"));
        var modelDir = Path.Combine(dir, "models");
        StageModelDir(modelDir, ModelStore.DetectorId);
        using var page = MangaFixture.EnglishPage();
        var cbz = MangaFixture.MakeCbz(page, dir);
        var (exit, stdout, stderr) = RunCliWithModelDir(
            modelDir, "--ocr", cbz, "--lang", "auto");
        _out.WriteLine(stdout);
        _out.WriteLine(stderr);
        Assert.Equal(3, exit);
        Assert.Contains("Model belum diunduh", stderr + stdout);
        Assert.Contains("OCR", stderr + stdout);
        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void ModelCatalog_IncludesEnglishOcr()
    {
        var entry = ModelStore.Entry("rec-en");
        Assert.Equal("OCR Inggris", entry.DisplayName);
        Assert.Equal(2, entry.Files.Count);
        Assert.Contains(entry.Files, f => f.CacheName == "rec-en.onnx");
        Assert.Contains(entry.Files, f => f.CacheName == "rec-en.dict.txt");
        Assert.Equal("en", OcrLanguage.English.Code);
        Assert.Equal("Inggris", OcrLanguage.English.DisplayName);
        Assert.Contains(OcrLanguage.English, OcrLanguage.All);
        Assert.Equal(OcrLanguage.English, OcrLanguage.FromCode("en"));
        Assert.Contains(ModelStore.RequiredFor(OcrLanguage.English),
            e => e.Id == "rec-en");
    }

    [SkippableFact]
    public void OcrService_TwoLineBubble_JoinsBothLines()
    {
        RequireModels(OcrLanguage.Japanese);
        using var page = MangaFixture.TwoLinePage();
        var r = MangaFixture.BubbleTwoLines;
        var box = new DetectedBox(r.Left, r.Top, r.Width, r.Height,
            DetectedClass.TextBubble, 0.99);
        using var ocr = new OcrService();
        var result = ocr.RecognizeBlock(page, box, OcrLanguage.Japanese);
        _out.WriteLine($"Dua baris: \"{result.Text}\" conf={result.Confidence:F2}");
        var flat = result.Text.Replace("\n", "");
        Assert.Contains("こんにちは", flat);
        Assert.Contains("せかい", flat);
    }

    [Fact]
    public void SplitLineBands_TwoLines_TwoBands()
    {
        using var page = MangaFixture.TwoLinePage();
        var r = MangaFixture.BubbleTwoLines;
        using var crop = new SKBitmap(r.Width, r.Height);
        using (var canvas = new SKCanvas(crop))
            canvas.DrawBitmap(page, -r.Left, -r.Top);
        var bands = OcrService.SplitLineBands(crop);
        Assert.Equal(2, bands.Count);
    }

    [Fact]
    public void ReadingOrder_Horizontal_SortsTopThenLeft()
    {
        TextBlock B(int x, int y) => new()
        {
            X = x, Y = y, Width = 100, Height = 40,
            Direction = TextDirection.Horizontal,
        };
        var blocks = new List<TextBlock> { B(300, 500), B(100, 100), B(300, 100), B(100, 500) };
        var sorted = DetectOcrPipeline.SortReadingOrder(blocks, OcrLanguage.Korean);
        Assert.Equal((100, 100), (sorted[0].X, sorted[0].Y));
        Assert.Equal((300, 100), (sorted[1].X, sorted[1].Y));
        Assert.Equal((100, 500), (sorted[2].X, sorted[2].Y));
        Assert.Equal((300, 500), (sorted[3].X, sorted[3].Y));
    }

    [Fact]
    public void ReadingOrder_MangaVertical_RightColumnFirst()
    {
        TextBlock B(int x, int y) => new()
        {
            X = x, Y = y, Width = 60, Height = 200,
            Direction = TextDirection.Vertical,
        };
        // Kolom kanan (x=800): dua blok; kolom kiri (x=200): satu blok.
        var blocks = new List<TextBlock> { B(200, 100), B(800, 400), B(800, 100) };
        var sorted = DetectOcrPipeline.SortReadingOrder(blocks, OcrLanguage.Japanese);
        Assert.Equal(800, sorted[0].X);
        Assert.Equal(100, sorted[0].Y);
        Assert.Equal(800, sorted[1].X);
        Assert.Equal(400, sorted[1].Y);
        Assert.Equal(200, sorted[2].X);
    }

    [Fact]
    public void Project_SaveLoad_RoundTripsTextBlocks()
    {
        var dir = Path.Combine(Path.GetTempPath(),
            "tinta-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "proyek.tinta.json");
        var project = new TintaProject { Name = "Uji Blok" };
        var chapter = new ProjectChapter
        {
            SourcePath = "/tmp/bab.cbz", Title = "Bab", Format = "ZIP (CBZ)",
            PageCount = 1,
        };
        var page = chapter.PageAt(0);
        page.Blocks.Add(new TextBlock
        {
            X = 10, Y = 20, Width = 300, Height = 80,
            Direction = TextDirection.Vertical, Language = "ja",
            Text = "ありがとう", Confidence = 0.87, DetectionScore = 0.93,
        });
        project.Chapters.Add(chapter);
        project.Save(path);

        var loaded = TintaProject.Load(path);
        Assert.Equal(TintaProject.CurrentVersion, loaded.Version);
        var block = Assert.Single(loaded.Chapters[0].Pages[0].Blocks);
        Assert.Equal("ありがとう", block.Text);
        Assert.Equal(TextDirection.Vertical, block.Direction);
        Assert.Equal(0.87, block.Confidence, precision: 3);
        Assert.Equal(300, block.Width);
        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void Project_LoadsV1File_PagesEmpty()
    {
        var dir = Path.Combine(Path.GetTempPath(),
            "tinta-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "lama.tinta.json");
        File.WriteAllText(path, """
            {
              "format": "tinta-studio-project",
              "version": 1,
              "name": "Proyek Lama",
              "chapters": [
                { "sourcePath": "/tmp/bab.cbz", "title": "Bab",
                  "format": "ZIP (CBZ)", "pageCount": 3 }
              ]
            }
            """);
        var loaded = TintaProject.Load(path);
        Assert.Equal(1, loaded.Version);
        Assert.Equal("Proyek Lama", loaded.Name);
        Assert.Empty(loaded.Chapters[0].Pages);
        Directory.Delete(dir, recursive: true);
    }

    [SkippableFact]
    public void Cli_Ocr_OnFixtureCbz_PrintsRecognizedText()
    {
        RequireModels(OcrLanguage.Japanese);
        // Lokasi rakitan tes (bukan AppContext.BaseDirectory — runner
        // dalam-proses menampung tes di prosesnya sendiri).
        var testBin = Path.GetDirectoryName(typeof(DetectOcrTests).Assembly.Location)!;
        var config = new DirectoryInfo(testBin).Parent?.Name ?? "Debug";
        var root = Path.GetFullPath(Path.Combine(testBin, "..", "..", "..", ".."));
        var appDll = Path.Combine(root, "bin", config, "net10.0", "TintaStudio.dll");
        Skip.If(!File.Exists(appDll), $"DLL aplikasi belum dibangun: {appDll}");

        var dir = Path.Combine(Path.GetTempPath(),
            "tinta-cli-" + Guid.NewGuid().ToString("N"));
        using var page = MangaFixture.JapanesePage();
        var cbz = MangaFixture.MakeCbz(page, dir);
        var psi = new ProcessStartInfo("dotnet", $"\"{appDll}\" --ocr \"{cbz}\" --lang ja")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.Environment["TINTA_CONFIG_DIR"] = Path.Combine(dir, "config");
        using var proc = Process.Start(psi)!;
        var stdout = proc.StandardOutput.ReadToEnd();
        proc.WaitForExit(180_000);
        _out.WriteLine(stdout);
        Assert.Equal(0, proc.ExitCode);
        Assert.Contains("こんにちは", stdout);
        // Proyek tersimpan berisi blok untuk bab ini.
        var projectsDir = Path.Combine(dir, "config", "projects");
        Assert.True(Directory.Exists(projectsDir));
        Assert.NotEmpty(Directory.GetFiles(projectsDir, "*.tinta.json"));
        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void DedupeBoxes_CrossClassDuplicates_KeepsHighestScore()
    {
        // Wilayah sama dilaporkan dua kelas teks (temuan uji
        // halaman asli 2026-10-27): hanya satu yang bertahan —
        // yang skornya tertinggi. Kotak berbeda yang sah dan
        // kotak yang hanya bertumpang sebagian tidak dibuang.
        var boxes = new List<DetectedBox>
        {
            new(100, 100, 200, 300, DetectedClass.TextBubble, 0.66),
            new(100, 100, 200, 300, DetectedClass.TextFree, 0.39),
            new(500, 100, 180, 260, DetectedClass.TextFree, 0.80),
            new(520, 120, 180, 260, DetectedClass.TextBubble, 0.70),
        };
        var kept = DetectOcrPipeline.DedupeBoxes(boxes);
        Assert.Equal(3, kept.Count);
        var first = Assert.Single(kept, b => b.X == 100 && b.Y == 100);
        Assert.Equal(0.66, first.Score);
        Assert.Equal(DetectedClass.TextBubble, first.Class);
    }
}
