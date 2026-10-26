using SkiaSharp;
using TintaStudio.Models;
using TintaStudio.Services;
using Xunit;

namespace TintaStudio.Tests;

public sealed class ComicImportTests : IDisposable
{
    private readonly string _root;
    private readonly string _work;

    public ComicImportTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "tinta-tests-" + Guid.NewGuid().ToString("N"));
        _work = Path.Combine(_root, "work");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (Exception) { /* abaikan sisa berkas uji */ }
    }

    private string PathOf(string name) => Path.Combine(_root, name);

    [Fact]
    public void Cbz_CampurFormat_UrutanNatural_ComicInfo_SampahDanBersarang()
    {
        var cbz = PathOf("bab1.cbz");
        FixtureBuilder.WriteZip(cbz, new[]
        {
            ("10.jpg", FixtureBuilder.MakeImage(120, 160, SKColors.DarkRed, SKEncodedImageFormat.Jpeg)),
            ("2.jpg", FixtureBuilder.MakeImage(110, 150, SKColors.DarkGreen, SKEncodedImageFormat.Jpeg)),
            ("1.png", FixtureBuilder.MakeImage(100, 140, SKColors.DarkBlue, SKEncodedImageFormat.Png)),
            ("sub/3.webp", FixtureBuilder.MakeImage(130, 170, SKColors.DarkMagenta, SKEncodedImageFormat.Webp)),
            ("ComicInfo.xml", System.Text.Encoding.UTF8.GetBytes(
                "<ComicInfo><Series>Seri Uji</Series><Title>Bab 1 — Awal</Title></ComicInfo>")),
            ("__MACOSX/._1.png", new byte[] { 1, 2, 3 }),
            ("Thumbs.db", new byte[] { 4, 5, 6 }),
            ("catatan.txt", System.Text.Encoding.UTF8.GetBytes("halo")),
        });

        Assert.Equal(ComicFormat.Zip, ComicImport.DetectFormat(cbz));
        var chapter = ComicImport.OpenChapter(cbz, _work);

        Assert.Equal("Bab 1 — Awal", chapter.Title);
        Assert.Equal("Seri Uji", chapter.Series);
        Assert.Equal("ZIP (CBZ)", chapter.Format);
        Assert.Equal(4, chapter.Pages.Count);
        Assert.Equal(
            new[] { "1.png", "2.jpg", "10.jpg", "sub/3.webp" },
            chapter.Pages.Select(p => p.SourceName).ToArray());
        Assert.Equal((100, 140), (chapter.Pages[0].Width, chapter.Pages[0].Height));
        Assert.All(chapter.Pages, p => Assert.True(File.Exists(p.WorkPath)));
        Assert.Contains(chapter.Skipped, s => s.Name == "Thumbs.db");
        Assert.Contains(chapter.Skipped, s => s.Name == "catatan.txt");
        Assert.Contains(chapter.Skipped, s => s.Name.Contains("__MACOSX"));
    }

    [Fact]
    public void Folder_GambarCampur_SatuRusakTerlewati()
    {
        var dir = PathOf("bab-folder");
        Directory.CreateDirectory(Path.Combine(dir, "sub"));
        File.WriteAllBytes(Path.Combine(dir, "01.jpg"),
            FixtureBuilder.MakeImage(90, 120, SKColors.Teal, SKEncodedImageFormat.Jpeg));
        File.WriteAllBytes(Path.Combine(dir, "02.png"),
            FixtureBuilder.MakeImage(90, 120, SKColors.Teal, SKEncodedImageFormat.Png));
        File.WriteAllBytes(Path.Combine(dir, "03.bmp"),
            FixtureBuilder.MakeBmp(90, 120, 0, 128, 128));
        File.WriteAllBytes(Path.Combine(dir, "rusak.png"),
            System.Text.Encoding.UTF8.GetBytes("ini bukan gambar"));
        File.WriteAllBytes(Path.Combine(dir, "sub", "04.tif"),
            FixtureBuilder.MakeTiff(90, 120, 0, 128, 128));

        Assert.Equal(ComicFormat.Folder, ComicImport.DetectFormat(dir));
        var chapter = ComicImport.OpenChapter(dir, _work);

        Assert.Equal("Folder", chapter.Format);
        Assert.Equal(4, chapter.Pages.Count);
        Assert.Contains(chapter.Skipped,
            s => s.Name == "rusak.png" && s.Reason.Contains("tidak terbaca"));
    }

    [Fact]
    public void Tar_Cbt_Terbaca()
    {
        var cbt = PathOf("bab.cbt");
        FixtureBuilder.WriteTar(cbt, new[]
        {
            ("b/2.png", FixtureBuilder.MakeImage(80, 100, SKColors.Brown, SKEncodedImageFormat.Png)),
            ("a/1.png", FixtureBuilder.MakeImage(80, 100, SKColors.Brown, SKEncodedImageFormat.Png)),
        });

        Assert.Equal(ComicFormat.Tar, ComicImport.DetectFormat(cbt));
        var chapter = ComicImport.OpenChapter(cbt, _work);
        Assert.Equal("TAR (CBT)", chapter.Format);
        Assert.Equal(2, chapter.Pages.Count);
        Assert.Equal(new[] { "a/1.png", "b/2.png" },
            chapter.Pages.Select(p => p.SourceName).ToArray());
    }

    [Fact]
    public void SevenZip_Cb7_Terbaca()
    {
        var cb7 = PathOf("bab.cb7");
        FixtureBuilder.WriteSevenZip(cb7, new[]
        {
            ("1.png", FixtureBuilder.MakeImage(80, 100, SKColors.Indigo, SKEncodedImageFormat.Png)),
            ("2.png", FixtureBuilder.MakeImage(80, 100, SKColors.Indigo, SKEncodedImageFormat.Png)),
        });

        Assert.Equal(ComicFormat.SevenZip, ComicImport.DetectFormat(cb7));
        var chapter = ComicImport.OpenChapter(cb7, _work);
        Assert.Equal("7z (CB7)", chapter.Format);
        Assert.Equal(2, chapter.Pages.Count);
    }

    [Fact]
    public void SalahLabel_IsiZipBernamaCbr_TerdeteksiZip()
    {
        var cbr = PathOf("bab.cbr");
        FixtureBuilder.WriteZip(cbr, new[]
        {
            ("1.png", FixtureBuilder.MakeImage(70, 90, SKColors.Olive, SKEncodedImageFormat.Png)),
        });

        Assert.Equal(ComicFormat.Zip, ComicImport.DetectFormat(cbr));
        var chapter = ComicImport.OpenChapter(cbr, _work);
        Assert.Single(chapter.Pages);
    }

    [Fact]
    public void Jpeg_RotasiExif_SalinanKerjaLurus()
    {
        // Gambar tersimpan 60x100 (tegak); orientasi EXIF 6 artinya
        // tampilan benarnya diputar 90° → salinan kerja harus 100x60.
        var jpeg = FixtureBuilder.MakeImage(60, 100, SKColors.Crimson, SKEncodedImageFormat.Jpeg);
        var withExif = FixtureBuilder.JpegWithExifOrientation(jpeg, 6);
        var file = PathOf("foto.jpg");
        File.WriteAllBytes(file, withExif);

        Assert.Equal(ComicFormat.Image, ComicImport.DetectFormat(file));
        var chapter = ComicImport.OpenChapter(file, _work);
        var page = Assert.Single(chapter.Pages);
        Assert.Equal((100, 60), (page.Width, page.Height));
    }

    [Fact]
    public void Pdf_Kecil_DuaHalamanTerrender()
    {
        var pdf = PathOf("bab.pdf");
        File.WriteAllBytes(pdf, FixtureBuilder.MakePdf(
            (160, 200, SKColors.DarkSlateBlue),
            (160, 200, SKColors.DarkCyan)));

        Assert.Equal(ComicFormat.Pdf, ComicImport.DetectFormat(pdf));
        var chapter = ComicImport.OpenChapter(pdf, _work);
        Assert.Equal("PDF", chapter.Format);
        Assert.Equal(2, chapter.Pages.Count);
        // Halaman PDF 200pt pada 150 DPI -> sekitar 417 px.
        Assert.InRange(chapter.Pages[0].Width, 400, 435);
        Assert.All(chapter.Pages, p => Assert.True(File.Exists(p.WorkPath)));
    }

    [Fact]
    public void Epub_Sederhana_IkutiUrutanSpine()
    {
        var epub = PathOf("bab.epub");
        FixtureBuilder.WriteEpub(epub, new[]
        {
            ("z-terakhir.png", FixtureBuilder.MakeImage(80, 100, SKColors.Gray, SKEncodedImageFormat.Png)),
            ("a-pertama.png", FixtureBuilder.MakeImage(80, 100, SKColors.Gray, SKEncodedImageFormat.Png)),
        });

        Assert.Equal(ComicFormat.Epub, ComicImport.DetectFormat(epub));
        var chapter = ComicImport.OpenChapter(epub, _work);
        Assert.Equal("EPUB", chapter.Format);
        Assert.Equal(2, chapter.Pages.Count);
        // Urutan spine: z-terakhir dulu (halaman XHTML pertama), bukan urutan abjad.
        Assert.Equal("OEBPS/images/z-terakhir.png", chapter.Pages[0].SourceName);
        Assert.Equal("OEBPS/images/a-pertama.png", chapter.Pages[1].SourceName);
    }

    [Fact]
    public void GambarTunggal_JadiBabSatuHalaman()
    {
        var file = PathOf("sampul.webp");
        File.WriteAllBytes(file,
            FixtureBuilder.MakeImage(200, 260, SKColors.DarkOrange, SKEncodedImageFormat.Webp));

        Assert.Equal(ComicFormat.Image, ComicImport.DetectFormat(file));
        var chapter = ComicImport.OpenChapter(file, _work);
        Assert.Single(chapter.Pages);
        Assert.Equal("sampul", chapter.Title);
    }

    [Fact]
    public void Avif_BelumDidukungCodec_DilaporkanJujur()
    {
        // Contoh AVIF asli (dibuat dengan ffmpeg/libaom, 50x70). Build
        // libSkiaSharp saat ini tidak membawa codec AVIF — halaman harus
        // terlewati dengan alasan yang menyebut AVIF, bukan gagal senyap.
        var avif = Convert.FromBase64String(
            "AAAAIGZ0eXBhdmlmAAAAAGF2aWZtaWYxbWlhZk1BMUIAAAD5bWV0YQAAAAAAAAAvaGRscgAAAAAAAAAAcGljdAAAAAAAAAAAAAAAAFBpY3R1cmVIYW5kbGVyAAAAAA5waXRtAAAAAAABAAAAHmlsb2MAAAAARAAAAQABAAAAAQAAASEAAAAkAAAAKGlpbmYAAAAAAAEAAAAaaW5mZQIAAAAAAQAAYXYwMUNvbG9yAAAAAGppcHJwAAAAS2lwY28AAAAUaXNwZQAAAAAAAAAyAAAARgAAABBwaXhpAAAAAAMICAgAAAAMYXYxQ4EADAAAAAATY29scm5jbHgAAgACAAIAAAAAF2lwbWEAAAAAAAAAAQABBAECgwQAAAAsbWRhdAoKAAAAArYxT/+YBDIWEACgAAABQAABAAHFecLcL50arqRqQA==");
        var dir = PathOf("bab-avif");
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "01.jpg"),
            FixtureBuilder.MakeImage(60, 80, SKColors.Teal, SKEncodedImageFormat.Jpeg));
        File.WriteAllBytes(Path.Combine(dir, "02.avif"), avif);

        var chapter = ComicImport.OpenChapter(dir, _work);
        Assert.Single(chapter.Pages);
        Assert.Contains(chapter.Skipped,
            s => s.Name == "02.avif" && s.Reason.Contains("AVIF"));
    }

    [Fact]
    public void BerkasTidakDikenal_DitolakJujur()
    {
        var file = PathOf("dokumen.txt");
        File.WriteAllText(file, "ini teks biasa, bukan komik");
        Assert.Equal(ComicFormat.Unknown, ComicImport.DetectFormat(file));
        Assert.Throws<ComicImportException>(() => ComicImport.OpenChapter(file, _work));
    }

    [Fact]
    public void Proyek_SimpanMuat_Roundtrip()
    {
        var project = new TintaProject
        {
            Name = "Proyek Uji",
            Chapters =
            {
                new ProjectChapter
                {
                    SourcePath = "/data/bab1.cbz",
                    Title = "Bab 1",
                    Format = "ZIP (CBZ)",
                    PageCount = 24,
                },
            },
        };
        var path = PathOf("uji.tinta.json");
        project.Save(path);

        var loaded = TintaProject.Load(path);
        Assert.Equal("Proyek Uji", loaded.Name);
        Assert.Equal(TintaProject.FormatMarker, loaded.Format);
        Assert.Equal(TintaProject.CurrentVersion, loaded.Version);
        var chapter = Assert.Single(loaded.Chapters);
        Assert.Equal("Bab 1", chapter.Title);
        Assert.Equal(24, chapter.PageCount);
    }

    [Fact]
    public void Pustaka_TambahDaftarHapus()
    {
        var libPath = PathOf("library.json");
        var service = new LibraryService(libPath);
        var fakeChapter = new Chapter
        {
            Title = "Bab Pustaka",
            SourcePath = "/data/bab.cbz",
            Format = "ZIP (CBZ)",
            Pages = new[]
            {
                new ComicPage
                {
                    Index = 0, SourceName = "1.png", SourceFormat = "PNG",
                    Width = 10, Height = 10, WorkPath = "/tmp/x.png",
                },
            },
            Skipped = Array.Empty<SkippedEntry>(),
        };

        service.AddOrUpdate(fakeChapter);
        Assert.Single(service.Entries);
        Assert.Equal("Bab Pustaka", service.Entries[0].Title);
        Assert.Equal(1, service.Entries[0].PageCount);

        // Muat ulang dari berkas — entri bertahan.
        var reloaded = new LibraryService(libPath);
        Assert.Single(reloaded.Entries);

        Assert.True(service.Remove("/data/bab.cbz"));
        Assert.Empty(service.Entries);
    }
}
