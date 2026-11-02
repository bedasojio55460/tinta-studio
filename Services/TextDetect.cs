using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SkiaSharp;

namespace TintaStudio.Services;

/// <summary>Kelas keluaran detektor ogkalu (data latih model).</summary>
public enum DetectedClass
{
    Bubble = 0,
    TextBubble = 1,
    TextFree = 2,
}

/// <summary>Satu kotak hasil deteksi, koordinat piksel halaman asli.</summary>
public sealed record DetectedBox(
    int X, int Y, int Width, int Height,
    DetectedClass Class, double Score);

/// <summary>
/// Matematika deteksi BERPETAK (v0.7.14) untuk halaman tinggi
/// (strip webtoon): halaman yang rasio tinggi/lebarnya melewati
/// ambang dipotong menjadi petak vertikal bertumpang-tindih, model
/// dijalankan per petak, kotak dipetakan kembali ke koordinat
/// halaman, lalu duplikat antar-petak digabung. Semua fungsi di
/// sini MURNI (tanpa model/gambar) agar bisa dites langsung.
///
/// Kenapa: detektor me-resize seluruh halaman ke 640×640 — pada
/// strip 900×15.412 bubble setinggi 100 px menyusut jadi ±4 px dan
/// tidak terdeteksi (di PC pemilik halaman seperti itu hanya
/// menghasilkan 4 blok sampah). Di dalam petak setinggi ±1,25×
/// lebar halaman, bubble tampil pada skala yang sama seperti di
/// halaman biasa.
/// </summary>
public static class TileMath
{
    /// <summary>Rasio tinggi/lebar di atas ini → halaman dipetak.</summary>
    public const double TallAspectThreshold = 2.0;

    /// <summary>Tinggi petak sebagai pengali lebar halaman.</summary>
    public const double TileHeightFactor = 1.25;

    /// <summary>Fraksi tumpang-tindih antar-petak berurutan —
    /// bubble yang terpotong tepi satu petak tampil utuh di
    /// petak tetangganya.</summary>
    public const double TileOverlapFraction = 0.20;

    /// <summary>Ambang IoU penggabungan duplikat antar-petak
    /// (kelas yang sama; skor tertinggi dipertahankan).</summary>
    public const double MergeIouThreshold = 0.5;

    public static bool NeedsTiling(int pageWidth, int pageHeight) =>
        pageWidth > 0 && pageHeight > 0
        && (double)pageHeight / pageWidth > TallAspectThreshold;

    /// <summary>Daftar petak (offset Y + tinggi) yang menutup
    /// halaman dari atas ke bawah; petak terakhir selalu berakhir
    /// tepat di ujung bawah halaman. Halaman yang tidak butuh
    /// petak menghasilkan satu petak setinggi halaman.</summary>
    public static IReadOnlyList<(int Y, int Height)> ComputeTiles(
        int pageWidth, int pageHeight)
    {
        var tiles = new List<(int Y, int Height)>();
        if (pageWidth <= 0 || pageHeight <= 0)
            return tiles;
        if (!NeedsTiling(pageWidth, pageHeight))
        {
            tiles.Add((0, pageHeight));
            return tiles;
        }
        int tileHeight = Math.Max(1, (int)Math.Round(pageWidth * TileHeightFactor));
        int overlap = (int)Math.Round(tileHeight * TileOverlapFraction);
        int step = Math.Max(1, tileHeight - overlap);
        for (int y = 0; ; y += step)
        {
            int h = Math.Min(tileHeight, pageHeight - y);
            tiles.Add((y, h));
            if (y + h >= pageHeight)
                break;
        }
        return tiles;
    }

    /// <summary>Petakan kotak hasil model pada satu petak (koordinat
    /// petak) ke koordinat halaman: geser Y sebesar offset petak,
    /// lalu jepit ke batas halaman; null bila hasilnya degenerat.</summary>
    public static DetectedBox? MapToPage(DetectedBox box, int tileY,
        int pageWidth, int pageHeight)
    {
        int x1 = Math.Clamp(box.X, 0, pageWidth - 1);
        int y1 = Math.Clamp(box.Y + tileY, 0, pageHeight - 1);
        int x2 = Math.Clamp(box.X + box.Width, x1 + 1, pageWidth);
        int y2 = Math.Clamp(box.Y + tileY + box.Height, y1 + 1, pageHeight);
        if (x2 - x1 < 4 || y2 - y1 < 4)
            return null;
        return new DetectedBox(x1, y1, x2 - x1, y2 - y1, box.Class, box.Score);
    }

    /// <summary>Gabung hasil semua petak: urut skor menurun, kotak
    /// yang IoU-nya ≥ ambang terhadap kotak tersimpan SEJENIS
    /// dibuang (duplikat wilayah yang sama dari petak tetangga).
    /// Per kelas karena kotak bubble dan kotak teks di dalamnya
    /// memang sah bertumpuk (IoU bisa tinggi) — duplikat antar-petak
    /// selalu sekelas.</summary>
    public static List<DetectedBox> Merge(IReadOnlyList<DetectedBox> boxes)
    {
        var kept = new List<DetectedBox>();
        foreach (var b in boxes.OrderByDescending(b => b.Score))
        {
            bool dup = kept.Any(k => k.Class == b.Class
                && TextDetector.Iou(k, b) >= MergeIouThreshold);
            if (!dup)
                kept.Add(b);
        }
        return kept;
    }
}

/// <summary>
/// Deteksi teks komik dengan model RT-DETR-v2 int8 (lihat
/// docs/models.md). Kontrak model (terkalibrasi empiris):
/// masukan "images" float32 [1,3,640,640] — halaman di-resize
/// langsung ke 640×640, normalisasi ImageNet; masukan
/// "orig_target_sizes" int64 [1,2] berisi [LEBAR, TINGGI] halaman
/// asal; keluaran labels/boxes/scores dengan boxes xyxy dalam
/// piksel halaman asal. RT-DETR tidak memakai NMS; NMS pengaman
/// IoU 0,6 tetap dipasang untuk membuang duplikat hasil kuantisasi.
///
/// Halaman tinggi (rasio &gt; 2) dideteksi BERPETAK — lihat
/// <see cref="TileMath"/>; halaman biasa lewat jalur lama persis
/// (satu kali jalan model atas seluruh halaman).
/// </summary>
public sealed class TextDetector : IDisposable
{
    public const int InputSize = 640;
    public const double DefaultThreshold = 0.35;

    private InferenceSession? _session;
    private readonly string _modelPath;

    public TextDetector(string? modelPath = null)
    {
        _modelPath = modelPath
            ?? ModelStore.PathOf(ModelStore.Entry(ModelStore.DetectorId).Files[0]);
    }

    private InferenceSession Session =>
        _session ??= new InferenceSession(_modelPath);

    /// <summary>Deteksi semua kotak (bubble + teks) pada satu halaman.
    /// Halaman tinggi dideteksi per petak lalu digabung; halaman
    /// biasa satu kali jalan seperti sebelumnya.</summary>
    public IReadOnlyList<DetectedBox> Detect(SKBitmap page, double threshold = DefaultThreshold)
    {
        if (!TileMath.NeedsTiling(page.Width, page.Height))
            return Nms(RunModel(page, threshold), iouLimit: 0.6);

        var all = new List<DetectedBox>();
        foreach (var (tileY, tileHeight) in TileMath.ComputeTiles(page.Width, page.Height))
        {
            using var tile = new SKBitmap(page.Width, tileHeight);
            using (var canvas = new SKCanvas(tile))
                canvas.DrawBitmap(page, 0, -tileY);
            foreach (var raw in RunModel(tile, threshold))
            {
                var mapped = TileMath.MapToPage(raw, tileY, page.Width, page.Height);
                if (mapped is not null)
                    all.Add(mapped);
            }
        }
        return TileMath.Merge(all);
    }

    /// <summary>Satu kali jalan model atas satu bitmap (halaman utuh
    /// atau satu petak): kotak dalam koordinat bitmap itu sendiri,
    /// tersaring ambang, TANPA NMS (pemanggil yang menggabungkan).</summary>
    private List<DetectedBox> RunModel(SKBitmap page, double threshold)
    {
        var input = Preprocess(page);
        var sizes = new DenseTensor<long>(new[] { 1, 2 });
        // PERHATIAN: ekspor model ini mengharapkan [lebar, tinggi].
        sizes[0, 0] = page.Width;
        sizes[0, 1] = page.Height;
        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("images", input),
            NamedOnnxValue.CreateFromTensor("orig_target_sizes", sizes),
        };
        using var results = Session.Run(inputs);
        Tensor<long>? labels = null;
        Tensor<float>? boxes = null, scores = null;
        foreach (var r in results)
        {
            switch (r.Name)
            {
                case "labels": labels = r.Value as Tensor<long>; break;
                case "boxes": boxes = r.Value as Tensor<float>; break;
                case "scores": scores = r.Value as Tensor<float>; break;
            }
        }
        if (labels is null || boxes is null || scores is null)
            throw new InvalidOperationException(
                "Keluaran detektor tidak sesuai kontrak (labels/boxes/scores hilang).");

        // Salin ke array datar: pengindeksan tensor hasil sesi tidak
        // selalu flat; indeks manual = i*4 + komponen (dim terakhir 4).
        var labelArr = labels.ToArray();
        var scoreArr = scores.ToArray();
        var boxArr = boxes.ToArray();
        int n = Math.Min(labelArr.Length, scoreArr.Length);
        var found = new List<DetectedBox>();
        for (int i = 0; i < n; i++)
        {
            double score = scoreArr[i];
            if (score < threshold)
                continue;
            int cls = (int)labelArr[i];
            if (cls is < 0 or > 2)
                continue;
            int x1 = (int)Math.Round(boxArr[i * 4 + 0]);
            int y1 = (int)Math.Round(boxArr[i * 4 + 1]);
            int x2 = (int)Math.Round(boxArr[i * 4 + 2]);
            int y2 = (int)Math.Round(boxArr[i * 4 + 3]);
            // Klip ke batas halaman; buang kotak degenerat.
            x1 = Math.Clamp(x1, 0, page.Width - 1);
            y1 = Math.Clamp(y1, 0, page.Height - 1);
            x2 = Math.Clamp(x2, x1 + 1, page.Width);
            y2 = Math.Clamp(y2, y1 + 1, page.Height);
            if (x2 - x1 < 4 || y2 - y1 < 4)
                continue;
            found.Add(new DetectedBox(x1, y1, x2 - x1, y2 - y1,
                (DetectedClass)cls, score));
        }
        return found;
    }

    private static DenseTensor<float> Preprocess(SKBitmap page)
    {
        using var resized = page.Resize(
            new SKImageInfo(InputSize, InputSize), SKSamplingOptions.Default);
        var tensor = new DenseTensor<float>(new[] { 1, 3, InputSize, InputSize });
        // Normalisasi ImageNet (model backbone ResNet).
        ReadOnlySpan<float> mean = stackalloc float[] { 0.485f, 0.456f, 0.406f };
        ReadOnlySpan<float> std = stackalloc float[] { 0.229f, 0.224f, 0.225f };
        for (int y = 0; y < InputSize; y++)
        {
            for (int x = 0; x < InputSize; x++)
            {
                var px = resized.GetPixel(x, y);
                tensor[0, 0, y, x] = (px.Red / 255f - mean[0]) / std[0];
                tensor[0, 1, y, x] = (px.Green / 255f - mean[1]) / std[1];
                tensor[0, 2, y, x] = (px.Blue / 255f - mean[2]) / std[2];
            }
        }
        return tensor;
    }

    private static List<DetectedBox> Nms(List<DetectedBox> boxes, double iouLimit)
    {
        var kept = new List<DetectedBox>();
        foreach (var b in boxes.OrderByDescending(b => b.Score))
        {
            bool suppressed = kept.Any(k => k.Class == b.Class && Iou(k, b) > iouLimit);
            if (!suppressed)
                kept.Add(b);
        }
        return kept;
    }

    internal static double Iou(DetectedBox a, DetectedBox b)
    {
        int x1 = Math.Max(a.X, b.X), y1 = Math.Max(a.Y, b.Y);
        int x2 = Math.Min(a.X + a.Width, b.X + b.Width);
        int y2 = Math.Min(a.Y + a.Height, b.Y + b.Height);
        if (x2 <= x1 || y2 <= y1)
            return 0;
        double inter = (double)(x2 - x1) * (y2 - y1);
        double union = (double)a.Width * a.Height + (double)b.Width * b.Height - inter;
        return union <= 0 ? 0 : inter / union;
    }

    public void Dispose() => _session?.Dispose();
}
