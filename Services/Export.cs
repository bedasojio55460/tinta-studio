using System.IO.Compression;
using System.Xml;
using SkiaSharp;
using TintaStudio.Models;

namespace TintaStudio.Services;

/// <summary>Bentuk keluaran ekspor bab (Tahap 6).</summary>
public enum ExportKind
{
    Cbz,
    Pdf,
    Folder,
}

/// <summary>Format gambar halaman hasil ekspor. Original = format
/// sumber tiap halaman (yang tidak bisa dikodekan ulang Skia —
/// mis. halaman PDF/AVIF — jatuh ke PNG).</summary>
public enum ExportImageFormat
{
    Original,
    Jpg,
    Png,
    WebP,
}

/// <summary>Pilihan ekspor: bentuk + format gambar + kualitas
/// (1–100, hanya berlaku untuk JPG/WebP; bawaan 90).</summary>
public sealed record ExportOptions(
    ExportKind Kind, ExportImageFormat ImageFormat, int Quality = 90);

/// <summary>Hasil ekspor: jalur keluaran akhir (sudah diunikkan
/// agar tidak menimpa), jumlah halaman, total byte tertulis, dan
/// daftar berkas (satu untuk CBZ/PDF; per halaman untuk Folder).</summary>
public sealed record ExportResult(
    string OutputPath, int PageCount, long TotalBytes,
    IReadOnlyList<string> Files);

/// <summary>
/// Layanan Ekspor (Tahap 6): mengekspor satu bab menjadi halaman
/// FINAL — gambar bersih Tahap 3 + typeset Tahap 5, dibangun lewat
/// jalur yang SAMA dengan CLI <c>--render</c>
/// (<see cref="TypesetService.BuildFinalPageBitmap"/>), bukan jalur
/// render terpisah. Typeset bawaan diterapkan dulu ke blok
/// berterjemahan yang belum di-typeset (sama seperti --render);
/// pemanggil menyimpan proyek sesudahnya bila ingin mempertahankannya.
///
/// Bentuk keluaran:
/// - CBZ: ZIP berisi page-001, page-002, … + ComicInfo.xml bab
///   (disalin persis dari sumber bila sumbernya arsip ZIP;
///   selain itu dibuat dari metadata judul/seri bab).
/// - PDF: satu halaman per gambar (SKDocument.CreatePdf SkiaSharp,
///   tanpa dependensi baru) — format gambar tidak berlaku di sini.
/// - Folder: satu berkas gambar per halaman.
///
/// Keluaran TIDAK PERNAH menimpa diam-diam: berkas tujuan yang
/// sudah ada diberi akhiran " (2)", " (3)", …; untuk Folder,
/// tiap berkas halaman diunikkan di dalam folder tujuan.
/// </summary>
public static class ExportService
{
    public static ExportResult ExportChapter(
        Chapter chapter, ProjectChapter projectChapter,
        string destination, ExportOptions options,
        CancellationToken ct = default, IProgress<string>? progress = null)
    {
        if (chapter.Pages.Count == 0)
            throw new InvalidOperationException(
                "Bab ini tidak punya halaman untuk diekspor.");
        int quality = Math.Clamp(options.Quality, 1, 100);

        TypesetService.AutoTypesetChapter(projectChapter,
            i => i >= 0 && i < chapter.Pages.Count ? chapter.Pages[i].Width : 0);

        using var clean = new CleanService();
        var files = new List<string>();
        long totalBytes = 0;

        switch (options.Kind)
        {
            case ExportKind.Folder:
            {
                Directory.CreateDirectory(destination);
                for (int i = 0; i < chapter.Pages.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    var (bytes, ext) = EncodeFinalPage(
                        chapter, projectChapter, clean, i, options.ImageFormat, quality, ct);
                    string file = UniqueFilePath(
                        Path.Combine(destination, $"page-{i + 1:D3}{ext}"));
                    File.WriteAllBytes(file, bytes);
                    files.Add(file);
                    totalBytes += bytes.Length;
                    progress?.Report($"Halaman {i + 1}/{chapter.Pages.Count} → {file}");
                }
                return new ExportResult(destination, chapter.Pages.Count, totalBytes, files);
            }

            case ExportKind.Cbz:
            {
                string outFile = UniqueFilePath(destination);
                var dir = Path.GetDirectoryName(Path.GetFullPath(outFile));
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
                using (var zip = ZipFile.Open(outFile, ZipArchiveMode.Create))
                {
                    var comicInfo = ComicInfoBytes(chapter);
                    if (comicInfo is not null)
                    {
                        var entry = zip.CreateEntry(
                            "ComicInfo.xml", CompressionLevel.Optimal);
                        using var es = entry.Open();
                        es.Write(comicInfo, 0, comicInfo.Length);
                    }
                    for (int i = 0; i < chapter.Pages.Count; i++)
                    {
                        ct.ThrowIfCancellationRequested();
                        var (bytes, ext) = EncodeFinalPage(
                            chapter, projectChapter, clean, i,
                            options.ImageFormat, quality, ct);
                        var entry = zip.CreateEntry(
                            $"page-{i + 1:D3}{ext}", CompressionLevel.Optimal);
                        using var es = entry.Open();
                        es.Write(bytes, 0, bytes.Length);
                        totalBytes += bytes.Length;
                        progress?.Report(
                            $"Halaman {i + 1}/{chapter.Pages.Count} → page-{i + 1:D3}{ext}");
                    }
                }
                totalBytes = new FileInfo(outFile).Length;
                files.Add(outFile);
                return new ExportResult(outFile, chapter.Pages.Count, totalBytes, files);
            }

            case ExportKind.Pdf:
            {
                string outFile = UniqueFilePath(destination);
                var dir = Path.GetDirectoryName(Path.GetFullPath(outFile));
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
                using (var fs = File.Create(outFile))
                using (var doc = SKDocument.CreatePdf(fs))
                {
                    for (int i = 0; i < chapter.Pages.Count; i++)
                    {
                        ct.ThrowIfCancellationRequested();
                        var page = projectChapter.PageAt(i);
                        using var final = TypesetService.BuildFinalPageBitmap(
                            chapter, projectChapter, page, clean, ct);
                        using var canvas = doc.BeginPage(final.Width, final.Height);
                        canvas.DrawBitmap(final, 0, 0);
                        doc.EndPage();
                        progress?.Report(
                            $"Halaman {i + 1}/{chapter.Pages.Count} → PDF");
                    }
                    doc.Close();
                }
                totalBytes = new FileInfo(outFile).Length;
                files.Add(outFile);
                return new ExportResult(outFile, chapter.Pages.Count, totalBytes, files);
            }

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(options), $"Bentuk ekspor tidak dikenal: {options.Kind}");
        }
    }

    /// <summary>Bangun halaman final ke-i lalu kodekan sesuai format
    /// pilihan. Mengembalikan byte gambar + ekstensi berkasnya.</summary>
    private static (byte[] Bytes, string Extension) EncodeFinalPage(
        Chapter chapter, ProjectChapter projectChapter, CleanService clean,
        int index, ExportImageFormat format, int quality, CancellationToken ct)
    {
        var page = projectChapter.PageAt(index);
        using var final = TypesetService.BuildFinalPageBitmap(
            chapter, projectChapter, page, clean, ct);
        var (skFormat, ext) = ResolveFormat(format, chapter.Pages[index].SourceFormat);
        using var img = SKImage.FromBitmap(final);
        using var data = img.Encode(skFormat, skFormat == SKEncodedImageFormat.Png ? 100 : quality)
            ?? throw new InvalidDataException(
                $"Gagal mengkodekan halaman {index + 1} sebagai {skFormat}.");
        return (data.ToArray(), ext);
    }

    /// <summary>Petakan pilihan format pengguna + format sumber ke
    /// format Skia + ekstensi. Original mengikuti format sumber
    /// (JPG/PNG/WebP/GIF); selain itu → PNG.</summary>
    private static (SKEncodedImageFormat Format, string Extension) ResolveFormat(
        ExportImageFormat chosen, string sourceFormat) => chosen switch
        {
            ExportImageFormat.Jpg => (SKEncodedImageFormat.Jpeg, ".jpg"),
            ExportImageFormat.Png => (SKEncodedImageFormat.Png, ".png"),
            ExportImageFormat.WebP => (SKEncodedImageFormat.Webp, ".webp"),
            _ => sourceFormat.Trim().ToUpperInvariant() switch
            {
                "JPG" or "JPEG" => (SKEncodedImageFormat.Jpeg, ".jpg"),
                "WEBP" => (SKEncodedImageFormat.Webp, ".webp"),
                "GIF" => (SKEncodedImageFormat.Gif, ".gif"),
                _ => (SKEncodedImageFormat.Png, ".png"),
            },
        };

    /// <summary>ComicInfo.xml untuk CBZ hasil ekspor: salinan persis
    /// dari sumber bila sumbernya berkas ZIP yang memuatnya; selain
    /// itu XML minimal dari metadata bab (judul + seri). Null bila
    /// tidak ada metadata sama sekali.</summary>
    private static byte[]? ComicInfoBytes(Chapter chapter)
    {
        try
        {
            if (File.Exists(chapter.SourcePath))
            {
                using var zip = ZipFile.OpenRead(chapter.SourcePath);
                var entry = zip.Entries.FirstOrDefault(e =>
                    Path.GetFileName(e.FullName).Equals(
                        "ComicInfo.xml", StringComparison.OrdinalIgnoreCase));
                if (entry is not null)
                {
                    using var es = entry.Open();
                    using var ms = new MemoryStream();
                    es.CopyTo(ms);
                    return ms.ToArray();
                }
            }
        }
        catch (Exception)
        {
            // Sumber bukan ZIP terbaca — jatuh ke metadata di bawah.
        }

        if (string.IsNullOrWhiteSpace(chapter.Title)
            && string.IsNullOrWhiteSpace(chapter.Series))
            return null;
        using var outMs = new MemoryStream();
        using (var w = XmlWriter.Create(outMs, new XmlWriterSettings
        {
            Encoding = new System.Text.UTF8Encoding(false),
            Indent = true,
        }))
        {
            w.WriteStartDocument();
            w.WriteStartElement("ComicInfo");
            if (!string.IsNullOrWhiteSpace(chapter.Title))
                w.WriteElementString("Title", chapter.Title);
            if (!string.IsNullOrWhiteSpace(chapter.Series))
                w.WriteElementString("Series", chapter.Series);
            w.WriteEndElement();
            w.WriteEndDocument();
        }
        return outMs.ToArray();
    }

    /// <summary>Jalur berkas yang belum ada: bila
    /// <paramref name="path"/> sudah terpakai, sisipkan " (2)",
    /// " (3)", … sebelum ekstensi.</summary>
    public static string UniqueFilePath(string path)
    {
        if (!File.Exists(path))
            return path;
        var dir = Path.GetDirectoryName(path) ?? "";
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        for (int n = 2; ; n++)
        {
            var candidate = Path.Combine(dir, $"{name} ({n}){ext}");
            if (!File.Exists(candidate))
                return candidate;
        }
    }

    /// <summary>Nama berkas aman dari judul bab (karakter terlarang
    /// nama berkas → '_'; kosong → "tinta-studio").</summary>
    public static string SafeFileName(string title)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = title.Trim().Select(
            c => invalid.Contains(c) ? '_' : c).ToArray();
        var name = new string(chars).Trim();
        return name.Length == 0 ? "tinta-studio" : name;
    }
}
