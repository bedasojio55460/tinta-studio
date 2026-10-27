using System.Diagnostics;
using SkiaSharp;
using TintaStudio.Models;
using TintaStudio.Services;
using Xunit;
using Xunit.Abstractions;

namespace TintaStudio.Tests;

/// <summary>
/// Tes Tahap 5 — Typesetting: teks terjemahan digambar ke halaman
/// bersih di dalam kotak bloknya. Metrik piksel: densitas tinta
/// (luminansi &lt; 160) di dalam/luar kotak render, pusat massa
/// tinta, dan piksel warna stroke. Halaman dasar sintetis polos —
/// tinta apa pun di hasil render pasti berasal dari typeset.
/// </summary>
public sealed class TypesetTests
{
    private readonly ITestOutputHelper _out;

    public TypesetTests(ITestOutputHelper output) => _out = output;

    // ─── Pembantu piksel ─────────────────────────────────────────

    private static bool IsInk(SKColor px) =>
        (px.Red * 299 + px.Green * 587 + px.Blue * 114) / 1000 < 160;

    private static double InkDensity(SKBitmap bmp, SKRectI rect)
    {
        int ink = 0, total = 0;
        for (int y = Math.Max(0, rect.Top); y < Math.Min(bmp.Height, rect.Bottom); y++)
        for (int x = Math.Max(0, rect.Left); x < Math.Min(bmp.Width, rect.Right); x++)
        {
            if (IsInk(bmp.GetPixel(x, y))) ink++;
            total++;
        }
        return total == 0 ? 0 : (double)ink / total;
    }

    /// <summary>Jumlah piksel yang BERBEDA dari bitmap dasar di luar
    /// kotak (kotak diperluas margin dulu).</summary>
    private static int DiffOutside(SKBitmap a, SKBitmap b, SKRectI box, int margin)
    {
        var ex = new SKRectI(box.Left - margin, box.Top - margin,
                             box.Right + margin, box.Bottom + margin);
        int diff = 0;
        for (int y = 0; y < a.Height; y++)
        for (int x = 0; x < a.Width; x++)
        {
            if (x >= ex.Left && x < ex.Right && y >= ex.Top && y < ex.Bottom)
                continue;
            if (a.GetPixel(x, y) != b.GetPixel(x, y))
                diff++;
        }
        return diff;
    }

    /// <summary>Kotak batas tinta di dalam wilayah cari.</summary>
    private static SKRectI InkBounds(SKBitmap bmp, SKRectI search)
    {
        int l = int.MaxValue, t = int.MaxValue, r = -1, b = -1;
        for (int y = Math.Max(0, search.Top); y < Math.Min(bmp.Height, search.Bottom); y++)
        for (int x = Math.Max(0, search.Left); x < Math.Min(bmp.Width, search.Right); x++)
        {
            if (!IsInk(bmp.GetPixel(x, y))) continue;
            if (x < l) l = x;
            if (x > r) r = x;
            if (y < t) t = y;
            if (y > b) b = y;
        }
        return r < 0 ? SKRectI.Empty : new SKRectI(l, t, r + 1, b + 1);
    }

    private static (double X, double Y) InkCentroid(SKBitmap bmp, SKRectI search)
    {
        long sx = 0, sy = 0, n = 0;
        for (int y = Math.Max(0, search.Top); y < Math.Min(bmp.Height, search.Bottom); y++)
        for (int x = Math.Max(0, search.Left); x < Math.Min(bmp.Width, search.Right); x++)
        {
            if (!IsInk(bmp.GetPixel(x, y))) continue;
            sx += x; sy += y; n++;
        }
        return n == 0 ? (0, 0) : ((double)sx / n, (double)sy / n);
    }

    private static SKBitmap BlankPage(SKColor color)
    {
        var bmp = new SKBitmap(MangaFixture.PageWidth, MangaFixture.PageHeight);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(color);
        canvas.Flush();
        return bmp;
    }

    private static TextBlock Block(int x, int y, int w, int h, string? translation) => new()
    {
        X = x, Y = y, Width = w, Height = h,
        Text = "こんにちは", Language = "ja",
        Translation = translation,
    };

    private static ProjectPage PageWith(params TextBlock[] blocks)
    {
        var page = new ProjectPage { Index = 0 };
        page.Blocks.AddRange(blocks);
        return page;
    }

    // ─── Render dasar ────────────────────────────────────────────

    [Fact]
    public void Render_DrawsTranslation_InkInside_OutsideUnchanged_BaseUntouched()
    {
        using var basePage = BlankPage(SKColors.White);
        string baseHash = CleanService.PngHash(basePage);
        var block = Block(100, 100, 440, 220, "Halo!");
        var page = PageWith(block);
        var box = new SKRectI(block.X, block.Y, block.X + block.Width, block.Y + block.Height);
        Assert.Equal(0.0, InkDensity(basePage, box));

        using var final = TypesetService.RenderFinalBitmap(basePage, page);
        double inside = InkDensity(final, box);
        _out.WriteLine($"densitas tinta di kotak sesudah render: {inside:F3}");
        Assert.True(inside > 0.02, $"Teks harus tergambar (densitas {inside:F3}).");
        int outside = DiffOutside(final, basePage, box, margin: 3);
        Assert.Equal(0, outside);
        // Bitmap dasar tidak diubah render (non-destruktif).
        Assert.Equal(baseHash, CleanService.PngHash(basePage));
    }

    [Fact]
    public void Render_SkipsBlocksWithoutTranslation()
    {
        using var basePage = BlankPage(SKColors.White);
        var withText = Block(100, 100, 300, 150, "Halo!");
        var empty = Block(600, 100, 300, 150, null);
        var page = PageWith(withText, empty);
        using var final = TypesetService.RenderFinalBitmap(basePage, page);
        Assert.True(InkDensity(final,
            new SKRectI(100, 100, 400, 250)) > 0.02);
        Assert.Equal(0.0, InkDensity(final,
            new SKRectI(600, 100, 900, 250)));
    }

    [Fact]
    public void AutoFit_LongText_StaysInsideBox()
    {
        using var basePage = BlankPage(SKColors.White);
        var block = Block(100, 100, 300, 110,
            "Ini adalah kalimat yang sangat panjang sekali untuk " +
            "diucapkan dalam satu bubble kecil bersama teman-teman");
        var page = PageWith(block);
        var box = new SKRectI(block.X, block.Y, block.X + block.Width, block.Y + block.Height);
        using var final = TypesetService.RenderFinalBitmap(basePage, page);
        double inside = InkDensity(final, box);
        _out.WriteLine($"teks panjang: densitas di kotak {inside:F3}");
        Assert.True(inside > 0.02);
        int outside = DiffOutside(final, basePage, box, margin: 3);
        Assert.Equal(0, outside);
    }

    [Fact]
    public void Vertical_ProducesTallColumnInk()
    {
        using var basePage = BlankPage(SKColors.White);
        var block = Block(100, 100, 150, 420, "Halo dunia");
        block.Typeset = new TypesetStyle { Vertical = true };
        var page = PageWith(block);
        using var final = TypesetService.RenderFinalBitmap(basePage, page);
        var ink = InkBounds(final, new SKRectI(90, 90, 260, 530));
        Assert.False(ink.IsEmpty);
        _out.WriteLine($"kotak tinta vertikal: {ink.Width}×{ink.Height}");
        Assert.True(ink.Height > ink.Width * 1.5,
            $"Tinta vertikal harus jauh lebih tinggi dari lebar ({ink.Width}×{ink.Height}).");
    }

    [Fact]
    public void Stroke_PaintsStrokeColor()
    {
        using var basePage = BlankPage(SKColors.White);
        var block = Block(100, 100, 440, 220, "Halo!");
        block.Typeset = new TypesetStyle
        {
            ColorHex = "#000000", StrokeHex = "#FF0000", StrokeWidth = 3,
        };
        var page = PageWith(block);
        using var final = TypesetService.RenderFinalBitmap(basePage, page);
        int red = 0;
        for (int y = 100; y < 320; y++)
        for (int x = 100; x < 540; x++)
        {
            var px = final.GetPixel(x, y);
            if (px.Red > 180 && px.Green < 90 && px.Blue < 90)
                red++;
        }
        _out.WriteLine($"piksel stroke merah: {red}");
        Assert.True(red > 40, $"Harus ada piksel stroke merah (ada {red}).");
    }

    // ─── Geometri & preset ───────────────────────────────────────

    [Fact]
    public void Geometry_DxShiftsInkCentroid()
    {
        using var basePage = BlankPage(SKColors.White);
        var search = new SKRectI(150, 150, 700, 420);

        var a = Block(200, 200, 300, 150, "Halo!");
        using var fa = TypesetService.RenderFinalBitmap(basePage, PageWith(a));
        var ca = InkCentroid(fa, search);

        var b = Block(200, 200, 300, 150, "Halo!");
        b.Typeset = new TypesetStyle { Dx = 80 };
        using var fb = TypesetService.RenderFinalBitmap(basePage, PageWith(b));
        var cb = InkCentroid(fb, search);

        double shift = cb.X - ca.X;
        _out.WriteLine($"geser pusat tinta: {shift:F1} px (target 80)");
        Assert.InRange(shift, 72, 88);
    }

    [Fact]
    public void Preset_Teriak_SetsBoldStroke_RendersStrokePixels()
    {
        var block = Block(100, 100, 440, 220, "Halo!");
        TypesetPresets.ApplyTo(block, TypesetPresets.Teriak);
        var style = Assert.IsType<TypesetStyle>(block.Typeset);
        Assert.True(style.Bold);
        Assert.True(style.StrokeWidth > 0);
        Assert.Equal(TypesetPresets.Teriak, style.PresetName);

        // Dasar abu: stroke putih preset terlihat sebagai piksel terang.
        using var basePage = BlankPage(new SKColor(128, 128, 128));
        using var final = TypesetService.RenderFinalBitmap(basePage, PageWith(block));
        int light = 0, dark = 0;
        for (int y = 100; y < 320; y++)
        for (int x = 100; x < 540; x++)
        {
            var px = final.GetPixel(x, y);
            int lum = (px.Red * 299 + px.Green * 587 + px.Blue * 114) / 1000;
            if (lum > 220) light++;
            if (lum < 60) dark++;
        }
        _out.WriteLine($"piksel stroke terang: {light}, piksel isi gelap: {dark}");
        Assert.True(light > 30, "Stroke putih preset Teriak harus tergambar.");
        Assert.True(dark > 30, "Isi teks hitam preset Teriak harus tergambar.");
    }

    [Fact]
    public void AutoTypeset_FillsOnlyTranslatedUntypesetBlocks()
    {
        var plain = Block(0, 0, 100, 50, "Halo!");
        var noText = Block(0, 60, 100, 50, null);
        var custom = Block(0, 120, 100, 50, "Hai!");
        custom.Typeset = new TypesetStyle { ColorHex = "#123456" };
        var page = PageWith(plain, noText, custom);

        int n = TypesetService.AutoTypesetPage(page);
        Assert.Equal(1, n);
        Assert.NotNull(plain.Typeset);
        Assert.Equal(TypesetPresets.Dialog, plain.Typeset!.PresetName);
        Assert.Null(noText.Typeset);
        Assert.Equal("#123456", custom.Typeset!.ColorHex);
        // Jalan kedua tidak mengisi apa-apa lagi.
        Assert.Equal(0, TypesetService.AutoTypesetPage(page));
    }

    [Fact]
    public void AutoTypeset_VerticalBlock_WidensBox_InkByWordInside()
    {
        // Blok OCR vertikal asli fixture: kotak 72×326 sesempit kolom
        // teks vertikalnya, terjemahan Indonesia di-typeset horizontal.
        var vBlock = new TextBlock
        {
            X = 745, Y = 245, Width = 72, Height = 326,
            Direction = TextDirection.Vertical,
            Text = "ありがとう", Language = "ja",
            Translation = "Terima kasih!",
        };
        var page = PageWith(vBlock);

        int n = TypesetService.AutoTypesetPage(page, MangaFixture.PageWidth);
        Assert.Equal(1, n);
        var style = Assert.IsType<TypesetStyle>(vBlock.Typeset);
        Assert.False(style.Vertical);
        // Pelebaran tercatat sebagai geometri tersimpan: lebar sasaran
        // max(72, round(326 × 0,55)) = 179 → Dw = 107, dan kotak
        // efektif (= RenderBox) selebar itu serta tetap di halaman.
        Assert.True(style.Dw > 0,
            $"Blok vertikal harus dilebarkan (Dw={style.Dw}).");
        Assert.Equal(179, vBlock.Width + style.Dw);
        var eff = TypesetService.RenderBox(vBlock, style);
        Assert.Equal(179, eff.Width);
        Assert.True(eff.Left >= 0 && eff.Right <= MangaFixture.PageWidth,
            $"Kotak efektif harus di dalam halaman: {eff}");

        using var basePage = BlankPage(SKColors.White);
        using var final = TypesetService.RenderFinalBitmap(basePage, page);
        // Semua tinta di dalam kotak yang dilebarkan (+ margin 3).
        Assert.Equal(0, DiffOutside(final, basePage, eff, margin: 3));
        // Lebar tinta jauh melampaui kolom asli 72 px: teks terbungkus
        // per kata, bukan menumpuk per karakter di kolom sempit.
        var ink = InkBounds(final, eff);
        Assert.False(ink.IsEmpty);
        _out.WriteLine($"kotak tinta: {ink.Width}×{ink.Height} " +
                       $"di kotak efektif {eff.Width}×{eff.Height}");
        Assert.True(ink.Width > vBlock.Width,
            $"Lebar tinta {ink.Width} harus > lebar kotak asli {vBlock.Width}.");
    }

    [Fact]
    public void AutoTypeset_HorizontalBlock_GeometryUntouched()
    {
        // Pembanding: blok horizontal tidak diubah geometrinya sama
        // sekali oleh terapkan-otomatis.
        var hBlock = Block(100, 100, 300, 150, "Halo!");
        var page = PageWith(hBlock);
        Assert.Equal(1, TypesetService.AutoTypesetPage(
            page, MangaFixture.PageWidth));
        var style = Assert.IsType<TypesetStyle>(hBlock.Typeset);
        Assert.Equal(0, style.Dw);
        Assert.Equal(0, style.Dx);
    }

    // ─── Proyek v5 ───────────────────────────────────────────────

    [Fact]
    public void Project_RoundTripV5_TypesetIdentical_RenderHashSame()
    {
        var dir = Path.Combine(Path.GetTempPath(),
            "tinta-typeset-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var block = Block(100, 100, 440, 220, "Halo!");
            block.Typeset = new TypesetStyle
            {
                FontFamily = "DejaVu Sans", FontSize = 30, Bold = true,
                Italic = true, ColorHex = "#112233", StrokeHex = "#FF0000",
                StrokeWidth = 2.5, Align = TypesetAlign.Right,
                LineSpacing = 1.3, LetterSpacing = 1.5, Vertical = false,
                Rotation = 90, Dx = 5, Dy = -7, Dw = 40, Dh = -20,
                PresetName = "Kustom",
            };
            var page = PageWith(block);
            var project = new TintaProject { Name = "Uji Typeset" };
            var chapter = new ProjectChapter
            {
                SourcePath = "/tmp/bab.cbz", Title = "Bab",
                Format = "ZIP (CBZ)", PageCount = 1,
            };
            chapter.Pages.Add(page);
            project.Chapters.Add(chapter);
            var path = Path.Combine(dir, "proyek.tinta.json");
            project.Save(path);

            var loaded = TintaProject.Load(path);
            Assert.Equal(5, loaded.Version);
            Assert.Equal(TintaProject.CurrentVersion, loaded.Version);
            var s = Assert.IsType<TypesetStyle>(
                loaded.Chapters[0].Pages[0].Blocks[0].Typeset);
            Assert.Equal("DejaVu Sans", s.FontFamily);
            Assert.Equal(30, s.FontSize);
            Assert.True(s.Bold);
            Assert.True(s.Italic);
            Assert.Equal("#112233", s.ColorHex);
            Assert.Equal("#FF0000", s.StrokeHex);
            Assert.Equal(2.5, s.StrokeWidth);
            Assert.Equal(TypesetAlign.Right, s.Align);
            Assert.Equal(1.3, s.LineSpacing);
            Assert.Equal(1.5, s.LetterSpacing);
            Assert.False(s.Vertical);
            Assert.Equal(90, s.Rotation);
            Assert.Equal(5, s.Dx);
            Assert.Equal(-7, s.Dy);
            Assert.Equal(40, s.Dw);
            Assert.Equal(-20, s.Dh);
            Assert.Equal("Kustom", s.PresetName);

            using var basePage = BlankPage(SKColors.White);
            using var fromOriginal = TypesetService.RenderFinalBitmap(basePage, page);
            using var fromLoaded = TypesetService.RenderFinalBitmap(
                basePage, loaded.Chapters[0].Pages[0]);
            Assert.Equal(CleanService.PngHash(fromOriginal),
                         CleanService.PngHash(fromLoaded));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Project_LoadsV4File_TypesetNull()
    {
        var dir = Path.Combine(Path.GetTempPath(),
            "tinta-typeset-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "v4.tinta.json");
            File.WriteAllText(path, """
                {
                  "format": "tinta-studio-project",
                  "version": 4,
                  "name": "Proyek v4",
                  "chapters": [
                    { "sourcePath": "/tmp/bab.cbz", "title": "Bab",
                      "format": "ZIP (CBZ)", "pageCount": 1,
                      "pages": [
                        { "index": 0, "blocks": [
                          { "id": "abc", "x": 1, "y": 2, "width": 30,
                            "height": 40, "direction": "horizontal",
                            "language": "ja", "text": "x",
                            "confidence": 0.5, "detectionScore": 0.6,
                            "translation": "Halo!" }
                        ], "cleanOps": [], "manualStrokes": [], "cleanSeq": 0 }
                      ] }
                  ]
                }
                """);
            var loaded = TintaProject.Load(path);
            Assert.Equal(4, loaded.Version);
            var block = loaded.Chapters[0].Pages[0].Blocks[0];
            Assert.Equal("Halo!", block.Translation);
            Assert.Null(block.Typeset);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // ─── CLI --render ────────────────────────────────────────────

    [SkippableFact]
    public void Cli_Render_OnOcrFixtureCbz_DrawsTranslatedBlocks()
    {
        var missing = ModelStore.MissingFor(OcrLanguage.Japanese);
        Skip.If(missing.Count > 0,
            "Model OCR belum diunduh: " + string.Join(", ", missing.Select(m => m.Id)));
        var testBin = Path.GetDirectoryName(typeof(TypesetTests).Assembly.Location)!;
        var config = new DirectoryInfo(testBin).Parent?.Name ?? "Debug";
        var root = Path.GetFullPath(Path.Combine(testBin, "..", "..", "..", ".."));
        var appDll = Path.Combine(root, "bin", config, "net10.0", "TintaStudio.dll");
        Skip.If(!File.Exists(appDll), $"DLL aplikasi belum dibangun: {appDll}");

        var dir = Path.Combine(Path.GetTempPath(),
            "tinta-cli-render-" + Guid.NewGuid().ToString("N"));
        var configDir = Path.Combine(dir, "config");
        using var page = MangaFixture.JapanesePage();
        var cbz = MangaFixture.MakeCbz(page, dir);

        string RunApp(params string[] args)
        {
            var psi = new ProcessStartInfo("dotnet",
                $"\"{appDll}\" " + string.Join(" ", args.Select(a => $"\"{a}\"")))
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            psi.Environment["TINTA_CONFIG_DIR"] = configDir;
            using var proc = Process.Start(psi)!;
            var stdout = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(300_000);
            Assert.Equal(0, proc.ExitCode);
            return stdout;
        }

        try
        {
            // 1) OCR lewat CLI: proyek bab fixture terisi blok asli.
            RunApp("--ocr", cbz, "--lang", "ja");

            // 2) Isi terjemahan + operasi bersih solid lewat berkas
            //    proyek (meniru hasil kerja pengguna di aplikasi).
            Environment.SetEnvironmentVariable("TINTA_CONFIG_DIR", configDir);
            var projectPath = ProjectStore.PathFor(cbz);
            Assert.True(File.Exists(projectPath), "Proyek OCR harus tersimpan.");
            var project = TintaProject.Load(projectPath);
            var projPage = project.Chapters[0].Pages[0];
            Assert.NotEmpty(projPage.Blocks);
            foreach (var b in projPage.Blocks)
            {
                b.Translation = "Halo!";
                CleanService.SetBlockOperation(projPage, b, CleanMode.Solid, "#FFFFFF");
            }
            project.Save(projectPath);
            var firstBox = projPage.Blocks
                .Select(b => new SKRectI(b.X, b.Y, b.X + b.Width, b.Y + b.Height))
                .First();

            // 3) Render lewat CLI.
            var outDir = Path.Combine(dir, "out");
            var stdout = RunApp("--render", cbz, "--out", outDir);
            _out.WriteLine(stdout);
            Assert.Contains("Render selesai", stdout);

            var finalPath = Path.Combine(outDir, "page-001.png");
            Assert.True(File.Exists(finalPath), "Keluaran final harus ada.");
            using var final = SKBitmap.Decode(finalPath);
            Assert.NotNull(final);
            Assert.Equal(MangaFixture.PageWidth, final.Width);
            Assert.Equal(MangaFixture.PageHeight, final.Height);

            // Dasar solid membersihkan teks Jepang; satu-satunya tinta
            // di kotak blok pertama adalah typeset "Halo!".
            double inside = InkDensity(final, firstBox);
            _out.WriteLine($"densitas tinta kotak blok pertama di final: {inside:F3}");
            Assert.True(inside > 0.01,
                $"Typeset harus tergambar di kotak blok (densitas {inside:F3}).");

            // Typeset otomatis tersimpan ke proyek oleh --render.
            var after = TintaProject.Load(projectPath);
            Assert.All(after.Chapters[0].Pages[0].Blocks,
                b => Assert.NotNull(b.Typeset));
        }
        finally
        {
            Environment.SetEnvironmentVariable("TINTA_CONFIG_DIR", null);
            Directory.Delete(dir, recursive: true);
        }
    }
}
