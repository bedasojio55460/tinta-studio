using System.Security.Cryptography;

namespace TintaStudio.Services;

/// <summary>Satu berkas model dalam katalog (nama cache, URL, hash).</summary>
public sealed record ModelFile(string CacheName, string Url, string Sha256, long SizeBytes);

/// <summary>Satu model logis: satu/dua berkas (onnx + kamus).</summary>
public sealed record ModelEntry(
    string Id, string DisplayName, string Role, IReadOnlyList<ModelFile> Files);

/// <summary>
/// Katalog model publik Tahap 2–3 + cache lokal di
/// ~/.cache/tinta-studio/models (override: env TINTA_MODEL_DIR).
/// Unduhan tidak masuk git; hash diverifikasi setiap unduh.
/// Daftar lengkap + alasan pemilihan: docs/models.md.
/// </summary>
public static class ModelStore
{
    public const string DetectorId = "detector";
    public const string ClsId = "cls";
    public const string InpaintId = "inpaint";

    public static string CacheDir
    {
        get
        {
            var env = Environment.GetEnvironmentVariable("TINTA_MODEL_DIR");
            if (!string.IsNullOrWhiteSpace(env))
                return env;
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".cache", "tinta-studio", "models");
        }
    }

    public static IReadOnlyList<ModelEntry> Catalog { get; } = new List<ModelEntry>
    {
        new(DetectorId, "Detektor teks komik", "RT-DETR-v2 int8 — kotak bubble/teks",
            new[]
            {
                new ModelFile("comic-text-detector.onnx",
                    "https://huggingface.co/ogkalu/comic-text-and-bubble-detector/resolve/main/detector-v4-s_int8.onnx",
                    "5fe9e4f576e49d4e7e8b0e029d6d3cdc252abd4694113e1cae120e62c931ea79", 11120765),
            }),
        new("rec-ja", "OCR Jepang", "PP-OCRv4 recognition + kamus",
            new[]
            {
                new ModelFile("rec-ja.onnx",
                    "https://huggingface.co/cycloneboy/japan_PP-OCRv4_rec_infer/resolve/main/model.onnx",
                    "bb0f23444c059d27a615c1a426414240bbd02d3658900d0455e007dc47d3e4a4", 9735983),
                new ModelFile("rec-ja.dict.txt",
                    "https://huggingface.co/cycloneboy/japan_PP-OCRv4_rec_infer/resolve/main/japan_dict.txt",
                    "2a0842e81ef31d6a99d811a36fc12c33e37aa9406667ef1bd9b468f9108a66bf", 21731),
            }),
        new("rec-ko", "OCR Korea", "PP-OCRv5 mobile recognition + kamus",
            new[]
            {
                new ModelFile("rec-ko.onnx",
                    "https://huggingface.co/monkt/paddleocr-onnx/resolve/main/languages/korean/rec.onnx",
                    "322f140154c820fcb83c3d24cfe42c9ec70dd1a1834163306a7338136e4f1eaa", 13401252),
                new ModelFile("rec-ko.dict.txt",
                    "https://huggingface.co/monkt/paddleocr-onnx/resolve/main/languages/korean/dict.txt",
                    "a88071c68c01707489baa79ebe0405b7beb5cca229f4fc94cc3ef992328802d7", 47451),
            }),
        new("rec-ch", "OCR Cina", "PP-OCRv5 server recognition + kamus",
            new[]
            {
                new ModelFile("rec-ch.onnx",
                    "https://huggingface.co/monkt/paddleocr-onnx/resolve/main/languages/chinese/rec.onnx",
                    "26fa4f47060f58e25962b9af6beaee05c8182b90e026c4ecc6db165d9dfdc38a", 84468836),
                new ModelFile("rec-ch.dict.txt",
                    "https://huggingface.co/monkt/paddleocr-onnx/resolve/main/languages/chinese/dict.txt",
                    "d1979e9f794c464c0d2e0b70a7fe14dd978e9dc644c0e71f14158cdf8342af1b", 74012),
            }),
        new("rec-en", "OCR Inggris", "PP-OCRv5 mobile recognition + kamus",
            new[]
            {
                new ModelFile("rec-en.onnx",
                    "https://huggingface.co/monkt/paddleocr-onnx/resolve/main/languages/english/rec.onnx",
                    "4e16deb22c4da6468bdca539b2cd3c8687825538b67109177c47d359ab994cd7", 7830888),
                new ModelFile("rec-en.dict.txt",
                    "https://huggingface.co/monkt/paddleocr-onnx/resolve/main/languages/english/dict.txt",
                    "e025a66d31f327ba0c232e03f407ae8d105e1e709e7ccb3f408aa778c24e70d6", 1416),
            }),
        new(ClsId, "Klasifikasi arah garis", "PP-LCNet 0°/180° (opsional)",
            new[]
            {
                new ModelFile("cls-textline.onnx",
                    "https://huggingface.co/bukuroo/PPOCRv5-ONNX/resolve/main/ppocrv5-cls.onnx",
                    "f4bb53707100c5f3d59ba834eb05bb400369f20aed35d4b26807b1bfadd2a70e", 582663),
            }),
        new(InpaintId, "Inpaint LaMA", "LaMA manga (ONNX) — isi bubble mengikuti latar",
            new[]
            {
                new ModelFile("inpaint-lama.onnx",
                    "https://huggingface.co/mayocream/lama-manga-onnx/resolve/main/lama-manga.onnx",
                    "4512adab295ee5a5e02ccd1bdf8d45dccbac88309d9cff1532ffd5de876f02a4", 207482644),
            }),
    };

    /// <summary>Model inpaint LaMA (Tahap 3).</summary>
    public static ModelEntry InpaintEntry => Entry(InpaintId);

    /// <summary>True bila model inpaint sudah siap di cache.</summary>
    public static bool InpaintReady => IsDownloaded(InpaintEntry);

    public static ModelEntry Entry(string id) => Catalog.First(e => e.Id == id);

    public static string PathOf(ModelFile file) => Path.Combine(CacheDir, file.CacheName);

    public static bool FileReady(ModelFile file)
    {
        var p = PathOf(file);
        return File.Exists(p) && new FileInfo(p).Length == file.SizeBytes;
    }

    public static bool IsDownloaded(ModelEntry entry) => entry.Files.All(FileReady);

    /// <summary>Model yang wajib ada untuk menjalankan pipeline bahasa ini.</summary>
    public static IReadOnlyList<ModelEntry> RequiredFor(OcrLanguage lang) => new[]
    {
        Entry(DetectorId), Entry("rec-" + lang.Code),
    };

    public static IReadOnlyList<ModelEntry> MissingFor(OcrLanguage lang)
    {
        // Mode Otomatis tidak punya model sendiri; yang wajib hanya
        // detektor. Kandidat bahasa ditentukan dari model rec yang
        // terpasang (InstalledLanguages) dan ketiadaannya dilaporkan
        // oleh pipa dengan pesan khusus.
        if (lang.IsAuto)
            return new[] { Entry(DetectorId) }.Where(e => !IsDownloaded(e)).ToList();
        return RequiredFor(lang).Where(e => !IsDownloaded(e)).ToList();
    }

    /// <summary>
    /// Bahasa yang model recognition-nya terpasang penuh di cache —
    /// kandidat mode Otomatis, urutan katalog.
    /// </summary>
    public static IReadOnlyList<OcrLanguage> InstalledLanguages() =>
        OcrLanguage.Concrete
            .Where(l => IsDownloaded(Entry("rec-" + l.Code)))
            .ToList();

    /// <summary>
    /// Unduh semua berkas model yang belum siap; kemajuan 0..1 dilaporkan
    /// per gabungan berkas. Kegagalan (jaringan/hash) dilempar apa adanya.
    /// </summary>
    public static async Task DownloadAsync(
        ModelEntry entry, IProgress<double>? progress, CancellationToken ct)
    {
        Directory.CreateDirectory(CacheDir);
        var todo = entry.Files.Where(f => !FileReady(f)).ToList();
        if (todo.Count == 0)
        {
            progress?.Report(1);
            return;
        }
        long total = todo.Sum(f => f.SizeBytes);
        long done = 0;
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
        foreach (var file in todo)
        {
            var target = PathOf(file);
            var part = target + ".part";
            using (var resp = await http.GetAsync(file.Url,
                       HttpCompletionOption.ResponseHeadersRead, ct))
            {
                resp.EnsureSuccessStatusCode();
                await using var src = await resp.Content.ReadAsStreamAsync(ct);
                await using var dst = File.Create(part);
                var buf = new byte[81920];
                int n;
                while ((n = await src.ReadAsync(buf, ct)) > 0)
                {
                    await dst.WriteAsync(buf.AsMemory(0, n), ct);
                    done += n;
                    progress?.Report(total > 0 ? Math.Min(1, (double)done / total) : 0);
                }
            }
            var hash = Convert.ToHexString(
                SHA256.HashData(await File.ReadAllBytesAsync(part, ct))).ToLowerInvariant();
            if (!string.Equals(hash, file.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(part);
                throw new InvalidOperationException(
                    $"SHA-256 tidak cocok untuk {file.CacheName} (unduhan korup/diganti).");
            }
            File.Move(part, target, overwrite: true);
        }
        progress?.Report(1);
    }
}

/// <summary>Dilempar saat model yang dibutuhkan belum ada di cache.</summary>
public sealed class ModelMissingException : Exception
{
    public IReadOnlyList<ModelEntry> Missing { get; }

    public ModelMissingException(IReadOnlyList<ModelEntry> missing)
        : base("Model belum diunduh: " + string.Join(", ", missing.Select(m => m.DisplayName)) +
               ". Unduh lewat kartu Model di aplikasi atau tools/fetch_models.sh.")
    {
        Missing = missing;
    }
}
