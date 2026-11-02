using System.Text.Json;
using System.Text.Json.Serialization;

namespace TintaStudio.Services;

/// <summary>Isi berkas penanda lokasi (location.json): versi format
/// + akar data baru yang dipilih pengguna. Akar adalah SATU folder
/// basis; di bawahnya aplikasi memakai subfolder "config" (data
/// aplikasi) dan "cache" (cache model) — cermin struktur bawaan
/// ~/.config/tinta-studio dan ~/.cache/tinta-studio.</summary>
public sealed class LocationMarker
{
    public int Version { get; set; } = 1;
    public string DataRoot { get; set; } = "";
}

/// <summary>
/// Resolver lokasi data berbasis penanda (v0.7.13). Berkas penanda
/// kecil <c>location.json</c> (± beberapa ratus byte) tinggal di
/// lokasi BAWAAN (DefaultConfigDir) dan berisi akar data baru bila
/// pengguna memindahkan data &amp; model ke folder/disk lain. Tanpa
/// penanda — atau penanda rusak/tidak sah — semua resolver jatuh ke
/// perilaku bawaan persis seperti sebelum fitur ini ada. Penanda
/// dibaca ulang setiap kali resolver ditanya (berkas kecil, lokal):
/// tidak ada cache yang bisa basi di tengah sesi, dan penghapusan
/// penanda oleh Hapus Bersih langsung berlaku.
///
/// Urutan mendahului (paling kuat lebih dulu): override env
/// pengembang TINTA_CONFIG_DIR / TINTA_MODEL_DIR (di AppConfig /
/// ModelStore, tidak berubah) → penanda lokasi → bawaan profil.
/// </summary>
public static class DataLocation
{
    /// <summary>Nama berkas penanda di lokasi bawaan.</summary>
    public const string MarkerFileName = "location.json";

    /// <summary>Subfolder data aplikasi di bawah akar pindahan.</summary>
    public const string ConfigSubdirName = "config";

    /// <summary>Subfolder cache model di bawah akar pindahan.</summary>
    public const string CacheSubdirName = "cache";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
    };

    private static string UserProfile =>
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>Folder data BAWAAN: env TINTA_CONFIG_DIR atau
    /// ~/.config/tinta-studio — persis logika AppConfig sebelum
    /// v0.7.13. Di sinilah penanda tinggal.</summary>
    public static string DefaultConfigDir
    {
        get
        {
            var env = Environment.GetEnvironmentVariable("TINTA_CONFIG_DIR");
            return !string.IsNullOrWhiteSpace(env)
                ? env
                : Path.Combine(UserProfile, ".config", "tinta-studio");
        }
    }

    /// <summary>Folder cache BAWAAN: ~/.cache/tinta-studio —
    /// persis logika ModelStore sebelum v0.7.13.</summary>
    public static string DefaultCacheRootDir =>
        Path.Combine(UserProfile, ".cache", "tinta-studio");

    /// <summary>Jalur berkas penanda (di lokasi bawaan).</summary>
    public static string MarkerPath => Path.Combine(DefaultConfigDir, MarkerFileName);

    // ── Baca / tulis penanda ────────────────────────────────────

    /// <summary>Baca penanda dari jalur eksplisit. Mengembalikan
    /// null bila berkas tidak ada, JSON rusak, akar kosong, atau
    /// akar bukan jalur mutlak — pemanggil jatuh ke bawaan. Tidak
    /// pernah melempar.</summary>
    public static LocationMarker? ReadMarker(string markerPath)
    {
        try
        {
            if (!File.Exists(markerPath))
                return null;
            var marker = JsonSerializer.Deserialize<LocationMarker>(
                File.ReadAllText(markerPath), JsonOptions);
            if (marker is null || marker.Version < 1)
                return null;
            var root = (marker.DataRoot ?? "").Trim();
            if (root.Length == 0 || !Path.IsPathFullyQualified(root))
                return null;
            marker.DataRoot = Path.GetFullPath(root);
            return marker;
        }
        catch
        {
            return null; // penanda rusak → bawaan, bukan galat
        }
    }

    /// <summary>Penanda yang berlaku saat ini (null = bawaan).</summary>
    public static LocationMarker? CurrentMarker => ReadMarker(MarkerPath);

    /// <summary>Apakah data sedang menunjuk lokasi pindahan.</summary>
    public static bool IsRelocated => CurrentMarker is not null;

    /// <summary>Tulis penanda ke lokasi bawaan (folder dibuat bila
    /// perlu). Dipanggil HANYA sesudah pemindahan berhasil — urutan
    /// ini yang membuat kegagalan pindah tidak pernah meninggalkan
    /// resolver menunjuk folder kosong.</summary>
    public static void WriteMarker(string dataRoot) => WriteMarker(MarkerPath, dataRoot);

    /// <summary>Tulis penanda ke jalur eksplisit (dipakai tes).</summary>
    public static void WriteMarker(string markerPath, string dataRoot)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(markerPath));
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        var marker = new LocationMarker
        {
            Version = 1,
            DataRoot = Path.GetFullPath(dataRoot),
        };
        File.WriteAllText(markerPath, JsonSerializer.Serialize(marker, JsonOptions));
    }

    /// <summary>Hapus penanda di lokasi bawaan (kembali ke bawaan).</summary>
    public static void DeleteMarker() => DeleteMarker(MarkerPath);

    /// <summary>Hapus penanda di jalur eksplisit (dipakai tes);
    /// tidak adanya berkas bukan galat.</summary>
    public static void DeleteMarker(string markerPath)
    {
        try { File.Delete(markerPath); }
        catch { /* penanda tak terhapus — resolver tetap membaca yang lama */ }
    }

    // ── Resolusi murni (teruji) ─────────────────────────────────

    /// <summary>Folder data efektif dari penanda + bawaan: dengan
    /// penanda → &lt;akar&gt;/config; tanpa → bawaan apa adanya.</summary>
    public static string ResolveConfigDir(LocationMarker? marker, string defaultConfigDir)
        => marker is null
            ? defaultConfigDir
            : Path.Combine(marker.DataRoot, ConfigSubdirName);

    /// <summary>Folder cache efektif dari penanda + bawaan:
    /// dengan penanda → &lt;akar&gt;/cache; tanpa → bawaan.</summary>
    public static string ResolveCacheRootDir(LocationMarker? marker, string defaultCacheRootDir)
        => marker is null
            ? defaultCacheRootDir
            : Path.Combine(marker.DataRoot, CacheSubdirName);

    /// <summary>Folder data yang dipakai aplikasi saat ini
    /// (penanda diterapkan di atas bawaan).</summary>
    public static string EffectiveConfigDir =>
        ResolveConfigDir(CurrentMarker, DefaultConfigDir);

    /// <summary>Folder cache yang dipakai aplikasi saat ini
    /// (penanda diterapkan di atas bawaan).</summary>
    public static string EffectiveCacheRootDir =>
        ResolveCacheRootDir(CurrentMarker, DefaultCacheRootDir);

    /// <summary>Pasangan folder (data, cache) untuk satu akar
    /// pindahan — dipakai rencana pindah &amp; UI.</summary>
    public static (string ConfigDir, string CacheRootDir) DirsForRoot(string dataRoot)
        => (Path.Combine(dataRoot, ConfigSubdirName),
            Path.Combine(dataRoot, CacheSubdirName));

    // ── Perbandingan jalur ──────────────────────────────────────

    /// <summary>Perbandingan jalur peka-huruf mengikuti platform:
    /// Windows tidak peka huruf, selain itu peka.</summary>
    public static StringComparison PathComparison =>
        OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    /// <summary>Dua jalur menunjuk tempat yang sama (sesudah
    /// dinormalkan ke jalur penuh).</summary>
    public static bool PathsEqual(string a, string b)
    {
        try
        {
            return string.Equals(
                Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                PathComparison);
        }
        catch
        {
            return string.Equals(a, b, PathComparison);
        }
    }

    /// <summary>Apakah <paramref name="path"/> berada DI DALAM
    /// <paramref name="parent"/> (ketat — kesamaan bukan "di dalam";
    /// pakai PathsEqual untuk itu).</summary>
    public static bool IsInside(string path, string parent)
    {
        try
        {
            var p = Path.GetFullPath(path);
            var par = Path.GetFullPath(parent)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            return p.StartsWith(par, PathComparison);
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>
/// Penawaran lokasi sekali-saja saat jalan pertama (v0.7.13):
/// pengguna baru ditawari memilih folder data &amp; model di awal —
/// seperti memilih folder saat instal — alih-alih diam-diam memakai
/// profil di disk sistem. Ditampilkan HANYA bila: belum ada penanda
/// lokasi, belum pernah ditawari (flag LocationOfferShown di
/// pengaturan), dan data bawaan masih nyaris kosong (pengguna
/// lama yang naik versi tidak diganggu). Logika keputusan murni;
/// pengumpulan keadaan nyata terpisah dan tipis.
/// </summary>
public static class FirstRunOffer
{
    /// <summary>Batas "nyaris kosong": pengaturan bawaan segar
    /// (settings.json ±1–2 KB) jauh di bawah ini; data pengguna
    /// lama (proyek/model) jauh di atasnya.</summary>
    public const long NearlyEmptyThresholdBytes = 64 * 1024;

    /// <summary>Keputusan murni: tawarkan hanya bila ketiganya
    /// terpenuhi — tanpa penanda, belum pernah ditawari, dan total
    /// data bawaan tidak melebihi ambang nyaris-kosong.</summary>
    public static bool ShouldOffer(bool hasMarker, bool alreadyOffered, long defaultDataBytes)
        => !hasMarker && !alreadyOffered
           && defaultDataBytes <= NearlyEmptyThresholdBytes;

    /// <summary>Total data di lokasi bawaan (data + cache),
    /// dihitung live; folder yang belum ada menyumbang 0.</summary>
    public static long DefaultDataBytes()
    {
        long total = 0;
        if (Directory.Exists(DataLocation.DefaultConfigDir))
            total += DataWipe.DirectorySize(DataLocation.DefaultConfigDir);
        if (Directory.Exists(DataLocation.DefaultCacheRootDir))
            total += DataWipe.DirectorySize(DataLocation.DefaultCacheRootDir);
        return total;
    }

    /// <summary>Apakah penawaran harus tampil pada jalan ini
    /// (keadaan nyata: penanda + flag pengaturan + ukuran live).</summary>
    public static bool ShouldOfferNow()
    {
        if (DataLocation.CurrentMarker is not null)
            return false;
        bool offered;
        try { offered = AppConfig.LoadSettings().EffectiveLocationOfferShown; }
        catch { offered = false; }
        if (offered)
            return false;
        return ShouldOffer(false, false, DefaultDataBytes());
    }

    /// <summary>Catat bahwa penawaran sudah ditampilkan (flag di
    /// pengaturan lokasi bawaan — penanda belum ditulis saat ini,
    /// jadi AppConfig masih menunjuk bawaan). Dipanggil untuk KEDUA
    /// pilihan pengguna: penawaran tidak pernah tampil dua kali.</summary>
    public static void MarkOffered()
    {
        var settings = AppConfig.LoadSettings();
        settings.LocationOfferShown = true;
        AppConfig.SaveSettings(settings);
    }
}
