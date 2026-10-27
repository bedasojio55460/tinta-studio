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

    [Fact]
    public void InkMask_LineProtection_KeepsCrossingLines_RemovesGlyphs()
    {
        // Potongan sintetis 220x300: garis vertikal 3 px menembus
        // potongan (menyentuh tepi atas/bawah), segmen garis yang
        // hanya melintasi batas wilayah blok (pola busur garis
        // page-c yang lepas dari komponen utama), dan dua glif
        // lepas utuh di dalam wilayah — satu dekat garis.
        using var crop = new SKBitmap(220, 300);
        using (var canvas = new SKCanvas(crop))
        {
            canvas.Clear(SKColors.White);
            using var black = new SKPaint
            { Color = SKColors.Black, Style = SKPaintStyle.Fill };
            canvas.DrawRect(new SKRect(170, 0, 173, 300), black);
            canvas.DrawRect(new SKRect(180, 100, 206, 103), black);
            canvas.DrawRect(new SKRect(60, 120, 92, 152), black);
            canvas.DrawRect(new SKRect(140, 120, 164, 152), black);
        }
        var region = new SKRectI(30, 60, 200, 240);
        var protect = InkMask.LineProtection(crop, region, inset: 2);
        Assert.True(protect[150 * 220 + 171],
            "Garis yang menembus potongan harus terlindungi.");
        Assert.True(protect[101 * 220 + 190],
            "Garis yang melintasi batas wilayah harus terlindungi.");
        Assert.False(protect[136 * 220 + 76],
            "Glif lepas di dalam wilayah tidak boleh terlindungi.");
        Assert.False(protect[136 * 220 + 152],
            "Glif dekat garis pun tidak boleh terlindungi.");

        using var mask = InkMask.Build(crop, region, dilate: 3, inset: 2, protect);
        Assert.Equal(0, (int)mask.GetPixel(171, 150).Red);
        Assert.Equal(0, (int)mask.GetPixel(190, 101).Red);
        Assert.Equal(255, (int)mask.GetPixel(76, 136).Red);
        Assert.Equal(255, (int)mask.GetPixel(152, 136).Red);

        // Perlindungan diterapkan lagi sesudah feathering: glif
        // dekat garis membuat alfa menyebar ke arah garis, tetapi
        // piksel garis harus tetap tepat 0.
        var alpha = InkMask.FeatherAlpha(mask, expand: 2, blur: 2, protect);
        Assert.Equal(0f, alpha[136 * 220 + 171]);
        Assert.True(alpha[136 * 220 + 152] > 0f);
    }

    /// <summary>
    /// Fixture v0.7.6: bubble seperti tes v0.7.5 (garis tepi hitam
    /// 3 px, kolom teks lepas ±6 px dari garis) DITAMBAH dua glif
    /// yang MENYATU fisik dengan garis tepi kanan (persegi tinta
    /// x 413..437 menempel ke pita garis x 437..440, y 420..452
    /// dan 472..504). Wilayah ukur glif menyatu: x 413..437,
    /// y 420..504.
    /// </summary>
    private static (SKBitmap Page, TextBlock Block) FusedGlyphFixture()
    {
        const int W = 500, H = 700;
        var page = new SKBitmap(W, H);
        using (var canvas = new SKCanvas(page))
        {
            canvas.Clear(SKColors.White);
            using var black = new SKPaint
            { Color = SKColors.Black, Style = SKPaintStyle.Fill };
            using var white = new SKPaint
            { Color = SKColors.White, Style = SKPaintStyle.Fill };
            canvas.DrawRect(new SKRect(60, 60, 440, 640), black);
            canvas.DrawRect(new SKRect(63, 63, 437, 637), white);
            // Kolom teks lepas dekat garis (seperti v0.7.5).
            for (int i = 0; i < 5; i++)
                canvas.DrawRect(new SKRect(407, 130 + i * 52, 431, 162 + i * 52), black);
            // Glif menyatu dengan garis tepi kanan.
            canvas.DrawRect(new SKRect(413, 420, 437, 452), black);
            canvas.DrawRect(new SKRect(413, 472, 437, 504), black);
        }
        var block = new TextBlock
        {
            X = 372, Y = 100, Width = 118, Height = 420,
            Text = "テスト", Language = "ja",
        };
        return (page, block);
    }

    private static readonly SKRectI FusedArea = new(413, 420, 437, 504);
    private static readonly SKRectI BorderBandArea = new(437, 140, 440, 500);

    [SkippableFact]
    public void Inpaint_LineProtectOn_KeepsBorderAndFusedGlyphRemnant()
    {
        Skip.If(!ModelStore.InpaintReady,
            "Model inpaint belum diunduh: inpaint-lama.onnx");
        var (page, block) = FusedGlyphFixture();
        using (page)
        {
            string hashBefore = CleanService.PngHash(page);
            double borderBefore = InkDensity(page, BorderBandArea);
            double fusedBefore = InkDensity(page, FusedArea);
            _out.WriteLine($"sebelum: garis {borderBefore:F3}, glif menyatu {fusedBefore:F3}");
            Assert.Equal(1.0, borderBefore);
            Assert.True(fusedBefore > 0.5, "Fixture glif menyatu harus bertinta pekat.");

            var projPage = PageWith(block);
            var op = CleanService.SetBlockOperation(
                projPage, block, CleanMode.Inpaint, null);
            Assert.True(op.LineProtect, "Bawaan SetBlockOperation harus jaga garis.");

            using var clean = new CleanService();
            using var cleaned = clean.BuildCleanedPage(page, projPage);
            double borderAfter = InkDensity(cleaned, BorderBandArea);
            double fusedAfter = InkDensity(cleaned, FusedArea);
            _out.WriteLine($"ON: garis {borderAfter:F3}, glif menyatu {fusedAfter:F3}");
            Assert.True(borderAfter >= borderBefore * 0.95,
                $"Garis tepi harus utuh ≥95% (sebelum {borderBefore:F3}, sesudah {borderAfter:F3}).");
            Assert.True(fusedAfter >= fusedBefore * 0.5,
                $"Sisa glif nempel harus masih terdeteksi jelas (sebelum {fusedBefore:F3}, sesudah {fusedAfter:F3}).");
            Assert.Equal(hashBefore, CleanService.PngHash(page));
        }
    }

    [SkippableFact]
    public void Inpaint_LineProtectOff_RemovesFusedGlyph()
    {
        Skip.If(!ModelStore.InpaintReady,
            "Model inpaint belum diunduh: inpaint-lama.onnx");
        var (page, block) = FusedGlyphFixture();
        using (page)
        {
            string hashBefore = CleanService.PngHash(page);
            double fusedBefore = InkDensity(page, FusedArea);
            Assert.True(fusedBefore > 0.5, "Fixture glif menyatu harus bertinta pekat.");

            var projPage = PageWith(block);
            var op = CleanService.SetBlockOperation(
                projPage, block, CleanMode.Inpaint, null, lineProtect: false);
            Assert.False(op.LineProtect);

            using var clean = new CleanService();
            using var cleaned = clean.BuildCleanedPage(page, projPage);
            double fusedAfter = InkDensity(cleaned, FusedArea);
            _out.WriteLine($"OFF: glif menyatu {fusedBefore:F3} → {fusedAfter:F3}");
            Assert.True(fusedAfter <= fusedBefore * 0.10,
                $"Tinta glif nempel harus hilang ≥90% tanpa perlindungan " +
                $"(sebelum {fusedBefore:F3}, sesudah {fusedAfter:F3}).");
            // Garis tepi BOLEH putus pada mode ini — tidak diasersikan.
            Assert.Equal(hashBefore, CleanService.PngHash(page));
        }
    }

    [Fact]
    public void Project_LoadsV5File_WithoutLineProtect_DefaultsOn_AndRoundTripsOff()
    {
        var dir = Path.Combine(Path.GetTempPath(),
            "tinta-clean-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            // Berkas gaya v5: cleanOps tanpa bidang lineProtect.
            var path = Path.Combine(dir, "v5.tinta.json");
            File.WriteAllText(path, """
                {
                  "format": "tinta-studio-project",
                  "version": 5,
                  "name": "Proyek v5",
                  "chapters": [
                    { "sourcePath": "/tmp/bab.cbz", "title": "Bab",
                      "format": "ZIP (CBZ)", "pageCount": 1,
                      "pages": [
                        { "index": 0, "blocks": [
                          { "id": "abc", "x": 1, "y": 2, "width": 30,
                            "height": 40, "direction": "horizontal",
                            "language": "ja", "text": "x",
                            "confidence": 0.5, "detectionScore": 0.6 }
                        ],
                        "cleanOps": [
                          { "blockId": "abc", "mode": "inpaint",
                            "fillColor": null, "seq": 1,
                            "appliedUtc": "2026-10-27T00:00:00Z" }
                        ] }
                      ] }
                  ]
                }
                """);
            var loaded = TintaProject.Load(path);
            Assert.Equal(5, loaded.Version);
            var op = Assert.Single(loaded.Chapters[0].Pages[0].CleanOps);
            Assert.Equal(CleanMode.Inpaint, op.Mode);
            Assert.True(op.LineProtect,
                "Op lama tanpa bidang lineProtect harus diperlakukan jaga garis (ON).");

            // Putaran simpan/muat pilihan OFF harus bertahan.
            op.LineProtect = false;
            var path2 = Path.Combine(dir, "v6.tinta.json");
            loaded.Save(path2);
            Assert.Contains("\"lineProtect\": false", File.ReadAllText(path2));
            var reloaded = TintaProject.Load(path2);
            Assert.Equal(5, reloaded.Version);
            Assert.False(Assert.Single(
                reloaded.Chapters[0].Pages[0].CleanOps).LineProtect);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>
    /// Fixture v0.7.7 untuk Penghapus Latar: halaman putih 500x700
    /// dengan bubble bergaris tepi hitam 3 px dan satu glif lepas
    /// 32x32 di (154,204). Wilayah ukur glif: interiornya
    /// (160,210)-(180,230).
    /// </summary>
    private static SKBitmap BgEraserFixture()
    {
        var page = new SKBitmap(500, 700);
        using var canvas = new SKCanvas(page);
        canvas.Clear(SKColors.White);
        using var black = new SKPaint
        { Color = SKColors.Black, Style = SKPaintStyle.Fill };
        using var white = new SKPaint
        { Color = SKColors.White, Style = SKPaintStyle.Fill };
        canvas.DrawRect(new SKRect(60, 60, 440, 640), black);
        canvas.DrawRect(new SKRect(63, 63, 437, 637), white);
        canvas.DrawRect(new SKRect(154, 204, 186, 236), black);
        return page;
    }

    private static BrushStroke BgStroke(bool lineProtect, params (int X, int Y)[] pts) => new()
    {
        Kind = StrokeKind.Inpaint,
        Erased = false,
        LineProtect = lineProtect,
        Radius = 26,
        Points = pts.Select(p => new StrokePoint { X = p.X, Y = p.Y }).ToList(),
    };

    [SkippableFact]
    public void BackgroundEraserStroke_RemovesGlyph_OutsideStrokePixelExact()
    {
        Skip.If(!ModelStore.InpaintReady,
            "Model inpaint belum diunduh: inpaint-lama.onnx");
        using var page = BgEraserFixture();
        string hashBefore = CleanService.PngHash(page);
        var glyphInner = new SKRectI(160, 210, 180, 230);
        double before = InkDensity(page, glyphInner);
        Assert.Equal(1.0, before);

        var projPage = PageWith();
        CleanService.AddStroke(projPage, BgStroke(true, (170, 220)));

        using var clean = new CleanService();
        using var cleaned = clean.BuildCleanedPage(page, projPage);
        double after = InkDensity(cleaned, glyphInner);
        _out.WriteLine($"penghapus latar: tinta glif {before:F3} → {after:F3}");
        Assert.True(after <= before * 0.10,
            $"Tinta glif di area sapuan harus hilang ≥90% (sebelum {before:F3}, sesudah {after:F3}).");
        // Di luar jangkauan sapuan + margin tempel: piksel persis asli.
        for (int y = 80; y < 180; y += 7)
            for (int x = 260; x < 420; x += 7)
                Assert.Equal(page.GetPixel(x, y), cleaned.GetPixel(x, y));
        for (int y = 520; y < 620; y += 7)
            for (int x = 90; x < 300; x += 7)
                Assert.Equal(page.GetPixel(x, y), cleaned.GetPixel(x, y));
        // Non-destruktif: bitmap masukan tidak berubah.
        Assert.Equal(hashBefore, CleanService.PngHash(page));

        // Bangun ulang dari proyek yang disimpan + dimuat: identik.
        var dir = Path.Combine(Path.GetTempPath(),
            "tinta-bgeraser-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var project = new TintaProject { Name = "Uji Penghapus Latar" };
            var chapter = new ProjectChapter
            {
                SourcePath = "/tmp/bab.cbz", Title = "Bab",
                Format = "ZIP (CBZ)", PageCount = 1,
            };
            chapter.Pages.Add(projPage);
            project.Chapters.Add(chapter);
            var path = Path.Combine(dir, "proyek.tinta.json");
            project.Save(path);
            Assert.Contains("\"kind\": \"inpaint\"", File.ReadAllText(path));

            var loaded = TintaProject.Load(path);
            var loadedPage = loaded.Chapters[0].Pages[0];
            var stroke = Assert.Single(loadedPage.ManualStrokes);
            Assert.Equal(StrokeKind.Inpaint, stroke.Kind);
            Assert.True(stroke.LineProtect);
            using var rebuilt = clean.BuildCleanedPage(page, loadedPage);
            Assert.Equal(CleanService.PngHash(cleaned), CleanService.PngHash(rebuilt));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>
    /// Fixture garis untuk Penghapus Latar: bubble fixture ditambah
    /// glif yang MENYATU dengan garis tepi kanan (persegi tinta
    /// x 413..437 menempel ke pita garis x 437..440, y 288..312) —
    /// pola yang sama dengan fixture blok v0.7.6. Wilayah ukur glif
    /// menyatu: interior (417,292)-(433,308); pita garis ukur:
    /// (437,288)-(440,312).
    /// </summary>
    private static SKBitmap BgEraserFusedFixture()
    {
        var page = BgEraserFixture();
        using var canvas = new SKCanvas(page);
        using var black = new SKPaint
        { Color = SKColors.Black, Style = SKPaintStyle.Fill };
        canvas.DrawRect(new SKRect(413, 288, 437, 312), black);
        canvas.Flush();
        return page;
    }

    private static readonly SKRectI FusedStrokeArea = new(417, 292, 433, 308);
    private static readonly SKRectI StrokeBorderBand = new(437, 288, 440, 312);

    [SkippableFact]
    public void BackgroundEraserStroke_LineProtectOn_KeepsCrossingBorder()
    {
        Skip.If(!ModelStore.InpaintReady,
            "Model inpaint belum diunduh: inpaint-lama.onnx");
        using var page = BgEraserFusedFixture();
        double borderBefore = InkDensity(page, StrokeBorderBand);
        double fusedBefore = InkDensity(page, FusedStrokeArea);
        Assert.Equal(1.0, borderBefore);
        Assert.Equal(1.0, fusedBefore);

        // Sapuan melintasi glif menyatu + garis tepi di belakangnya.
        var projPage = PageWith();
        var stroke = BgStroke(true, (380, 300), (402, 300), (424, 300));
        stroke.Radius = 18;
        CleanService.AddStroke(projPage, stroke);

        using var clean = new CleanService();
        using var cleaned = clean.BuildCleanedPage(page, projPage);
        double borderAfter = InkDensity(cleaned, StrokeBorderBand);
        double fusedAfter = InkDensity(cleaned, FusedStrokeArea);
        _out.WriteLine($"disapu (jaga): garis {borderBefore:F3} → {borderAfter:F3}, " +
                       $"glif menyatu {fusedBefore:F3} → {fusedAfter:F3}");
        Assert.True(borderAfter >= borderBefore * 0.95,
            $"Garis yang melintasi sapuan harus utuh ≥95% saat jaga garis " +
            $"(sebelum {borderBefore:F3}, sesudah {borderAfter:F3}).");
        Assert.True(fusedAfter >= fusedBefore * 0.5,
            $"Glif yang menyatu dengan garis ikut terlindungi (satu komponen) " +
            $"dan harus masih terdeteksi (sebelum {fusedBefore:F3}, sesudah {fusedAfter:F3}).");
    }

    [SkippableFact]
    public void BackgroundEraserStroke_LineProtectOff_ErasesCrossingBorder()
    {
        Skip.If(!ModelStore.InpaintReady,
            "Model inpaint belum diunduh: inpaint-lama.onnx");
        using var page = BgEraserFusedFixture();
        double fusedBefore = InkDensity(page, FusedStrokeArea);
        Assert.Equal(1.0, fusedBefore);

        // Sapuan yang sama persis dengan tes jaga garis, tanpa
        // perlindungan: glif menyatu harus hilang tuntas. (Segmen
        // garis di belakang glif tidak diasersikan: model boleh
        // menyambung kembali garis lurus yang terpotong pendek —
        // perbedaan perilaku yang diasersikan adalah pada tinta
        // yang menyatu dengan garis.)
        var projPage = PageWith();
        var stroke = BgStroke(false, (380, 300), (402, 300), (424, 300));
        stroke.Radius = 18;
        CleanService.AddStroke(projPage, stroke);

        using var clean = new CleanService();
        using var cleaned = clean.BuildCleanedPage(page, projPage);
        double fusedAfter = InkDensity(cleaned, FusedStrokeArea);
        _out.WriteLine($"disapu (tuntas): glif menyatu {fusedBefore:F3} → {fusedAfter:F3}");
        Assert.True(fusedAfter <= fusedBefore * 0.10,
            $"Tinta glif menyatu harus hilang ≥90% tanpa perlindungan " +
            $"(sebelum {fusedBefore:F3}, sesudah {fusedAfter:F3}).");
    }

    [Fact]
    public void Project_LoadsStrokesWithoutKind_FromErased_AndRoundTripsNewKinds()
    {
        var dir = Path.Combine(Path.GetTempPath(),
            "tinta-bgeraser-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            // Berkas gaya lama: manualStrokes TANPA bidang kind.
            var path = Path.Combine(dir, "lama.tinta.json");
            File.WriteAllText(path, """
                {
                  "format": "tinta-studio-project",
                  "version": 6,
                  "name": "Proyek lama",
                  "chapters": [
                    { "sourcePath": "/tmp/bab.cbz", "title": "Bab",
                      "format": "ZIP (CBZ)", "pageCount": 1,
                      "pages": [
                        { "index": 0, "blocks": [],
                          "manualStrokes": [
                            { "erased": true, "color": "#FFFFFF",
                              "radius": 9, "points": [ { "x": 5, "y": 6 } ],
                              "seq": 1 },
                            { "erased": false, "color": "#123456",
                              "radius": 9, "points": [ { "x": 7, "y": 8 } ],
                              "seq": 2 }
                          ] }
                      ] }
                  ]
                }
                """);
            var loaded = TintaProject.Load(path);
            var strokes = loaded.Chapters[0].Pages[0].ManualStrokes;
            Assert.Equal(2, strokes.Count);
            Assert.Equal(StrokeKind.Erase, strokes[0].Kind);
            Assert.Equal(StrokeKind.Paint, strokes[1].Kind);

            // Jenis baru bertahan putaran simpan/muat, termasuk
            // pilihan garis tepi false.
            strokes.Add(new BrushStroke
            {
                Kind = StrokeKind.Inpaint, Erased = false, LineProtect = false,
                Radius = 15,
                Points = new List<StrokePoint> { new() { X = 50, Y = 60 } },
                Seq = 3,
            });
            var path2 = Path.Combine(dir, "baru.tinta.json");
            loaded.Save(path2);
            var json = File.ReadAllText(path2);
            Assert.Contains("\"kind\": \"inpaint\"", json);
            Assert.Contains("\"lineProtect\": false", json);
            var reloaded = TintaProject.Load(path2);
            var s3 = reloaded.Chapters[0].Pages[0].ManualStrokes[2];
            Assert.Equal(StrokeKind.Inpaint, s3.Kind);
            Assert.False(s3.LineProtect);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void CleanStore_Signature_ChangesOnlyForInpaintStrokes()
    {
        var chapter = new ProjectChapter { SourcePath = "/tmp/bab.cbz" };
        var info = new ComicPage
        {
            Index = 0, SourceName = "p1.png", SourceFormat = "PNG",
            Width = 100, Height = 100, WorkPath = "/tmp/p1.png",
        };
        var page = PageWith();
        var baseline = CleanStore.OpsSignature(chapter, page, info);

        // Sapuan cat TIDAK mengubah tanda tangan gambar dasar.
        CleanService.AddStroke(page, new BrushStroke
        {
            Kind = StrokeKind.Paint, Erased = false, Color = "#000000", Radius = 8,
            Points = new List<StrokePoint> { new() { X = 10, Y = 10 } },
        });
        Assert.Equal(baseline, CleanStore.OpsSignature(chapter, page, info));

        // Sapuan penghapus latar MENGUBAH tanda tangan (dan pilihan
        // garis tepinya ikut menentukan).
        var bg = BgStroke(true, (50, 50));
        CleanService.AddStroke(page, bg);
        var withBg = CleanStore.OpsSignature(chapter, page, info);
        Assert.NotEqual(baseline, withBg);
        bg.LineProtect = false;
        Assert.NotEqual(withBg, CleanStore.OpsSignature(chapter, page, info));
    }

    [SkippableFact]
    public void Inpaint_ProtectsBubbleOutlineNearTextColumn()
    {
        Skip.If(!ModelStore.InpaintReady,
            "Model inpaint belum diunduh: inpaint-lama.onnx");
        // Meniru page-c: bubble bergaris tepi hitam 3 px, kolom
        // teks vertikal gelap ±6 px dari garis kanan, dan kotak
        // blok rapat yang ikut mencakup segmen garis tepi — tanpa
        // perlindungan, segmen itu masuk mask dan ter-inpaint.
        const int W = 500, H = 700;
        using var page = new SKBitmap(W, H);
        using (var canvas = new SKCanvas(page))
        {
            canvas.Clear(SKColors.White);
            using var black = new SKPaint
            { Color = SKColors.Black, Style = SKPaintStyle.Fill };
            using var white = new SKPaint
            { Color = SKColors.White, Style = SKPaintStyle.Fill };
            canvas.DrawRect(new SKRect(60, 60, 440, 640), black);
            canvas.DrawRect(new SKRect(63, 63, 437, 637), white);
            for (int i = 0; i < 7; i++)
                canvas.DrawRect(new SKRect(407, 130 + i * 52, 431, 162 + i * 52), black);
        }
        string hashBefore = CleanService.PngHash(page);
        var borderBand = new SKRectI(437, 140, 440, 500);
        var interior = new SKRectI(400, 120, 436, 500);
        double borderBefore = InkDensity(page, borderBand);
        double inkBefore = InkDensity(page, interior);
        _out.WriteLine($"sebelum: garis {borderBefore:F3}, interior {inkBefore:F3}");
        Assert.Equal(1.0, borderBefore);
        Assert.True(inkBefore > 0.10, "Fixture interior harus bertinta (ada kolom teks).");

        var block = new TextBlock
        {
            X = 372, Y = 100, Width = 118, Height = 420,
            Text = "テスト", Language = "ja",
        };
        var projPage = PageWith(block);
        CleanService.SetBlockOperation(projPage, block, CleanMode.Inpaint, null);

        using var clean = new CleanService();
        using var cleaned = clean.BuildCleanedPage(page, projPage);
        double borderAfter = InkDensity(cleaned, borderBand);
        double inkAfter = InkDensity(cleaned, interior);
        _out.WriteLine($"sesudah: garis {borderAfter:F3}, interior {inkAfter:F3}");
        Assert.True(borderAfter >= borderBefore * 0.95,
            $"Garis tepi harus utuh ≥95% (sebelum {borderBefore:F3}, sesudah {borderAfter:F3}).");
        Assert.True(inkAfter <= inkBefore * 0.10,
            $"Tinta interior harus hilang ≥90% (sebelum {inkBefore:F3}, sesudah {inkAfter:F3}).");
        Assert.Equal(hashBefore, CleanService.PngHash(page));
    }
}
