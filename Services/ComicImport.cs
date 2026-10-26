using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using SharpCompress.Archives;
using SharpCompress.Readers;
using SkiaSharp;

namespace TintaStudio.Services;

/// <summary>Format wadah komik yang dikenali dari magic bytes / sifat sumber.</summary>
public enum ComicFormat
{
    Zip,
    Rar,
    SevenZip,
    Tar,
    Pdf,
    Epub,
    Folder,
    Image,
    Unknown,
}

/// <summary>Kesalahan impor yang pesannya layak ditampilkan apa adanya ke pengguna.</summary>
public sealed class ComicImportException : Exception
{
    public ComicImportException(string message) : base(message) { }
    public ComicImportException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>Satu entri yang dilewati saat impor, beserta alasannya (dilaporkan jujur).</summary>
public sealed record SkippedEntry(string Name, string Reason);

/// <summary>Satu halaman komik hasil impor yang sudah dinormalisasi.</summary>
public sealed class ComicPage
{
    public required int Index { get; init; }
    /// <summary>Nama entri asal di dalam wadah (atau nama berkas).</summary>
    public required string SourceName { get; init; }
    /// <summary>Format gambar asal, mis. "JPG", "PNG", "PDF".</summary>
    public required string SourceFormat { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    /// <summary>Jalur salinan kerja PNG ternormalisasi.</summary>
    public required string WorkPath { get; init; }
}

/// <summary>Satu bab komik hasil impor.</summary>
public sealed class Chapter
{
    public required string Title { get; init; }
    public required string SourcePath { get; init; }
    /// <summary>Nama format tampil, mis. "ZIP (CBZ)".</summary>
    public required string Format { get; init; }
    public string? Series { get; init; }
    public IReadOnlyList<ComicPage> Pages { get; init; } = Array.Empty<ComicPage>();
    public IReadOnlyList<SkippedEntry> Skipped { get; init; } = Array.Empty<SkippedEntry>();
}

/// <summary>
/// Impor bab komik dari beragam wadah. Format dikenali dari MAGIC BYTES isi
/// berkas (bukan ekstensi — berkas komik di dunia nyata sering salah label),
/// halaman diurutkan secara natural, berkas sampah disaring, dan setiap
/// halaman dinormalisasi (rotasi EXIF diterapkan, didekode ulang) menjadi
/// salinan kerja PNG. Berkas asli tidak pernah diubah.
/// </summary>
public static class ComicImport
{
    public static string DisplayName(ComicFormat format) => format switch
    {
        ComicFormat.Zip => "ZIP (CBZ)",
        ComicFormat.Rar => "RAR (CBR)",
        ComicFormat.SevenZip => "7z (CB7)",
        ComicFormat.Tar => "TAR (CBT)",
        ComicFormat.Pdf => "PDF",
        ComicFormat.Epub => "EPUB",
        ComicFormat.Folder => "Folder",
        ComicFormat.Image => "Gambar",
        _ => "Tidak dikenali",
    };

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".gif", ".bmp", ".tif", ".tiff", ".avif",
    };

    private static readonly HashSet<string> JunkNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Thumbs.db", "desktop.ini", ".DS_Store", ".AppleDouble", ".LSOverride",
    };

    /// <summary>Kenali format sumber dari magic bytes / sifatnya.</summary>
    public static ComicFormat DetectFormat(string path)
    {
        if (Directory.Exists(path)) return ComicFormat.Folder;
        if (!File.Exists(path))
            throw new ComicImportException($"Berkas tidak ditemukan: {path}");

        Span<byte> head = stackalloc byte[512];
        int read;
        using (var fs = File.OpenRead(path))
            read = fs.Read(head);
        head = head[..read];

        // ZIP: "PK\x03\x04" (atau arsip kosong / spanned) -> keluarga zip.
        if (read >= 4 && head[0] == (byte)'P' && head[1] == (byte)'K')
        {
            return IsEpub(path) ? ComicFormat.Epub : ComicFormat.Zip;
        }
        // RAR: "Rar!\x1a\x07\x00" (RAR1.5-4) atau "Rar!\x1a\x07\x01\x00" (RAR5).
        if (read >= 7 && head[0] == (byte)'R' && head[1] == (byte)'a' && head[2] == (byte)'r'
            && head[3] == (byte)'!' && head[4] == 0x1A && head[5] == 0x07)
            return ComicFormat.Rar;
        // 7z: 37 7A BC AF 27 1C.
        if (read >= 6 && head[0] == 0x37 && head[1] == 0x7A && head[2] == 0xBC
            && head[3] == 0xAF && head[4] == 0x27 && head[5] == 0x1C)
            return ComicFormat.SevenZip;
        // PDF: "%PDF-".
        if (read >= 5 && head[0] == (byte)'%' && head[1] == (byte)'P' && head[2] == (byte)'D'
            && head[3] == (byte)'F' && head[4] == (byte)'-')
            return ComicFormat.Pdf;
        // TAR: penanda "ustar" pada offset 257 (format POSIX/GNU).
        if (read >= 262 && Encoding.ASCII.GetString(head.Slice(257, 5)) == "ustar")
            return ComicFormat.Tar;
        // TAR format lama (V7, tanpa magic — mis. keluaran SharpCompress):
        // sahkan lewat checksum blok header 512 byte pertama.
        if (read >= 512 && LooksLikeTarHeader(head[..512]))
            return ComicFormat.Tar;

        // Gambar tunggal: bisa didekode SkiaSharp?
        try
        {
            using var stream = File.OpenRead(path);
            using var codec = SKCodec.Create(stream);
            if (codec is not null) return ComicFormat.Image;
        }
        catch (Exception)
        {
            // abaikan — jatuh ke Unknown
        }
        return ComicFormat.Unknown;
    }

    /// <summary>
    /// Uji apakah blok 512 byte adalah header TAR yang sah: bendera tipe
    /// dikenal + checksum oktal di offset 148 cocok dengan jumlah byte
    /// header (bidang checksum dihitung sebagai spasi). Cara baku yang
    /// dipakai pustaka arsip untuk format tanpa magic.
    /// </summary>
    private static bool LooksLikeTarHeader(ReadOnlySpan<byte> block)
    {
        if (block.Length < 512) return false;
        var typeFlag = block[156];
        if (!(typeFlag == 0 || (typeFlag >= (byte)'0' && typeFlag <= (byte)'7')
              || typeFlag == (byte)'g' || typeFlag == (byte)'x'
              || typeFlag == (byte)'L' || typeFlag == (byte)'K'))
            return false;

        // Baca nilai checksum tersimpan (oktal, diakhiri NUL/spasi).
        long stored = 0;
        bool anyDigit = false;
        for (int i = 148; i < 156; i++)
        {
            var c = block[i];
            if (c >= (byte)'0' && c <= (byte)'7')
            {
                stored = stored * 8 + (c - (byte)'0');
                anyDigit = true;
            }
            else if (anyDigit)
            {
                break;
            }
        }
        if (!anyDigit || stored == 0) return false;

        long unsigned = 0;
        long signed = 0;
        for (int i = 0; i < 512; i++)
        {
            var b = (i >= 148 && i < 156) ? (byte)' ' : block[i];
            unsigned += b;
            signed += (sbyte)b;
        }
        return stored == unsigned || stored == signed;
    }

    private static bool IsEpub(string path)
    {
        try
        {
            using var zip = ZipFile.OpenRead(path);
            var mimetype = zip.GetEntry("mimetype");
            if (mimetype is null) return false;
            using var reader = new StreamReader(mimetype.Open());
            var content = reader.ReadToEnd().Trim();
            return content.StartsWith("application/epub+zip", StringComparison.Ordinal);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Buka satu bab komik. Salinan kerja PNG tiap halaman ditulis ke
    /// <paramref name="workDir"/> (dibuat bila belum ada).
    /// </summary>
    public static Chapter OpenChapter(string source, string workDir)
    {
        var format = DetectFormat(source);
        if (format == ComicFormat.Unknown)
            throw new ComicImportException(
                $"Format berkas tidak dikenali dari isinya: {Path.GetFileName(source)}. " +
                "Yang didukung: CBZ/ZIP, CBR/RAR, CB7/7z, CBT/TAR, PDF, EPUB, folder, dan gambar lepas.");

        Directory.CreateDirectory(workDir);
        var skipped = new List<SkippedEntry>();
        string? comicInfoTitle = null;
        string? comicInfoSeries = null;

        List<ComicPage> pages;

        if (format == ComicFormat.Pdf)
        {
            pages = ImportPdfPages(source, workDir, skipped);
        }
        else
        {
            // Kumpulkan kandidat entri gambar (nama, data mentah) dari wadah.
            var raw = new List<(string Name, byte[] Data)>();
            var preserveOrder = false;
            switch (format)
            {
                case ComicFormat.Folder:
                    CollectFolder(source, raw, skipped, out comicInfoTitle, out comicInfoSeries);
                    break;
                case ComicFormat.Image:
                    raw.Add((Path.GetFileName(source), File.ReadAllBytes(source)));
                    break;
                case ComicFormat.Zip:
                    CollectZip(source, raw, skipped, out comicInfoTitle, out comicInfoSeries);
                    break;
                case ComicFormat.Epub:
                    CollectEpub(source, raw, skipped, out comicInfoTitle, out comicInfoSeries,
                        out preserveOrder);
                    break;
                case ComicFormat.Rar:
                case ComicFormat.SevenZip:
                case ComicFormat.Tar:
                    CollectSharpCompress(source, raw, skipped, out comicInfoTitle, out comicInfoSeries);
                    break;
            }

            pages = NormalizePages(raw, workDir, skipped, preserveOrder);
        }

        if (pages.Count == 0)
            throw new ComicImportException(
                $"Tidak ada halaman gambar yang bisa dibaca dari {Path.GetFileName(source)} " +
                $"({skipped.Count} entri dilewati).");

        var title = comicInfoTitle
                    ?? comicInfoSeries
                    ?? DefaultTitle(source, format);
        return new Chapter
        {
            Title = title,
            SourcePath = Path.GetFullPath(source),
            Format = DisplayName(format),
            Series = comicInfoSeries,
            Pages = pages,
            Skipped = skipped,
        };
    }

    private static string DefaultTitle(string source, ComicFormat format)
    {
        if (format == ComicFormat.Folder)
            return new DirectoryInfo(source).Name;
        return Path.GetFileNameWithoutExtension(source);
    }

    // ------------------------------------------------------------------
    // Pengumpulan entri per wadah
    // ------------------------------------------------------------------

    private sealed class EntrySink
    {
        public List<(string Name, byte[] Data)> Raw { get; } = new();
        public string? ComicInfoTitle;
        public string? ComicInfoSeries;

        public void AcceptComicInfo(byte[] data)
        {
            try
            {
                var doc = XDocument.Parse(Encoding.UTF8.GetString(data));
                var root = doc.Root;
                if (root is null) return;
                ComicInfoSeries = root.Element("Series")?.Value?.Trim();
                ComicInfoTitle = root.Element("Title")?.Value?.Trim();
                if (string.IsNullOrEmpty(ComicInfoTitle))
                    ComicInfoTitle = null;
                if (string.IsNullOrEmpty(ComicInfoSeries))
                    ComicInfoSeries = null;
            }
            catch (Exception)
            {
                // ComicInfo.xml rusak — abaikan metadatanya saja.
            }
        }
    }

    private static bool IsJunkPath(string name)
    {
        var segments = name.Split('/', '\\');
        foreach (var seg in segments)
        {
            if (seg.Equals("__MACOSX", StringComparison.OrdinalIgnoreCase)) return true;
            if (JunkNames.Contains(seg)) return true;
            if (seg.StartsWith("._", StringComparison.Ordinal)) return true; // fork sumber daya macOS
        }
        return false;
    }

    private static bool IsComicInfo(string name) =>
        Path.GetFileName(name).Equals("ComicInfo.xml", StringComparison.OrdinalIgnoreCase);

    private static void RouteEntry(
        EntrySink sink, List<SkippedEntry> skipped, string name, Func<byte[]> readData)
    {
        var normalized = name.Replace('\\', '/');
        if (IsJunkPath(normalized))
        {
            skipped.Add(new SkippedEntry(normalized, "berkas sampah sistem"));
            return;
        }
        if (IsComicInfo(normalized))
        {
            sink.AcceptComicInfo(readData());
            return;
        }
        var ext = Path.GetExtension(normalized);
        if (!ImageExtensions.Contains(ext))
        {
            skipped.Add(new SkippedEntry(normalized, "bukan berkas gambar"));
            return;
        }
        sink.Raw.Add((normalized, readData()));
    }

    private static void CollectFolder(
        string folder, List<(string Name, byte[] Data)> raw, List<SkippedEntry> skipped,
        out string? comicInfoTitle, out string? comicInfoSeries)
    {
        var sink = new EntrySink();
        var files = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .Select(f => (Full: f, Rel: Path.GetRelativePath(folder, f)))
            .OrderBy(x => x.Rel, NaturalStringComparer.Instance);
        foreach (var (full, rel) in files)
        {
            RouteEntry(sink, skipped, rel, () => File.ReadAllBytes(full));
        }
        raw.AddRange(sink.Raw);
        comicInfoTitle = sink.ComicInfoTitle;
        comicInfoSeries = sink.ComicInfoSeries;
    }

    private static void CollectZip(
        string path, List<(string Name, byte[] Data)> raw, List<SkippedEntry> skipped,
        out string? comicInfoTitle, out string? comicInfoSeries)
    {
        var sink = new EntrySink();
        try
        {
            using var zip = ZipFile.OpenRead(path);
            foreach (var entry in zip.Entries)
            {
                if (entry.FullName.EndsWith('/')) continue; // entri direktori
                RouteEntry(sink, skipped, entry.FullName, () =>
                {
                    using var s = entry.Open();
                    using var ms = new MemoryStream();
                    s.CopyTo(ms);
                    return ms.ToArray();
                });
            }
        }
        catch (InvalidDataException ex)
        {
            throw new ComicImportException(
                "Arsip ZIP tidak bisa dibuka — berkas mungkin rusak atau terenkripsi/dilindungi kata sandi.", ex);
        }
        raw.AddRange(sink.Raw);
        comicInfoTitle = sink.ComicInfoTitle;
        comicInfoSeries = sink.ComicInfoSeries;
    }

    private static void CollectSharpCompress(
        string path, List<(string Name, byte[] Data)> raw, List<SkippedEntry> skipped,
        out string? comicInfoTitle, out string? comicInfoSeries)
    {
        var sink = new EntrySink();
        try
        {
            using var fs = File.OpenRead(path);
            using var archive = ArchiveFactory.Open(fs, new SharpCompress.Readers.ReaderOptions());
            foreach (var entry in archive.Entries)
            {
                if (entry.IsDirectory) continue;
                if (entry.IsEncrypted)
                    throw new ComicImportException(
                        "Arsip terenkripsi/dilindungi kata sandi — Tinta Studio belum mendukung arsip berkunci.");
                var key = entry.Key ?? "";
                RouteEntry(sink, skipped, key, () =>
                {
                    using var s = entry.OpenEntryStream();
                    using var ms = new MemoryStream();
                    s.CopyTo(ms);
                    return ms.ToArray();
                });
            }
        }
        catch (ComicImportException)
        {
            throw;
        }
        catch (Exception ex) when (IsPasswordRelated(ex))
        {
            throw new ComicImportException(
                "Arsip terenkripsi/dilindungi kata sandi — Tinta Studio belum mendukung arsip berkunci.", ex);
        }
        catch (Exception ex)
        {
            throw new ComicImportException($"Arsip tidak bisa dibuka: {ex.Message}", ex);
        }
        raw.AddRange(sink.Raw);
        comicInfoTitle = sink.ComicInfoTitle;
        comicInfoSeries = sink.ComicInfoSeries;
    }

    private static bool IsPasswordRelated(Exception ex)
    {
        for (Exception? e = ex; e is not null; e = e.InnerException)
        {
            var text = e.GetType().Name + " " + e.Message;
            if (text.Contains("password", StringComparison.OrdinalIgnoreCase)
                || text.Contains("encrypt", StringComparison.OrdinalIgnoreCase)
                || text.Contains("Cryptographic", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static void CollectEpub(
        string path, List<(string Name, byte[] Data)> raw, List<SkippedEntry> skipped,
        out string? comicInfoTitle, out string? comicInfoSeries, out bool usedSpineOrder)
    {
        comicInfoTitle = null;
        comicInfoSeries = null;
        usedSpineOrder = false;
        var sink = new EntrySink();
        using var zip = ZipFile.OpenRead(path);

        byte[] ReadEntry(ZipArchiveEntry entry)
        {
            using var s = entry.Open();
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            return ms.ToArray();
        }

        // Urutan halaman dari spine OPF bila strukturnya terbaca.
        var orderedNames = TryEpubSpineOrder(zip, ReadEntry);
        var byName = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in zip.Entries)
            byName[entry.FullName.Replace('\\', '/')] = entry;

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (orderedNames is not null)
        {
            usedSpineOrder = true;
            foreach (var name in orderedNames)
            {
                if (!byName.TryGetValue(name, out var entry)) continue;
                used.Add(name);
                RouteEntry(sink, skipped, name, () => ReadEntry(entry));
            }
        }
        // Gambar yang tidak tercakup spine: tambahkan di akhir secara natural.
        foreach (var entry in zip.Entries)
        {
            var name = entry.FullName.Replace('\\', '/');
            if (used.Contains(name)) continue;
            if (entry.FullName.EndsWith('/')) continue;
            RouteEntry(sink, skipped, name, () => ReadEntry(entry));
        }
        raw.AddRange(sink.Raw);
        comicInfoTitle = sink.ComicInfoTitle;
        comicInfoSeries = sink.ComicInfoSeries;
    }

    /// <summary>Rapikan segmen "." dan ".." pada jalur relatif di dalam arsip.</summary>
    private static string NormalizeArchivePath(string path)
    {
        var parts = new List<string>();
        foreach (var seg in path.Replace('\\', '/').Split('/'))
        {
            if (seg.Length == 0 || seg == ".") continue;
            if (seg == "..")
            {
                if (parts.Count > 0) parts.RemoveAt(parts.Count - 1);
                continue;
            }
            parts.Add(seg);
        }
        return string.Join("/", parts);
    }

    /// <summary>
    /// Baca container.xml → OPF → spine untuk menentukan urutan gambar EPUB.
    /// Mengembalikan null bila struktur tidak terbaca (pemanggil jatuh ke
    /// urutan natural semua entri gambar).
    /// </summary>
    private static List<string>? TryEpubSpineOrder(
        ZipArchive zip, Func<ZipArchiveEntry, byte[]> readEntry)
    {
        try
        {
            var container = zip.GetEntry("META-INF/container.xml");
            if (container is null) return null;
            var containerDoc = XDocument.Parse(Encoding.UTF8.GetString(readEntry(container)));
            XNamespace cns = "urn:oasis:names:tc:opendocument:xmlns:container";
            var opfPath = containerDoc.Descendants(cns + "rootfile")
                .FirstOrDefault()?.Attribute("full-path")?.Value;
            if (opfPath is null) return null;
            var opfEntry = zip.GetEntry(opfPath);
            if (opfEntry is null) return null;

            var opfDoc = XDocument.Parse(Encoding.UTF8.GetString(readEntry(opfEntry)));
            XNamespace ons = "http://www.idpf.org/2007/opf";
            var baseDir = Path.GetDirectoryName(opfPath.Replace('\\', '/')) ?? "";
            string Resolve(string href) =>
                (baseDir.Length > 0 ? baseDir + "/" : "") + href;

            var manifest = new Dictionary<string, (string Href, string MediaType)>();
            foreach (var item in opfDoc.Descendants(ons + "item"))
            {
                var id = item.Attribute("id")?.Value;
                var href = item.Attribute("href")?.Value;
                var media = item.Attribute("media-type")?.Value ?? "";
                if (id is not null && href is not null)
                    manifest[id] = (Resolve(Uri.UnescapeDataString(href)), media);
            }

            var result = new List<string>();
            foreach (var itemref in opfDoc.Descendants(ons + "itemref"))
            {
                var idref = itemref.Attribute("idref")?.Value;
                if (idref is null || !manifest.TryGetValue(idref, out var item)) continue;
                if (item.MediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(item.Href);
                }
                else if (item.MediaType.Contains("html", StringComparison.OrdinalIgnoreCase))
                {
                    // Halaman XHTML EPUB gambar-tetap: ambil <img> di dalamnya.
                    var pageEntry = zip.GetEntry(item.Href);
                    if (pageEntry is null) continue;
                    var pageDoc = XDocument.Parse(Encoding.UTF8.GetString(readEntry(pageEntry)));
                    var pageDir = Path.GetDirectoryName(item.Href) ?? "";
                    foreach (var img in pageDoc.Descendants().Where(e => e.Name.LocalName == "img"))
                    {
                        var src = img.Attribute("src")?.Value;
                        if (src is null) continue;
                        var joined = (pageDir.Length > 0 ? pageDir + "/" : "")
                                     + Uri.UnescapeDataString(src);
                        result.Add(NormalizeArchivePath(joined));
                    }
                }
            }
            return result.Count > 0 ? result : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    // ------------------------------------------------------------------
    // PDF
    // ------------------------------------------------------------------

    private static List<ComicPage> ImportPdfPages(
        string path, string workDir, List<SkippedEntry> skipped)
    {
        var pages = new List<ComicPage>();
        var pdfBytes = File.ReadAllBytes(path);
        int count;
        try
        {
            count = PdfPageRenderer.GetPageCount(pdfBytes);
        }
        catch (Exception ex)
        {
            throw new ComicImportException($"PDF tidak bisa dibuka: {ex.Message}", ex);
        }

        for (int i = 0; i < count; i++)
        {
            try
            {
                var (pngBytes, width, height) = PdfPageRenderer.RenderPagePng(pdfBytes, i, dpi: 150);
                var workPath = Path.Combine(workDir, $"page-{i + 1:D4}.png");
                File.WriteAllBytes(workPath, pngBytes);
                pages.Add(new ComicPage
                {
                    Index = pages.Count,
                    SourceName = $"Halaman PDF {i + 1}",
                    SourceFormat = "PDF",
                    Width = width,
                    Height = height,
                    WorkPath = workPath,
                });
            }
            catch (Exception ex)
            {
                skipped.Add(new SkippedEntry($"Halaman PDF {i + 1}", $"gagal dirender: {ex.Message}"));
            }
        }
        return pages;
    }

    // ------------------------------------------------------------------
    // Normalisasi gambar
    // ------------------------------------------------------------------

    private static List<ComicPage> NormalizePages(
        List<(string Name, byte[] Data)> raw, string workDir, List<SkippedEntry> skipped,
        bool preserveOrder = false)
    {
        // Urutan dipertahankan apa adanya hanya untuk EPUB yang spine-nya
        // terbaca (urutan baca resmi dari penerbit mengalahkan abjad).
        if (!preserveOrder)
            raw.Sort((a, b) => NaturalStringComparer.Instance.Compare(a.Name, b.Name));
        var pages = new List<ComicPage>();
        foreach (var (name, data) in raw)
        {
            try
            {
                var (pngBytes, width, height, srcFormat) = NormalizeImage(data);
                var workPath = Path.Combine(workDir, $"page-{pages.Count + 1:D4}.png");
                File.WriteAllBytes(workPath, pngBytes);
                pages.Add(new ComicPage
                {
                    Index = pages.Count,
                    SourceName = name,
                    SourceFormat = srcFormat,
                    Width = width,
                    Height = height,
                    WorkPath = workPath,
                });
            }
            catch (Exception ex)
            {
                skipped.Add(new SkippedEntry(name, $"gambar tidak terbaca ({ex.Message})"));
            }
        }
        return pages;
    }

    /// <summary>
    /// Dekode satu gambar mentah, terapkan rotasi EXIF, enkode ulang ke PNG.
    /// Gambar animasi diambil bingkai pertamanya (perilaku dekode SkiaSharp).
    /// TIFF didekode lewat LibTiff (build libSkiaSharp tidak membawa codec
    /// TIFF); AVIF belum didukung codec yang tersedia dan dilaporkan jujur.
    /// </summary>
    internal static (byte[] Png, int Width, int Height, string SourceFormat) NormalizeImage(byte[] data)
    {
        string format = "GAMBAR";
        SKBitmap? decoded = null;
        using (var probe = new MemoryStream(data))
        using (var codec = SKCodec.Create(probe))
        {
            if (codec is not null)
            {
                format = codec.EncodedFormat.ToString().ToUpperInvariant();
                decoded = SKBitmap.Decode(data);
            }
        }
        if (decoded is null)
        {
            decoded = TiffDecoder.TryDecode(data);
            if (decoded is not null) format = "TIFF";
        }
        if (decoded is null)
        {
            if (IsAvif(data))
                throw new InvalidDataException(
                    "AVIF belum bisa didekode codec gambar yang terbundel versi ini");
            throw new InvalidDataException("dekode gambar gagal (format tidak didukung codec)");
        }

        using (decoded)
        {
            var orientation = ExifOrientation.Read(data);
            using var oriented = orientation > 1
                ? ExifOrientation.Apply(decoded, orientation)
                : null;
            var final = oriented ?? decoded;

            using var image = SKImage.FromBitmap(final);
            using var png = image.Encode(SKEncodedImageFormat.Png, 100)
                ?? throw new InvalidDataException("enkode PNG gagal");
            return (png.ToArray(), final.Width, final.Height, format);
        }
    }

    /// <summary>Deteksi wadah ISO-BMFF berjenis AVIF dari kotak ftyp.</summary>
    private static bool IsAvif(byte[] data)
    {
        if (data.Length < 12) return false;
        if (Encoding.ASCII.GetString(data, 4, 4) != "ftyp") return false;
        var brand = Encoding.ASCII.GetString(data, 8, 4);
        return brand is "avif" or "avis" or "mif1" or "mif2";
    }
}

/// <summary>
/// Dekoder TIFF cadangan berbasis LibTiff terkelola — build libSkiaSharp
/// 3.119.4 yang dipakai Avalonia tidak menyertakan codec TIFF sama sekali.
/// Hanya direktori/gambar pertama yang dibaca; raster RGBA libtiff
/// berawal dari sudut kiri-bawah sehingga baris dibalik ke atas-bawah.
/// </summary>
internal static class TiffDecoder
{
    public static SKBitmap? TryDecode(byte[] data)
    {
        if (data.Length < 8) return null;
        bool isTiff = (data[0] == (byte)'I' && data[1] == (byte)'I' && data[2] == 0x2A && data[3] == 0)
                      || (data[0] == (byte)'M' && data[1] == (byte)'M' && data[2] == 0 && data[3] == 0x2A);
        if (!isTiff) return null;

        try
        {
            BitMiracle.LibTiff.Classic.Tiff.SetErrorHandler(new SilentTiffErrorHandler());
            using var stream = new MemoryStream(data);
            using var tiff = BitMiracle.LibTiff.Classic.Tiff.ClientOpen(
                "tinta-tiff", "r", stream, new BitMiracle.LibTiff.Classic.TiffStream());
            if (tiff is null) return null;

            var widthField = tiff.GetField(BitMiracle.LibTiff.Classic.TiffTag.IMAGEWIDTH);
            var heightField = tiff.GetField(BitMiracle.LibTiff.Classic.TiffTag.IMAGELENGTH);
            if (widthField is null || heightField is null) return null;
            int width = widthField[0].ToInt();
            int height = heightField[0].ToInt();
            if (width <= 0 || height <= 0 || (long)width * height > 200_000_000) return null;

            var raster = new int[width * height];
            if (!tiff.ReadRGBAImage(width, height, raster)) return null;

            var bmp = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
            var bytes = new byte[width * height * 4];
            for (int y = 0; y < height; y++)
            {
                int srcRow = height - 1 - y; // raster libtiff: baris pertama = bawah
                for (int x = 0; x < width; x++)
                {
                    int v = raster[srcRow * width + x]; // ABGR
                    int i = (y * width + x) * 4;
                    bytes[i] = (byte)(v >> 16);       // B
                    bytes[i + 1] = (byte)(v >> 8);    // G
                    bytes[i + 2] = (byte)v;           // R
                    bytes[i + 3] = (byte)(v >> 24);   // A
                }
            }
            System.Runtime.InteropServices.Marshal.Copy(bytes, 0, bmp.GetPixels(), bytes.Length);
            return bmp;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private sealed class SilentTiffErrorHandler : BitMiracle.LibTiff.Classic.TiffErrorHandler
    {
        public override void ErrorHandler(
            BitMiracle.LibTiff.Classic.Tiff tiff, string module, string format, params object[] args)
        {
        }

        public override void WarningHandler(
            BitMiracle.LibTiff.Classic.Tiff tiff, string module, string format, params object[] args)
        {
        }
    }
}

/// <summary>Pengurutan natural: potongan angka dibandingkan sebagai bilangan (1, 2, 10).</summary>
public sealed class NaturalStringComparer : IComparer<string>
{
    public static readonly NaturalStringComparer Instance = new();

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return -1;
        if (y is null) return 1;
        int ix = 0, iy = 0;
        while (ix < x.Length && iy < y.Length)
        {
            var cx = x[ix];
            var cy = y[iy];
            if (char.IsDigit(cx) && char.IsDigit(cy))
            {
                int sx = ix, sy = iy;
                while (ix < x.Length && char.IsDigit(x[ix])) ix++;
                while (iy < y.Length && char.IsDigit(y[iy])) iy++;
                var nx = x.AsSpan(sx, ix - sx).TrimStart('0');
                var ny = y.AsSpan(sy, iy - sy).TrimStart('0');
                if (nx.Length != ny.Length) return nx.Length.CompareTo(ny.Length);
                var cmp = nx.CompareTo(ny, StringComparison.Ordinal);
                if (cmp != 0) return cmp;
            }
            else
            {
                var cmp = char.ToUpperInvariant(cx).CompareTo(char.ToUpperInvariant(cy));
                if (cmp != 0) return cmp;
                ix++;
                iy++;
            }
        }
        return (x.Length - ix).CompareTo(y.Length - iy);
    }
}

/// <summary>
/// Pembaca & penerap orientasi EXIF (tag 0x0112) untuk JPEG/TIFF mentah.
/// SkiaSharp tidak menerapkan orientasi EXIF secara otomatis saat dekode,
/// jadi halaman hasil pindai/foto yang "miring" diluruskan di sini.
/// </summary>
internal static class ExifOrientation
{
    public static int Read(byte[] data)
    {
        try
        {
            if (data.Length >= 2 && data[0] == 0xFF && data[1] == 0xD8)
                return ReadFromJpeg(data);
            if (data.Length >= 8)
                return ReadFromTiff(data, 0);
            return 1;
        }
        catch (Exception)
        {
            return 1;
        }
    }

    private static int ReadFromJpeg(byte[] data)
    {
        int pos = 2;
        while (pos + 4 <= data.Length)
        {
            if (data[pos] != 0xFF) break;
            var marker = data[pos + 1];
            if (marker == 0xD9 || marker == 0xDA) break; // EOI / SOS
            var segLen = (data[pos + 2] << 8) | data[pos + 3];
            if (segLen < 2 || pos + 2 + segLen > data.Length) break;
            if (marker == 0xE1 && segLen >= 14
                && data[pos + 4] == (byte)'E' && data[pos + 5] == (byte)'x'
                && data[pos + 6] == (byte)'i' && data[pos + 7] == (byte)'f'
                && data[pos + 8] == 0 && data[pos + 9] == 0)
            {
                return ReadFromTiff(data, pos + 10);
            }
            pos += 2 + segLen;
        }
        return 1;
    }

    private static int ReadFromTiff(byte[] data, int offset)
    {
        if (offset + 8 > data.Length) return 1;
        bool little;
        if (data[offset] == (byte)'I' && data[offset + 1] == (byte)'I') little = true;
        else if (data[offset] == (byte)'M' && data[offset + 1] == (byte)'M') little = false;
        else return 1;

        ushort ReadU16(int p) => little
            ? (ushort)(data[p] | (data[p + 1] << 8))
            : (ushort)((data[p] << 8) | data[p + 1]);
        uint ReadU32(int p) => little
            ? (uint)(data[p] | (data[p + 1] << 8) | (data[p + 2] << 16) | (data[p + 3] << 24))
            : (uint)((data[p] << 24) | (data[p + 1] << 16) | (data[p + 2] << 8) | data[p + 3]);

        if (ReadU16(offset + 2) != 42) return 1;
        var ifdOffset = offset + (int)ReadU32(offset + 4);
        if (ifdOffset + 2 > data.Length) return 1;
        var count = ReadU16(ifdOffset);
        for (int i = 0; i < count; i++)
        {
            var entry = ifdOffset + 2 + i * 12;
            if (entry + 12 > data.Length) break;
            if (ReadU16(entry) == 0x0112) // Orientation
            {
                var value = ReadU16(entry + 8);
                return value is >= 1 and <= 8 ? value : 1;
            }
        }
        return 1;
    }

    /// <summary>Terapkan orientasi EXIF (1–8) ke bitmap; mengembalikan bitmap BARU.</summary>
    public static SKBitmap Apply(SKBitmap src, int orientation)
    {
        var w = src.Width;
        var h = src.Height;
        bool swap = orientation >= 5;
        var dst = new SKBitmap(swap ? h : w, swap ? w : h, src.ColorType, src.AlphaType);
        using var canvas = new SKCanvas(dst);
        // Matriks pemetaan koordinat sumber -> tujuan per nilai orientasi EXIF.
        SKMatrix m = orientation switch
        {
            2 => new SKMatrix(-1, 0, w, 0, 1, 0, 0, 0, 1),        // balik horizontal
            3 => new SKMatrix(-1, 0, w, 0, -1, h, 0, 0, 1),       // putar 180
            4 => new SKMatrix(1, 0, 0, 0, -1, h, 0, 0, 1),        // balik vertikal
            5 => new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1),         // transpose
            6 => new SKMatrix(0, -1, h, 1, 0, 0, 0, 0, 1),        // putar 90 searah jarum jam
            7 => new SKMatrix(0, -1, h, -1, 0, w, 0, 0, 1),       // transverse
            8 => new SKMatrix(0, 1, 0, -1, 0, w, 0, 0, 1),        // putar 270 searah jarum jam
            _ => SKMatrix.Identity,
        };
        canvas.SetMatrix(m);
        canvas.DrawBitmap(src, 0, 0);
        canvas.Flush();
        return dst;
    }
}
