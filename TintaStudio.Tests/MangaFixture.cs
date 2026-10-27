using System.IO.Compression;
using SkiaSharp;

namespace TintaStudio.Tests;

/// <summary>
/// Halaman manga sintetis untuk tes Tahap 2: bubble elips putih
/// bergaris hitam berisi teks yang DIKETAHUI, digambar dengan
/// SkiaSharp + font Noto Sans CJK sistem (fixture saja — tidak
/// dibundel ke aplikasi). Bila font tidak ada, diunduh dari
/// Google Fonts ke folder sementara.
/// </summary>
public static class MangaFixture
{
    public const int PageWidth = 1000;
    public const int PageHeight = 1400;

    /// <summary>Bubble A: Jepang horizontal 「こんにちは」.</summary>
    public static readonly SKRectI BubbleJaH = new(80, 140, 520, 360);
    /// <summary>Bubble B: Jepang vertikal 「ありがとう」.</summary>
    public static readonly SKRectI BubbleJaV = new(690, 240, 870, 760);
    /// <summary>Bubble C: Korea 「안녕하세요」.</summary>
    public static readonly SKRectI BubbleKo = new(80, 560, 560, 760);
    /// <summary>Bubble D: Cina 「谢谢你」.</summary>
    public static readonly SKRectI BubbleCh = new(80, 960, 520, 1160);
    /// <summary>Bubble dua baris: 「こんにちは」 + 「せかい」.</summary>
    public static readonly SKRectI BubbleTwoLines = new(120, 300, 640, 560);
    /// <summary>Bubble E: Inggris horizontal "Hello world".</summary>
    public static readonly SKRectI BubbleEn = new(80, 560, 560, 760);
    /// <summary>Bubble campur A: Jepang horizontal 「こんにちは」.</summary>
    public static readonly SKRectI BubbleMixJa = new(80, 140, 560, 340);
    /// <summary>Bubble campur B: Inggris horizontal "Hello world".</summary>
    public static readonly SKRectI BubbleMixEn = new(80, 900, 620, 1120);

    public static SKTypeface TypefaceFor(string lang)
    {
        var families = lang switch
        {
            "ko" => new[] { "Noto Sans CJK KR", "Noto Sans KR", "Noto Sans CJK" },
            "ch" => new[] { "Noto Sans CJK SC", "Noto Sans SC", "Noto Sans CJK" },
            "en" => new[] { "Noto Sans", "Liberation Sans", "DejaVu Sans" },
            _ => new[] { "Noto Sans CJK JP", "Noto Sans JP", "Noto Sans CJK" },
        };
        foreach (var fam in families)
        {
            var tf = SKFontManager.Default.MatchFamily(fam);
            if (tf is not null)
                return tf;
        }
        // Cadangan: unduh font dari Google Fonts (aset fixture).
        var dir = Path.Combine(Path.GetTempPath(), "tinta-test-fonts");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, lang == "en" ? "roboto-en.ttf" : $"noto-{lang}.otf");
        if (!File.Exists(file))
        {
            var url = lang switch
            {
                "ko" => "https://github.com/googlefonts/noto-cjk/raw/main/Sans/OTF/Korean/NotoSansCJKkr-Regular.otf",
                "ch" => "https://github.com/googlefonts/noto-cjk/raw/main/Sans/OTF/SimplifiedChinese/NotoSansCJKsc-Regular.otf",
                "en" => "https://github.com/google/fonts/raw/main/apache/roboto/Roboto-Regular.ttf",
                _ => "https://github.com/googlefonts/noto-cjk/raw/main/Sans/OTF/Japanese/NotoSansCJKjp-Regular.otf",
            };
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
            var bytes = http.GetByteArrayAsync(url).GetAwaiter().GetResult();
            File.WriteAllBytes(file, bytes);
        }
        return SKTypeface.FromFile(file)
            ?? throw new InvalidOperationException("Font fixture tidak dapat dimuat.");
    }

    private static void DrawBubble(SKCanvas canvas, SKRectI rect, bool rounded = false)
    {
        using var fill = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Fill };
        using var stroke = new SKPaint
        {
            Color = SKColors.Black,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 4,
            IsAntialias = true,
        };
        var r = new SKRect(rect.Left, rect.Top, rect.Right, rect.Bottom);
        if (rounded)
        {
            canvas.DrawRoundRect(r, 80, 80, fill);
            canvas.DrawRoundRect(r, 80, 80, stroke);
        }
        else
        {
            canvas.DrawOval(r, fill);
            canvas.DrawOval(r, stroke);
        }
    }

    private static void DrawCenteredText(SKCanvas canvas, string text, string lang,
        SKRectI rect, float size)
    {
        using var tf = TypefaceFor(lang);
        using var font = new SKFont(tf, size);
        using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = true };
        float w = font.MeasureText(text, paint);
        float x = rect.MidX - w / 2;
        float y = rect.MidY + size * 0.35f;
        canvas.DrawText(text, x, y, SKTextAlign.Left, font, paint);
    }

    private static void DrawVerticalText(SKCanvas canvas, string text, string lang,
        SKRectI rect, float size)
    {
        using var tf = TypefaceFor(lang);
        using var font = new SKFont(tf, size);
        using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = true };
        float y = rect.Top + size * 1.05f;
        foreach (var ch in text)
        {
            var s = ch.ToString();
            float w = font.MeasureText(s, paint);
            canvas.DrawText(s, rect.MidX - w / 2, y, SKTextAlign.Left, font, paint);
            y += size * 1.12f;
        }
    }

    private static SKBitmap NewPage()
    {
        var bmp = new SKBitmap(PageWidth, PageHeight);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.White);
        return bmp;
    }

    /// <summary>Halaman Jepang: bubble horizontal + vertikal.</summary>
    public static SKBitmap JapanesePage()
    {
        var bmp = NewPage();
        using var canvas = new SKCanvas(bmp);
        DrawBubble(canvas, BubbleJaH);
        DrawCenteredText(canvas, "こんにちは", "ja", BubbleJaH, 64);
        DrawBubble(canvas, BubbleJaV, rounded: true);
        DrawVerticalText(canvas, "ありがとう", "ja", BubbleJaV, 56);
        canvas.Flush();
        return bmp;
    }

    /// <summary>Halaman Korea satu bubble.</summary>
    public static SKBitmap KoreanPage()
    {
        var bmp = NewPage();
        using var canvas = new SKCanvas(bmp);
        DrawBubble(canvas, BubbleKo);
        DrawCenteredText(canvas, "안녕하세요", "ko", BubbleKo, 60);
        canvas.Flush();
        return bmp;
    }

    /// <summary>Halaman Cina satu bubble.</summary>
    public static SKBitmap ChinesePage()
    {
        var bmp = NewPage();
        using var canvas = new SKCanvas(bmp);
        DrawBubble(canvas, BubbleCh);
        DrawCenteredText(canvas, "谢谢你", "ch", BubbleCh, 72);
        canvas.Flush();
        return bmp;
    }

    /// <summary>Halaman Inggris satu bubble: teks Latin horizontal.</summary>
    public static SKBitmap EnglishPage()
    {
        var bmp = NewPage();
        using var canvas = new SKCanvas(bmp);
        DrawBubble(canvas, BubbleEn);
        DrawCenteredText(canvas, "Hello world", "en", BubbleEn, 58);
        canvas.Flush();
        return bmp;
    }

    /// <summary>
    /// Halaman campur: satu bubble Jepang + satu bubble Inggris,
    /// untuk tes mode Otomatis (bahasa per blok harus benar dua-duanya).
    /// </summary>
    public static SKBitmap MixedPage()
    {
        var bmp = NewPage();
        using var canvas = new SKCanvas(bmp);
        DrawBubble(canvas, BubbleMixJa);
        DrawCenteredText(canvas, "こんにちは", "ja", BubbleMixJa, 58);
        DrawBubble(canvas, BubbleMixEn);
        DrawCenteredText(canvas, "Hello world", "en", BubbleMixEn, 54);
        canvas.Flush();
        return bmp;
    }

    /// <summary>Satu bubble berisi dua baris teks Jepang.</summary>
    public static SKBitmap TwoLinePage()
    {
        var bmp = NewPage();
        using var canvas = new SKCanvas(bmp);
        DrawBubble(canvas, BubbleTwoLines);
        var line1 = new SKRectI(BubbleTwoLines.Left, BubbleTwoLines.Top + 30,
            BubbleTwoLines.Right, BubbleTwoLines.MidY + 10);
        var line2 = new SKRectI(BubbleTwoLines.Left, BubbleTwoLines.MidY - 10,
            BubbleTwoLines.Right, BubbleTwoLines.Bottom - 30);
        DrawCenteredText(canvas, "こんにちは", "ja", line1, 58);
        DrawCenteredText(canvas, "せかい", "ja", line2, 58);
        canvas.Flush();
        return bmp;
    }

    /// <summary>Tulis halaman sebagai PNG.</summary>
    public static void SavePng(SKBitmap bmp, string path)
    {
        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        using var fs = File.Create(path);
        data.SaveTo(fs);
    }

    /// <summary>Buat CBZ dari satu halaman + ComicInfo.xml.</summary>
    public static string MakeCbz(SKBitmap page, string dir, string fileName = "fixture.cbz")
    {
        Directory.CreateDirectory(dir);
        var pngPath = Path.Combine(dir, "page.png");
        SavePng(page, pngPath);
        var cbz = Path.Combine(dir, fileName);
        using (var zip = ZipFile.Open(cbz, ZipArchiveMode.Create))
        {
            zip.CreateEntryFromFile(pngPath, "001.png");
            var info = zip.CreateEntry("ComicInfo.xml");
            using var w = new StreamWriter(info.Open());
            w.Write("<?xml version=\"1.0\"?>\n<ComicInfo>" +
                    "<Title>Bab Fixture OCR</Title><Series>Fixture Tinta</Series>" +
                    "</ComicInfo>");
        }
        return cbz;
    }

    /// <summary>Kemiripan dua string: 1 − jarak Levenshtein / panjang maks.</summary>
    public static double Similarity(string a, string b)
    {
        if (a.Length == 0 || b.Length == 0)
            return a == b ? 1 : 0;
        var d = new int[a.Length + 1, b.Length + 1];
        for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (int j = 0; j <= b.Length; j++) d[0, j] = j;
        for (int i = 1; i <= a.Length; i++)
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost);
            }
        return 1.0 - (double)d[a.Length, b.Length] / Math.Max(a.Length, b.Length);
    }
}
