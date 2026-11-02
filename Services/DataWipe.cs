namespace TintaStudio.Services;

/// <summary>Satu folder target Hapus Bersih: label manusiawi,
/// jalur persis dari resolver aplikasi, keberadaan di disk, dan
/// ukuran live dalam byte (0 bila tidak ada / tidak terbaca).</summary>
public sealed record WipeTarget(string Label, string Path, bool Exists, long SizeBytes);

/// <summary>Rencana Hapus Bersih: daftar target + total ukuran.
/// Dibangun dari resolver yang SAMA dengan yang dipakai aplikasi
/// (AppConfig.ConfigDir dan ModelStore.CacheRootDir) — tidak ada
/// jalur hardcode duplikat di sini.</summary>
public sealed record WipePlan(IReadOnlyList<WipeTarget> Targets)
{
    public long TotalBytes => Targets.Sum(t => t.SizeBytes);
    public int ExistingCount => Targets.Count(t => t.Exists);
}

/// <summary>Hasil eksekusi Hapus Bersih: folder yang benar-benar
/// terhapus, folder yang memang sudah tidak ada (diperlakukan
/// "sudah bersih", bukan galat), total byte yang dibebaskan
/// (ukuran sebelum hapus), dan pesan galat per folder yang gagal
/// (mis. berkas terkunci) — kegagalan satu folder tidak membatalkan
/// yang lain.</summary>
public sealed record WipeResult(
    IReadOnlyList<WipeTarget> Deleted,
    IReadOnlyList<WipeTarget> AlreadyAbsent,
    IReadOnlyList<string> Errors,
    long FreedBytes);

/// <summary>
/// Hapus Bersih (v0.7.11): menghapus SELURUH data lokal Tinta
/// Studio — folder konfigurasi (keys.json, settings.json,
/// library.json, proyek &amp; cadangan yang tersimpan di dalamnya)
/// dan folder cache model OCR. TIDAK menyentuh: berkas .exe
/// aplikasi (tidak bisa menghapus dirinya yang sedang berjalan),
/// proyek .json/.cbz yang disimpan pengguna di folder lain, dan
/// model Ollama (milik Ollama, di ~/.ollama/models — dipakai
/// banyak aplikasi). Logika rencana &amp; ukuran murni/statis agar
/// teruji; eksekusi penghapusan rekursif tipis.
/// </summary>
public static class DataWipe
{
    /// <summary>Folder model Ollama — disebut di teks instruksi
    /// sebagai yang TIDAK ikut terhapus (bukan target).</summary>
    public static string OllamaModelsDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".ollama", "models");

    /// <summary>Bangun rencana dari resolver aplikasi saat ini.</summary>
    public static WipePlan BuildPlan() => BuildPlan(
        AppConfig.ConfigDir, ModelStore.CacheRootDir);

    /// <summary>Bangun rencana dari dua akar eksplisit (dipakai tes
    /// dan UI): urutan tetap data aplikasi dulu, lalu cache model.
    /// Ukuran dihitung live dari disk; folder yang tidak ada
    /// tercatat Exists=false berukuran 0.</summary>
    public static WipePlan BuildPlan(string configDir, string cacheRootDir)
    {
        return new WipePlan(new[]
        {
            Measure("Folder data aplikasi — key API, pengaturan, proyek & cadangan", configDir),
            Measure("Folder cache model — model OCR yang sudah terunduh", cacheRootDir),
        });
    }

    private static WipeTarget Measure(string label, string path)
    {
        bool exists = Directory.Exists(path);
        return new WipeTarget(label, path, exists,
            exists ? DirectorySize(path) : 0);
    }

    /// <summary>Total ukuran pohon berkas (byte). Berkas/folder yang
    /// tidak bisa dibaca dilewati diam-diam (ukuran = perkiraan
    /// terbaik), agar panel tidak pernah meledak hanya karena satu
    /// berkas terkunci.</summary>
    public static long DirectorySize(string path)
    {
        long total = 0;
        try
        {
            foreach (var file in Directory.EnumerateFiles(path, "*",
                         SearchOption.AllDirectories))
            {
                try { total += new FileInfo(file).Length; }
                catch { /* berkas hilang/terkunci di tengah jalan — lewati */ }
            }
        }
        catch { /* folder tidak terbaca — kembalikan yang terkumpul */ }
        return total;
    }

    /// <summary>Format ukuran untuk UI: MB dengan satu desimal
    /// id-ID di bawah 1 GB ("12,3 MB"), GB di atasnya ("1,25 GB");
    /// 0 byte → "0 MB".</summary>
    public static string FormatSize(long bytes)
    {
        var id = System.Globalization.CultureInfo.GetCultureInfo("id-ID");
        if (bytes <= 0) return "0 MB";
        double gb = bytes / 1_073_741_824.0;
        if (gb >= 1)
            return gb.ToString("0.00", id) + " GB";
        double mb = bytes / 1_048_576.0;
        return mb.ToString("0.0", id) + " MB";
    }

    /// <summary>Eksekusi rencana: hapus rekursif setiap target
    /// yang ada. Target yang tidak ada dicatat AlreadyAbsent
    /// (bukan galat); target yang gagal dicatat di Errors dan
    /// eksekusi lanjut ke target berikutnya.</summary>
    public static WipeResult Execute(WipePlan plan)
    {
        var deleted = new List<WipeTarget>();
        var absent = new List<WipeTarget>();
        var errors = new List<string>();
        long freed = 0;
        foreach (var target in plan.Targets)
        {
            if (!target.Exists || !Directory.Exists(target.Path))
            {
                absent.Add(target);
                continue;
            }
            try
            {
                Directory.Delete(target.Path, recursive: true);
                deleted.Add(target);
                freed += target.SizeBytes;
            }
            catch (Exception ex)
            {
                errors.Add($"{target.Path}: {ex.Message}");
            }
        }
        return new WipeResult(deleted, absent, errors, freed);
    }
}
