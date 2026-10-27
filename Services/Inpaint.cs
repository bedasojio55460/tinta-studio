using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SkiaSharp;

namespace TintaStudio.Services;

/// <summary>
/// Inpainting LaMA (ONNX) untuk menghapus teks dari bubble mengikuti
/// latar di sekitarnya. Model: ekspor publik LaMA yang di-fine-tune
/// untuk manga (lihat docs/models.md).
///
/// Kontrak I/O — hasil KALIBRASI EMPIRIS di VM ini (bukan asumsi):
/// masukan `image` float32 [B,3,512,512] nilai 0..1 (RGB),
/// masukan `mask` float32 [B,1,512,512] (1 = area yang di-inpaint,
/// 0 = dipertahankan), keluaran `output` float32 [B,3,512,512]
/// nilai ±0..1. Ukuran masukan TETAP 512×512 — potongan blok diubah
/// ukurannya ke 512×512, hasil diubah kembali ke ukuran potongan.
/// Pemanggil menempelkan hasil hanya di dalam mask (bertepi lembut),
/// jadi piksel di luar mask dijamin persis asli.
/// </summary>
public sealed class LamaInpainter : IDisposable
{
    /// <summary>Ukuran masukan tetap model.</summary>
    public const int ModelSize = 512;

    private InferenceSession? _session;

    private InferenceSession Session
    {
        get
        {
            if (_session is not null)
                return _session;
            var entry = ModelStore.InpaintEntry;
            if (!ModelStore.IsDownloaded(entry))
                throw new ModelMissingException(new[] { entry });
            _session = new InferenceSession(
                ModelStore.PathOf(entry.Files[0]));
            return _session;
        }
    }

    /// <summary>
    /// Inpaint satu potongan gambar. <paramref name="mask"/> bitmap
    /// abu-abu seukuran potongan: piksel terang (≥128) = area yang
    /// diisi model. Mengembalikan bitmap baru seukuran potongan
    /// (seluruh bidang hasil model — penempelan selektif adalah
    /// urusan pemanggil).
    /// </summary>
    public SKBitmap Inpaint(SKBitmap crop, SKBitmap mask)
    {
        if (mask.Width != crop.Width || mask.Height != crop.Height)
            throw new ArgumentException("Mask harus seukuran potongan.");
        var session = Session;

        using var img512 = crop.Resize(
            new SKImageInfo(ModelSize, ModelSize), SKSamplingOptions.Default);
        using var mask512 = mask.Resize(
            new SKImageInfo(ModelSize, ModelSize), SKSamplingOptions.Default);

        var image = new DenseTensor<float>(new[] { 1, 3, ModelSize, ModelSize });
        var maskT = new DenseTensor<float>(new[] { 1, 1, ModelSize, ModelSize });
        for (int y = 0; y < ModelSize; y++)
        {
            for (int x = 0; x < ModelSize; x++)
            {
                var px = img512.GetPixel(x, y);
                image[0, 0, y, x] = px.Red / 255f;
                image[0, 1, y, x] = px.Green / 255f;
                image[0, 2, y, x] = px.Blue / 255f;
                var m = mask512.GetPixel(x, y);
                maskT[0, 0, y, x] = m.Red >= 128 ? 1f : 0f;
            }
        }

        string imageName = session.InputMetadata
            .First(kv => kv.Value.Dimensions.Length == 4 && kv.Value.Dimensions[1] == 3).Key;
        string maskName = session.InputMetadata
            .First(kv => kv.Value.Dimensions.Length == 4 && kv.Value.Dimensions[1] == 1).Key;
        using var results = session.Run(new[]
        {
            NamedOnnxValue.CreateFromTensor(imageName, image),
            NamedOnnxValue.CreateFromTensor(maskName, maskT),
        });
        var output = results.First().Value as Tensor<float>
            ?? throw new InvalidOperationException("Keluaran model LaMA bukan tensor float.");

        using var out512 = new SKBitmap(ModelSize, ModelSize);
        for (int y = 0; y < ModelSize; y++)
        {
            for (int x = 0; x < ModelSize; x++)
            {
                byte r = ToByte(output[0, 0, y, x]);
                byte g = ToByte(output[0, 1, y, x]);
                byte b = ToByte(output[0, 2, y, x]);
                out512.SetPixel(x, y, new SKColor(r, g, b));
            }
        }
        if (crop.Width == ModelSize && crop.Height == ModelSize)
            return out512.Copy();
        return out512.Resize(
            new SKImageInfo(crop.Width, crop.Height), SKSamplingOptions.Default);
    }

    private static byte ToByte(float v) =>
        (byte)Math.Clamp((int)Math.Round(v * 255f), 0, 255);

    public void Dispose() => _session?.Dispose();
}

/// <summary>Pembangunan mask tinta dari sebuah wilayah halaman.</summary>
public static class InkMask
{
    /// <summary>Ambang luminansi piksel "tinta gelap".</summary>
    public const int InkLuminance = 140;

    public static bool IsInk(SKColor px) =>
        (px.Red * 299 + px.Green * 587 + px.Blue * 114) / 1000 < InkLuminance;

    /// <summary>
    /// Peta piksel GARIS yang dilindungi pada bitmap (biasanya
    /// potongan inpaint): piksel tinta — ambang sama dengan
    /// <see cref="IsInk"/> — yang termasuk komponen terhubung
    /// (8-arah) yang (a) menyentuh tepi bitmap, ATAU (b) melintasi
    /// batas <paramref name="region"/> yang menyusut
    /// <paramref name="inset"/> piksel: komponen itu punya piksel
    /// tinta di dalam DAN di luar wilayah tersebut. Komponen
    /// semacam itu dianggap garis tepi bubble / karya garis yang
    /// melintasi potongan, bukan glif teks lepas (glif normal utuh
    /// di dalam wilayah). Piksel terlindungi tidak boleh masuk
    /// mask inpaint: garis tepi yang masuk mask ikut terhapus
    /// LaMA (cacat v0.7.5 — pada page-c, kolom teks duduk ±6 px
    /// dari garis tepi; segmen garis yang terhubung ke karya
    /// garis yang keluar potongan hilang sebagian, dan segmen
    /// busur garis yang lepas dari komponen utama pun hilang
    /// karena melintasi batas kotak blok).
    /// </summary>
    public static bool[] LineProtection(SKBitmap bmp, SKRectI region, int inset = 2)
    {
        int w = bmp.Width, h = bmp.Height;
        var result = new bool[w * h];
        if (w <= 0 || h <= 0)
            return result;
        var ink = new bool[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                ink[y * w + x] = IsInk(bmp.GetPixel(x, y));

        int x0 = Math.Clamp(region.Left + inset, 0, w);
        int y0 = Math.Clamp(region.Top + inset, 0, h);
        int x1 = Math.Clamp(region.Right - inset, 0, w);
        int y1 = Math.Clamp(region.Bottom - inset, 0, h);
        bool Inside(int px, int py) => px >= x0 && px < x1 && py >= y0 && py < y1;

        var label = new int[w * h];
        Array.Fill(label, -1);
        var stack = new List<int>();
        var members = new List<int>();
        int nextLabel = 0;
        for (int sy = 0; sy < h; sy++)
        {
            for (int sx = 0; sx < w; sx++)
            {
                int start = sy * w + sx;
                if (!ink[start] || label[start] >= 0)
                    continue;
                members.Clear();
                stack.Clear();
                stack.Add(start);
                label[start] = nextLabel;
                bool touchesEdge = false, hasIn = false, hasOut = false;
                while (stack.Count > 0)
                {
                    int cur = stack[^1];
                    stack.RemoveAt(stack.Count - 1);
                    members.Add(cur);
                    int cx = cur % w, cy = cur / w;
                    if (cx == 0 || cy == 0 || cx == w - 1 || cy == h - 1)
                        touchesEdge = true;
                    if (Inside(cx, cy)) hasIn = true; else hasOut = true;
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            if (dx == 0 && dy == 0)
                                continue;
                            int nx = cx + dx, ny = cy + dy;
                            if (nx < 0 || ny < 0 || nx >= w || ny >= h)
                                continue;
                            int ni = ny * w + nx;
                            if (ink[ni] && label[ni] < 0)
                            {
                                label[ni] = nextLabel;
                                stack.Add(ni);
                            }
                        }
                    }
                }
                if (touchesEdge || (hasIn && hasOut))
                    foreach (int m in members)
                        result[m] = true;
                nextLabel++;
            }
        }
        return result;
    }

    /// <summary>
    /// Bangun mask (putih = di-inpaint) SEUKURAN bitmap sumber, dengan
    /// tinta hanya diambil dari piksel gelap di dalam
    /// <paramref name="region"/>, lalu dilasi <paramref name="dilate"/>
    /// piksel agar sisa anti-alias teks ikut tertutup. Tepi region
    /// menyusut <paramref name="inset"/> piksel dulu, supaya garis
    /// bubble yang kebetulan masuk kotak deteksi tidak ikut terhapus.
    /// Piksel garis terlindungi (<see cref="LineProtection"/>) tidak
    /// pernah masuk mask dan tidak menjadi sumber dilasi;
    /// <paramref name="protect"/> boleh diisi pemanggil agar peta
    /// yang sama dipakai ulang sesudah feathering.
    /// </summary>
    public static SKBitmap Build(SKBitmap page, SKRectI region, int dilate = 3, int inset = 2,
        bool[]? protect = null)
    {
        int w = page.Width, h = page.Height;
        if (protect is null || protect.Length != w * h)
            protect = LineProtection(page, region, inset);
        var raw = new bool[w * h];
        int x0 = Math.Clamp(region.Left + inset, 0, w);
        int y0 = Math.Clamp(region.Top + inset, 0, h);
        int x1 = Math.Clamp(region.Right - inset, 0, w);
        int y1 = Math.Clamp(region.Bottom - inset, 0, h);
        for (int y = y0; y < y1; y++)
        {
            for (int x = x0; x < x1; x++)
            {
                if (IsInk(page.GetPixel(x, y)) && !protect[y * w + x])
                    raw[y * w + x] = true;
            }
        }
        var mask = new SKBitmap(w, h);
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                bool on = false;
                for (int dy = -dilate; dy <= dilate && !on; dy++)
                {
                    for (int dx = -dilate; dx <= dilate && !on; dx++)
                    {
                        int sx = x + dx, sy = y + dy;
                        if (sx >= 0 && sy >= 0 && sx < w && sy < h && raw[sy * w + sx])
                            on = true;
                    }
                }
                // Garis terlindungi tidak pernah termask, walau
                // dilasi dari glif tetangga mencapainya.
                if (protect[y * w + x])
                    on = false;
                mask.SetPixel(x, y, on ? SKColors.White : SKColors.Black);
            }
        }
        return mask;
    }

    /// <summary>
    /// Peta alfa bertepi lembut dari mask biner: nilai 0..1 per piksel
    /// (1 = sepenuhnya hasil inpaint) — mask diperluas
    /// <paramref name="expand"/> piksel dulu, lalu kabur kotak
    /// <paramref name="blur"/> piksel supaya tempelan menyatu tanpa
    /// menyentuh garis bubble jauh di luar mask. Bila
    /// <paramref name="protect"/> diisi (peta dari
    /// <see cref="LineProtection"/>), alfa pada piksel terlindungi
    /// dipaksa 0 di AKHIR: perluasan + kabur tidak boleh menimpa
    /// garis tepi yang sudah dikeluarkan dari mask.
    /// </summary>
    public static float[] FeatherAlpha(SKBitmap mask, int expand = 2, int blur = 2,
        bool[]? protect = null)
    {
        int w = mask.Width, h = mask.Height;
        var a = new float[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                a[y * w + x] = mask.GetPixel(x, y).Red >= 128 ? 1f : 0f;
        if (expand > 0)
        {
            var src = (float[])a.Clone();
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    if (src[y * w + x] > 0)
                        continue;
                    bool near = false;
                    for (int dy = -expand; dy <= expand && !near; dy++)
                        for (int dx = -expand; dx <= expand && !near; dx++)
                        {
                            int sx = x + dx, sy = y + dy;
                            if (sx >= 0 && sy >= 0 && sx < w && sy < h
                                && src[sy * w + sx] > 0)
                                near = true;
                        }
                    if (near)
                        a[y * w + x] = 1f;
                }
        }
        for (int pass = 0; pass < blur; pass++)
        {
            var src = (float[])a.Clone();
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float sum = 0;
                    int n = 0;
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int sx = x + dx, sy = y + dy;
                            if (sx >= 0 && sy >= 0 && sx < w && sy < h)
                            {
                                sum += src[sy * w + sx];
                                n++;
                            }
                        }
                    a[y * w + x] = sum / n;
                }
        }
        // Perlindungan diterapkan lagi paling akhir (v0.7.5):
        // piksel garis harus persis asli, bukan campuran inpaint.
        if (protect is not null && protect.Length == a.Length)
        {
            for (int i = 0; i < a.Length; i++)
                if (protect[i])
                    a[i] = 0f;
        }
        return a;
    }
}
