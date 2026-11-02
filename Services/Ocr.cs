using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SkiaSharp;
using TintaStudio.Models;

namespace TintaStudio.Services;

/// <summary>Bahasa OCR yang didukung (pilihan pengguna).</summary>
public sealed record OcrLanguage(string Code, string DisplayName)
{
    /// <summary>
    /// Mode Otomatis: bahasa ditebak per halaman dari model yang
    /// terpasang (lihat DetectOcrPipeline). Bukan bahasa sungguhan —
    /// tidak pernah punya model sendiri dan tidak pernah tercatat
    /// sebagai bahasa blok; blok selalu menyimpan bahasa aktual
    /// yang memenangi tebakan.
    /// </summary>
    public static readonly OcrLanguage Auto = new("auto", "Otomatis");

    public static readonly OcrLanguage Japanese = new("ja", "Jepang");
    public static readonly OcrLanguage Korean = new("ko", "Korea");
    public static readonly OcrLanguage Chinese = new("ch", "Cina");
    public static readonly OcrLanguage English = new("en", "Inggris");

    /// <summary>Pilihan di pemilih bahasa UI: Otomatis di teratas.</summary>
    public static IReadOnlyList<OcrLanguage> All { get; } =
        new[] { Auto, Japanese, Korean, Chinese, English };

    /// <summary>Bahasa sungguhan (punya model rec + kamus).</summary>
    public static IReadOnlyList<OcrLanguage> Concrete { get; } =
        new[] { Japanese, Korean, Chinese, English };

    public bool IsAuto => Code == Auto.Code;

    public static OcrLanguage FromCode(string code) => code switch
    {
        "auto" => Auto,
        "ja" => Japanese,
        "ko" => Korean,
        "ch" => Chinese,
        "en" => English,
        _ => throw new ArgumentException(
            $"Kode bahasa tidak dikenal: '{code}' (pakai auto|ja|ko|ch|en).", nameof(code)),
    };
}

/// <summary>
/// Penilaian calon bahasa untuk mode Otomatis: skor = keyakinan OCR
/// rata-rata tertimbang panjang teks, ditambah bonus konsistensi
/// aksara dari teks terdekode. Aksara khas menjadi penentu kuat:
/// kana hanya muncul dari teks Jepang, hangul hanya dari Korea,
/// huruf Latin + spasi menandakan Inggris; Han murni netral
/// (Jepang dan Cina berbagi Han — keyakinan yang memutuskan).
/// </summary>
public static class LanguageGuess
{
    /// <summary>Ambang keyakinan blok untuk memicu coba-ulang per blok.</summary>
    public const double BlockRetryThreshold = 0.5;

    /// <summary>Bonus skor per aksara khas yang konsisten.</summary>
    public const double ScriptBonus = 0.25;

    /// <summary>True bila teks mengandung hiragana/katakana.</summary>
    public static bool HasKana(string text) => text.Any(c =>
        (c >= '\u3040' && c <= '\u30FF') || (c >= '\u31F0' && c <= '\u31FF'));

    /// <summary>True bila teks mengandung suku kata hangul.</summary>
    public static bool HasHangul(string text) => text.Any(c =>
        (c >= '\uAC00' && c <= '\uD7AF') || (c >= '\u1100' && c <= '\u11FF'));

    /// <summary>True bila teks mengandung huruf Latin.</summary>
    public static bool HasLatin(string text) => text.Any(c =>
        (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z')
        || (c >= '\u00C0' && c <= '\u024F'));

    /// <summary>
    /// Bonus konsistensi aksara untuk pasangan (bahasa, teks hasil
    /// baca bahasa itu). Kana/hangul yang muncul dari model yang
    /// salah hampir mustahil, jadi kehadirannya menguatkan bahasa
    /// pemilik aksara itu; teks Han murni tidak memberi bonus.
    /// </summary>
    public static double ScriptBonusFor(OcrLanguage lang, string text) => lang.Code switch
    {
        "ja" => HasKana(text) ? ScriptBonus : 0,
        "ko" => HasHangul(text) ? ScriptBonus : 0,
        "en" => HasLatin(text) && text.Contains(' ') ? ScriptBonus : 0,
        _ => 0,
    };

    /// <summary>Skor satu hasil baca: keyakinan + bonus aksara bahasanya.</summary>
    public static double ScoreOf(OcrLanguage lang, OcrResult result) =>
        result.Confidence + ScriptBonusFor(lang, result.Text);

    /// <summary>
    /// True bila teks hasil baca sama sekali tidak memakai aksara
    /// bahasa yang membacanya — penanda blok ini mungkin berbahasa
    /// lain walau keyakinannya tinggi. Kasus nyata: kamus Jepang
    /// memuat huruf Latin, jadi teks Inggris terbaca "Helo world."
    /// berkeyakinan 0,98 oleh model ja tanpa satu pun kana/Han.
    /// Cina tidak punya uji ini: Han adalah aksaranya dan teks
    /// pendek Cina yang sah selalu lulus uji Han ja juga.
    /// </summary>
    public static bool ScriptMismatch(OcrLanguage lang, string text)
    {
        var flat = text.Replace("\n", "").Replace(" ", "");
        if (flat.Length == 0)
            return false;
        return lang.Code switch
        {
            "ja" => !flat.Any(c => (c >= '\u3040' && c <= '\u30FF')
                                   || (c >= '\u4E00' && c <= '\u9FFF')),
            "ko" => !HasHangul(flat),
            "en" => !HasLatin(flat),
            _ => false,
        };
    }
}

/// <summary>Hasil OCR satu blok: teks, keyakinan 0..1, arah terpilih.</summary>
public sealed record OcrResult(string Text, double Confidence, TextDirection Direction);

/// <summary>
/// Mesin OCR satu bahasa: model recognition PaddleOCR (ONNX) + kamus.
/// Pra-proses mengikuti konvensi PaddleOCR: tinggi diseragamkan ke
/// tinggi masukan model (dari metadata), kanal BGR, normalisasi
/// (v/255 − 0,5)/0,5. Keluaran model sudah probabilitas softmax
/// (diverifikasi) — dekode CTC rakus: 0=blank, 1..N=kamus,
/// indeks terakhir (bila kelas = kamus+2) = spasi.
/// </summary>
public sealed class OcrEngine : IDisposable
{
    private InferenceSession? _session;
    private string[]? _chars;
    private int _inputHeight = 48;
    private readonly ModelEntry _entry;

    public OcrLanguage Language { get; }

    public OcrEngine(OcrLanguage language)
    {
        Language = language;
        _entry = ModelStore.Entry("rec-" + language.Code);
    }

    private InferenceSession Session
    {
        get
        {
            if (_session is not null)
                return _session;
            var onnx = _entry.Files.First(f => f.CacheName.EndsWith(".onnx"));
            _session = new InferenceSession(ModelStore.PathOf(onnx));
            var input = _session.InputMetadata.First().Value;
            if (input.Dimensions.Length == 4 && input.Dimensions[2] > 0)
                _inputHeight = input.Dimensions[2];
            var dictFile = _entry.Files.First(f => f.CacheName.EndsWith(".txt"));
            _chars = File.ReadAllLines(ModelStore.PathOf(dictFile));
            return _session;
        }
    }

    /// <summary>OCR satu garis teks horizontal dari bitmap potongan.</summary>
    public (string Text, double Confidence) RecognizeLine(SKBitmap line)
    {
        var session = Session;
        int h = _inputHeight;
        int w = Math.Clamp((int)Math.Round(h * (double)line.Width / Math.Max(1, line.Height)),
            8, 4096);
        using var resized = line.Resize(new SKImageInfo(w, h), SKSamplingOptions.Default);
        var tensor = new DenseTensor<float>(new[] { 1, 3, h, w });
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                var px = resized.GetPixel(x, y);
                // PaddleOCR membaca gambar sebagai BGR (cv2).
                tensor[0, 0, y, x] = (px.Blue / 255f - 0.5f) / 0.5f;
                tensor[0, 1, y, x] = (px.Green / 255f - 0.5f) / 0.5f;
                tensor[0, 2, y, x] = (px.Red / 255f - 0.5f) / 0.5f;
            }
        }
        var inputName = session.InputMetadata.First().Key;
        using var results = session.Run(new[]
        {
            NamedOnnxValue.CreateFromTensor(inputName, tensor),
        });
        var output = results.First().Value as Tensor<float>
            ?? throw new InvalidOperationException("Keluaran model OCR bukan tensor float.");
        return CtcDecode(output);
    }

    private (string Text, double Confidence) CtcDecode(Tensor<float> output)
    {
        var dims = output.Dimensions;
        int classes = dims[dims.Length - 1];
        var flat = output.ToArray();
        int steps = flat.Length / classes;
        // Keluaran seharusnya probabilitas; bila terlihat seperti logit
        // (ada nilai negatif), terapkan softmax per baris secara defensif.
        bool needSoftmax = false;
        for (int i = 0; i < flat.Length && !needSoftmax; i += 97)
            if (flat[i] < -1e-4f)
                needSoftmax = true;

        var chars = _chars!;
        var sb = new System.Text.StringBuilder();
        double confSum = 0;
        int confCount = 0;
        int prev = -1;
        var row = new float[classes];
        for (int t = 0; t < steps; t++)
        {
            Array.Copy(flat, t * classes, row, 0, classes);
            if (needSoftmax)
                Softmax(row);
            int best = 0;
            float bestVal = row[0];
            for (int c = 1; c < classes; c++)
                if (row[c] > bestVal)
                {
                    bestVal = row[c];
                    best = c;
                }
            if (best != 0 && best != prev)
            {
                if (best - 1 < chars.Length)
                    sb.Append(chars[best - 1]);
                else if (best == classes - 1)
                    sb.Append(' ');
                confSum += bestVal;
                confCount++;
            }
            prev = best;
        }
        return (sb.ToString(), confCount > 0 ? confSum / confCount : 0);
    }

    private static void Softmax(float[] row)
    {
        float max = row.Max();
        double sum = 0;
        for (int i = 0; i < row.Length; i++)
        {
            row[i] = (float)Math.Exp(row[i] - max);
            sum += row[i];
        }
        for (int i = 0; i < row.Length; i++)
            row[i] = (float)(row[i] / sum);
    }

    public void Dispose() => _session?.Dispose();
}

/// <summary>
/// Klasifikasi arah garis teks 0°/180° (PP-LCNet PP-OCRv5, opsional —
/// dipakai untuk memutar balik potongan yang terbalik 180°).
/// </summary>
public sealed class TextLineOrientation : IDisposable
{
    private InferenceSession? _session;

    private InferenceSession? TrySession
    {
        get
        {
            var entry = ModelStore.Entry(ModelStore.ClsId);
            if (!ModelStore.IsDownloaded(entry))
                return null;
            return _session ??= new InferenceSession(
                ModelStore.PathOf(entry.Files[0]));
        }
    }

    /// <summary>True bila garis kemungkinan besar terbalik 180°.</summary>
    public bool IsUpsideDown(SKBitmap line)
    {
        var session = TrySession;
        if (session is null)
            return false;
        var meta = session.InputMetadata.First();
        int h = meta.Value.Dimensions.Length == 4 && meta.Value.Dimensions[2] > 0
            ? meta.Value.Dimensions[2] : 48;
        int w = meta.Value.Dimensions.Length == 4 && meta.Value.Dimensions[3] > 0
            ? meta.Value.Dimensions[3] : 192;
        using var resized = line.Resize(new SKImageInfo(w, h), SKSamplingOptions.Default);
        var tensor = new DenseTensor<float>(new[] { 1, 3, h, w });
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var px = resized.GetPixel(x, y);
                tensor[0, 0, y, x] = (px.Blue / 255f - 0.5f) / 0.5f;
                tensor[0, 1, y, x] = (px.Green / 255f - 0.5f) / 0.5f;
                tensor[0, 2, y, x] = (px.Red / 255f - 0.5f) / 0.5f;
            }
        using var results = session.Run(new[]
        {
            NamedOnnxValue.CreateFromTensor(meta.Key, tensor),
        });
        var output = results.First().Value as Tensor<float>;
        if (output is null || output.Length < 2)
            return false;
        var flat = output.ToArray();
        // Kelas: indeks 0 = "0", indeks 1 = "180".
        return flat[1] > flat[0] && flat[1] > 0.6f;
    }

    public void Dispose() => _session?.Dispose();
}

/// <summary>
/// Layanan OCR tingkat blok: memotong blok dari halaman, menentukan
/// arah, membagi garis (proyeksi tinta), menangani teks vertikal
/// (putar 90° dua arah, pilih keyakinan terbaik), koreksi 180° via
/// model klasifikasi arah bila tersedia.
/// </summary>
public sealed class OcrService : IDisposable
{
    private readonly Dictionary<string, OcrEngine> _engines = new();
    private readonly TextLineOrientation _orientation = new();

    public OcrEngine EngineFor(OcrLanguage lang)
    {
        if (!_engines.TryGetValue(lang.Code, out var engine))
        {
            engine = new OcrEngine(lang);
            _engines[lang.Code] = engine;
        }
        return engine;
    }

    /// <summary>OCR satu kotak deteksi pada halaman.</summary>
    public OcrResult RecognizeBlock(SKBitmap page, DetectedBox box, OcrLanguage lang)
    {
        using var crop = CropWithPadding(page, box);
        var direction = box.Height > box.Width * 1.15
            ? TextDirection.Vertical
            : TextDirection.Horizontal;
        var engine = EngineFor(lang);
        if (direction == TextDirection.Horizontal)
        {
            var (text, conf) = RecognizeHorizontal(crop, engine);
            return new OcrResult(text, conf, TextDirection.Horizontal);
        }
        // Vertikal: coba dua arah putar 90°, pilih keyakinan tertinggi.
        using var rotA = Rotate90(crop, counterClockwise: true);
        using var rotB = Rotate90(crop, counterClockwise: false);
        var a = RecognizeHorizontal(rotA, engine);
        var b = RecognizeHorizontal(rotB, engine);
        var best = a.Confidence >= b.Confidence ? a : b;
        return new OcrResult(best.Text, best.Confidence, TextDirection.Vertical);
    }

    /// <summary>
    /// OCR bitmap yang sudah horizontal: bagi menjadi pita garis
    /// berdasarkan baris kosong (proyeksi tinta), OCR per garis,
    /// gabung dengan '\n'. Satu pita = perilaku garis tunggal biasa.
    /// </summary>
    private (string Text, double Confidence) RecognizeHorizontal(
        SKBitmap image, OcrEngine engine)
    {
        var bands = SplitLineBands(image);
        var texts = new List<string>();
        double confSum = 0;
        int weight = 0;
        foreach (var band in bands)
        {
            using var lineBmp = new SKBitmap(band.Width, band.Height);
            using (var canvas = new SKCanvas(lineBmp))
                canvas.DrawBitmap(image, -band.Left, -band.Top);
            var (text, conf) = RecognizeOneLine(lineBmp, engine);
            if (text.Length == 0)
                continue;
            texts.Add(text);
            confSum += conf * text.Length;
            weight += text.Length;
        }
        return (string.Join("\n", texts), weight > 0 ? confSum / weight : 0);
    }

    private (string Text, double Confidence) RecognizeOneLine(
        SKBitmap line, OcrEngine engine)
    {
        // Pra-proses khusus Inggris (v0.7.15): garis diberi bantalan
        // putih di sekeliling sebelum inferensi — potongan garis dari
        // bubble komik mepet ke tepi (huruf pertama/terakhir nempel
        // tepi potongan) dan model rec membaca spasi antar-kata
        // lebih andal dengan margin kosong, seperti data latihnya.
        // Dievaluasi pada potongan bubble asli bab pemilik bersama
        // model mobile & server (tabel di project-state v0.7.15).
        using var padded = engine.Language.Code == "en"
            ? PadLineWhite(line) : null;
        var src = padded ?? line;
        var first = engine.RecognizeLine(src);
        // Koreksi teks terbalik 180° bila model klasifikasi tersedia.
        if (_orientation.IsUpsideDown(src))
        {
            using var flipped = Rotate180(src);
            var second = engine.RecognizeLine(flipped);
            if (second.Confidence > first.Confidence)
                return second;
        }
        return first;
    }

    /// <summary>
    /// Salin garis ke bitmap baru berlatar putih bersih dengan
    /// bantalan: horizontal ±½ tinggi garis (dijepit 6–48 px),
    /// vertikal ±¼ tinggi garis (dijepit 2–16 px). Latar dipaksa
    /// putih murni agar tepi potongan yang memuat lengkung bubble
    /// atau sisa panel tidak ikut terbaca sebagai tanda.
    /// </summary>
    internal static SKBitmap PadLineWhite(SKBitmap line)
    {
        int padX = Math.Clamp(line.Height / 2, 6, 48);
        int padY = Math.Clamp(line.Height / 4, 2, 16);
        var dst = new SKBitmap(line.Width + padX * 2, line.Height + padY * 2);
        using var canvas = new SKCanvas(dst);
        canvas.Clear(SKColors.White);
        canvas.DrawBitmap(line, padX, padY);
        return dst;
    }

    internal static SKBitmap CropWithPadding(SKBitmap page, DetectedBox box)
    {
        int pad = Math.Max(4, (int)(Math.Min(box.Width, box.Height) * 0.08));
        int x = Math.Max(0, box.X - pad);
        int y = Math.Max(0, box.Y - pad);
        int w = Math.Min(page.Width - x, box.Width + pad * 2);
        int h = Math.Min(page.Height - y, box.Height + pad * 2);
        var crop = new SKBitmap(w, h);
        using var canvas = new SKCanvas(crop);
        canvas.DrawBitmap(page, new SKRect(x, y, x + w, y + h),
            new SKRect(0, 0, w, h));
        return crop;
    }

    /// <summary>
    /// Putar 90°. counterClockwise=true: tepi ATAS menjadi tepi KIRI
    /// (konvensi yang benar untuk kolom vertikal Jepang satu kolom:
    /// karakter teratas menjadi paling kiri).
    /// </summary>
    internal static SKBitmap Rotate90(SKBitmap src, bool counterClockwise)
    {
        var dst = new SKBitmap(src.Height, src.Width);
        using var canvas = new SKCanvas(dst);
        if (counterClockwise)
        {
            canvas.Translate(0, src.Width);
            canvas.RotateDegrees(-90);
        }
        else
        {
            canvas.Translate(src.Height, 0);
            canvas.RotateDegrees(90);
        }
        canvas.DrawBitmap(src, 0, 0);
        return dst;
    }

    internal static SKBitmap Rotate180(SKBitmap src)
    {
        var dst = new SKBitmap(src.Width, src.Height);
        using var canvas = new SKCanvas(dst);
        canvas.Translate(src.Width, src.Height);
        canvas.RotateDegrees(180);
        canvas.DrawBitmap(src, 0, 0);
        return dst;
    }

    /// <summary>
    /// Bagi gambar horizontal menjadi pita-pita garis teks memakai
    /// proyeksi tinta per baris. Sebuah baris dihitung "bertinta"
    /// hanya bila jumlah piksel tintanya melewati ambang densitas —
    /// garis tepi bubble (elips) menyumbang tinta tipis di SEMUA
    /// baris tengah sehingga uji ada/tidak tinta saja tidak pernah
    /// menemukan celah antarbaris di dalam bubble. Celah ≥ minGap
    /// memisahkan garis; pita yang terlalu pendek (lengkung tepi
    /// bubble) dibuang. Bila tak ada pita sah, seluruh gambar = 1 pita.
    /// </summary>
    internal static List<SKRectI> SplitLineBands(SKBitmap image)
    {
        int w = image.Width, h = image.Height;
        int inkThreshold = Math.Max(12, (int)(w * 0.03));
        var rowInk = new bool[h];
        for (int y = 0; y < h; y++)
        {
            int count = 0;
            for (int x = 0; x < w; x++)
            {
                var px = image.GetPixel(x, y);
                int lum = (px.Red * 299 + px.Green * 587 + px.Blue * 114) / 1000;
                if (lum < 160)
                    count++;
            }
            rowInk[y] = count > inkThreshold;
        }
        // Celah minimum antarbaris: 4% tinggi potongan, dijepit
        // [4, 8] px. Kalibrasi v0.7.15 pada bab strip asli pemilik
        // + fixture sintetis (profil baris tinta terukur persis):
        // rumus lama 6% (12–17 px pada potongan 215–300 px)
        // MENYATUKAN baris Inggris yang celah nyatanya cuma 8–16
        // px — semua baris menyatu jadi satu pita dan model
        // satu-baris membacanya jadi sampah satu huruf ("T", "N").
        // Nilai 8 adalah porosnya: celah DALAM glyph fixture
        // Jepang terbesar 7 px (tidak boleh membelah baris),
        // celah antarbaris nyata terkecil 8 px (bubble kuas ungu,
        // harus membelah). Celah antarbaris bubble Inggris lain
        // 10–16 px; celah dalam satu baris tidak pernah selebar
        // ini karena proyeksi dihitung berdensitas (satu baris
        // harus nyaris kosong total untuk dihitung celah).
        int minGap = Math.Clamp((int)(h * 0.04), 4, 8);
        int minBand = Math.Max(6, (int)(h * 0.10));
        var bands = new List<SKRectI>();
        int start = -1, gap = 0;
        for (int y = 0; y <= h; y++)
        {
            bool ink = y < h && rowInk[y];
            if (ink)
            {
                if (start < 0)
                    start = y;
                gap = 0;
            }
            else if (start >= 0)
            {
                gap++;
                if (gap >= minGap || y == h)
                {
                    // Baris tinta terakhir = y - gap; ujung eksklusif = +1.
                    // Di tepi bawah gambar dengan celah pendek, pita
                    // diteruskan sampai tepi.
                    int end = (y == h && gap < minGap) ? h : y - gap + 1;
                    if (end - start >= minBand)
                        bands.Add(new SKRectI(0, start, w, end));
                    start = -1;
                    gap = 0;
                }
            }
        }
        if (bands.Count == 0)
            bands.Add(new SKRectI(0, 0, w, h));
        return bands;
    }

    public void Dispose()
    {
        foreach (var e in _engines.Values)
            e.Dispose();
        _orientation.Dispose();
    }
}
