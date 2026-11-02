using System.Diagnostics;
using System.Text.Json;

namespace TintaStudio.Services;

/// <summary>Satu model yang sedang dimuat LM Studio (dari GET
/// /v1/models — format kompatibel OpenAI: {"data":[{"id": …}]}).</summary>
public sealed record LmStudioModelInfo(string Id);

/// <summary>Keadaan LM Studio di mesin pengguna — tiga keadaan
/// yang dibedakan UI, sejajar pola Ollama.</summary>
public enum LmStudioState
{
    /// <summary>Server menjawab /v1/models: siap dipakai.</summary>
    Running,
    /// <summary>Executable ditemukan di disk tetapi server tidak menjawab.</summary>
    InstalledNotRunning,
    /// <summary>Tidak ada server dan tidak ada executable yang ditemukan.</summary>
    NotInstalled,
}

/// <summary>Hasil probe keadaan LM Studio: keadaan + daftar model
/// yang sedang dimuat (hanya terisi saat Running) + jalur
/// executable bila ditemukan di disk.</summary>
public sealed record LmStudioProbe(
    LmStudioState State, IReadOnlyList<LmStudioModelInfo> Models, string? ExecutablePath);

/// <summary>
/// Jembatan status ke LM Studio lokal untuk kartunya di dialog
/// Pengaturan Key &amp; Model (v0.7.13): probe server, daftar
/// model yang sedang dimuat, deteksi pemasangan, buka aplikasi,
/// dan buka halaman unduh resmi. Berbeda dari Ollama, server LM
/// Studio TIDAK bisa dinyalakan dari luar (diaktifkan dari dalam
/// aplikasinya, tab Developer/Server) dan pemasangannya TIDAK
/// diunduh langsung (tautan installer berubah tiap versi) — dua
/// batas itu dinyatakan apa adanya di UI. Logika parsing murni
/// &amp; statis, teruji lewat stub HttpMessageHandler
/// dalam-proses, mengikuti pola OllamaService.
/// </summary>
public sealed class LmStudioService
{
    /// <summary>Base URL bawaan (sama seperti kolom Base URL kartu).</summary>
    public const string DefaultApiBase = "http://localhost:1234/v1";

    /// <summary>Halaman unduh resmi — dibuka di browser untuk
    /// keadaan belum terpasang (tautan berkas pemasang berubah
    /// tiap versi, jadi tidak diunduh langsung dari aplikasi).</summary>
    public const string DownloadPageUrl = "https://lmstudio.ai/download";

    private readonly HttpClient _http;
    private readonly Func<string?> _findExecutable;

    /// <param name="http">Klien HTTP (tes menyuntik stub lewat handler).</param>
    /// <param name="findExecutable">Pencari executable di disk
    /// (bawaan: PATH + lokasi pasang standar Windows); tes
    /// menyuntik tiruan agar pemetaan keadaan bisa disimulasikan.</param>
    public LmStudioService(HttpClient? http = null, Func<string?>? findExecutable = null)
    {
        _http = http ?? new HttpClient();
        _findExecutable = findExecutable ?? FindExecutableOnDisk;
    }

    // ── Logika murni (teruji) ───────────────────────────────────

    /// <summary>Normalisasi Base URL kartu: kosong → bawaan;
    /// garis miring akhir dibuang. Endpoint status adalah
    /// {base}/models (base bawaan sudah termasuk /v1, sama
    /// seperti yang dipakai jalur terjemahan).</summary>
    public static string NormalizeBase(string? baseUrl)
    {
        var b = (baseUrl ?? "").Trim();
        return (b.Length == 0 ? DefaultApiBase : b).TrimEnd('/');
    }

    /// <summary>Parse isi JSON GET /v1/models menjadi daftar
    /// model yang dimuat. JSON rusak / tanpa field data → daftar
    /// kosong (bukan galat); entri tanpa id dilewati.</summary>
    public static IReadOnlyList<LmStudioModelInfo> ParseModels(string json)
    {
        var result = new List<LmStudioModelInfo>();
        if (string.IsNullOrWhiteSpace(json))
            return result;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var data)
                || data.ValueKind != JsonValueKind.Array)
                return result;
            foreach (var m in data.EnumerateArray())
            {
                if (!m.TryGetProperty("id", out var idEl)
                    || idEl.ValueKind != JsonValueKind.String)
                    continue;
                var id = idEl.GetString();
                if (!string.IsNullOrWhiteSpace(id))
                    result.Add(new LmStudioModelInfo(id));
            }
        }
        catch (JsonException)
        {
            return new List<LmStudioModelInfo>();
        }
        return result;
    }

    /// <summary>Pemetaan keadaan dari dua fakta probe: apakah
    /// /v1/models menjawab, dan apakah executable ditemukan.</summary>
    public static LmStudioState MapState(bool serverResponded, bool executableFound)
    {
        if (serverResponded) return LmStudioState.Running;
        return executableFound
            ? LmStudioState.InstalledNotRunning
            : LmStudioState.NotInstalled;
    }

    // ── Probe ───────────────────────────────────────────────────

    /// <summary>Probe lengkap: panggil {base}/models (timeout
    /// ±3 detik; base dari kolom Base URL kartu agar suntingan
    /// pengguna dihormati) dan cari executable di disk. Kegagalan
    /// jaringan apa pun = server tidak menjawab (bukan
    /// pengecualian), kecuali pembatalan oleh pemanggil.</summary>
    public async Task<LmStudioProbe> ProbeAsync(
        string? baseUrl, CancellationToken ct = default)
    {
        IReadOnlyList<LmStudioModelInfo>? models = null;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            using var resp = await _http.GetAsync(
                NormalizeBase(baseUrl) + "/models", timeout.Token).ConfigureAwait(false);
            if (resp.IsSuccessStatusCode)
            {
                var json = await resp.Content.ReadAsStringAsync(timeout.Token)
                    .ConfigureAwait(false);
                models = ParseModels(json);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            models = null; // server tidak menjawab — bedakan lewat executable
        }
        var exe = SafeFindExecutable();
        var state = MapState(models is not null, exe is not null);
        return new LmStudioProbe(state, models ?? Array.Empty<LmStudioModelInfo>(), exe);
    }

    // ── Proses lokal ────────────────────────────────────────────

    private string? SafeFindExecutable()
    {
        try { return _findExecutable(); }
        catch { return null; }
    }

    /// <summary>Cari executable LM Studio: PATH lebih dulu, lalu
    /// lokasi pasang per-pengguna yang lazim di Windows
    /// (%LOCALAPPDATA%\Programs\LM Studio dan varian Program
    /// Files). Mengembalikan null bila tidak ditemukan.</summary>
    public static string? FindExecutableOnDisk()
    {
        string exeName = OperatingSystem.IsWindows() ? "LM Studio.exe" : "lm-studio";
        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathVar.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            try
            {
                var candidate = Path.Combine(dir.Trim(), exeName);
                if (File.Exists(candidate)) return candidate;
            }
            catch { /* entri PATH rusak — lewati */ }
        }
        if (OperatingSystem.IsWindows())
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var progFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            foreach (var candidate in new[]
            {
                Path.Combine(local, "Programs", "LM Studio", "LM Studio.exe"),
                Path.Combine(progFiles, "LM Studio", "LM Studio.exe"),
                Path.Combine(local, "Programs", "LM-Studio", "LM Studio.exe"),
            })
            {
                if (File.Exists(candidate)) return candidate;
            }
        }
        return null;
    }

    /// <summary>Buka aplikasi LM Studio (jendela GUI). Server-nya
    /// sendiri harus dinyalakan pengguna dari dalam aplikasi —
    /// tidak ada cara resmi menyalakannya dari luar.</summary>
    public static void StartLmStudio(string executablePath)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = true,
        });
    }

    /// <summary>Buka halaman unduh resmi LM Studio di browser.</summary>
    public static void OpenDownloadPage()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = DownloadPageUrl,
            UseShellExecute = true,
        });
    }
}
