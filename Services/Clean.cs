using System.Security.Cryptography;
using System.Text;
using SkiaSharp;
using TintaStudio.Models;

namespace TintaStudio.Services;

/// <summary>
/// Layanan Pembersihan Bubble (Tahap 3) — NON-DESTRUKTIF: halaman
/// asli tidak pernah diubah; gambar bersih selalu DIBANGUN ULANG
/// dari salinan halaman asli + daftar operasi halaman (per blok:
/// Solid/Inpaint; lalu sapuan manual). Menghapus operasi / Urungkan
/// cukup membangun ulang.
///
/// Mode per blok:
/// - Solid: isi seluruh kotak blok dengan warna (pilihan pengguna,
///   atau sampel otomatis warna kertas di dalam bubble).
/// - Inpaint: LaMA per potongan blok+margin; mask dari piksel tinta
///   di dalam kotak blok (dilasi), hasil ditempel hanya di dalam
///   mask bertepi lembut — garis bubble &amp; gambar di luar mask
///   persis asli.
/// Sapuan manual: kuas warna menggambar di atas hasil bersih;
/// penghapus mengembalikan piksel ASLI di cakram sapuan.
/// </summary>
public sealed class CleanService : IDisposable
{
    private readonly LamaInpainter _inpainter = new();

    /// <summary>Inpainter bersama (sesi ONNX malas).</summary>
    public LamaInpainter Inpainter => _inpainter;

    public void Dispose() => _inpainter.Dispose();

    /// <summary>Halaman ini punya pekerjaan bersih apa pun?</summary>
    public static bool HasWork(ProjectPage page) =>
        page.CleanOps.Count > 0 || page.ManualStrokes.Count > 0;

    /// <summary>
    /// Bangun gambar bersih satu halaman dari bitmap asli (asli
    /// TIDAK diubah — semua gambar dilakukan pada salinan).
    /// <paramref name="progress"/> melaporkan teks tahap berjalan.
    /// </summary>
    public SKBitmap BuildCleanedPage(
        SKBitmap original, ProjectPage page, CancellationToken ct = default,
        IProgress<string>? progress = null)
    {
        var result = BuildOpsBitmap(original, page, ct, progress);
        ApplyStrokes(result, original, page, ct);
        return result;
    }

    /// <summary>
    /// Bangun gambar dari operasi per blok SAJA (tanpa sapuan
    /// manual). Ini bagian yang mahal (inpaint), jadi hasilnya yang
    /// di-cache CleanStore; sapuan dikomposit di atasnya.
    /// </summary>
    public SKBitmap BuildOpsBitmap(
        SKBitmap original, ProjectPage page, CancellationToken ct = default,
        IProgress<string>? progress = null)
    {
        var result = original.Copy();
        // Operasi per blok dalam urutan Seq.
        foreach (var op in page.CleanOps.OrderBy(o => o.Seq))
        {
            ct.ThrowIfCancellationRequested();
            var block = page.Blocks.FirstOrDefault(b => b.Id == op.BlockId);
            if (block is null)
                continue; // blok sudah dihapus/diganti deteksi baru
            if (op.Mode == CleanMode.Solid)
            {
                var color = op.FillColor is not null
                    ? ParseColor(op.FillColor)
                    : SampleBubbleColor(original, block);
                FillBlock(result, block, color);
            }
            else
            {
                progress?.Report($"Inpaint blok di ({block.X},{block.Y})…");
                InpaintBlock(result, block, ct, op.LineProtect);
            }
        }
        return result;
    }

    /// <summary>Terapkan semua sapuan manual halaman, urut Seq.</summary>
    public static void ApplyStrokes(
        SKBitmap target, SKBitmap original, ProjectPage page,
        CancellationToken ct = default)
    {
        foreach (var stroke in page.ManualStrokes.OrderBy(s => s.Seq))
        {
            ct.ThrowIfCancellationRequested();
            ApplyStroke(target, original, stroke);
        }
    }

    /// <summary>Isi kotak blok dengan warna solid.</summary>
    public static void FillBlock(SKBitmap target, TextBlock block, SKColor color)
    {
        var rect = ClampRect(target, block);
        if (rect.Width <= 0 || rect.Height <= 0)
            return;
        using var canvas = new SKCanvas(target);
        using var paint = new SKPaint { Color = color, Style = SKPaintStyle.Fill };
        canvas.DrawRect(new SKRect(rect.Left, rect.Top, rect.Right, rect.Bottom), paint);
        canvas.Flush();
    }

    /// <summary>
    /// Warna bawaan mode Solid: rata-rata piksel terang (kertas) di
    /// dalam kotak blok — warna interior bubble itu sendiri; jatuh
    /// ke putih murni bila tak ada sampel cukup.
    /// </summary>
    public static SKColor SampleBubbleColor(SKBitmap page, TextBlock block)
    {
        var rect = ClampRect(page, block);
        long r = 0, g = 0, b = 0;
        int n = 0;
        for (int y = rect.Top; y < rect.Bottom; y += 2)
        {
            for (int x = rect.Left; x < rect.Right; x += 2)
            {
                var px = page.GetPixel(x, y);
                int lum = (px.Red * 299 + px.Green * 587 + px.Blue * 114) / 1000;
                if (lum >= 200)
                {
                    r += px.Red; g += px.Green; b += px.Blue;
                    n++;
                }
            }
        }
        if (n < 8)
            return SKColors.White;
        return new SKColor((byte)(r / n), (byte)(g / n), (byte)(b / n));
    }

    /// <summary>
    /// Inpaint satu blok pada bitmap target (diubah di tempat):
    /// potong blok+margin, mask tinta dari dalam kotak blok, jalankan
    /// LaMA, tempel balik hanya di area mask bertepi lembut. Bila
    /// <paramref name="lineProtect"/> menyala (bawaan), komponen
    /// tinta yang menyentuh tepi potongan atau melintasi batas kotak
    /// blok (garis tepi bubble / karya garis) DILINDUNGI — tidak
    /// masuk mask dan alfanya dipaksa 0 — supaya garis tepi yang
    /// duduk dekat kolom teks tidak ikut terhapus (v0.7.5); huruf
    /// yang menyatu fisik dengan garis ikut terlindungi dan bisa
    /// tertinggal. Bila mati (v0.7.6), tidak ada perlindungan sama
    /// sekali: semua tinta di dalam kotak dihapus tuntas dan garis
    /// tepi bisa terpotong — pilihan per operasi (CleanOperation.
    /// LineProtect), supaya pengguna memilih hasil terbaik per kasus.
    /// </summary>
    public void InpaintBlock(SKBitmap target, TextBlock block,
        CancellationToken ct = default, bool lineProtect = true)
    {
        var blockRect = ClampRect(target, block);
        if (blockRect.Width <= 0 || blockRect.Height <= 0)
            return;
        int margin = Math.Clamp(Math.Max(blockRect.Width, blockRect.Height) * 2 / 5, 24, 160);
        var cropRect = SKRectI.Inflate(blockRect, margin, margin);
        cropRect.Left = Math.Max(0, cropRect.Left);
        cropRect.Top = Math.Max(0, cropRect.Top);
        cropRect.Right = Math.Min(target.Width, cropRect.Right);
        cropRect.Bottom = Math.Min(target.Height, cropRect.Bottom);
        if (cropRect.Width <= 0 || cropRect.Height <= 0)
            return;

        ct.ThrowIfCancellationRequested();
        using var crop = new SKBitmap(cropRect.Width, cropRect.Height);
        using (var canvas = new SKCanvas(crop))
            canvas.DrawBitmap(target,
                new SKRect(cropRect.Left, cropRect.Top, cropRect.Right, cropRect.Bottom),
                new SKRect(0, 0, cropRect.Width, cropRect.Height));

        // Mask hanya dari tinta DI DALAM kotak blok (relatif ke potongan).
        var blockInCrop = new SKRectI(
            blockRect.Left - cropRect.Left, blockRect.Top - cropRect.Top,
            blockRect.Right - cropRect.Left, blockRect.Bottom - cropRect.Top);
        // Peta perlindungan garis dihitung sekali dari potongan,
        // dipakai mask (sebelum dilasi) dan alfa (sesudah feather).
        // Mode hapus tuntas: peta serba-false seukuran potongan —
        // InkMask memakai peta apa adanya (panjang cocok), jadi
        // tidak ada piksel yang dilindungi di mask maupun alfa.
        var protect = lineProtect
            ? InkMask.LineProtection(crop, blockInCrop, inset: 2)
            : new bool[cropRect.Width * cropRect.Height];
        using var mask = InkMask.Build(crop, blockInCrop, dilate: 3, inset: 2, protect);
        var alpha = InkMask.FeatherAlpha(mask, expand: 2, blur: 2, protect);

        using var filled = _inpainter.Inpaint(crop, mask);
        for (int y = 0; y < cropRect.Height; y++)
        {
            for (int x = 0; x < cropRect.Width; x++)
            {
                float a = alpha[y * cropRect.Width + x];
                if (a <= 0f)
                    continue;
                var dst = target.GetPixel(cropRect.Left + x, cropRect.Top + y);
                var src = filled.GetPixel(x, y);
                if (a >= 1f)
                {
                    target.SetPixel(cropRect.Left + x, cropRect.Top + y, src);
                }
                else
                {
                    target.SetPixel(cropRect.Left + x, cropRect.Top + y, new SKColor(
                        (byte)(dst.Red + (src.Red - dst.Red) * a),
                        (byte)(dst.Green + (src.Green - dst.Green) * a),
                        (byte)(dst.Blue + (src.Blue - dst.Blue) * a)));
                }
            }
        }
    }

    /// <summary>Terapkan satu sapuan manual pada hasil bersih.</summary>
    public static void ApplyStroke(SKBitmap target, SKBitmap original, BrushStroke stroke)
    {
        if (stroke.Points.Count == 0)
            return;
        using var canvas = new SKCanvas(target);
        if (stroke.Erased)
        {
            // Penghapus: salin kembali cakram piksel ASLI di tiap titik.
            foreach (var (cx, cy) in Densify(stroke))
            {
                canvas.Save();
                using var clip = new SKPath();
                clip.AddCircle(cx, cy, stroke.Radius);
                canvas.ClipPath(clip);
                canvas.DrawBitmap(original, 0, 0);
                canvas.Restore();
            }
        }
        else
        {
            using var paint = new SKPaint
            {
                Color = ParseColor(stroke.Color),
                Style = SKPaintStyle.Fill,
                IsAntialias = true,
            };
            foreach (var (cx, cy) in Densify(stroke))
                canvas.DrawCircle(cx, cy, stroke.Radius, paint);
        }
        canvas.Flush();
    }

    /// <summary>Titik-titik sapuan dirapatkan (langkah ≤ ½ radius)
    /// agar garis sapuan tidak putus-putus.</summary>
    private static IEnumerable<(float X, float Y)> Densify(BrushStroke stroke)
    {
        var pts = stroke.Points;
        float step = Math.Max(2, stroke.Radius / 2);
        for (int i = 0; i < pts.Count; i++)
        {
            if (i == 0)
            {
                yield return (pts[0].X, pts[0].Y);
                continue;
            }
            float dx = pts[i].X - pts[i - 1].X, dy = pts[i].Y - pts[i - 1].Y;
            float dist = MathF.Sqrt(dx * dx + dy * dy);
            int n = Math.Max(1, (int)(dist / step));
            for (int k = 1; k <= n; k++)
                yield return (pts[i - 1].X + dx * k / n, pts[i - 1].Y + dy * k / n);
        }
    }

    // ─── Manajemen operasi (dipakai UI & CLI) ───────────────────────

    /// <summary>Catat/ganti operasi bersih untuk satu blok.
    /// <paramref name="lineProtect"/> hanya bermakna untuk mode
    /// Inpaint (lihat CleanOperation.LineProtect).</summary>
    public static CleanOperation SetBlockOperation(
        ProjectPage page, TextBlock block, CleanMode mode, string? fillColor,
        bool lineProtect = true)
    {
        var op = page.CleanOps.FirstOrDefault(o => o.BlockId == block.Id);
        if (op is null)
        {
            op = new CleanOperation { BlockId = block.Id };
            page.CleanOps.Add(op);
        }
        op.Mode = mode;
        op.FillColor = fillColor;
        op.LineProtect = lineProtect;
        op.Seq = ++page.CleanSeq;
        op.AppliedUtc = DateTime.UtcNow;
        return op;
    }

    /// <summary>Tambah sapuan manual ke halaman.</summary>
    public static BrushStroke AddStroke(ProjectPage page, BrushStroke stroke)
    {
        stroke.Seq = ++page.CleanSeq;
        page.ManualStrokes.Add(stroke);
        return stroke;
    }

    /// <summary>
    /// Urungkan pekerjaan bersih terakhir halaman ini (operasi blok
    /// atau sapuan, menurut Seq terbesar). False bila tak ada apa pun.
    /// </summary>
    public static bool UndoLast(ProjectPage page)
    {
        var lastOp = page.CleanOps.OrderByDescending(o => o.Seq).FirstOrDefault();
        var lastStroke = page.ManualStrokes.OrderByDescending(s => s.Seq).FirstOrDefault();
        if (lastOp is null && lastStroke is null)
            return false;
        if (lastStroke is not null && (lastOp is null || lastStroke.Seq > lastOp.Seq))
            page.ManualStrokes.Remove(lastStroke);
        else
            page.CleanOps.Remove(lastOp!);
        return true;
    }

    // ─── Utilitas ────────────────────────────────────────────────────

    public static SKRectI ClampRect(SKBitmap bmp, TextBlock block)
    {
        int x = Math.Clamp(block.X, 0, bmp.Width);
        int y = Math.Clamp(block.Y, 0, bmp.Height);
        int right = Math.Clamp(block.X + block.Width, 0, bmp.Width);
        int bottom = Math.Clamp(block.Y + block.Height, 0, bmp.Height);
        return new SKRectI(x, y, Math.Max(x, right), Math.Max(y, bottom));
    }

    public static SKColor ParseColor(string hex)
    {
        if (SKColor.TryParse(hex, out var color))
            return color;
        return SKColors.White;
    }

    public static string ToHex(SKColor c) => $"#{c.Red:X2}{c.Green:X2}{c.Blue:X2}";

    /// <summary>Hash SHA-256 byte PNG bitmap — pembanding identitas gambar.</summary>
    public static string PngHash(SKBitmap bmp)
    {
        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        return Convert.ToHexString(SHA256.HashData(data.AsSpan())).ToLowerInvariant();
    }
}

/// <summary>
/// Cache berkas turunan gambar bersih per (bab, halaman), berdampingan
/// dengan proyek kerja di &lt;config&gt;/cleaned/&lt;hash-bab&gt;/.
/// Yang di-cache adalah gambar DASAR hasil operasi per blok saja
/// (bagian mahal: inpaint) — nama berkas memuat tanda tangan operasi,
/// jadi operasi yang berubah otomatis menidakberlakukan cache lama.
/// Sapuan manual TIDAK ikut tanda tangan: sapuan dikomposit di atas
/// gambar dasar saat gambar diminta, sehingga menyunting sapuan
/// tidak pernah memicu inpaint ulang.
/// </summary>
public static class CleanStore
{
    public static string StoreDir
    {
        get
        {
            var env = Environment.GetEnvironmentVariable("TINTA_CONFIG_DIR");
            var baseDir = !string.IsNullOrWhiteSpace(env)
                ? env
                : Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".config", "tinta-studio");
            return Path.Combine(baseDir, "cleaned");
        }
    }

    /// <summary>Tanda tangan operasi per blok satu halaman.</summary>
    public static string OpsSignature(ProjectChapter chapter, ProjectPage page, ComicPage info)
    {
        var sb = new StringBuilder();
        sb.Append(chapter.SourcePath).Append('|').Append(info.Width).Append('x').Append(info.Height);
        foreach (var op in page.CleanOps.OrderBy(o => o.Seq))
        {
            var b = page.Blocks.FirstOrDefault(x => x.Id == op.BlockId);
            sb.Append("|op:").Append(op.BlockId).Append(':').Append(op.Mode)
              .Append(':').Append(op.FillColor ?? "auto")
              .Append(':').Append(op.LineProtect ? "garis" : "tuntas");
            if (b is not null)
                sb.Append(':').Append(b.X).Append(',').Append(b.Y)
                  .Append(',').Append(b.Width).Append(',').Append(b.Height);
        }
        return Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())))[..16].ToLowerInvariant();
    }

    /// <summary>
    /// Gambar bersih penuh satu halaman (dasar operasi + sapuan
    /// manual terkomposit), atau null bila halaman belum punya
    /// pekerjaan bersih apa pun (bersih = asli). Gambar dasar diambil
    /// dari cache berkas bila tanda tangan operasi cocok.
    /// </summary>
    public static SKBitmap? GetCleanedPageBitmap(
        CleanService clean, Chapter chapter, ProjectChapter projectChapter,
        ProjectPage page, CancellationToken ct = default,
        IProgress<string>? progress = null)
    {
        if (!CleanService.HasWork(page))
            return null;
        var info = chapter.Pages[page.Index];
        using var original = SKBitmap.Decode(info.WorkPath)
            ?? throw new InvalidDataException($"Halaman kerja tidak terbaca: {info.SourceName}");

        SKBitmap baseBmp;
        if (page.CleanOps.Count == 0)
        {
            baseBmp = original.Copy();
        }
        else
        {
            var sig = OpsSignature(projectChapter, page, info);
            var dir = Path.Combine(StoreDir,
                Path.GetFileNameWithoutExtension(
                    ProjectStore.PathFor(chapter.SourcePath)));
            var file = Path.Combine(dir, $"p{page.Index}-{sig}.png");
            baseBmp = (File.Exists(file) ? SKBitmap.Decode(file) : null)
                ?? clean.BuildOpsBitmap(original, page, ct, progress);
            if (!File.Exists(file))
            {
                Directory.CreateDirectory(dir);
                foreach (var old in Directory.GetFiles(dir, $"p{page.Index}-*.png"))
                {
                    if (!string.Equals(old, file, StringComparison.Ordinal))
                    {
                        try { File.Delete(old); } catch (IOException) { }
                    }
                }
                using var img = SKImage.FromBitmap(baseBmp);
                using var data = img.Encode(SKEncodedImageFormat.Png, 100);
                using var fs = File.Create(file);
                data.SaveTo(fs);
            }
        }
        CleanService.ApplyStrokes(baseBmp, original, page, ct);
        return baseBmp;
    }
}
