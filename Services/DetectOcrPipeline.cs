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
    /// Bahasa yang terdeteksi per halaman pada jalan mode Otomatis
    /// terakhir (AnalyzeChapterAsync / AnalyzePage dipanggil dari
    /// luar dengan lang Otomatis). Indeks = indeks halaman;
    /// diisi ulang setiap bab dianalisis. Konsumen (UI/CLI)
    /// membacanya untuk transparansi "Bahasa terdeteksi: …".
    /// </summary>
    public IReadOnlyDictionary<int, OcrLanguage> DetectedLanguages
    {
        get => _detectedLanguages;
    }

    private readonly Dictionary<int, OcrLanguage> _detectedLanguages = new();

    /// <summary>Jumlah blok sampel terbesar untuk tebakan bahasa halaman.</summary>
    internal const int PageGuessSampleSize = 5;

    /// <summary>
    /// Jalankan deteksi + OCR pada bitmap satu halaman. Mengembalikan
    /// blok teks (kelas text_bubble/text_free saja) dalam urutan baca.
    /// lang Otomatis: bahasa halaman ditebak dari model terpasang
    /// (lihat GuessPageLanguage), lalu blok berkeyakinan rendah
    /// diperbaiki per blok dengan kandidat lain.
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

        if (lang.IsAuto)
            return AnalyzePageAuto(page, boxes, ct);

        var blocks = RecognizeBoxes(page, boxes, lang, ct);
        return SortReadingOrder(blocks, lang);
    }

    /// <summary>
    /// Kandidat mode Otomatis: bahasa yang model rec-nya terpasang.
    /// Bila kosong → galat jujur menyebut unduhan yang dibutuhkan.
    /// </summary>
    internal static IReadOnlyList<OcrLanguage> AutoCandidates()
    {
        var installed = ModelStore.InstalledLanguages();
        if (installed.Count == 0)
            throw new ModelMissingException(
                ModelStore.Catalog
                    .Where(e => e.Id.StartsWith("rec-", StringComparison.Ordinal))
                    .ToList());
        return installed;
    }

    /// <summary>
    /// Jalan mode Otomatis satu halaman: tebak bahasa halaman dari
    /// sampel blok terbesar, OCR semua blok dengan bahasa itu, lalu
    /// perbaiki blok berkeyakinan &lt; ambang dengan kandidat lain
    /// (skor terbaik yang disimpan, bahasa aktual tercatat per blok).
    /// </summary>
    private IReadOnlyList<TextBlock> AnalyzePageAuto(
        SKBitmap page, List<DetectedBox> boxes, CancellationToken ct)
    {
        var candidates = AutoCandidates();
        var pageLang = candidates.Count == 1
            ? candidates[0]
            : GuessPageLanguage(page, boxes, candidates, ct);
        _detectedLanguages[0] = pageLang;

        var blocks = new List<TextBlock>();
        foreach (var box in boxes)
        {
            ct.ThrowIfCancellationRequested();
            var result = _ocr.RecognizeBlock(page, box, pageLang);
            var used = pageLang;
            if (candidates.Count > 1
                && (result.Confidence < LanguageGuess.BlockRetryThreshold
                    || LanguageGuess.ScriptMismatch(pageLang, result.Text)))
            {
                // Coba-ulang per blok: hasil bahasa halaman lemah,
                // ATAU aksaranya sama sekali bukan aksara bahasa
                // halaman — mungkin blok ini berbahasa lain
                // (halaman campur).
                foreach (var alt in candidates)
                {
                    if (alt.Code == pageLang.Code)
                        continue;
                    ct.ThrowIfCancellationRequested();
                    var altResult = _ocr.RecognizeBlock(page, box, alt);
                    if (LanguageGuess.ScoreOf(alt, altResult)
                        > LanguageGuess.ScoreOf(used, result))
                    {
                        result = altResult;
                        used = alt;
                    }
                }
            }
            // Blok yang OCR-nya kosong tidak disimpan (sama seperti
            // mode manual: umumnya artefak lengkung bubble).
            if (string.IsNullOrWhiteSpace(result.Text))
                continue;
            blocks.Add(ToBlock(box, result, used));
        }
        return SortReadingOrder(blocks, pageLang);
    }

    /// <summary>
    /// Tebak bahasa halaman: sampel sampai
    /// <see cref="PageGuessSampleSize"/> blok terbesar (luas) dibaca
    /// tiap kandidat; skor kandidat = rata-rata ScoreOf tertimbang
    /// panjang teks keluaran. Biaya: kandidat × sampel kali rec.
    /// </summary>
    internal OcrLanguage GuessPageLanguage(
        SKBitmap page, IReadOnlyList<DetectedBox> boxes,
        IReadOnlyList<OcrLanguage> candidates, CancellationToken ct = default)
    {
        var sample = boxes
            .OrderByDescending(b => (long)b.Width * b.Height)
            .Take(PageGuessSampleSize)
            .ToList();
        if (sample.Count == 0)
            return candidates[0];

        OcrLanguage? best = null;
        double bestScore = double.NegativeInfinity;
        foreach (var cand in candidates)
        {
            double sum = 0;
            long weight = 0;
            foreach (var box in sample)
            {
                ct.ThrowIfCancellationRequested();
                var r = _ocr.RecognizeBlock(page, box, cand);
                if (string.IsNullOrWhiteSpace(r.Text))
                    continue;
                int w = Math.Max(1, r.Text.Replace("\n", "").Length);
                sum += LanguageGuess.ScoreOf(cand, r) * w;
                weight += w;
            }
            double score = weight > 0 ? sum / weight : double.NegativeInfinity;
            if (score > bestScore)
            {
                bestScore = score;
                best = cand;
            }
        }
        // Semua kandidat membaca kosong pada sampel (halaman tanpa
        // teks terbaca): jatuhkan ke kandidat pertama agar blok
        // kosong terbuang seperti mode manual.
        return best ?? candidates[0];
    }

    private List<TextBlock> RecognizeBoxes(
        SKBitmap page, IReadOnlyList<DetectedBox> boxes, OcrLanguage lang,
        CancellationToken ct)
    {
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
            blocks.Add(ToBlock(box, result, lang));
        }
        return blocks;
    }

    private static TextBlock ToBlock(
        DetectedBox box, OcrResult result, OcrLanguage used) => new()
        {
            X = box.X,
            Y = box.Y,
            Width = box.Width,
            Height = box.Height,
            Direction = result.Direction,
            Language = used.Code,
            Text = result.Text,
            Confidence = result.Confidence,
            DetectionScore = box.Score,
        };

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
        if (lang.IsAuto)
            AutoCandidates(); // gagal cepat & jujur sebelum halaman pertama

        _detectedLanguages.Clear();
        int total = chapter.Pages.Count;
        for (int i = 0; i < total; i++)
        {
            ct.ThrowIfCancellationRequested();
            var pageInfo = chapter.Pages[i];
            int pageIndex = i;
            var blocks = await Task.Run(() =>
            {
                using var bmp = SKBitmap.Decode(pageInfo.WorkPath)
                    ?? throw new InvalidDataException(
                        $"Halaman kerja tidak terbaca: {pageInfo.SourceName}");
                var result = AnalyzePage(bmp, lang, ct);
                if (lang.IsAuto && _detectedLanguages.TryGetValue(0, out var guessed))
                    _detectedLanguages[pageIndex] = guessed;
                return result;
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
