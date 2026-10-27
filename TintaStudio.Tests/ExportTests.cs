using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using SkiaSharp;
using TintaStudio.Models;
using TintaStudio.Services;
using Xunit;
using Xunit.Abstractions;

namespace TintaStudio.Tests;

/// <summary>
/// Tes Tahap 6 — Ekspor &amp; Packaging: bab diekspor menjadi
/// halaman FINAL (jalur render yang sama dengan --render) ke
/// CBZ / PDF / folder dengan format gambar pilihan. Metrik:
/// jumlah &amp; nama entri ZIP, magic bytes format, dekode ukuran
/// gambar, penanda halaman PDF, dan densitas tinta typeset.
/// </summary>
public sealed class ExportTests
{
    private readonly ITestOutputHelper _out;

    public ExportTests(ITestOutputHelper output) => _out = output;

    // ─── Pembantu ────────────────────────────────────────────────

    private static string NewTempDir(string prefix)
    {
        var dir = Path.Combine(Path.GetTempPath(),
            prefix + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static byte[] PngBytes(SKBitmap bmp)
    {
        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>CBZ 3 halaman dari halaman fixture + ComicInfo.xml.</summary>
    private static string MakeThreePageCbz(string dir)
    {
        using var p1 = MangaFixture.JapanesePage();
        using var p2 = MangaFixture.KoreanPage();
        using var p3 = MangaFixture.ChinesePage();
        var cbz = Path.Combine(dir, "tiga.cbz");
        using var zip = ZipFile.Open(cbz, ZipArchiveMode.Create);
        foreach (var (bmp, name) in new[]
                 {
                     (p1, "001.png"), (p2, "002.png"), (p3, "003.png"),
                 })
        {
            var entry = zip.CreateEntry(name);
            using var es = entry.Open();
            var bytes = PngBytes(bmp);
            es.Write(bytes, 0, bytes.Length);
        }
        var info = zip.CreateEntry("ComicInfo.xml");
        using (var w = new StreamWriter(info.Open()))
            w.Write("<?xml version=\"1.0\"?>\n<ComicInfo>" +
                    "<Title>Bab Tiga Halaman</Title><Series>Fixture Tinta</Series>" +
                    "</ComicInfo>");
        return cbz;
    }

    /// <summary>Buka bab + proyeknya dari CBZ uji di folder temp.</summary>
    private static (Chapter Chapter, ProjectChapter Project) OpenFixture(string dir)
    {
        var cbz = MakeThreePageCbz(dir);
        var chapter = ComicImport.OpenChapter(cbz, Path.Combine(dir, "work"));
        var (_, projectChapter) = ProjectStore.GetOrCreate(chapter);
        return (chapter, projectChapter);
    }

    private static bool IsInk(SKColor px) =>
        (px.Red * 299 + px.Green * 587 + px.Blue * 114) / 1000 < 160;

    private static double InkDensity(SKBitmap bmp, SKRectI rect)
    {
        int ink = 0, total = 0;
        for (int y = Math.Max(0, rect.Top); y < Math.Min(bmp.Height, rect.Bottom); y++)
        for (int x = Math.Max(0, rect.Left); x < Math.Min(bmp.Width, rect.Right); x++)
        {
            if (IsInk(bmp.GetPixel(x, y))) ink++;
            total++;
        }
        return total == 0 ? 0 : (double)ink / total;
    }

    // ─── CBZ ─────────────────────────────────────────────────────

    [Fact]
    public void Export_CbzJpg_EntriesComicInfoAndFirstPageSize()
    {
        var dir = NewTempDir("tinta-export-cbz");
        try
        {
            var (chapter, project) = OpenFixture(dir);
            var dest = Path.Combine(dir, "hasil.cbz");
            var result = ExportService.ExportChapter(chapter, project, dest,
                new ExportOptions(ExportKind.Cbz, ExportImageFormat.Jpg, 90));
            Assert.Equal(dest, result.OutputPath);
            Assert.Equal(3, result.PageCount);
            Assert.True(new FileInfo(dest).Length > 0);

            using var zip = ZipFile.OpenRead(dest);
            var names = zip.Entries.Select(e => e.FullName).ToList();
            Assert.Equal(
                new[] { "ComicInfo.xml", "page-001.jpg", "page-002.jpg", "page-003.jpg" },
                names);

            var infoEntry = zip.GetEntry("ComicInfo.xml")!;
            using (var sr = new StreamReader(infoEntry.Open()))
                Assert.Contains("Fixture Tinta", sr.ReadToEnd());

            var first = zip.GetEntry("page-001.jpg")!;
            using var es = first.Open();
            using var ms = new MemoryStream();
            es.CopyTo(ms);
            var bytes = ms.ToArray();
            // Magic JPEG: FF D8 FF.
            Assert.Equal(0xFF, bytes[0]);
            Assert.Equal(0xD8, bytes[1]);
            using var bmp = SKBitmap.Decode(bytes);
            Assert.NotNull(bmp);
            Assert.Equal(MangaFixture.PageWidth, bmp.Width);
            Assert.Equal(MangaFixture.PageHeight, bmp.Height);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Export_CbzOriginal_KeepsSourcePngFormat()
    {
        var dir = NewTempDir("tinta-export-asli");
        try
        {
            var (chapter, project) = OpenFixture(dir);
            var dest = Path.Combine(dir, "hasil.cbz");
            ExportService.ExportChapter(chapter, project, dest,
                new ExportOptions(ExportKind.Cbz, ExportImageFormat.Original, 90));
            using var zip = ZipFile.OpenRead(dest);
            var pageNames = zip.Entries.Select(e => e.FullName)
                .Where(n => n.StartsWith("page-", StringComparison.Ordinal)).ToList();
            Assert.Equal(
                new[] { "page-001.png", "page-002.png", "page-003.png" }, pageNames);
            using var es = zip.GetEntry("page-001.png")!.Open();
            using var ms = new MemoryStream();
            es.CopyTo(ms);
            var bytes = ms.ToArray();
            // Magic PNG: 89 50 4E 47.
            Assert.Equal(0x89, bytes[0]);
            Assert.Equal(0x50, bytes[1]);
            Assert.Equal(0x4E, bytes[2]);
            Assert.Equal(0x47, bytes[3]);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Export_CbzTwice_DoesNotOverwrite_SecondGetsSuffix()
    {
        var dir = NewTempDir("tinta-export-unik");
        try
        {
            var (chapter, project) = OpenFixture(dir);
            var dest = Path.Combine(dir, "hasil.cbz");
            var options = new ExportOptions(ExportKind.Cbz, ExportImageFormat.Png, 90);
            var first = ExportService.ExportChapter(chapter, project, dest, options);
            var second = ExportService.ExportChapter(chapter, project, dest, options);
            Assert.Equal(dest, first.OutputPath);
            Assert.NotEqual(first.OutputPath, second.OutputPath);
            Assert.Contains("(2)", Path.GetFileName(second.OutputPath));
            Assert.True(File.Exists(first.OutputPath));
            Assert.True(File.Exists(second.OutputPath));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // ─── Folder + WebP ───────────────────────────────────────────

    [Fact]
    public void Export_FolderWebP_OneFilePerPage_MagicAndSize()
    {
        var dir = NewTempDir("tinta-export-folder");
        try
        {
            var (chapter, project) = OpenFixture(dir);
            var dest = Path.Combine(dir, "keluar");
            var result = ExportService.ExportChapter(chapter, project, dest,
                new ExportOptions(ExportKind.Folder, ExportImageFormat.WebP, 85));
            Assert.Equal(3, result.Files.Count);
            for (int i = 1; i <= 3; i++)
            {
                var file = Path.Combine(dest, $"page-{i:D3}.webp");
                Assert.True(File.Exists(file), $"Berkas halaman {i} harus ada: {file}");
                var bytes = File.ReadAllBytes(file);
                // Magic WebP: "RIFF"...."WEBP".
                Assert.Equal("RIFF", Encoding.ASCII.GetString(bytes, 0, 4));
                Assert.Equal("WEBP", Encoding.ASCII.GetString(bytes, 8, 4));
            }
            using var bmp = SKBitmap.Decode(result.Files[0]);
            Assert.NotNull(bmp);
            Assert.Equal(MangaFixture.PageWidth, bmp.Width);
            Assert.Equal(MangaFixture.PageHeight, bmp.Height);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // ─── PDF ─────────────────────────────────────────────────────

    [Fact]
    public void Export_Pdf_HeaderAndPageCount()
    {
        var dir = NewTempDir("tinta-export-pdf");
        try
        {
            var (chapter, project) = OpenFixture(dir);
            var dest = Path.Combine(dir, "hasil.pdf");
            var result = ExportService.ExportChapter(chapter, project, dest,
                new ExportOptions(ExportKind.Pdf, ExportImageFormat.Original, 90));
            Assert.Equal(3, result.PageCount);
            var bytes = File.ReadAllBytes(dest);
            Assert.Equal("%PDF", Encoding.ASCII.GetString(bytes, 0, 4));
            var text = Encoding.Latin1.GetString(bytes);
            // Pohon halaman Skia mencatat jumlah halaman; tiap
            // halaman punya MediaBox sendiri.
            Assert.Contains("/Count 3", text);
            Assert.Equal(3, Regex.Matches(text, "/MediaBox").Count);
            _out.WriteLine($"PDF: {bytes.Length} byte");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // ─── Typeset ikut tergambar ──────────────────────────────────

    [Fact]
    public void Export_TypesetBlock_DrawnInExportedPage()
    {
        var dir = NewTempDir("tinta-export-typeset");
        try
        {
            var cbz = Path.Combine(dir, "satu.cbz");
            using (var pageBmp = MangaFixture.JapanesePage())
                MangaFixture.SavePng(pageBmp, Path.Combine(dir, "page.png"));
            using (var zip = ZipFile.Open(cbz, ZipArchiveMode.Create))
                zip.CreateEntryFromFile(Path.Combine(dir, "page.png"), "001.png");

            var chapter = ComicImport.OpenChapter(cbz, Path.Combine(dir, "work"));
            var project = new ProjectChapter
            {
                SourcePath = chapter.SourcePath,
                Title = chapter.Title,
                Format = chapter.Format,
                PageCount = 1,
            };
            var page = project.PageAt(0);
            var block = new TextBlock
            {
                X = 170, Y = 185, Width = 260, Height = 100,
                Text = "こんにちは", Language = "ja",
                Translation = "Halo!",
            };
            page.Blocks.Add(block);
            CleanService.SetBlockOperation(page, block, CleanMode.Solid, "#FFFFFF");

            var dest = Path.Combine(dir, "keluar");
            ExportService.ExportChapter(chapter, project, dest,
                new ExportOptions(ExportKind.Folder, ExportImageFormat.Png, 90));
            var outFile = Path.Combine(dest, "page-001.png");
            Assert.True(File.Exists(outFile));
            using var final = SKBitmap.Decode(outFile);
            Assert.NotNull(final);
            var box = new SKRectI(block.X, block.Y,
                block.X + block.Width, block.Y + block.Height);
            double inside = InkDensity(final, box);
            _out.WriteLine($"densitas tinta typeset di halaman ekspor: {inside:F3}");
            Assert.True(inside > 0.02,
                $"Teks typeset harus tergambar di hasil ekspor (densitas {inside:F3}).");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // ─── CLI ─────────────────────────────────────────────────────

    [SkippableFact]
    public void Cli_Export_FolderPng_ProducesFilesAndSummary()
    {
        var missing = ModelStore.MissingFor(OcrLanguage.Japanese);
        Skip.If(missing.Count > 0,
            "Model OCR belum diunduh: " + string.Join(", ", missing.Select(m => m.Id)));
        var testBin = Path.GetDirectoryName(typeof(ExportTests).Assembly.Location)!;
        var config = new DirectoryInfo(testBin).Parent?.Name ?? "Debug";
        var root = Path.GetFullPath(Path.Combine(testBin, "..", "..", "..", ".."));
        var appDll = Path.Combine(root, "bin", config, "net10.0", "TintaStudio.dll");
        Skip.If(!File.Exists(appDll), $"DLL aplikasi belum dibangun: {appDll}");

        var dir = NewTempDir("tinta-cli-export");
        try
        {
            using var page = MangaFixture.JapanesePage();
            var cbz = MangaFixture.MakeCbz(page, dir);
            var outDir = Path.Combine(dir, "hasil");
            var psi = new ProcessStartInfo("dotnet",
                $"\"{appDll}\" --export \"{cbz}\" --format folder " +
                $"--img png --out \"{outDir}\"")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            psi.Environment["TINTA_CONFIG_DIR"] = Path.Combine(dir, "config");
            using var proc = Process.Start(psi)!;
            var stdout = proc.StandardOutput.ReadToEnd();
            var stderr = proc.StandardError.ReadToEnd();
            proc.WaitForExit(300_000);
            _out.WriteLine(stdout + stderr);
            Assert.Equal(0, proc.ExitCode);
            Assert.Contains("Ekspor selesai", stdout);
            Assert.True(File.Exists(Path.Combine(outDir, "page-001.png")),
                "Halaman ekspor CLI harus ada.");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
