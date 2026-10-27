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
