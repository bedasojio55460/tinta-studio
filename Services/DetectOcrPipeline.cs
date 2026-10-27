using SkiaSharp;
using TintaStudio.Models;

namespace TintaStudio.Services;

/// <summary>
/// Pipa Deteksi → OCR → blok teks proyek.
/// Urutan baca (heuristik sederhana, terdokumentasi):
/// - Bahasa Jepang dengan mayoritas blok vertikal (manga): blok
///   dikelompokkan per kolom — kolom paling KANAN dibaca dulu;
///   di dalam kolom, dari ATAS ke bawah.
/// - Selain itu (horizontal): kelompok baris dari ATAS ke bawah;
///   di dalam baris, dari KIRI ke kanan.
/// Pengelompokan memakai tumpang-tindih proyeksi (pusat blok masuk
/// rentang blok sebelumnya dalam kelompok yang sama).
/// </summary>
public sealed class DetectOcrPipeline : IDisposable
{
    private readonly TextDetector _detector = new();
    private readonly OcrService _ocr = new();

    /// <summary>
    /// Jalankan deteksi + OCR pada bitmap satu halaman. Mengembalikan
    /// blok teks (kelas text_bubble/text_free saja) dalam urutan baca.
    /// </summary>
    public IReadOnlyList<TextBlock> AnalyzePage(
        SKBitmap page, OcrLanguage lang, CancellationToken ct = default)
    {
        var missing = ModelStore.MissingFor(lang);
        if (missing.Count > 0)
            throw new ModelMissingException(missing);

        var boxes = DedupeBoxes(_detector.Detect(page)
            .Where(b => b.Class is DetectedClass.TextBubble or DetectedClass.TextFree)
            .ToList());

        var blocks = new List<TextBlock>();
        foreach (var box in boxes)
        {
            ct.ThrowIfCancellationRequested();
            var result = _ocr.RecognizeBlock(page, box, lang);
            // Blok yang OCR-nya kosong tidak disimpan: umumnya artefak
            // lengkung atas bubble yang terdeteksi sebagai teks, bukan
            // teks sungguhan (bahasa salah → jalankan ulang bahasanya;
            // blok kosong tidak berguna bagi tahap berikutnya).
            if (string.IsNullOrWhiteSpace(result.Text))
                continue;
            blocks.Add(new TextBlock
            {
                X = box.X,
                Y = box.Y,
                Width = box.Width,
                Height = box.Height,
                Direction = result.Direction,
                Language = lang.Code,
                Text = result.Text,
                Confidence = result.Confidence,
                DetectionScore = box.Score,
            });
        }
        return SortReadingOrder(blocks, lang);
    }

    /// <summary>
    /// Buang kotak duplikat LINTAS kelas teks: wilayah yang sama
    /// bisa dilaporkan detektor dua kali — sekali sebagai
    /// text_bubble dan sekali sebagai text_free — dengan kotak
    /// nyaris identik (NMS di detektor hanya menekan duplikat
    /// sekelas). Ditemukan pada uji halaman ilustrasi asli
    /// 2026-10-27: dua pasang kotak identik persis lolos jadi blok
    /// ganda. Yang dipertahankan: skor deteksi tertinggi. Ambang
    /// IoU 0,85: dua kotak teks berbeda yang sah tidak pernah
    /// bertumpang sejauh itu.
    /// </summary>
    internal static List<DetectedBox> DedupeBoxes(List<DetectedBox> boxes)
    {
        var kept = new List<DetectedBox>();
        foreach (var b in boxes.OrderByDescending(b => b.Score))
        {
            if (kept.Any(k => TextDetector.Iou(k, b) > 0.85))
                continue;
            kept.Add(b);
        }
        return kept;
    }

    /// <summary>
    /// Analisis satu bab penuh: impor halaman kerja bab lalu analisis
    /// tiap halaman, simpan blok ke proyek. Kemajuan dilaporkan
    /// sebagai (halaman selesai, total halaman).
    /// </summary>
    public async Task AnalyzeChapterAsync(
        Chapter chapter, ProjectChapter projectChapter,
        OcrLanguage lang, IProgress<(int Done, int Total)>? progress,
        CancellationToken ct)
    {
        var missing = ModelStore.MissingFor(lang);
        if (missing.Count > 0)
            throw new ModelMissingException(missing);

        int total = chapter.Pages.Count;
        for (int i = 0; i < total; i++)
        {
            ct.ThrowIfCancellationRequested();
            var pageInfo = chapter.Pages[i];
            var blocks = await Task.Run(() =>
            {
                using var bmp = SKBitmap.Decode(pageInfo.WorkPath)
                    ?? throw new InvalidDataException(
                        $"Halaman kerja tidak terbaca: {pageInfo.SourceName}");
                return AnalyzePage(bmp, lang, ct);
            }, ct);
            var stored = projectChapter.PageAt(i);
            stored.Blocks.Clear();
            stored.Blocks.AddRange(blocks);
            progress?.Report((i + 1, total));
        }
    }

    internal static IReadOnlyList<TextBlock> SortReadingOrder(
        List<TextBlock> blocks, OcrLanguage lang)
    {
        if (blocks.Count <= 1)
            return blocks;
        int vertical = blocks.Count(b => b.Direction == TextDirection.Vertical);
        bool mangaColumns = lang.Code == "ja" && vertical * 2 > blocks.Count;

        var sorted = new List<TextBlock>();
        if (mangaColumns)
        {
            // Kolom: urutkan kandidat menurut tepi kanan menurun, lalu
            // kelompokkan blok yang proyeksi-X-nya tumpang tindih.
            var remaining = blocks.OrderByDescending(b => b.X + b.Width).ToList();
            while (remaining.Count > 0)
            {
                var seed = remaining[0];
                var column = remaining
                    .Where(b => Overlap1D(b.X, b.Width, seed.X, seed.Width) > 0.3)
                    .OrderBy(b => b.Y)
                    .ToList();
                sorted.AddRange(column);
                foreach (var b in column)
                    remaining.Remove(b);
            }
        }
        else
        {
            var remaining = blocks.OrderBy(b => b.Y).ToList();
            while (remaining.Count > 0)
            {
                var seed = remaining[0];
                var row = remaining
                    .Where(b => Overlap1D(b.Y, b.Height, seed.Y, seed.Height) > 0.3)
                    .OrderBy(b => b.X)
                    .ToList();
                sorted.AddRange(row);
                foreach (var b in row)
                    remaining.Remove(b);
            }
        }
        return sorted;
    }

    /// <summary>Rasio tumpang tindih dua rentang 1D terhadap rentang terkecil.</summary>
    private static double Overlap1D(int aPos, int aLen, int bPos, int bLen)
    {
        int start = Math.Max(aPos, bPos);
        int end = Math.Min(aPos + aLen, bPos + bLen);
        if (end <= start)
            return 0;
        return (double)(end - start) / Math.Min(aLen, bLen);
    }

    public void Dispose()
    {
        _detector.Dispose();
        _ocr.Dispose();
    }
}

/// <summary>
/// Penyimpanan proyek kerja otomatis per bab: berkas
/// ~/.config/tinta-studio/projects/&lt;hash&gt;.tinta.json
/// (override basis: env TINTA_CONFIG_DIR). Satu bab = satu proyek
/// berisi bab itu; dibuat saat pertama dibuka, diperbarui saat
/// deteksi/suntingan blok terjadi.
/// </summary>
public static class ProjectStore
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
            return Path.Combine(baseDir, "projects");
        }
    }

    public static string PathFor(string sourcePath)
    {
        var hash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(
                    Path.GetFullPath(sourcePath))))[..16].ToLowerInvariant();
        return Path.Combine(StoreDir, hash + ".tinta.json");
    }

    /// <summary>Muat proyek bab bila ada; selain itu buat baru (belum disimpan).</summary>
    public static (TintaProject Project, ProjectChapter Chapter) GetOrCreate(
        Chapter chapter)
    {
        var path = PathFor(chapter.SourcePath);
        TintaProject project;
        if (File.Exists(path))
        {
            try
            {
                project = TintaProject.Load(path);
            }
            catch
            {
                project = NewProject(chapter);
            }
        }
        else
        {
            project = NewProject(chapter);
        }
        var pc = project.Chapters.FirstOrDefault(
            c => string.Equals(c.SourcePath, chapter.SourcePath,
                StringComparison.Ordinal));
        if (pc is null)
        {
            pc = new ProjectChapter
            {
                SourcePath = chapter.SourcePath,
                Title = chapter.Title,
                Format = chapter.Format,
                PageCount = chapter.Pages.Count,
            };
            project.Chapters.Add(pc);
        }
        else
        {
            pc.Title = chapter.Title;
            pc.Format = chapter.Format;
            pc.PageCount = chapter.Pages.Count;
        }
        return (project, pc);
    }

    private static TintaProject NewProject(Chapter chapter) => new()
    {
        Name = string.IsNullOrWhiteSpace(chapter.Series)
            ? chapter.Title
            : chapter.Series,
    };

    public static void Save(TintaProject project, string sourcePath)
    {
        Directory.CreateDirectory(StoreDir);
        project.Save(PathFor(sourcePath));
    }
}
