using System.Text.Json;
using System.Text.Json.Serialization;

namespace TintaStudio.Services;

/// <summary>Pengaturan satu penyedia terjemahan: alamat dasar + model.</summary>
public sealed class ProviderSettings
{
    public string BaseUrl { get; set; } = "";
    public string Model { get; set; } = "";
}

/// <summary>
/// Pengaturan aplikasi (Tahap 4): penyedia terjemahan terpilih, bahasa
/// tujuan, dan alamat/model per penyedia. Disimpan di
/// ~/.config/tinta-studio/settings.json (override basis lewat env
/// TINTA_CONFIG_DIR, sama seperti ProjectStore).
/// API KEY TIDAK disimpan di sini — lihat <see cref="AppSecrets"/>.
/// </summary>
public sealed class AppSettings
{
    /// <summary>Id penyedia terpilih (lihat TranslateService.Providers).</summary>
    public string Provider { get; set; } = TranslateService.GeminiId;

    /// <summary>Kode bahasa tujuan terjemahan: "id" | "en".</summary>
    public string TargetLanguage { get; set; } = "id";

    /// <summary>Jeda antar permintaan (detik) selama jalan bab &amp;
    /// sebelum coba-ulang blok hilang — meredam limit penyedia.
    /// Bawaan 2; 0 = tanpa jeda.</summary>
    public int RequestDelaySeconds { get; set; } = 2;

    /// <summary>Pilihan "Garis tepi" terakhir di halaman Bersihkan
    /// (v0.7.6): true = jaga garis, false = hapus tuntas. Nullable
    /// supaya nilai false benar-benar tertulis (opsi serializer
    /// WhenWritingDefault akan membuang bool false); null = belum
    /// pernah dipilih → bawaan true lewat
    /// <see cref="EffectiveCleanLineProtect"/>.</summary>
    public bool? CleanLineProtect { get; set; }

    /// <summary>Pilihan garis tepi efektif (bawaan true).</summary>
    public bool EffectiveCleanLineProtect => CleanLineProtect ?? true;

    /// <summary>Apakah penawaran memilih lokasi data saat jalan
    /// pertama (v0.7.13) sudah pernah ditampilkan? Nullable dengan
    /// alasan yang sama seperti CleanLineProtect (false akan
    /// dibuang serializer WhenWritingDefault); null = belum pernah
    /// ditawari. Flag ini HANYA mematikan penawaran — lokasi tetap
    /// bisa diganti kapan pun dari seksi Lokasi Data &amp; Model.</summary>
    public bool? LocationOfferShown { get; set; }

    /// <summary>Status penawaran lokasi efektif (bawaan false).</summary>
    public bool EffectiveLocationOfferShown => LocationOfferShown ?? false;

    /// <summary>Pilihan bahasa terakhir di halaman Deteksi &amp;
    /// OCR (v0.7.17): kode bahasa ("auto"|"ja"|"ko"|"ch"|"en").
    /// String (bukan nullable-bool) supaya nilai apa pun tertulis
    /// apa adanya; null/kode tak dikenal = pengguna belum pernah
    /// memilih → bawaan Otomatis lewat
    /// <see cref="EffectiveDetectLanguage"/>. Sebelumnya bawaan
    /// selalu Jepang — bab Inggris hasil deteksi ulang ikut
    /// tertanda "ja".</summary>
    public string? DetectLanguage { get; set; }

    /// <summary>Bahasa deteksi efektif: pilihan tersimpan yang
    /// sah, selain itu Otomatis.</summary>
    public OcrLanguage EffectiveDetectLanguage => DetectLanguage switch
    {
        "ja" => OcrLanguage.Japanese,
        "ko" => OcrLanguage.Korean,
        "ch" => OcrLanguage.Chinese,
        "en" => OcrLanguage.English,
        _ => OcrLanguage.Auto,
    };

    public Dictionary<string, ProviderSettings> Providers { get; set; } = new();

    /// <summary>Pengaturan penyedia ini; dibuat dengan nilai bawaan
    /// penyedia bila belum ada.</summary>
    public ProviderSettings For(string providerId)
    {
        if (!Providers.TryGetValue(providerId, out var ps))
        {
            var info = TranslateService.ProviderById(providerId);
            ps = new ProviderSettings
            {
                BaseUrl = info.DefaultBaseUrl,
                Model = info.DefaultModel,
            };
            Providers[providerId] = ps;
        }
        return ps;
    }

    /// <summary>Isi nilai bawaan untuk semua penyedia yang dikenal.</summary>
    public void EnsureDefaults()
    {
        foreach (var p in TranslateService.Providers)
            For(p.Id);
        MigrateRetiredModels();
        if (TranslateService.Providers.All(p => p.Id != Provider))
            Provider = TranslateService.GeminiId;
        if (TargetLanguage is not ("id" or "en"))
            TargetLanguage = "id";
        RequestDelaySeconds = Math.Clamp(RequestDelaySeconds, 0, 600);
    }

    /// <summary>
    /// Migrasi model mati (v0.7.16): entri penyedia yang model
    /// tersimpannya PERSIS ID yang sudah dimatikan penyedia
    /// (lihat <see cref="TranslateService.RetiredModels"/>) diganti
    /// senyap ke pengganti resminya. Berjalan dari EnsureDefaults
    /// SESUDAH bawaan terisi — bawaan baru tidak pernah berupa ID
    /// mati, jadi yang berubah hanya entri warisan berkas lama.
    /// Model kustom (apa pun selain ID mati persis) tidak pernah
    /// disentuh; BaseUrl tidak pernah diubah. Idempoten: sesudah
    /// diganti, model tidak lagi cocok dengan ID mati, dan hasil
    /// migrasi bertahan lewat SaveSettings berikutnya. Mengembalikan
    /// jumlah entri yang dimigrasikan.
    /// </summary>
    public int MigrateRetiredModels()
    {
        int changed = 0;
        foreach (var r in TranslateService.RetiredModels)
        {
            if (Providers.TryGetValue(r.ProviderId, out var ps)
                && TranslateService.ReplacementForRetired(r.ProviderId, ps.Model) is { } repl
                && ps.Model != repl)
            {
                ps.Model = repl;
                changed++;
            }
        }
        return changed;
    }
}

/// <summary>Muat/simpan settings.json + lokasi konfigurasi aplikasi.</summary>
public static class AppConfig
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
    };

    /// <summary>Direktori konfigurasi: env TINTA_CONFIG_DIR bila
    /// diatur (override pengembang — mengabaikan penanda);
    /// selain itu resolver penanda lokasi (DataLocation):
    /// &lt;akar pindahan&gt;/config bila pengguna memindahkan
    /// data, atau ~/.config/tinta-studio bawaan.</summary>
    public static string ConfigDir
    {
        get
        {
            var env = Environment.GetEnvironmentVariable("TINTA_CONFIG_DIR");
            return !string.IsNullOrWhiteSpace(env)
                ? env
                : DataLocation.EffectiveConfigDir;
        }
    }

    public static string SettingsPath => Path.Combine(ConfigDir, "settings.json");
    public static string KeysPath => Path.Combine(ConfigDir, "keys.json");

    public static AppSettings LoadSettings()
    {
        AppSettings settings;
        try
        {
            settings = File.Exists(SettingsPath)
                ? JsonSerializer.Deserialize<AppSettings>(
                      File.ReadAllText(SettingsPath), JsonOptions) ?? new AppSettings()
                : new AppSettings();
        }
        catch
        {
            settings = new AppSettings();
        }
        settings.EnsureDefaults();
        return settings;
    }

    public static void SaveSettings(AppSettings settings)
    {
        Directory.CreateDirectory(ConfigDir);
        settings.EnsureDefaults();
        File.WriteAllText(SettingsPath,
            JsonSerializer.Serialize(settings, JsonOptions));
    }

    internal static JsonSerializerOptions Options => JsonOptions;
}

/// <summary>
/// Keadaan key satu penyedia di keys.json: daftar key (urut =
/// prioritas), cap waktu cooldown per indeks key (akibat HTTP 429 /
/// kuota), dan indeks key yang ditandai buruk (401/403 tidak sah).
/// Indeks bersifat posisi dalam daftar saat ini; menyunting daftar
/// (tambah/hapus) mengosongkan cooldown &amp; tanda buruk.
/// </summary>
public sealed class ProviderKeyState
{
    public List<string> Keys { get; set; } = new();
    public Dictionary<string, DateTime> CooldownUntilUtc { get; set; } = new();
    public List<int> BadIndexes { get; set; } = new();
    /// <summary>Indeks key yang terakhir berhasil dipakai (0-based).</summary>
    public int LastGoodIndex { get; set; }
}

/// <summary>Berkas keys.json utuh.</summary>
public sealed class KeyFileState
{
    public int Version { get; set; } = 1;
    public Dictionary<string, ProviderKeyState> Providers { get; set; } = new();
}

/// <summary>
/// Penyimpanan API key BYOK: HANYA di ~/.config/tinta-studio/keys.json
/// (izin 0600 di sistem non-Windows), terpisah dari settings.json dan
/// TIDAK PERNAH masuk proyek .tinta.json, git, log, atau keluaran CLI.
/// Mendukung BANYAK key per penyedia dengan rotasi otomatis
/// (lihat <see cref="KeyPool"/>).
/// </summary>
public static class AppSecrets
{
    private static readonly object Gate = new();
    private static KeyFileState? _state;
    private static string? _stateDir;

    private static KeyFileState State
    {
        get
        {
            var dir = AppConfig.ConfigDir;
            if (_state is null || _stateDir != dir)
            {
                _state = LoadFromDisk();
                _stateDir = dir;
            }
            return _state;
        }
    }

    private static KeyFileState LoadFromDisk()
    {
        try
        {
            if (File.Exists(AppConfig.KeysPath))
            {
                var s = JsonSerializer.Deserialize<KeyFileState>(
                    File.ReadAllText(AppConfig.KeysPath), AppConfig.Options);
                if (s is not null)
                {
                    s.Providers ??= new Dictionary<string, ProviderKeyState>();
                    return s;
                }
            }
        }
        catch
        {
            // berkas rusak → mulai kosong (key bisa diisi ulang pengguna)
        }
        return new KeyFileState();
    }

    /// <summary>Tulis keys.json + paksa izin 0600 (non-Windows).
    /// Pemanggil memegang Gate.</summary>
    private static void PersistLocked()
    {
        Directory.CreateDirectory(AppConfig.ConfigDir);
        File.WriteAllText(AppConfig.KeysPath,
            JsonSerializer.Serialize(State, AppConfig.Options));
        if (!OperatingSystem.IsWindows())
        {
            try
            {
                File.SetUnixFileMode(AppConfig.KeysPath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            catch
            {
                // sistem berkas tanpa mode Unix — abaikan
            }
        }
    }

    internal static ProviderKeyState EntryFor(string providerId)
    {
        var state = State;
        if (!state.Providers.TryGetValue(providerId, out var entry))
        {
            entry = new ProviderKeyState();
            state.Providers[providerId] = entry;
        }
        entry.Keys ??= new List<string>();
        entry.CooldownUntilUtc ??= new Dictionary<string, DateTime>();
        entry.BadIndexes ??= new List<int>();
        return entry;
    }

    /// <summary>Daftar key penyedia (salinan; urut = prioritas).</summary>
    public static IReadOnlyList<string> GetKeys(string providerId)
    {
        lock (Gate)
            return EntryFor(providerId).Keys.ToList();
    }

    public static int KeyCount(string providerId) => GetKeys(providerId).Count;

    /// <summary>Ganti seluruh daftar key penyedia (dibersihkan: trim,
    /// kosong dibuang, duplikat dibuang). Cooldown &amp; tanda buruk
    /// di-reset karena indeks berubah makna.</summary>
    public static void SetKeys(string providerId, IEnumerable<string> keys)
    {
        lock (Gate)
        {
            var entry = EntryFor(providerId);
            var clean = new List<string>();
            foreach (var k in keys)
            {
                var v = (k ?? "").Trim();
                if (v.Length > 0 && !clean.Contains(v))
                    clean.Add(v);
            }
            entry.Keys = clean;
            entry.CooldownUntilUtc.Clear();
            entry.BadIndexes.Clear();
            entry.LastGoodIndex = 0;
            PersistLocked();
        }
    }

    internal static void Persist()
    {
        lock (Gate)
            PersistLocked();
    }

    internal static object SyncRoot => Gate;

    /// <summary>Ringkasan status key untuk UI — HANYA angka urut,
    /// tidak pernah nilai key: (jumlah key, nomor key aktif 1-based,
    /// jumlah key sedang cooldown, jumlah key buruk).</summary>
    public static (int Count, int ActiveNumber, int Cooling, int Bad) GetStatus(
        string providerId)
    {
        lock (Gate)
        {
            var entry = EntryFor(providerId);
            var pool = new KeyPool(providerId);
            return (entry.Keys.Count, pool.ActiveNumber,
                    pool.CoolingCount, pool.BadCount);
        }
    }

    /// <summary>Buang cache dalam-memori (dipakai tes agar env
    /// TINTA_CONFIG_DIR yang baru terbaca ulang dari disk).</summary>
    internal static void ResetCache()
    {
        lock (Gate)
        {
            _state = null;
            _stateDir = null;
        }
    }
}

/// <summary>
/// Kumpulan key satu penyedia dengan rotasi otomatis: kandidat
/// dicoba mulai dari key yang terakhir berhasil, lalu berurutan
/// (membungkus). Key yang membalas 429/kuota masuk cooldown
/// (<see cref="CooldownDuration"/>) dengan cap waktu yang DISIMPAN
/// di keys.json (bertahan antar-jalan); key 401/403 ditandai buruk
/// dan dilewati. Keadaan hidup di <see cref="AppSecrets"/> — pool
/// ini hanya tampilan operasi atas keadaan itu.
/// </summary>
public sealed class KeyPool
{
    /// <summary>Lama cooldown sebuah key sesudah kena limit.</summary>
    public static readonly TimeSpan CooldownDuration = TimeSpan.FromSeconds(60);

    private readonly string _providerId;

    public KeyPool(string providerId) => _providerId = providerId;

    private ProviderKeyState Entry => AppSecrets.EntryFor(_providerId);

    public int Count => Entry.Keys.Count;

    private bool IsCooling(int index) =>
        Entry.CooldownUntilUtc.TryGetValue(index.ToString(), out var until)
        && until > DateTime.UtcNow;

    private bool IsBad(int index) => Entry.BadIndexes.Contains(index);

    public bool IsAvailable(int index) =>
        index >= 0 && index < Count && !IsCooling(index) && !IsBad(index);

    public int CoolingCount => Enumerable.Range(0, Count).Count(IsCooling);
    public int BadCount => Enumerable.Range(0, Count).Count(IsBad);

    /// <summary>Nomor key aktif (1-based) untuk ditampilkan: key yang
    /// terakhir berhasil bila masih tersedia, selain itu kandidat
    /// pertama yang tersedia, selain itu 1 (atau 0 bila tak ada key).</summary>
    public int ActiveNumber
    {
        get
        {
            if (Count == 0)
                return 0;
            int start = Math.Clamp(Entry.LastGoodIndex, 0, Count - 1);
            for (int step = 0; step < Count; step++)
            {
                int idx = (start + step) % Count;
                if (IsAvailable(idx))
                    return idx + 1;
            }
            return start + 1;
        }
    }

    /// <summary>
    /// Ambil kandidat berikutnya yang tersedia &amp; belum dicoba
    /// dalam permintaan ini. Urutan: mulai dari key terakhir
    /// berhasil, berurutan membungkus daftar.
    /// </summary>
    public bool TryNext(ISet<int> tried, out int index, out string key)
    {
        lock (AppSecrets.SyncRoot)
        {
            int start = Count == 0 ? 0
                : Math.Clamp(Entry.LastGoodIndex, 0, Count - 1);
            for (int step = 0; step < Count; step++)
            {
                int idx = (start + step) % Count;
                if (tried.Contains(idx) || !IsAvailable(idx))
                    continue;
                index = idx;
                key = Entry.Keys[idx];
                return true;
            }
        }
        index = -1;
        key = "";
        return false;
    }

    /// <summary>Tandai key kena limit: cooldown + simpan cap waktu.</summary>
    public void MarkCooldown(int index) => MarkCooldown(index, CooldownDuration);

    /// <summary>Tandai key kena limit dengan durasi tertentu
    /// (mis. dari header Retry-After penyedia).</summary>
    public void MarkCooldown(int index, TimeSpan duration)
    {
        lock (AppSecrets.SyncRoot)
        {
            Entry.CooldownUntilUtc[index.ToString()] =
                DateTime.UtcNow.Add(duration);
            AppSecrets.Persist();
        }
    }

    /// <summary>
    /// Key yang paling awal pulih dari cooldown (yang tidak ditandai
    /// buruk — key buruk tidak akan pulih dengan menunggu).
    /// </summary>
    public bool TryGetEarliestRecovery(out DateTime untilUtc, out int index)
    {
        lock (AppSecrets.SyncRoot)
        {
            untilUtc = DateTime.MaxValue;
            index = -1;
            for (int i = 0; i < Count; i++)
            {
                if (IsBad(i))
                    continue;
                if (Entry.CooldownUntilUtc.TryGetValue(i.ToString(), out var until)
                    && until > DateTime.UtcNow && until < untilUtc)
                {
                    untilUtc = until;
                    index = i;
                }
            }
            return index >= 0;
        }
    }

    /// <summary>Tandai key tidak sah (401/403): dilewati seterusnya
    /// sampai daftar key disunting pengguna.</summary>
    public void MarkBad(int index)
    {
        lock (AppSecrets.SyncRoot)
        {
            if (!Entry.BadIndexes.Contains(index))
                Entry.BadIndexes.Add(index);
            AppSecrets.Persist();
        }
    }

    /// <summary>Catat key yang berhasil dipakai (jadi titik awal
    /// rotasi berikutnya) + simpan.</summary>
    public void MarkSuccess(int index)
    {
        lock (AppSecrets.SyncRoot)
        {
            if (Entry.LastGoodIndex != index)
            {
                Entry.LastGoodIndex = index;
                AppSecrets.Persist();
            }
        }
    }
}
