using System.IO.Compression;
using System.Text;
using SkiaSharp;

namespace TintaStudio.Tests;

/// <summary>
/// Pembangun fixtures uji secara programatis: gambar via SkiaSharp,
/// arsip via System.IO.Compression / SharpCompress, PDF via backend
/// PDF SkiaSharp, EPUB dirakit manual sebagai zip terstruktur.
/// </summary>
internal static class FixtureBuilder
{
    public static byte[] MakeImage(int width, int height, SKColor color,
        SKEncodedImageFormat format, string? label = null)
    {
        using var bmp = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bmp))
        {
            canvas.Clear(color);
            if (label is not null)
            {
                using var paint = new SKPaint
                {
                    Color = SKColors.White,
                    IsAntialias = true,
                };
                using var font = new SKFont(SKTypeface.Default, 28);
                canvas.DrawText(label, 12, 40, SKTextAlign.Left, font, paint);
            }
            canvas.Flush();
        }
        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(format, 90)
            ?? throw new InvalidOperationException("Enkode fixture gagal: " + format);
        return data.ToArray();
    }

    public static void WriteZip(string path, IEnumerable<(string Name, byte[] Data)> entries)
    {
        using var fs = File.Create(path);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        foreach (var (name, data) in entries)
        {
            var entry = zip.CreateEntry(name);
            using var s = entry.Open();
            s.Write(data, 0, data.Length);
        }
    }

    public static void WriteTar(string path, IEnumerable<(string Name, byte[] Data)> entries)
    {
        using var fs = File.Create(path);
        using var archive = SharpCompress.Archives.Tar.TarArchive.Create();
        foreach (var (name, data) in entries)
            // Ukuran wajib diberikan eksplisit: SharpCompress 1.0 mencatat 0
            // (entri kosong) bila mengandalkan panjang stream.
            archive.AddEntry(name, new MemoryStream(data), true, data.Length, null);
        archive.SaveTo(fs, new SharpCompress.Writers.WriterOptions(
            SharpCompress.Common.CompressionType.None));
    }

    /// <summary>
    /// Rakit BMP 24-bit minimal (enkoder BMP/GIF tidak tersedia di build
    /// SkiaSharp ini, jadi fixture ditulis manual — Skia tetap mendekodenya).
    /// </summary>
    public static byte[] MakeBmp(int width, int height, byte r, byte g, byte b)
    {
        int rowSize = (width * 3 + 3) & ~3; // baris diratakan ke kelipatan 4
        int pixelBytes = rowSize * height;
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(new byte[] { (byte)'B', (byte)'M' });
        w.Write(54 + pixelBytes);       // ukuran berkas
        w.Write(0);                      // reserved
        w.Write(54);                     // offset data piksel
        w.Write(40);                     // ukuran BITMAPINFOHEADER
        w.Write(width);
        w.Write(height);                 // positif = bottom-up
        w.Write((ushort)1);              // planes
        w.Write((ushort)24);             // bpp
        w.Write(0);                      // tanpa kompresi
        w.Write(pixelBytes);
        w.Write(2835);
        w.Write(2835);
        w.Write(0);
        w.Write(0);
        var row = new byte[rowSize];
        for (int x = 0; x < width; x++)
        {
            row[x * 3] = b;
            row[x * 3 + 1] = g;
            row[x * 3 + 2] = r;
        }
        for (int y = 0; y < height; y++)
            w.Write(row);
        w.Flush();
        return ms.ToArray();
    }

    public static void WriteSevenZip(string path, IEnumerable<(string Name, byte[] Data)> entries)
    {
        // SharpCompress 1.0 hanya MEMBACA 7z (tidak bisa menulis), jadi
        // wadah fixture dipak dengan CLI 7z bawaan mesin; jalur baca yang
        // diuji tetap SharpCompress di kode produksi.
        var staging = Path.Combine(Path.GetTempPath(), "tinta-7z-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            foreach (var (name, data) in entries)
            {
                var dest = Path.Combine(staging, name.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.WriteAllBytes(dest, data);
            }
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "7z",
                ArgumentList = { "a", "-t7z", "-y", path, "." },
                WorkingDirectory = staging,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var proc = System.Diagnostics.Process.Start(psi)
                ?? throw new InvalidOperationException("CLI 7z tidak bisa dijalankan");
            proc.WaitForExit();
            if (proc.ExitCode != 0 || !File.Exists(path))
                throw new InvalidOperationException(
                    "CLI 7z gagal membuat fixture: " + proc.StandardError.ReadToEnd());
        }
        finally
        {
            try { Directory.Delete(staging, recursive: true); } catch (Exception) { }
        }
    }

    /// <summary>
    /// Rakit TIFF RGB tak-terkompresi minimal (SkiaSharp bisa mendekodenya,
    /// tapi tidak menyediakan enkoder TIFF untuk membuat fixture).
    /// </summary>
    public static byte[] MakeTiff(int width, int height, byte r, byte g, byte b)
    {
        var pixels = new byte[width * height * 3];
        for (int i = 0; i < width * height; i++)
        {
            pixels[i * 3] = r;
            pixels[i * 3 + 1] = g;
            pixels[i * 3 + 2] = b;
        }

        var entries = new List<(ushort Tag, ushort Type, uint Count, uint Value)>
        {
            (256, 4, 1, (uint)width),          // ImageWidth (LONG)
            (257, 4, 1, (uint)height),         // ImageLength
            (258, 3, 3, 0),                    // BitsPerSample -> diisi offset di bawah
            (259, 3, 1, 1),                    // Compression = none
            (262, 3, 1, 2),                    // Photometric = RGB
            (273, 4, 1, 0),                    // StripOffsets -> diisi di bawah
            (277, 3, 1, 3),                    // SamplesPerPixel
            (278, 4, 1, (uint)height),         // RowsPerStrip
            (279, 4, 1, (uint)pixels.Length),  // StripByteCounts
            (284, 3, 1, 1),                    // PlanarConfiguration
        };
        entries.Sort((a, b) => a.Tag.CompareTo(b.Tag));

        int headerSize = 8;
        int ifdSize = 2 + entries.Count * 12 + 4;
        int bitsOffset = headerSize + ifdSize;               // tempat array BitsPerSample (3 SHORT)
        int stripOffset = bitsOffset + 6;

        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(new byte[] { (byte)'I', (byte)'I', 42, 0 });
        w.Write((uint)headerSize); // offset IFD0
        w.Write((ushort)entries.Count);
        foreach (var (tag, type, count, value) in entries)
        {
            w.Write(tag);
            w.Write(type);
            w.Write(count);
            var v = tag switch
            {
                258 => (uint)bitsOffset,
                273 => (uint)stripOffset,
                _ => value,
            };
            if (type == 3 && count == 1) // SHORT tunggal: nilai di 2 byte pertama
                w.Write((uint)(v & 0xFFFF));
            else
                w.Write(v);
        }
        w.Write(0u); // IFD berikutnya
        w.Write((ushort)8);
        w.Write((ushort)8);
        w.Write((ushort)8); // BitsPerSample = 8,8,8
        w.Write(pixels);
        w.Flush();
        return ms.ToArray();
    }

    /// <summary>
    /// Sisipkan segmen EXIF (APP1) berisi orientasi ke JPEG mentah
    /// (sesudah penanda SOI), seperti hasil kamera/pemindai asli.
    /// </summary>
    public static byte[] JpegWithExifOrientation(byte[] jpeg, ushort orientation)
    {
        var tiff = new List<byte>();
        tiff.AddRange(new byte[] { (byte)'I', (byte)'I', 42, 0, 8, 0, 0, 0 }); // header TIFF LE, IFD0 di offset 8
        tiff.AddRange(new byte[] { 1, 0 });                                    // 1 entri
        tiff.AddRange(new byte[] { 0x12, 0x01 });                              // tag Orientation
        tiff.AddRange(new byte[] { 3, 0 });                                    // type SHORT
        tiff.AddRange(new byte[] { 1, 0, 0, 0 });                              // count 1
        tiff.AddRange(new byte[] { (byte)orientation, 0, 0, 0 });             // nilai
        tiff.AddRange(new byte[] { 0, 0, 0, 0 });                              // IFD berikutnya: tidak ada

        var app1 = new List<byte>();
        app1.AddRange(Encoding.ASCII.GetBytes("Exif\0\0"));
        app1.AddRange(tiff);

        var result = new List<byte> { 0xFF, 0xD8, 0xFF, 0xE1 };
        var len = app1.Count + 2;
        result.Add((byte)(len >> 8));
        result.Add((byte)(len & 0xFF));
        result.AddRange(app1);
        result.AddRange(jpeg.AsSpan(2).ToArray());
        return result.ToArray();
    }

    /// <summary>Buat PDF kecil berisi gambar-gambar (satu gambar per halaman).</summary>
    public static byte[] MakePdf(params (int Width, int Height, SKColor Color)[] pages)
    {
        using var ms = new MemoryStream();
        using (var doc = SKDocument.CreatePdf(ms))
        {
            foreach (var (w, h, color) in pages)
            {
                using var bmp = new SKBitmap(w, h);
                using (var c = new SKCanvas(bmp))
                {
                    c.Clear(color);
                    c.Flush();
                }
                using var canvas = doc.BeginPage(200, 280);
                using var img = SKImage.FromBitmap(bmp);
                canvas.DrawImage(img, new SKRect(10, 10, 190, 270));
                doc.EndPage();
            }
            doc.Close();
        }
        return ms.ToArray();
    }

    /// <summary>
    /// Rakit EPUB gambar sederhana: spine berisi halaman XHTML yang
    /// masing-masing membungkus satu gambar (pola EPUB komik umum).
    /// </summary>
    public static void WriteEpub(string path, IReadOnlyList<(string Name, byte[] Data)> images)
    {
        using var fs = File.Create(path);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);

        void Add(string name, string content)
        {
            var e = zip.CreateEntry(name);
            using var s = e.Open();
            var bytes = Encoding.UTF8.GetBytes(content);
            s.Write(bytes, 0, bytes.Length);
        }
        void AddBytes(string name, byte[] data)
        {
            var e = zip.CreateEntry(name);
            using var s = e.Open();
            s.Write(data, 0, data.Length);
        }

        Add("mimetype", "application/epub+zip");
        Add("META-INF/container.xml",
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
            "<container version=\"1.0\" xmlns=\"urn:oasis:names:tc:opendocument:xmlns:container\">" +
            "<rootfiles><rootfile full-path=\"OEBPS/content.opf\" " +
            "media-type=\"application/oebps-package+xml\"/></rootfiles></container>");

        var manifest = new StringBuilder();
        var spine = new StringBuilder();
        for (int i = 0; i < images.Count; i++)
        {
            manifest.Append(
                $"<item id=\"img{i}\" href=\"images/{images[i].Name}\" media-type=\"image/png\"/>" +
                $"<item id=\"page{i}\" href=\"pages/p{i}.xhtml\" media-type=\"application/xhtml+xml\"/>");
            spine.Append($"<itemref idref=\"page{i}\"/>");
            Add($"OEBPS/pages/p{i}.xhtml",
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
                "<html xmlns=\"http://www.w3.org/1999/xhtml\"><body>" +
                $"<img src=\"../images/{images[i].Name}\"/></body></html>");
            AddBytes($"OEBPS/images/{images[i].Name}", images[i].Data);
        }
        Add("OEBPS/content.opf",
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
            "<package xmlns=\"http://www.idpf.org/2007/opf\" version=\"3.0\" unique-identifier=\"bid\">" +
            "<metadata xmlns:dc=\"http://purl.org/dc/elements/1.1/\">" +
            "<dc:identifier id=\"bid\">urn:uuid:uji</dc:identifier><dc:title>EPUB Uji</dc:title>" +
            "<dc:language>id</dc:language></metadata>" +
            $"<manifest>{manifest}</manifest><spine>{spine}</spine></package>");
    }
}
