using System.Diagnostics;
using SkiaSharp;
using TintaStudio.Models;
using TintaStudio.Services;
using Xunit;
using Xunit.Abstractions;

namespace TintaStudio.Tests;

/// <summary>
/// Tes Tahap 3 — Pembersihan Bubble pada fixture manga sintetis
/// Tahap 2 (bubble berteks diketahui). Metrik: densitas tinta
/// (piksel luminansi &lt; 160) di interior blok. Uji inpaint memakai
/// model LaMA asli dan di-skip jujur bila model belum diunduh
/// (tools/fetch_models.sh). Prinsip non-destruktif diuji eksplisit:
/// berkas &amp; bitmap asli tidak pernah berubah.
/// </summary>
public sealed class CleanTests
{
    private readonly ITestOutputHelper _out;

    public CleanTests(ITestOutputHelper output) => _out = output;

    /// <summary>Densitas tinta di dalam rect (0..1).</summary>
    private static double InkDensity(SKBitmap bmp, SKRectI rect)
    {
        int ink = 0, total = 0;
        for (int y = rect.Top; y < rect.Bottom; y++)
        {
            for (int x = rect.Left; x < rect.Right; x++)
            {
                var px = bmp.GetPixel(x, y);
                int lum = (px.Red * 299 + px.Green * 587 + px.Blue * 114) / 1000;
                if (lum < 160)
                    ink++;
                total++;
            }
        }
        return total == 0 ? 0 : (double)ink / total;
    }

    /// <summary>Rect interior (persentase menyusut dari tiap tepi).</summary>
    private static SKRectI Inner(SKRectI r, double frac)
    {
        int dx = (int)(r.Width * frac), dy = (int)(r.Height * frac);
        return new SKRectI(r.Left + dx, r.Top + dy, r.Right - dx, r.Bottom - dy);
    }

    /// <summary>Blok teks di atas area teks bubble fixture
    /// (kotak deteksi nyata rapat ke teks; di sini kotak bubble
    /// menyusut 18% meniru kotak teks).</summary>
    private static TextBlock BlockOver(SKRectI bubble, string text) => new()
    {
        X = bubble.Left + (int)(bubble.Width * 0.18),
        Y = bubble.Top + (int)(bubble.Height * 0.18),
        Width = (int)(bubble.Width * 0.64),
        Height = (int)(bubble.Height * 0.64),
        Text = text,
        Language = "ja",
    };

    private static ProjectPage PageWith(params TextBlock[] blocks)
    {
        var page = new ProjectPage { Index = 0 };
        page.Blocks.AddRange(blocks);
        return page;
    }

    [Fact]
    public void Solid_RemovesText_KeepsOriginalUntouched()
    {
        using var page = MangaFixture.JapanesePage();
        var innerH = Inner(MangaFixture.BubbleJaH, 0.30);
        double before = InkDensity(page, innerH);
        _out.WriteLine($"densitas tinta interior sebelum: {before:F3}");
        Assert.True(before > 0.05, "Fixture seharusnya bertinta (ada teks).");
        string hashBefore = CleanService.PngHash(page);

        var block = BlockOver(MangaFixture.BubbleJaH, "こんにちは");
        var projPage = PageWith(block);
        CleanService.SetBlockOperation(projPage, block, CleanMode.Solid, "#FFFFFF");

        using var clean = new CleanService();
        using var cleaned = clean.BuildCleanedPage(page, projPage);
        double after = InkDensity(cleaned, innerH);
        _out.WriteLine($"densitas tinta interior sesudah solid: {after:F3}");
        Assert.True(after <= before * 0.10,
            $"Teks harus hilang ≥90% (sebelum {before:F3}, sesudah {after:F3}).");
        Assert.Equal(page.Width, cleaned.Width);
        Assert.Equal(page.Height, cleaned.Height);
        // Non-destruktif: bitmap masukan tidak berubah sama sekali.
        Assert.Equal(hashBefore, CleanService.PngHash(page));
    }

    [Fact]
    public void Solid_AutoColor_SamplesBubbleInterior()
    {
        using var page = MangaFixture.JapanesePage();
        var block = BlockOver(MangaFixture.BubbleJaV, "ありがとう");
        var sampled = CleanService.SampleBubbleColor(page, block);
        _out.WriteLine($"warna sampel: {CleanService.ToHex(sampled)}");
        // Bubble putih → sampel harus nyaris putih.
        Assert.True(sampled.Red >= 245 && sampled.Green >= 245 && sampled.Blue >= 245);
    }

    [SkippableFact]
    public void Inpaint_RemovesText_SameSize_OutsideUnchanged()
    {
        Skip.If(!ModelStore.InpaintReady,
            "Model inpaint belum diunduh: inpaint-lama.onnx");
        using var page = MangaFixture.JapanesePage();
        var innerH = Inner(MangaFixture.BubbleJaH, 0.30);
        double before = InkDensity(page, innerH);
        Assert.True(before > 0.05);
        string hashBefore = CleanService.PngHash(page);

        var block = BlockOver(MangaFixture.BubbleJaH, "こんにちは");
        var projPage = PageWith(block);
        CleanService.SetBlockOperation(projPage, block, CleanMode.Inpaint, null);

        using var clean = new CleanService();
        using var cleaned = clean.BuildCleanedPage(page, projPage);
        double after = InkDensity(cleaned, innerH);
        _out.WriteLine($"inpaint: densitas {before:F3} → {after:F3}");
        Assert.True(after <= before * 0.10,
            $"Teks harus hilang ≥90% oleh inpaint (sebelum {before:F3}, sesudah {after:F3}).");
        Assert.Equal(page.Width, cleaned.Width);
        Assert.Equal(page.Height, cleaned.Height);
        // Di luar wilayah blok+margin, piksel harus PERSIS asli
        // (penempelan hanya di dalam mask bertepi lembut).
        for (int y = 1150; y < 1350; y += 7)
            for (int x = 600; x < 950; x += 7)
                Assert.Equal(page.GetPixel(x, y), cleaned.GetPixel(x, y));
        Assert.Equal(hashBefore, CleanService.PngHash(page));
    }

    [Fact]
    public void Undo_RestoresPreviousStates()
    {
        using var page = MangaFixture.JapanesePage();
        var blockH = BlockOver(MangaFixture.BubbleJaH, "こんにちは");
        var blockV = BlockOver(MangaFixture.BubbleJaV, "ありがとう");
        var projPage = PageWith(blockH, blockV);
        using var clean = new CleanService();
        string hashOriginal = CleanService.PngHash(page);

        CleanService.SetBlockOperation(projPage, blockH, CleanMode.Solid, "#FFFFFF");
        string hashOne;
        using (var one = clean.BuildCleanedPage(page, projPage))
            hashOne = CleanService.PngHash(one);

        CleanService.SetBlockOperation(projPage, blockV, CleanMode.Solid, "#FFFFFF");
        string hashTwo;
        using (var two = clean.BuildCleanedPage(page, projPage))
            hashTwo = CleanService.PngHash(two);
        Assert.NotEqual(hashOne, hashTwo);

        Assert.True(CleanService.UndoLast(projPage));
        using (var back = clean.BuildCleanedPage(page, projPage))
            Assert.Equal(hashOne, CleanService.PngHash(back));

        Assert.True(CleanService.UndoLast(projPage));
        Assert.Empty(projPage.CleanOps);
        Assert.False(CleanService.UndoLast(projPage));
        // Tanpa operasi apa pun gambar bersih == asli.
        using var none = clean.BuildCleanedPage(page, projPage);
        Assert.Equal(hashOriginal, CleanService.PngHash(none));
    }

    [Fact]
    public void ManualStrokes_PaintThenEraseRestoresOriginal()
    {
        using var page = MangaFixture.JapanesePage();
        var projPage = PageWith();
        using var clean = new CleanService();
        // Area uji: sudut kosong halaman (putih polos).
        var area = new SKRectI(60, 1180, 260, 1300);
        Assert.Equal(0.0, InkDensity(page, area));

        var stroke = new BrushStroke
        {
            Erased = false,
            Color = "#000000",
            Radius = 36,
            Points = new List<StrokePoint> { new() { X = 160, Y = 1240 } },
        };
        CleanService.AddStroke(projPage, stroke);
        using (var painted = clean.BuildCleanedPage(page, projPage))
        {
            double d = InkDensity(painted, area);
            _out.WriteLine($"densitas sesudah kuas: {d:F3}");
            Assert.True(d > 0.10, "Kuas hitam harus meninggalkan tinta.");
        }

        var erase = new BrushStroke
        {
            Erased = true,
            Radius = 42,
            Points = new List<StrokePoint> { new() { X = 160, Y = 1240 } },
        };
        CleanService.AddStroke(projPage, erase);
        using (var restored = clean.BuildCleanedPage(page, projPage))
        {
            Assert.Equal(0.0, InkDensity(restored, area));
            Assert.Equal(CleanService.PngHash(page), CleanService.PngHash(restored));
        }
    }

    [Fact]
    public void Project_SaveLoad_RebuildsIdenticalCleanedImage()
    {
        var dir = Path.Combine(Path.GetTempPath(),
            "tinta-clean-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            using var page = MangaFixture.JapanesePage();
            var block = BlockOver(MangaFixture.BubbleJaH, "こんにちは");
            var project = new TintaProject { Name = "Uji Bersih" };
            var chapter = new ProjectChapter
            {
                SourcePath = "/tmp/bab.cbz", Title = "Bab",
                Format = "ZIP (CBZ)", PageCount = 1,
            };
            var projPage = chapter.PageAt(0);
            projPage.Blocks.Add(block);
            CleanService.SetBlockOperation(projPage, block, CleanMode.Solid, "#FEFEFE");
            CleanService.AddStroke(projPage, new BrushStroke
            {
                Erased = false, Color = "#112233", Radius = 10,
                Points = new List<StrokePoint> { new() { X = 900, Y = 1300 } },
            });
            project.Chapters.Add(chapter);
            var path = Path.Combine(dir, "proyek.tinta.json");
            project.Save(path);

            var loaded = TintaProject.Load(path);
            Assert.Equal(TintaProject.CurrentVersion, loaded.Version);
            var loadedPage = loaded.Chapters[0].Pages[0];
            var op = Assert.Single(loadedPage.CleanOps);
            Assert.Equal(CleanMode.Solid, op.Mode);
            Assert.Equal("#FEFEFE", op.FillColor);
            Assert.Single(loadedPage.ManualStrokes);

            using var clean = new CleanService();
            using var fromOriginal = clean.BuildCleanedPage(page, chapter.PageAt(0));
            using var fromLoaded = clean.BuildCleanedPage(page, loadedPage);
            Assert.Equal(CleanService.PngHash(fromOriginal),
                         CleanService.PngHash(fromLoaded));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Project_LoadsV2File_CleanStateEmpty()
    {
        var dir = Path.Combine(Path.GetTempPath(),
            "tinta-clean-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "v2.tinta.json");
            File.WriteAllText(path, """
                {
                  "format": "tinta-studio-project",
                  "version": 2,
                  "name": "Proyek v2",
                  "chapters": [
                    { "sourcePath": "/tmp/bab.cbz", "title": "Bab",
                      "format": "ZIP (CBZ)", "pageCount": 1,
                      "pages": [
                        { "index": 0, "blocks": [
                          { "id": "abc", "x": 1, "y": 2, "width": 30,
                            "height": 40, "direction": "horizontal",
                            "language": "ja", "text": "x",
                            "confidence": 0.5, "detectionScore": 0.6 }
                        ] }
                      ] }
                  ]
                }
                """);
            var loaded = TintaProject.Load(path);
            Assert.Equal(2, loaded.Version);
            var page = loaded.Chapters[0].Pages[0];
            Assert.Single(page.Blocks);
            Assert.Empty(page.CleanOps);
            Assert.Empty(page.ManualStrokes);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [SkippableFact]
    public void Cli_Clean_Solid_OnFixtureCbz_WritesCleanedPage()
    {
        var missing = ModelStore.MissingFor(OcrLanguage.Japanese);
        Skip.If(missing.Count > 0,
            "Model OCR belum diunduh: " + string.Join(", ", missing.Select(m => m.Id)));
        var testBin = Path.GetDirectoryName(typeof(CleanTests).Assembly.Location)!;
        var config = new DirectoryInfo(testBin).Parent?.Name ?? "Debug";
        var root = Path.GetFullPath(Path.Combine(testBin, "..", "..", "..", ".."));
        var appDll = Path.Combine(root, "bin", config, "net10.0", "TintaStudio.dll");
        Skip.If(!File.Exists(appDll), $"DLL aplikasi belum dibangun: {appDll}");

        var dir = Path.Combine(Path.GetTempPath(),
            "tinta-cli-clean-" + Guid.NewGuid().ToString("N"));
        using var page = MangaFixture.JapanesePage();
        var cbz = MangaFixture.MakeCbz(page, dir);
        var outDir = Path.Combine(dir, "out");
        var psi = new ProcessStartInfo("dotnet",
            $"\"{appDll}\" --clean \"{cbz}\" --mode solid --out \"{outDir}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.Environment["TINTA_CONFIG_DIR"] = Path.Combine(dir, "config");
        using var proc = Process.Start(psi)!;
        var stdout = proc.StandardOutput.ReadToEnd();
        proc.WaitForExit(240_000);
        _out.WriteLine(stdout);
        Assert.Equal(0, proc.ExitCode);
        Assert.Contains("dibersihkan", stdout);

        var cleanedPath = Path.Combine(outDir, "page-001.png");
        Assert.True(File.Exists(cleanedPath), "Keluaran bersih harus ada.");
        using var cleanedBmp = SKBitmap.Decode(cleanedPath);
        Assert.NotNull(cleanedBmp);
        Assert.Equal(MangaFixture.PageWidth, cleanedBmp.Width);
        Assert.Equal(MangaFixture.PageHeight, cleanedBmp.Height);

        // Interior bubble horizontal: teks harus sudah hilang di keluaran.
        var innerH = Inner(MangaFixture.BubbleJaH, 0.30);
        double beforeInk = InkDensity(page, innerH);
        double afterInk = InkDensity(cleanedBmp, innerH);
        _out.WriteLine($"CLI bersih: densitas interior {beforeInk:F3} → {afterInk:F3}");
        Assert.True(afterInk <= beforeInk * 0.10,
            $"Keluaran CLI masih bertinta ({afterInk:F3} dari {beforeInk:F3}).");
        // Berkas sumber CBZ tidak berubah (hash file tetap).
        Assert.True(File.Exists(cbz));
        Directory.Delete(dir, recursive: true);
    }
}
