using SkiaSharp;
using TintaStudio.Models;

namespace TintaStudio.Services;

/// <summary>
/// Preset gaya typeset bawaan (Tahap 5): kumpulan PROPERTI gaya,
/// bukan berkas font. Nama preset tercatat di TypesetStyle.PresetName.
/// </summary>
public static class TypesetPresets
{
    public const string Dialog = "Dialog";
    public const string Teriak = "Teriak";
    public const string Bisik = "Bisik";
    public const string Narasi = "Narasi";

    public static IReadOnlyList<string> Names { get; } =
        new[] { Dialog, Teriak, Bisik, Narasi };

    /// <summary>Gaya baru hasil penerapan preset (gaya lama diganti).</summary>
    public static TypesetStyle Create(string name)
    {
        var s = new TypesetStyle();
        switch (name)
        {
            case Teriak:
                s.Bold = true;
                s.ColorHex = "#000000";
                s.StrokeHex = "#FFFFFF";
                s.StrokeWidth = 3;
                s.LetterSpacing = 0.5;
                break;
            case Bisik:
                s.Italic = true;
                s.ColorHex = "#444444";
                break;
            case Narasi:
                s.Italic = true;
                break;
            default:
                name = Dialog;
                break;
        }
        s.PresetName = name;
        return s;
    }

    /// <summary>Terapkan preset ke blok (mengganti gaya typesetnya).</summary>
    public static void ApplyTo(TextBlock block, string name) =>
        block.Typeset = Create(name);
}

/// <summary>
/// Mesin render typeset (Tahap 5): menggambar teks terjemahan ke
/// halaman bersih di dalam kotak render tiap blok, dengan gaya per
/// blok (font sistem, ukuran otomatis/manual, bold/italic, warna,
/// outline stroke, perataan, spasi baris/huruf, vertikal CJK,
/// rotasi sederhana, geser/ubah kotak). Semua digambar dengan
/// SkiaSharp di atas SALINAN bitmap — bitmap dasar tidak diubah.
///
/// Ukuran font otomatis (FontSize = 0): bungkus kata per baris lalu
/// cari (biner) ukuran terbesar yang seluruh teksnya muat di kotak
/// TANPA memecah kata; bila tidak ada ukuran yang memuat semua kata
/// utuh (mis. satu kata memang lebih lebar dari kotak), jatuh ke
/// ukuran terbesar yang muat dengan pemecahan per karakter seperti
/// biasa. Aturan pemecah kata itu sendiri tidak diubah — yang
/// dipilih hanya ukuran fontnya, agar kata tidak terpenggal hanya
/// karena ukuran yang lebih besar kebetulan masih "muat" berkat
/// penggalan.
/// Ukuran manual (FontSize &gt; 0): ukuran itu dipakai apa adanya;
/// teks tetap dibungkus dan boleh meluber keluar kotak bila memang
/// tidak muat — itu pilihan pengguna, digambar apa adanya.
/// </summary>
public static class TypesetService
{
    /// <summary>Gaya bawaan aman (sama dengan nilai bawaan kelas model).</summary>
    public static TypesetStyle DefaultStyle() => new();

    /// <summary>Gaya efektif sebuah blok: Typeset eksplisit atau bawaan.</summary>
    public static TypesetStyle EffectiveStyle(TextBlock block) =>
        block.Typeset ?? DefaultStyle();

    /// <summary>Kotak render blok: kotak blok + geser (Dx,Dy) +
    /// perubahan ukuran (Dw,Dh), dijepit minimum 8×8.</summary>
    public static SKRectI RenderBox(TextBlock block, TypesetStyle style) =>
        new(block.X + style.Dx, block.Y + style.Dy,
            block.X + style.Dx + Math.Max(8, block.Width + style.Dw),
            block.Y + style.Dy + Math.Max(8, block.Height + style.Dh));

    /// <summary>
    /// Keputusan panel peringatan "halaman belum dibersihkan" di
    /// Typesetting (v0.7.16, fungsi murni): tampil HANYA bila
    /// halaman punya blok tapi belum ada pekerjaan bersih sama
    /// sekali — tanpa operasi bersih/sapuan DAN tanpa halaman
    /// bersih tersimpan di CleanStore. Tanpa blok tidak ada yang
    /// tertimpa, jadi tidak ada peringatan; halaman yang sudah
    /// dibersihkan (atau punya hasil bersih tersimpan) juga tidak.
    /// </summary>
    public static bool ShouldWarnPageNotCleaned(
        bool hasBlocks, bool hasCleanWork, bool hasStoredCleanedPage) =>
        hasBlocks && !hasCleanWork && !hasStoredCleanedPage;

    /// <summary>
    /// Apakah bingkai blok digambar di kanvas Typesetting menurut
    /// toggle "Kotak" (v0.7.16): mati = kanvas menampilkan bitmap
    /// final murni untuk review, tanpa bingkai/lencana/pegangan.
    /// </summary>
    public static bool BlockFramesVisible(bool showBoxes) => showBoxes;

    /// <summary>
    /// Apakah klik/seret blok di kanvas Typesetting aktif menurut
    /// toggle "Kotak" (v0.7.16): selama kotak disembunyikan,
    /// suntingan blok dari kanvas dinonaktifkan agar tidak ada
    /// perubahan tak terlihat; geser tampilan (pan) dan zoom
    /// tetap jalan karena bukan suntingan.
    /// </summary>
    public static bool BlockEditingEnabled(bool showBoxes) => showBoxes;

    /// <summary>
    /// Gambar halaman final: salinan <paramref name="basePage"/> +
    /// semua blok berterjemahan digambar. Blok tanpa terjemahan
    /// dilewati. Bitmap masukan tidak diubah.
    /// </summary>
    public static SKBitmap RenderFinalBitmap(SKBitmap basePage, ProjectPage page)
    {
        var result = basePage.Copy();
        using var canvas = new SKCanvas(result);
        foreach (var block in page.Blocks)
        {
            if (string.IsNullOrWhiteSpace(block.Translation))
                continue;
            DrawBlock(canvas, block, EffectiveStyle(block));
        }
        canvas.Flush();
        return result;
    }

    /// <summary>
    /// Halaman final dari bab: dasarnya gambar bersih halaman
    /// (CleanStore — operasi Tahap 3), atau halaman asli bila belum
    /// ada pekerjaan bersih; lalu blok ter-typeset digambar di
    /// atasnya. Pemanggil memiliki bitmap hasil.
    /// </summary>
    public static SKBitmap BuildFinalPageBitmap(
        Chapter chapter, ProjectChapter projectChapter, ProjectPage page,
        CleanService clean, CancellationToken ct = default,
        IProgress<string>? progress = null)
    {
        SKBitmap baseBmp = CleanStore.GetCleanedPageBitmap(
                clean, chapter, projectChapter, page, ct, progress)
            ?? SKBitmap.Decode(chapter.Pages[page.Index].WorkPath)
            ?? throw new InvalidDataException(
                $"Halaman kerja tidak terbaca: {chapter.Pages[page.Index].SourceName}");
        try
        {
            // Jalur pintar (v0.7.0): bitmap dasar sudah di tangan —
            // blok berterjemahan yang belum di-typeset diisi gaya
            // serapi hasil manual (kotak = interior bubble).
            SmartAutoTypesetPage(page, baseBmp);
            return RenderFinalBitmap(baseBmp, page);
        }
        finally
        {
            baseBmp.Dispose();
        }
    }

    /// <summary>Isi Typeset bawaan (preset Dialog) ke semua blok
    /// berterjemahan yang belum punya Typeset. Mengembalikan jumlah
    /// blok yang diisi. Blok yang sudah punya Typeset (eksplisit dari
    /// pengguna) tidak ditimpa. Arah typeset mengikuti arah sumber:
    /// blok OCR VERTIKAL di-typeset vertikal juga (huruf lurus ke
    /// bawah di kotak aslinya), bukan dipaksa horizontal — pengguna
    /// tetap bisa mematikan Vertikal di panel properti bila lebih
    /// suka horizontal. <paramref name="pageWidth"/> = lebar halaman
    /// asal dalam piksel (0 = tidak diketahui).</summary>
    public static int AutoTypesetPage(ProjectPage page, int pageWidth = 0)
    {
        int n = 0;
        foreach (var block in page.Blocks)
        {
            if (block.Typeset is null && !string.IsNullOrWhiteSpace(block.Translation))
            {
                var style = TypesetPresets.Create(TypesetPresets.Dialog);
                if (block.Direction == TextDirection.Vertical)
                    style.Vertical = true;
                block.Typeset = style;
                n++;
            }
        }
        return n;
    }

    /// <summary>Varian bab dari AutoTypesetPage.
    /// <paramref name="pageWidthFor"/> memetakan indeks halaman ke
    /// lebar halamannya (null = lebar tidak diketahui).</summary>
    public static int AutoTypesetChapter(
        ProjectChapter chapter, Func<int, int>? pageWidthFor = null) =>
        chapter.Pages.Sum(p =>
            AutoTypesetPage(p, pageWidthFor?.Invoke(p.Index) ?? 0));

    // ─── Typeset otomatis pintar (v0.7.0) ─────────────────────────
    //
    // Meniru hasil typeset manual yang disetujui pengguna pada
    // halaman manga asli: teks HORIZONTAL terpusat di dalam bubble
    // (bukan di kotak kolom OCR), ukuran font SERAGAM sehalaman, dan
    // font gaya komik bila tersedia. Kuncinya: kotak render = kotak
    // interior bubble yang diukur dari bitmap dasar (halaman bersih
    // bila ada) — DrawHorizontal memang memusatkan isi secara
    // vertikal di kotak render, jadi kotak yang benar = teks yang
    // duduk pas di tengah bubble.
    //
    // Aturan keras yang sama seperti jalur lama: hanya blok
    // berterjemahan yang BELUM punya Typeset yang diisi; gaya
    // eksplisit pengguna tidak pernah ditimpa. Jalur lama tanpa
    // bitmap (AutoTypesetPage) tidak berubah sama sekali.

    /// <summary>Ukuran hasil auto-fit (pencarian kata-utuh
    /// FitLayout) sebuah teks pada kotak tertentu dengan gaya
    /// tertentu — dipakai jalur pintar untuk menyelaraskan ukuran
    /// font sehalaman sebelum gaya disimpan. Gaya yang diberikan
    /// WAJIB gaya final blok itu (keluarga + bold hasil
    /// SmartFontChoice): mengukur dengan typeface lain menghasilkan
    /// ukuran yang tidak berlaku pada typeface gambar.</summary>
    internal static float MeasureAutoFitSize(
        string text, TypesetStyle style, float boxW, float boxH)
    {
        using var typeface = ResolveTypeface(style);
        return FitLayout(text, style, typeface, boxW, boxH).Size;
    }

    /// <summary>Apakah tata letak teks pada SATU ukuran eksplisit
    /// persis (nilai yang akan disimpan &amp; digambar) bersih:
    /// muat di kotak DAN tidak memecah kata — dihitung dengan
    /// typeface gaya itu sendiri. Pemeriksaan pada nilai persis
    /// ini wajib karena lebar glif ter-hint TIDAK monoton terhadap
    /// ukuran: tata letak pada 26,499 bersih (kata terlebar 105 px)
    /// bisa memecah kata pada 26,5 (kata yang sama 109 px) — jadi
    /// ukuran hasil pencarian biner tidak boleh dibulatkan lalu
    /// dipercaya begitu saja (regresi v0.7.0 → v0.7.1).</summary>
    internal static bool LayoutCleanAtSize(
        string text, TypesetStyle style, double size, float boxW, float boxH)
    {
        var probe = style.Clone();
        probe.FontSize = size;
        using var typeface = ResolveTypeface(probe);
        var layout = BuildLayout(text, probe, typeface, (float)size, boxW, boxH);
        return !layout.SplitWord && layout.Fits(probe.Vertical, boxW, boxH);
    }

    /// <summary>Keluarga + keberanian font jalur pintar: "Comic
    /// Neue" tebal bila keluarga itu terdaftar di manajer font
    /// sistem; selain itu sans aman (keluarga kosong, tidak tebal).</summary>
    internal static (string Family, bool Bold) SmartFontChoice()
    {
        bool comic = SKFontManager.Default.FontFamilies
            .Contains("Comic Neue", StringComparer.OrdinalIgnoreCase);
        return comic ? ("Comic Neue", true) : ("", false);
    }

    private static TypesetStyle SmartBaseStyle()
    {
        var (family, bold) = SmartFontChoice();
        var style = TypesetPresets.Create(TypesetPresets.Dialog);
        style.FontFamily = family;
        style.Bold = bold;
        style.Vertical = false;
        return style;
    }

    /// <summary>Piksel "interior bubble": terang dan nyaris netral
    /// (ambang sama seperti algoritme ukur yang terbukti pada
    /// halaman asli — teks Jepang sudah dibersihkan dari bitmap
    /// dasar, jadi interior terbaca polos).</summary>
    private static bool IsBubbleInterior(SKColor px) =>
        Math.Min(px.Red, Math.Min(px.Green, px.Blue)) > 200 &&
        Math.Max(px.Red, Math.Max(px.Green, px.Blue)) -
            Math.Min(px.Red, Math.Min(px.Green, px.Blue)) < 28;

    /// <summary>Piksel GELAP = penghalang keras pindai (v0.7.4):
    /// garis tepi bubble / garis gambar. Latar berwarna terang
    /// (krem pucat ±250,240,225) lolos uji interior karena rentang
    /// warnanya &lt; 28, dan garis tepi bubble yang tipis (±2–3 px)
    /// bisa dilompati langkah 2 px + toleransi &gt; 3 sampel —
    /// pindai lalu bocor ke latar sampai tepi halaman. Karena itu
    /// sampel dengan saluran minimum &lt; 140 menghentikan pindai
    /// SEKETIKA, sebelum hitungan toleransi: toleransi celah hanya
    /// berlaku untuk piksel non-interior yang TIDAK gelap
    /// (anti-alias terang / bercak). Ambang 140 jauh di bawah
    /// interior (&gt; 200) dan di atas garis tepi yang digambar
    /// penuh (hitam/abu tua), jadi anti-alias tepi bubble (abu
    /// muda &gt; 140 di sisi dalam) tidak memicu berhenti dini.</summary>
    internal const int DarkBarrierThreshold = 140;

    private static bool IsDarkBarrier(SKColor px) =>
        Math.Min(px.Red, Math.Min(px.Green, px.Blue)) < DarkBarrierThreshold;

    /// <summary>
    /// Ukur kotak interior bubble sebuah blok dari bitmap dasar:
    /// dari pusat kotak OCR, pindai 4 arah (langkah 2 px) sampai
    /// lebih dari 3 sampel berurutan bukan interior — titik interior
    /// terakhir adalah tepi. Piksel gelap (saluran minimum &lt; 140)
    /// menghentikan pindai seketika di semua pindai (v0.7.4, lihat
    /// IsDarkBarrier): tanpa itu latar terang di luar bubble yang
    /// lolos uji interior membuat pindai melompati garis tepi tipis
    /// dan bocor sampai tepi halaman. Hasil di-inset max(6 px, ~4% dimensi)
    /// per sisi, kecuali sisi yang menyentuh tepi halaman. Fallback:
    /// kotak OCR apa adanya bila lebar/tinggi hasil &lt; 60% kotak
    /// OCR (pindai gagal, mis. bubble berwarna/gelap).
    /// Pagar pengaman: jangkauan pindai per arah dibatasi 2×
    /// dimensi kotak OCR pada sumbu itu — bubble asli selalu
    /// memuat teksnya tak jauh dari kotak teks (rasio hasil yang
    /// terverifikasi ≤ ~1,65), sedangkan interior putih TANPA
    /// batas (halaman polos putih, margin kosong) akan bocor ke
    /// seluruh halaman dan teks mendarat jauh dari bubble-nya.
    /// Konservatif multi-baris (v0.7.3): chord tengah saja tidak
    /// cukup untuk bubble yang menyempit jauh dari tengahnya
    /// (awan bergelombang, terutama yang menempel tepi halaman —
    /// di ketinggian baris atas/bawah interiornya jauh lebih
    /// sempit, sehingga baris-baris itu luber keluar bubble).
    /// Sesudah rentang vertikal dari pindai pusat didapat, rentang
    /// horizontal diukur ULANG pada ketinggian ⅓ dan ⅔ rentang itu
    /// (aturan pindai sama, dari x pusat blok), lalu tepi final
    /// tiap sisi = nilai konservatif kedua dari kumpulan
    /// {pusat, ⅓, ⅔} (kiri: terbesar kedua; kanan: terkecil
    /// kedua) — satu baris menyimpang tidak mendominasi, tetapi
    /// penyempitan yang muncul di kedua baris sampel berlaku.
    /// Hasil tidak pernah MELEBAR melewati chord tengah, dan
    /// baris sampel yang gagal (lebar &lt; 40% chord tengah /
    /// tidak beririsan) dibuang. Bubble persegi/oval lebar nyaris
    /// tidak berubah: chord pada ⅓ &amp; ⅔ tingginya ≈ 94% chord
    /// tengah untuk elips mulus.
    /// </summary>
    internal static SKRectI EstimateBubbleBox(SKBitmap baseBmp, TextBlock block)
    {
        var ocr = new SKRectI(block.X, block.Y,
            block.X + block.Width, block.Y + block.Height);
        int w = baseBmp.Width, h = baseBmp.Height;
        if (w <= 0 || h <= 0)
            return ocr;
        int cx = Math.Clamp(ocr.MidX, 0, w - 1);
        int cy = Math.Clamp(ocr.MidY, 0, h - 1);

        int Scan(int dx, int dy)
        {
            int limit = (dx != 0 ? ocr.Width : ocr.Height) * 2;
            int x = cx, y = cy, last = dx != 0 ? cx : cy, bad = 0, dist = 0;
            while (x >= 0 && x < w && y >= 0 && y < h && dist <= limit)
            {
                var px = baseBmp.GetPixel(x, y);
                // v0.7.4: piksel gelap menghentikan pindai
                // seketika — garis tepi tipis tidak bisa lagi
                // dilompati toleransi celah.
                if (IsDarkBarrier(px))
                    break;
                if (IsBubbleInterior(px))
                {
                    last = dx != 0 ? x : y;
                    bad = 0;
                }
                else if (++bad > 3)
                    break;
                x += dx * 2;
                y += dy * 2;
                dist += 2;
            }
            return last;
        }

        // Pindai horizontal pada baris y tertentu dari x pusat —
        // aturan sama persis seperti pindai pusat (langkah 2,
        // berhenti sesudah > 3 sampel non-interior, pagar 2×
        // lebar kotak OCR per arah, penghalang gelap v0.7.4).
        int ScanRow(int y, int dx)
        {
            int limit = ocr.Width * 2;
            int x = cx, last = cx, bad = 0, dist = 0;
            while (x >= 0 && x < w && dist <= limit)
            {
                var px = baseBmp.GetPixel(x, y);
                if (IsDarkBarrier(px))
                    break;
                if (IsBubbleInterior(px))
                {
                    last = x;
                    bad = 0;
                }
                else if (++bad > 3)
                    break;
                x += dx * 2;
                dist += 2;
            }
            return last;
        }

        int left = Scan(-1, 0), right = Scan(1, 0);
        int top = Scan(0, -1), bottom = Scan(0, 1);

        // v0.7.3: ukur ulang rentang horizontal pada ketinggian
        // ⅓ dan ⅔ rentang vertikal mentah, lalu persempit tepi
        // ke nilai konservatif kedua (lihat catatan di atas).
        int centerLeft = left, centerRight = right;
        int rawWidth = centerRight - centerLeft + 1;
        int spanY = bottom - top;
        if (rawWidth >= 8 && spanY >= 24)
        {
            var lefts = new List<int> { centerLeft };
            var rights = new List<int> { centerRight };
            foreach (int y in new[] { top + spanY / 3, top + 2 * spanY / 3 })
            {
                if (y == cy)
                    continue;
                int rowLeft = ScanRow(y, -1), rowRight = ScanRow(y, 1);
                int rowWidth = rowRight - rowLeft + 1;
                // Baris gagal (interior tak ditemukan / menyusut
                // tak masuk akal) tidak ikut menentukan.
                if (rowWidth < Math.Max(8, rawWidth * 2 / 5)
                    || rowLeft > centerRight || rowRight < centerLeft)
                    continue;
                lefts.Add(rowLeft);
                rights.Add(rowRight);
            }
            left = ConservativeEdge(lefts, leftSide: true);
            right = ConservativeEdge(rights, leftSide: false);
            // Tidak pernah melebar melewati chord tengah.
            left = Math.Max(left, centerLeft);
            right = Math.Min(right, centerRight);
        }

        int insetX = Math.Max(6, (right - left + 1) * 4 / 100);
        int insetY = Math.Max(6, (bottom - top + 1) * 4 / 100);
        if (left > 0) left += insetX;
        if (right < w - 1) right -= insetX;
        if (top > 0) top += insetY;
        if (bottom < h - 1) bottom -= insetY;
        var box = new SKRectI(left, top, right + 1, bottom + 1);
        if (box.Width < ocr.Width * 0.6 || box.Height < ocr.Height * 0.6
            || box.Width < 8 || box.Height < 8)
            return ocr;
        return box;
    }

    /// <summary>Tepi konservatif kedua dari kumpulan sampel tepi
    /// satu sisi (v0.7.3): untuk sisi kiri nilai terbesar kedua,
    /// untuk sisi kanan nilai terkecil kedua. Satu sampel (hanya
    /// chord tengah yang sah) → nilai itu sendiri; dua sampel →
    /// yang lebih konservatif dari keduanya.</summary>
    private static int ConservativeEdge(List<int> samples, bool leftSide)
    {
        var sorted = samples.OrderBy(v => v).ToList();
        if (sorted.Count == 1)
            return sorted[0];
        if (sorted.Count == 2)
            return leftSide ? sorted[1] : sorted[0];
        return leftSide ? sorted[^2] : sorted[1];
    }

    /// <summary>
    /// Partisi kotak-kotak bubble hasil ukur: blok yang kotak
    /// ukurnya beririsan &gt; 35% dari luas kotak yang lebih kecil
    /// dianggap berbagi SATU bubble. Grup digabung (union) lalu
    /// diiris pada titik tengah antar pusat blok yang berurutan di
    /// sepanjang sumbu dengan sebaran pusat terbesar, sehingga tiap
    /// anggota menerima potongan union yang tidak tumpang tindih.
    /// Sumbu yang TIDAK diiris memakai rentang ukur anggota itu
    /// SENDIRI, bukan rentang union: cuping bubble tempat sebuah
    /// blok duduk umumnya tidak setinggi/setebal union seluruh
    /// bubble — memakai union membuat teks anggota terpusat di
    /// tengah union (jauh dari cupingnya) dan bertabrakan visual
    /// dengan teks anggota lain (regresi v0.7.0 → v0.7.1).
    /// Blok tunggal memakai kotak ukurnya sendiri.
    /// </summary>
    internal static Dictionary<TextBlock, SKRectI> PartitionBubbleBoxes(
        IReadOnlyList<TextBlock> blocks,
        IReadOnlyDictionary<TextBlock, SKRectI> measured)
    {
        var result = new Dictionary<TextBlock, SKRectI>();
        var assigned = new HashSet<TextBlock>();
        foreach (var seed in blocks)
        {
            if (assigned.Contains(seed))
                continue;
            // Grup = komponen terhubung lewat irisan > 35%.
            var group = new List<TextBlock> { seed };
            assigned.Add(seed);
            for (int i = 0; i < group.Count; i++)
            {
                foreach (var other in blocks)
                {
                    if (assigned.Contains(other))
                        continue;
                    var a = measured[group[i]];
                    var b = measured[other];
                    var inter = SKRectI.Intersect(a, b);
                    int smaller = Math.Min(a.Width * a.Height, b.Width * b.Height);
                    if (!inter.IsEmpty && smaller > 0 &&
                        (double)inter.Width * inter.Height > 0.35 * smaller)
                    {
                        group.Add(other);
                        assigned.Add(other);
                    }
                }
            }
            if (group.Count == 1)
            {
                result[seed] = measured[seed];
                continue;
            }
            var union = measured[group[0]];
            foreach (var m in group.Skip(1))
                union = SKRectI.Union(union, measured[m]);
            double Spread(bool xAxis) =>
                group.Max(b => xAxis ? (double)CenterOf(b).X : CenterOf(b).Y)
              - group.Min(b => xAxis ? (double)CenterOf(b).X : CenterOf(b).Y);
            bool vertical = Spread(false) >= Spread(true);
            var ordered = group.OrderBy(b =>
                vertical ? (double)CenterOf(b).Y : CenterOf(b).X).ToList();
            int start = vertical ? union.Top : union.Left;
            for (int i = 0; i < ordered.Count; i++)
            {
                int end;
                if (i == ordered.Count - 1)
                    end = vertical ? union.Bottom : union.Right;
                else
                {
                    var c1 = CenterOf(ordered[i]);
                    var c2 = CenterOf(ordered[i + 1]);
                    end = (int)Math.Round(
                        ((vertical ? c1.Y : c1.X) + (vertical ? c2.Y : c2.X)) / 2.0);
                }
                var own = measured[ordered[i]];
                result[ordered[i]] = vertical
                    ? new SKRectI(own.Left, start, own.Right, end)
                    : new SKRectI(start, own.Top, end, own.Bottom);
                start = end;
            }
        }
        return result;

        static (float X, float Y) CenterOf(TextBlock b) =>
            (b.X + b.Width / 2f, b.Y + b.Height / 2f);
    }

    /// <summary>
    /// Isi gaya typeset pintar ke blok berterjemahan yang belum
    /// punya Typeset, diukur dari bitmap dasar halaman. Mengembalikan
    /// jumlah blok yang diisi. Lihat catatan bagian di atas untuk
    /// aturan lengkapnya.
    /// </summary>
    public static int SmartAutoTypesetPage(ProjectPage page, SKBitmap baseBmp)
    {
        var targets = page.Blocks
            .Where(b => b.Typeset is null
                        && !string.IsNullOrWhiteSpace(b.Translation))
            .ToList();
        if (targets.Count == 0)
            return 0;

        var measured = targets.ToDictionary(
            b => b, b => EstimateBubbleBox(baseBmp, b));
        var boxes = PartitionBubbleBoxes(targets, measured);
        // Gaya final tiap blok dibuat SEKALI dan dipakai untuk
        // SEMUA pengukuran blok itu — pengukuran auto-fit wajib
        // memakai typeface final (keluarga + bold SmartFontChoice)
        // yang sama dengan yang nanti menggambar.
        var styles = targets.ToDictionary(b => b, _ => SmartBaseStyle());

        var fits = targets.ToDictionary(b => b, b =>
            MeasureAutoFitSize(b.Translation!, styles[b],
                boxes[b].Width, boxes[b].Height));
        var sorted = fits.Values.OrderBy(v => v).ToList();
        double target = sorted[Math.Min(sorted.Count - 1,
            (int)Math.Ceiling(sorted.Count * 0.75) - 1)];
        target = Math.Clamp(target, 20, 28);

        foreach (var b in targets)
        {
            var box = boxes[b];
            var style = styles[b];
            double size = Math.Round(
                Math.Max(14, Math.Min(target, fits[b])), 1);
            // Validasi pada nilai tersimpan PERSIS: lebar glif
            // ter-hint tidak monoton, jadi hasil bulatan bisa
            // memecah kata walau ukuran pencarian binernya bersih
            // (lihat LayoutCleanAtSize). Turunkan bertahap sampai
            // tata letak pada ukuran itu sendiri bersih; lantai
            // 14 px sama seperti sebelumnya.
            while (size > 14 && !LayoutCleanAtSize(
                       b.Translation!, style, size, box.Width, box.Height))
                size = Math.Round(size - 0.5, 1);
            style.FontSize = size;
            style.Dx = box.Left - b.X;
            style.Dy = box.Top - b.Y;
            style.Dw = box.Width - b.Width;
            style.Dh = box.Height - b.Height;
            b.Typeset = style;
        }
        return targets.Count;
    }

    /// <summary>Varian bab dari SmartAutoTypesetPage:
    /// <paramref name="baseBitmapFor"/> memetakan indeks halaman ke
    /// bitmap dasarnya (null = lewati halaman itu).</summary>
    public static int SmartAutoTypesetChapter(
        ProjectChapter chapter, Func<int, SKBitmap?> baseBitmapFor) =>
        chapter.Pages.Sum(p =>
        {
            using var bmp = baseBitmapFor(p.Index);
            return bmp is null ? 0 : SmartAutoTypesetPage(p, bmp);
        });

    // ─── Font ────────────────────────────────────────────────────────

    /// <summary>Keluarga font yang tersedia: kandidat sans aman yang
    /// benar-benar ada ditaruh teratas, sisanya semua keluarga dari
    /// SKFontManager sistem (di Windows = font terpasang pengguna),
    /// urut abjad.</summary>
    public static IReadOnlyList<string> AvailableFonts()
    {
        var all = SKFontManager.Default.FontFamilies
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var safe = new[] { "DejaVu Sans", "Noto Sans", "Liberation Sans",
                           "Arial", "Helvetica", "Segoe UI" };
        var head = safe.Where(s => all.Contains(s, StringComparer.OrdinalIgnoreCase))
                       .ToList();
        var rest = all.Except(head, StringComparer.OrdinalIgnoreCase)
                      .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                      .ToList();
        return head.Concat(rest).ToList();
    }

    /// <summary>Selesaikan typeface untuk gaya: keluarga diminta →
    /// rantai sans aman → typeface bawaan Skia. Bold/italic dipetakan
    /// ke SKFontStyle.</summary>
    public static SKTypeface ResolveTypeface(TypesetStyle style)
    {
        var fontStyle = new SKFontStyle(
            style.Bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal,
            SKFontStyleWidth.Normal,
            style.Italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright);
        if (!string.IsNullOrWhiteSpace(style.FontFamily))
        {
            var hit = SKFontManager.Default.MatchFamily(style.FontFamily, fontStyle);
            if (hit is not null)
                return hit;
        }
        foreach (var fam in new[] { "DejaVu Sans", "Noto Sans",
                                    "Liberation Sans", "Arial", "Helvetica" })
        {
            var hit = SKFontManager.Default.MatchFamily(fam, fontStyle);
            if (hit is not null)
                return hit;
        }
        return SKTypeface.FromFamilyName("", fontStyle) ?? SKTypeface.Default;
    }

    // ─── Tata letak ──────────────────────────────────────────────────

    /// <summary>Hasil tata letak satu blok pada satu ukuran font.</summary>
    private sealed class Layout
    {
        public float Size;
        public List<string> Lines { get; } = new();          // horizontal
        public List<float> LineWidths { get; } = new();
        public List<List<char>> Columns { get; } = new();    // vertikal
        public float ColumnWidth;
        public float LineAdvance;   // jarak antar baris / antar huruf vertikal
        public float GlyphHeight;   // Descent − Ascent
        public float Ascent;        // positif: jarak puncak ke baseline
        public bool SplitWord;      // ada kata yang dipecah per karakter

        public float ContentWidth(bool vertical) => vertical
            ? Columns.Count * ColumnWidth
            : (LineWidths.Count == 0 ? 0 : LineWidths.Max());

        public float ContentHeight(bool vertical) => vertical
            ? (Columns.Count == 0 ? 0
                : Columns.Max(c => c.Count) * LineAdvance - (LineAdvance - GlyphHeight))
            : Lines.Count == 0 ? 0
                : LineAdvance * (Lines.Count - 1) + GlyphHeight;

        public bool Fits(bool vertical, float w, float h) =>
            ContentWidth(vertical) <= w + 0.75f &&
            ContentHeight(vertical) <= h + 0.75f;
    }

    private static Layout BuildLayout(
        string text, TypesetStyle style, SKTypeface typeface, float size,
        float boxW, float boxH)
    {
        using var font = new SKFont(typeface, size);
        using var measure = new SKPaint();
        var metrics = font.Metrics;
        float glyphHeight = metrics.Descent - metrics.Ascent;
        var layout = new Layout
        {
            Size = size,
            GlyphHeight = glyphHeight,
            Ascent = -metrics.Ascent,
        };
        var advances = new Dictionary<char, float>();
        float Advance(char c)
        {
            if (!advances.TryGetValue(c, out var a))
                advances[c] = a = font.MeasureText(c.ToString(), measure);
            return a;
        }
        float TextWidth(string s)
        {
            float w = 0;
            foreach (var c in s)
                w += Advance(c);
            if (s.Length > 1)
                w += (float)style.LetterSpacing * (s.Length - 1);
            return w;
        }

        if (style.Vertical)
        {
            layout.LineAdvance = size * (float)style.LineSpacing;
            layout.ColumnWidth = Math.Max(size * 0.6f,
                text.Length == 0 ? size : text.Max(Advance));
            var column = new List<char>();
            void FlushColumn()
            {
                if (column.Count > 0)
                {
                    layout.Columns.Add(column);
                    column = new List<char>();
                }
            }
            foreach (var c in text)
            {
                if (c == '\n') { FlushColumn(); continue; }
                if (column.Count == 0 && c == ' ')
                    continue; // spasi di awal kolom dibuang
                if (column.Count > 0 &&
                    (column.Count + 1) * layout.LineAdvance -
                        (layout.LineAdvance - glyphHeight) > boxH + 0.75f)
                    FlushColumn();
                column.Add(c);
            }
            FlushColumn();
            return layout;
        }

        layout.LineAdvance = glyphHeight * (float)style.LineSpacing;
        float spaceWidth = Advance(' ') + (float)style.LetterSpacing;
        foreach (var paragraph in text.Replace("\r\n", "\n").Split('\n'))
        {
            var words = paragraph.Split(' ',
                StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0)
            {
                layout.Lines.Add("");
                layout.LineWidths.Add(0);
                continue;
            }
            var line = new List<string>();
            float lineWidth = 0;
            void FlushLine()
            {
                layout.Lines.Add(string.Join(' ', line));
                layout.LineWidths.Add(lineWidth);
                line = new List<string>();
                lineWidth = 0;
            }
            foreach (var word in words)
            {
                float wordWidth = TextWidth(word);
                if (wordWidth > boxW + 0.75f && TextWidth(word) > 0)
                {
                    // Kata lebih lebar dari kotak: pecah per karakter.
                    layout.SplitWord = true;
                    if (line.Count > 0)
                        FlushLine();
                    var chunk = "";
                    float chunkWidth = 0;
                    foreach (var c in word)
                    {
                        float cw = Advance(c) +
                            (chunk.Length > 0 ? (float)style.LetterSpacing : 0);
                        if (chunk.Length > 0 && chunkWidth + cw > boxW + 0.75f)
                        {
                            layout.Lines.Add(chunk);
                            layout.LineWidths.Add(chunkWidth);
                            chunk = "";
                            chunkWidth = 0;
                            cw = Advance(c);
                        }
                        chunk += c;
                        chunkWidth += cw;
                    }
                    if (chunk.Length > 0)
                    {
                        line.Add(chunk);
                        lineWidth = chunkWidth;
                    }
                    continue;
                }
                float added = line.Count == 0 ? wordWidth : lineWidth + spaceWidth + wordWidth;
                if (line.Count > 0 && added > boxW + 0.75f)
                    FlushLine();
                line.Add(word);
                lineWidth = line.Count == 1 && lineWidth == 0
                    ? wordWidth
                    : lineWidth + spaceWidth + wordWidth;
                if (line.Count == 1)
                    lineWidth = wordWidth;
            }
            if (line.Count > 0)
                FlushLine();
        }
        return layout;
    }

    /// <summary>Pilih ukuran font: manual bila FontSize &gt; 0;
    /// selain itu pencarian biner ukuran terbesar yang muat —
    /// horizontal diutamakan yang tidak memecah kata (lihat
    /// ringkasan kelas); vertikal diutamakan SATU kolom lurus ke
    /// bawah selama ukurannya masih terbaca (≥ 12 px) — teks yang
    /// terlalu panjang untuk satu kolom jatuh ke pencarian biasa
    /// (banyak kolom, kanan ke kiri). Bila tidak ada yang muat,
    /// dipakai hasil pencarian biasa.</summary>
    private static Layout FitLayout(
        string text, TypesetStyle style, SKTypeface typeface,
        float boxW, float boxH)
    {
        if (style.FontSize > 0)
            return BuildLayout(text, style, typeface, (float)style.FontSize, boxW, boxH);
        if (style.Vertical)
        {
            var single = SearchFit(text, style, typeface, boxW, boxH,
                requireNoSplit: false, maxColumns: 1);
            if (single is not null && single.Size >= 12f)
                return single;
        }
        else
        {
            var unsplit = SearchFit(text, style, typeface, boxW, boxH,
                requireNoSplit: true);
            if (unsplit is not null)
                return unsplit;
        }
        return SearchFit(text, style, typeface, boxW, boxH,
            requireNoSplit: false)!;
    }

    /// <summary>Pencarian biner ukuran terbesar yang tata letaknya
    /// muat di kotak. Dengan <paramref name="requireNoSplit"/>, hanya
    /// tata letak tanpa kata terpecah yang diterima dan null
    /// dikembalikan bila tidak ada. Dengan
    /// <paramref name="maxColumns"/> &gt; 0 (vertikal), hanya tata
    /// letak dengan jumlah kolom ≤ itu yang diterima. Predikat
    /// "muat (dan utuh, dan kolom terbatas)" adalah awalan terhadap
    /// ukuran — yang muat pada satu ukuran muat pada semua ukuran
    /// lebih kecil — jadi pencarian biner sah.</summary>
    private static Layout? SearchFit(
        string text, TypesetStyle style, SKTypeface typeface,
        float boxW, float boxH, bool requireNoSplit, int maxColumns = 0)
    {
        float hi = Math.Clamp(Math.Max(boxW, boxH) * 1.6f, 16f, 1200f);
        float lo = 4f;
        Layout? best = null;
        for (int i = 0; i < 16; i++)
        {
            float mid = (lo + hi) / 2;
            var candidate = BuildLayout(text, style, typeface, mid, boxW, boxH);
            bool ok = candidate.Fits(style.Vertical, boxW, boxH)
                      && (!requireNoSplit || !candidate.SplitWord)
                      && (maxColumns <= 0 || candidate.Columns.Count <= maxColumns);
            if (ok)
            {
                best = candidate;
                lo = mid;
            }
            else
            {
                hi = mid;
            }
        }
        // Sama seperti perilaku lama: bila tidak ada yang muat sama
        // sekali, gambar pada ukuran minimum apa adanya.
        return best ?? (requireNoSplit || maxColumns > 0
            ? null
            : BuildLayout(text, style, typeface, lo, boxW, boxH));
    }

    // ─── Gambar ──────────────────────────────────────────────────────

    private static void DrawBlock(SKCanvas canvas, TextBlock block, TypesetStyle style)
    {
        var box = RenderBox(block, style);
        int rotation = ((style.Rotation % 360) + 360) % 360;
        bool swapped = rotation is 90 or 270;
        float drawW = swapped ? box.Height : box.Width;
        float drawH = swapped ? box.Width : box.Height;
        // Kotak gambar dalam ruang terotasi (berpusat sama dengan kotak render).
        var drawBox = new SKRect(
            box.MidX - drawW / 2, box.MidY - drawH / 2,
            box.MidX + drawW / 2, box.MidY + drawH / 2);

        using var typeface = ResolveTypeface(style);
        var layout = FitLayout(block.Translation!, style, typeface, drawW, drawH);
        using var font = new SKFont(typeface, layout.Size);
        using var fill = new SKPaint
        {
            Color = CleanService.ParseColor(style.ColorHex),
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
        };
        using var stroke = new SKPaint
        {
            Color = CleanService.ParseColor(style.StrokeHex),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = (float)style.StrokeWidth,
            StrokeJoin = SKStrokeJoin.Round,
            IsAntialias = true,
        };
        bool hasStroke = style.StrokeWidth > 0.05;

        canvas.Save();
        if (rotation != 0)
        {
            canvas.Translate(box.MidX, box.MidY);
            canvas.RotateDegrees(rotation);
            canvas.Translate(-box.MidX, -box.MidY);
        }
        try
        {
            if (style.Vertical)
                DrawVertical(canvas, layout, style, font, fill, stroke, hasStroke, drawBox);
            else
                DrawHorizontal(canvas, layout, style, font, fill, stroke, hasStroke, drawBox);
        }
        finally
        {
            canvas.Restore();
        }
    }

    private static void DrawGlyph(SKCanvas canvas, string glyph, float x, float baseline,
        SKFont font, SKPaint fill, SKPaint stroke, bool hasStroke)
    {
        if (hasStroke)
            canvas.DrawText(glyph, x, baseline, SKTextAlign.Left, font, stroke);
        canvas.DrawText(glyph, x, baseline, SKTextAlign.Left, font, fill);
    }

    private static void DrawHorizontal(
        SKCanvas canvas, Layout layout, TypesetStyle style, SKFont font,
        SKPaint fill, SKPaint stroke, bool hasStroke, SKRect box)
    {
        using var measure = new SKPaint();
        float contentHeight = layout.ContentHeight(false);
        float top = box.Top + Math.Max(0, (box.Height - contentHeight) / 2);
        for (int i = 0; i < layout.Lines.Count; i++)
        {
            string line = layout.Lines[i];
            float width = layout.LineWidths[i];
            float x = style.Align switch
            {
                TypesetAlign.Left => box.Left,
                TypesetAlign.Right => box.Right - width,
                _ => box.Left + (box.Width - width) / 2,
            };
            float baseline = top + layout.Ascent + i * layout.LineAdvance;
            foreach (var c in line)
            {
                var glyph = c.ToString();
                DrawGlyph(canvas, glyph, x, baseline, font, fill, stroke, hasStroke);
                x += font.MeasureText(glyph, measure) + (float)style.LetterSpacing;
            }
        }
    }

    private static void DrawVertical(
        SKCanvas canvas, Layout layout, TypesetStyle style, SKFont font,
        SKPaint fill, SKPaint stroke, bool hasStroke, SKRect box)
    {
        using var measure = new SKPaint();
        float contentWidth = layout.ContentWidth(true);
        // Kolom pertama menempel di kanan; gugus kolom terpusat dalam kotak.
        float right = box.Left + (box.Width + contentWidth) / 2;
        float contentHeight = layout.ContentHeight(true);
        float top = box.Top + Math.Max(0, (box.Height - contentHeight) / 2);
        for (int j = 0; j < layout.Columns.Count; j++)
        {
            float colRight = right - j * layout.ColumnWidth;
            var column = layout.Columns[j];
            for (int k = 0; k < column.Count; k++)
            {
                var glyph = column[k].ToString();
                float gw = font.MeasureText(glyph, measure);
                float x = colRight - layout.ColumnWidth +
                          (layout.ColumnWidth - gw) / 2;
                float cellTop = top + k * layout.LineAdvance;
                float baseline = cellTop + layout.Ascent +
                    (layout.LineAdvance - layout.GlyphHeight) / 2;
                DrawGlyph(canvas, glyph, x, baseline, font, fill, stroke, hasStroke);
            }
        }
    }
}
