using SkiaSharp;
using TintaStudio.Models;
using TintaStudio.Services;
using Xunit;
using Xunit.Abstractions;

namespace TintaStudio.Tests;

/// <summary>
/// Tes v0.7.0 — Typeset Otomatis Pintar: kotak render diukur dari
/// interior bubble pada bitmap dasar, blok satu bubble dipartisi,
/// ukuran font diseragamkan sehalaman, font gaya komik bila
/// tersedia. Bitmap sintetis: elips putih (bubble) di latar gelap.
/// </summary>
public sealed class SmartTypesetTests
{
    private readonly ITestOutputHelper _out;

    public SmartTypesetTests(ITestOutputHelper output) => _out = output;

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

    /// <summary>Halaman latar gelap dengan elips-elips putih
    /// (bubble) di kotak-kotak yang diberikan.</summary>
    private static SKBitmap PageWithBubbles(params SKRectI[] ellipses)
    {
        var bmp = new SKBitmap(MangaFixture.PageWidth, MangaFixture.PageHeight);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(new SKColor(32, 40, 48));
        using var fill = new SKPaint
        {
            Color = SKColors.White, Style = SKPaintStyle.Fill, IsAntialias = true,
        };
        foreach (var e in ellipses)
            canvas.DrawOval(new SKRect(e.Left, e.Top, e.Right, e.Bottom), fill);
        canvas.Flush();
        return bmp;
    }

    // ─── Estimator kotak bubble ──────────────────────────────────

    [Fact]
    public void EstimateBubbleBox_EllipseInterior_Approx()
    {
        var ellipse = new SKRectI(150, 150, 850, 650);
        using var bmp = PageWithBubbles(ellipse);
        // Kotak OCR di pusat elips dengan dimensi yang masih
        // menjangkau tepi lewat pagar 2× (kasus nyata: kotak teks
        // memang mengisi sebagian besar bubble-nya).
        var block = Block(400, 300, 200, 200, "Halo!");
        var box = TypesetService.EstimateBubbleBox(bmp, block);
        _out.WriteLine($"kotak ukur: {box} (elips {ellipse})");
        // Pindai dari pusat elips: lebar ≈ lebar elips, tinggi ≈
        // tinggi elips (keduanya sesudah inset ~4% + tepi langkah).
        Assert.InRange(box.Width, (int)(ellipse.Width * 0.85), ellipse.Width);
        Assert.InRange(box.Height, (int)(ellipse.Height * 0.85), ellipse.Height);
        // Terpusat pada pusat elips ± toleransi pindai.
        Assert.InRange(box.MidX, ellipse.MidX - 16, ellipse.MidX + 16);
        Assert.InRange(box.MidY, ellipse.MidY - 16, ellipse.MidY + 16);
    }

    [Fact]
    public void EstimateBubbleBox_WhitePage_ScanCappedNearBlock()
    {
        // Halaman putih polos (tanpa bubble tergambar): interior
        // terbaca di mana-mana — pagar jangkauan pindai harus
        // menahan kotak hasil di sekitar kotak OCR (maks ~2×
        // dimensi per arah dari pusat), bukan selebar halaman.
        var bmp = new SKBitmap(MangaFixture.PageWidth, MangaFixture.PageHeight);
        using (var canvas = new SKCanvas(bmp))
            canvas.Clear(SKColors.White);
        var block = Block(400, 350, 200, 100, "Halo!");
        var box = TypesetService.EstimateBubbleBox(bmp, block);
        _out.WriteLine($"kotak di halaman putih: {box}");
        // Rentang pindai mentah ≤ pusat ± 2× dimensi OCR; hasil
        // akhir hanya bisa menyusut oleh inset.
        Assert.True(box.Width <= block.Width * 4 + 8,
            $"Lebar {box.Width} melewati pagar pindai 2× per arah.");
        Assert.True(box.Height <= block.Height * 4 + 8,
            $"Tinggi {box.Height} melewati pagar pindai 2× per arah.");
        Assert.True(box.Width < MangaFixture.PageWidth * 0.85,
            "Kotak tidak boleh bocor selebar halaman.");
        Assert.InRange(box.MidX, block.X + block.Width / 2 - 4,
                           block.X + block.Width / 2 + 4);
        bmp.Dispose();
    }

    [Fact]
    public void EstimateBubbleBox_NoWhiteInterior_FallsBackToOcrBox()
    {
        using var bmp = PageWithBubbles(); // latar gelap polos
        var block = Block(400, 350, 200, 100, "Halo!");
        var box = TypesetService.EstimateBubbleBox(bmp, block);
        Assert.Equal(block.X, box.Left);
        Assert.Equal(block.Y, box.Top);
        Assert.Equal(block.Width, box.Width);
        Assert.Equal(block.Height, box.Height);
    }

    // ─── Partisi bubble gabungan ─────────────────────────────────

    [Fact]
    public void Smart_TwoBlocksOneBubble_Partitioned_NoOverlap_UnionCovered()
    {
        var ellipse = new SKRectI(100, 100, 900, 700);
        using var bmp = PageWithBubbles(ellipse);
        var top = Block(400, 200, 200, 100, "Bagian atas.");
        var bottom = Block(400, 450, 200, 100, "Bagian bawah.");
        var page = PageWith(top, bottom);

        int n = TypesetService.SmartAutoTypesetPage(page, bmp);
        Assert.Equal(2, n);
        var boxTop = TypesetService.RenderBox(top, top.Typeset!);
        var boxBottom = TypesetService.RenderBox(bottom, bottom.Typeset!);
        _out.WriteLine($"kotak atas {boxTop}, kotak bawah {boxBottom}");
        // Tidak tumpang tindih (berbagi tepi persis di garis iris
        // itu sah — luas irisan yang dihitung harus nol)…
        var inter = SKRectI.Intersect(boxTop, boxBottom);
        int overlapArea = inter.IsEmpty ? 0
            : Math.Max(0, inter.Width) * Math.Max(0, inter.Height);
        Assert.Equal(0, overlapArea);
        // …dan gabungannya menutup interior elips (± toleransi).
        var union = SKRectI.Union(boxTop, boxBottom);
        Assert.True(union.Width >= ellipse.Width * 0.85);
        Assert.True(union.Height >= ellipse.Height * 0.85);
        // Yang atas benar-benar di atas yang bawah.
        Assert.True(boxTop.Bottom <= boxBottom.Top);
    }

    // ─── Ukuran seragam & gaya ───────────────────────────────────

    [Fact]
    public void Smart_UniformSize_ShortAndLong_SameUnlessFitSmaller()
    {
        var e1 = new SKRectI(80, 100, 480, 340);
        var e2 = new SKRectI(520, 100, 920, 340);
        using var bmp = PageWithBubbles(e1, e2);
        var shortBlock = Block(230, 190, 100, 60, "Halo!");
        var longBlock = Block(620, 190, 200, 60,
            "Kalimat ini jauh lebih panjang daripada yang pertama " +
            "dan butuh beberapa baris untuk dituliskan semua");
        var page = PageWith(shortBlock, longBlock);

        int n = TypesetService.SmartAutoTypesetPage(page, bmp);
        Assert.Equal(2, n);
        double sShort = shortBlock.Typeset!.FontSize;
        double sLong = longBlock.Typeset!.FontSize;
        _out.WriteLine($"ukuran pendek {sShort}, panjang {sLong}");
        // Target seragam halaman berada di pita [20, 28]…
        Assert.InRange(sShort, 20, 28);
        // …dan blok panjang memakai min(target, fit-nya sendiri):
        // tidak pernah lebih besar dari blok pendek.
        Assert.True(sLong <= sShort,
            $"Blok panjang ({sLong}) tidak boleh lebih besar dari target ({sShort}).");
        Assert.True(sLong >= 14);
        // Semua blok hasil jalur pintar horizontal (tidak vertikal).
        Assert.False(shortBlock.Typeset!.Vertical);
        Assert.False(longBlock.Typeset!.Vertical);
    }

    [Fact]
    public void Smart_FontChoice_ComicNeueOrSafeSans()
    {
        var (family, _) = TypesetService.SmartFontChoice();
        Assert.Contains(family, new[] { "Comic Neue", "" });

        var e1 = new SKRectI(80, 100, 480, 340);
        using var bmp = PageWithBubbles(e1);
        var block = Block(230, 190, 100, 60, "Halo!");
        TypesetService.SmartAutoTypesetPage(PageWith(block), bmp);
        var style = Assert.IsType<TypesetStyle>(block.Typeset);
        Assert.Equal(family, style.FontFamily);
        Assert.Equal(family == "Comic Neue", style.Bold);
        Assert.Equal(TypesetPresets.Dialog, style.PresetName);
        Assert.Equal(TypesetAlign.Center, style.Align);
    }

    [Fact]
    public void Smart_RenderedInk_InsideBubbleBox()
    {
        var ellipse = new SKRectI(150, 150, 850, 650);
        using var bmp = PageWithBubbles(ellipse);
        var block = Block(400, 350, 200, 100, "Halo dunia!");
        var page = PageWith(block);
        TypesetService.SmartAutoTypesetPage(page, bmp);
        var box = TypesetService.RenderBox(block, block.Typeset!);
        using var final = TypesetService.RenderFinalBitmap(bmp, page);
        int inkOutside = 0, inkInside = 0;
        for (int y = 0; y < final.Height; y++)
        for (int x = 0; x < final.Width; x++)
        {
            var before = bmp.GetPixel(x, y);
            var after = final.GetPixel(x, y);
            if (before == after) continue;
            if (x >= box.Left && x < box.Right && y >= box.Top && y < box.Bottom)
                inkInside++;
            else
                inkOutside++;
        }
        _out.WriteLine($"tinta di dalam kotak {inkInside}, di luar {inkOutside}");
        Assert.True(inkInside > 100);
        Assert.Equal(0, inkOutside);
    }

    // ─── Regresi aturan lama ─────────────────────────────────────

    [Fact]
    public void Smart_ExplicitStyle_NeverOverwritten()
    {
        var e1 = new SKRectI(80, 100, 480, 340);
        using var bmp = PageWithBubbles(e1);
        var custom = Block(230, 190, 100, 60, "Hai!");
        custom.Typeset = new TypesetStyle { ColorHex = "#123456", FontSize = 33 };
        var plain = Block(600, 190, 100, 60, "Halo!");
        var noText = Block(600, 500, 100, 60, null);
        var page = PageWith(custom, plain, noText);

        int n = TypesetService.SmartAutoTypesetPage(page, bmp);
        Assert.Equal(1, n);
        Assert.Equal("#123456", custom.Typeset!.ColorHex);
        Assert.Equal(33, custom.Typeset.FontSize);
        Assert.NotNull(plain.Typeset);
        Assert.Null(noText.Typeset);
        // Jalan kedua: tidak ada lagi yang diisi.
        Assert.Equal(0, TypesetService.SmartAutoTypesetPage(page, bmp));
    }

    [Fact]
    public void MeasureAutoFit_ShortTextInBigBox_BiggerThanLongText()
    {
        var style = new TypesetStyle();
        float big = TypesetService.MeasureAutoFitSize("Halo!", style, 400, 240);
        float small = TypesetService.MeasureAutoFitSize(
            "Satu kalimat yang sangat panjang sekali untuk bubble",
            style, 400, 240);
        _out.WriteLine($"fit pendek {big:F1}, fit panjang {small:F1}");
        Assert.True(big > small);
        Assert.True(small >= 4);
    }
}
