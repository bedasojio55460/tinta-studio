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

    // ─── Regresi v0.7.1 ──────────────────────────────────────────

    /// <summary>Regresi #1: pada halaman manga asli, jalur pintar
    /// v0.7.0 menyimpan ukuran 26,5 / 22,5 yang MEMECAH kata
    /// ("Reaksim/u", "Sakamoto-kun" + "?" yatim) — pencarian biner
    /// menemukan ukuran bersih pada nilai pecahan (26,499…), lalu
    /// pembulatan ke 0,1 melompat ke lebar glif ter-hint yang lebih
    /// besar. Geometri di sini mereplikasi bubble aslinya (kotak
    /// pintar ±107 px &amp; ±149 px). Invarian yang dijaga: pada
    /// ukuran final yang TERSIMPAN, dengan typeface final blok,
    /// setiap kata terjemahan terukur (SKFont mentah, independen
    /// dari BuildLayout) muat utuh di lebar kotak render — kata
    /// yang lebih lebar dari kotak pasti dipecah per karakter oleh
    /// mesin render.</summary>
    [Fact]
    public void Smart_LongWords_NotSplitAtStoredSize_RealGeometry()
    {
        var bmp = new SKBitmap(1487, 2048);
        using (var canvas = new SKCanvas(bmp))
        {
            canvas.Clear(new SKColor(32, 40, 48));
            using var fill = new SKPaint
            {
                Color = SKColors.White, Style = SKPaintStyle.Fill, IsAntialias = true,
            };
            // Bubble reaksi (kotak OCR asli 1391,1415 96×269) dan
            // bubble Sakamoto (1132,202 103×146), elips seukuran
            // interior hasil pindai halaman asli.
            canvas.DrawOval(new SKRect(1366, 1393, 1486, 1705), fill);
            canvas.DrawOval(new SKRect(1105, 173, 1265, 381), fill);
            canvas.Flush();
        }
        var reaction = Block(1391, 1415, 96, 269, "Reaksimu selalu lucu, sih.");
        var sakamoto = Block(1132, 202, 103, 146, "Sakamoto-kun?");
        var page = PageWith(reaction, sakamoto);

        int n = TypesetService.SmartAutoTypesetPage(page, bmp);
        Assert.Equal(2, n);
        foreach (var block in new[] { reaction, sakamoto })
        {
            var style = Assert.IsType<TypesetStyle>(block.Typeset);
            var box = TypesetService.RenderBox(block, style);
            _out.WriteLine($"{block.Translation} -> kotak {box}, ukuran {style.FontSize}, font '{style.FontFamily}' bold={style.Bold}");
            Assert.InRange(style.FontSize, 14, 28);
            using var tf = TypesetService.ResolveTypeface(style);
            using var font = new SKFont(tf, (float)style.FontSize);
            using var paint = new SKPaint();
            foreach (var word in block.Translation!.Split(' '))
            {
                float w = font.MeasureText(word, paint);
                Assert.True(w <= box.Width + 0.75f,
                    $"Kata '{word}' terukur {w:F1} px pada ukuran {style.FontSize} " +
                    $"melebihi kotak {box.Width} px — akan terpecah saat digambar.");
            }
        }
        bmp.Dispose();
    }

    /// <summary>Regresi #2 (sumbu X diiris): dua blok berdampingan
    /// dalam SATU bubble — sesudah partisi, sumbu yang tidak diiris
    /// (Y) harus rentang ukur anggota sendiri, BUKAN tinggi union
    /// (v0.7.0 memberi keduanya tinggi union penuh 411 sehingga
    /// teks blok kanan terpusat di tengah union, bukan cupingnya).
    /// Angka mereplikasi geometri halaman asli.</summary>
    [Fact]
    public void Partition_SideBySide_NonSlicedAxisUsesOwnMeasuredRange()
    {
        var left = Block(201, 1514, 157, 286, "Pas aku ganti gaya rambut…");
        var right = Block(368, 1419, 111, 201, "Ah, waktu itu ya…");
        var measured = new Dictionary<TextBlock, SKRectI>
        {
            [left] = new SKRectI(198, 1473, 383, 1830),
            [right] = new SKRectI(217, 1419, 507, 1633),
        };
        var parts = TypesetService.PartitionBubbleBoxes(
            new List<TextBlock> { left, right }, measured);
        _out.WriteLine($"kiri {parts[left]}, kanan {parts[right]}");
        // Sumbu iris (X): berbagi tepi di titik tengah pusat blok.
        Assert.Equal(parts[left].Right, parts[right].Left);
        Assert.Equal(198, parts[left].Left);
        Assert.Equal(507, parts[right].Right);
        // Sumbu non-iris (Y): rentang ukur masing-masing.
        Assert.Equal(1473, parts[left].Top);
        Assert.Equal(1830, parts[left].Bottom);
        Assert.Equal(1419, parts[right].Top);
        Assert.Equal(1633, parts[right].Bottom);
    }

    // ─── Regresi v0.7.3: estimator konservatif multi-baris ───────

    /// <summary>Bubble awan (lingkaran utama + tonjolan 4 arah,
    /// lekukan diagonal di antaranya) menempel tepi kiri halaman
    /// 1020×1254 — mereplikasi sampel asli "page-c".</summary>
    private static SKBitmap CloudBitmap()
    {
        var bmp = new SKBitmap(1020, 1254);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(new SKColor(32, 40, 48));
        using var fill = new SKPaint
        {
            Color = SKColors.White, Style = SKPaintStyle.Fill, IsAntialias = true,
        };
        canvas.DrawCircle(new SKPoint(180, 640), 150, fill);
        canvas.DrawCircle(new SKPoint(55, 640), 55, fill);
        canvas.DrawCircle(new SKPoint(305, 640), 55, fill);
        canvas.DrawCircle(new SKPoint(180, 515), 55, fill);
        canvas.DrawCircle(new SKPoint(180, 765), 55, fill);
        canvas.Flush();
        return bmp;
    }

    [Fact]
    public void EstimateBubbleBox_ScallopedCloudAtPageEdge_NarrowedFromCenterChord()
    {
        using var bmp = CloudBitmap();
        var block = Block(70, 545, 150, 190,
            "Kalau gini, mulai musim semi kita bareng lagi, ya!!");
        var box = TypesetService.EstimateBubbleBox(bmp, block);
        _out.WriteLine($"kotak awan: {box}");
        // Algoritme lama (chord tengah saja) memberi Left=15,
        // Width=331: tepi kiri nyaris di tepi halaman walau
        // interior pada ketinggian ⅓ & ⅔ jauh lebih sempit.
        // Estimator multi-baris harus menarik tepi kiri masuk
        // dan mempersempit kotak mengikuti interior baris sampel.
        Assert.InRange(box.Left, 45, 70);
        Assert.InRange(box.Width, 230, 275);
        Assert.True(box.Width < 331,
            "Kotak harus lebih sempit dari kotak chord-tengah lama (331).");
    }

    [Fact]
    public void Smart_CloudBubbleAtPageEdge_InkStaysInBubble()
    {
        using var bmp = CloudBitmap();
        // Teks panjang (6 baris) — tumpukan mengisi sebagian
        // besar kotak, baris teratas/terbawah duduk di daerah
        // bubble yang menyempit, seperti cacat page-c asli.
        var block = Block(70, 545, 150, 190,
            "Kalau gini, mulai musim semi nanti kita bisa pergi " +
            "bareng lagi, ya!! Aku sudah nggak sabar nunggu hari itu datang.");
        var page = PageWith(block);
        int n = TypesetService.SmartAutoTypesetPage(page, bmp);
        Assert.Equal(1, n);
        var box = TypesetService.RenderBox(block, block.Typeset!);
        _out.WriteLine($"kotak render awan: {box}, ukuran {block.Typeset!.FontSize}");
        using var final = TypesetService.RenderFinalBitmap(bmp, page);
        int inkOnDark = 0, inkOutsideBox = 0, minInkX = int.MaxValue;
        for (int y = 0; y < final.Height; y++)
        for (int x = 0; x < final.Width; x++)
        {
            var bg = bmp.GetPixel(x, y);
            if (bg == final.GetPixel(x, y)) continue;
            if (x < minInkX) minInkX = x;
            bool interior = Math.Min(bg.Red, Math.Min(bg.Green, bg.Blue)) > 200
                && Math.Max(bg.Red, Math.Max(bg.Green, bg.Blue))
                 - Math.Min(bg.Red, Math.Min(bg.Green, bg.Blue)) < 28;
            if (!interior) inkOnDark++;
            if (x < box.Left || x >= box.Right || y < box.Top || y >= box.Bottom)
                inkOutsideBox++;
        }
        _out.WriteLine($"tinta di latar gelap {inkOnDark}, di luar kotak {inkOutsideBox}, x tinta terkiri {minInkX}");
        // Kode lama: 239 px tinta di latar gelap, tinta mulai
        // x=17. Sesudah perbaikan: sisa ≤ 24 px (baris ekstrem
        // di luar pita sampel ⅓–⅔ + antialias tepi) dan tinta
        // terkiri jauh di dalam bubble.
        Assert.True(inkOnDark <= 24,
            $"Tinta di luar bubble: {inkOnDark} px (lama: 239).");
        Assert.True(minInkX >= 50,
            $"Tinta terkiri x={minInkX} masih mepet tepi halaman/bubble.");
        Assert.Equal(0, inkOutsideBox);
    }

    [Fact]
    public void EstimateBubbleBox_PageBGeometry_ShrinkWithin10Percent()
    {
        // Geometri replika tiga bubble halaman demo page-b yang
        // hasilnya sudah bagus di v0.7.1/v0.7.2 (elips + blok
        // asli). Lebar kotak lama terukur pada kode sebelum
        // v0.7.3: reaksi 107, Sakamoto 147, toko 203. Aturan
        // multi-baris tidak boleh menyusutkan kotak-kotak ini
        // lebih dari 10% (dan tidak boleh melebar).
        var cases = new[]
        {
            (Ellipse: new SKRect(1366, 1393, 1486, 1705),
             Block: Block(1391, 1415, 96, 269, "Reaksimu selalu lucu, sih."),
             OldWidth: 107),
            (Ellipse: new SKRect(1105, 173, 1265, 381),
             Block: Block(1132, 202, 103, 146, "Sakamoto-kun?"),
             OldWidth: 147),
            (Ellipse: new SKRect(810, 1430, 1032, 1856),
             Block: Block(842, 1477, 151, 353, "Ke pegawai toko aja dia pakai bahasa sopan. Hebat, ya."),
             OldWidth: 203),
        };
        foreach (var (ellipse, block, oldWidth) in cases)
        {
            var bmp = new SKBitmap(1487, 2048);
            using (var canvas = new SKCanvas(bmp))
            {
                canvas.Clear(new SKColor(32, 40, 48));
                using var fill = new SKPaint
                {
                    Color = SKColors.White, Style = SKPaintStyle.Fill, IsAntialias = true,
                };
                canvas.DrawOval(ellipse, fill);
                canvas.Flush();
            }
            var box = TypesetService.EstimateBubbleBox(bmp, block);
            _out.WriteLine($"blok ({block.X},{block.Y}): lebar lama {oldWidth} -> baru {box.Width}");
            Assert.InRange(box.Width,
                (int)Math.Ceiling(oldWidth * 0.90), oldWidth + 2);
            bmp.Dispose();
        }
    }

    [Fact]
    public void EstimateBubbleBox_TallEllipse_AndRectangle()
    {
        // Elips tinggi rasio 1:2 (200×400): chord pada ⅓ & ⅔
        // tinggi ≈ 94% chord tengah → lebar kotak ≈ 87% chord
        // tengah (sesudah inset), BUKAN selebar chord tengah.
        using (var bmp = PageWithBubbles(new SKRectI(400, 200, 600, 600)))
        {
            var block = Block(450, 350, 100, 100, "Halo!");
            var box = TypesetService.EstimateBubbleBox(bmp, block);
            _out.WriteLine($"elips tinggi: {box}");
            Assert.InRange(box.Width, 165, 182);
            Assert.True(box.Width < 185,
                "Harus menyempit dari hasil chord-tengah lama (185).");
        }
        // Persegi panjang: semua baris selebar chord penuh →
        // lebar ≈ chord penuh sesudah inset (tidak berubah
        // dari perilaku lama, 553).
        var rectBmp = new SKBitmap(MangaFixture.PageWidth, MangaFixture.PageHeight);
        using (var canvas = new SKCanvas(rectBmp))
        {
            canvas.Clear(new SKColor(32, 40, 48));
            using var fill = new SKPaint
            {
                Color = SKColors.White, Style = SKPaintStyle.Fill, IsAntialias = true,
            };
            canvas.DrawRect(new SKRect(200, 200, 800, 600), fill);
            canvas.Flush();
        }
        var rectBlock = Block(400, 300, 200, 200, "Halo!");
        var rectBox = TypesetService.EstimateBubbleBox(rectBmp, rectBlock);
        _out.WriteLine($"persegi: {rectBox}");
        Assert.InRange(rectBox.Width, 540, 560);
        rectBmp.Dispose();
    }

    [Fact]
    public void EstimateBubbleBox_CreamBackgroundThinBorder_NoLeak()
    {
        // Regresi v0.7.4 — halaman berwarna terang ala "page-c"
        // (1020×1254): latar KREM pucat (250,240,225) LOLOS uji
        // interior (min 225 > 200, rentang warna 25 < 28), jadi
        // tanpa penghalang gelap pindai melompati garis tepi
        // bubble yang tipis (2 px — hanya 1–2 sampel gelap,
        // tertelan toleransi > 3) dan bocor di latar sampai tepi
        // halaman: blok kiri asli terukur (0,0,234x614) dari
        // interior sebenarnya ±(55,84)-(210,484). Kini piksel
        // gelap menghentikan pindai seketika.
        var cream = new SKColor(250, 240, 225);
        var bmp = new SKBitmap(1020, 1254);
        using (var canvas = new SKCanvas(bmp))
        {
            canvas.Clear(cream);
            using var fill = new SKPaint
            {
                Color = SKColors.White, Style = SKPaintStyle.Fill, IsAntialias = false,
            };
            using var rim = new SKPaint
            {
                Color = SKColors.Black, Style = SKPaintStyle.Stroke,
                StrokeWidth = 2, IsAntialias = false,
            };
            // Dua bubble bulat-persegi seukuran bubble asli.
            canvas.DrawRoundRect(new SKRect(55, 84, 210, 484), 60, 60, fill);
            canvas.DrawRoundRect(new SKRect(55, 84, 210, 484), 60, 60, rim);
            canvas.DrawRoundRect(new SKRect(815, 50, 1020, 450), 60, 60, fill);
            canvas.DrawRoundRect(new SKRect(815, 50, 1020, 450), 60, 60, rim);
            canvas.Flush();
        }
        // Blok OCR pada posisi asli page-c.
        var left = Block(60, 109, 137, 347, "Teks kiri.");
        var right = Block(855, 75, 155, 326, "Teks kanan.");
        foreach (var (block, interior) in new[]
        {
            (left, new SKRectI(55, 84, 210, 484)),
            (right, new SKRectI(815, 50, 1020, 450)),
        })
        {
            var box = TypesetService.EstimateBubbleBox(bmp, block);
            _out.WriteLine($"blok ({block.X},{block.Y}): {box} (interior {interior})");
            // Tidak menyentuh tepi halaman (bocor lama: top 0).
            Assert.True(box.Top > 0 && box.Left > 0,
                $"Kotak menyentuh tepi halaman: {box}");
            // Dekat interior sebenarnya, tidak membengkak:
            // tinggi < 1,5× tinggi interior, lebar ≤ lebar
            // interior, dan tetap memuat sebagian besar
            // interior (bukan fallback/susut ekstrem).
            Assert.True(box.Height < interior.Height * 3 / 2,
                $"Tinggi {box.Height} membengkak dari interior {interior.Height}.");
            Assert.True(box.Width <= interior.Width,
                $"Lebar {box.Width} melewati interior {interior.Width}.");
            Assert.True(box.Width >= interior.Width * 3 / 4
                        && box.Height >= interior.Height * 3 / 4,
                $"Kotak menyusut jauh dari interior: {box} vs {interior}.");
            Assert.InRange(box.MidX, interior.MidX - 14, interior.MidX + 14);
            Assert.InRange(box.MidY, interior.MidY - 14, interior.MidY + 14);
        }
        bmp.Dispose();
    }

    /// <summary>Regresi #2 (sumbu Y diiris): dua blok bertumpuk
    /// dalam satu bubble — sumbu non-iris (X) harus rentang ukur
    /// anggota sendiri, bukan lebar union.</summary>
    [Fact]
    public void Partition_Stacked_NonSlicedAxisUsesOwnMeasuredRange()
    {
        var top = Block(400, 200, 200, 100, "Bagian atas.");
        var bottom = Block(400, 450, 200, 100, "Bagian bawah.");
        var measured = new Dictionary<TextBlock, SKRectI>
        {
            [top] = new SKRectI(181, 114, 818, 437),
            [bottom] = new SKRectI(154, 315, 847, 684),
        };
        var parts = TypesetService.PartitionBubbleBoxes(
            new List<TextBlock> { top, bottom }, measured);
        _out.WriteLine($"atas {parts[top]}, bawah {parts[bottom]}");
        // Sumbu iris (Y): berbagi tepi di titik tengah pusat blok.
        Assert.Equal(parts[top].Bottom, parts[bottom].Top);
        Assert.Equal(114, parts[top].Top);
        Assert.Equal(684, parts[bottom].Bottom);
        // Sumbu non-iris (X): rentang ukur masing-masing.
        Assert.Equal(181, parts[top].Left);
        Assert.Equal(818, parts[top].Right);
        Assert.Equal(154, parts[bottom].Left);
        Assert.Equal(847, parts[bottom].Right);
    }
}
