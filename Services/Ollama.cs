using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

namespace TintaStudio.Services;

/// <summary>Satu model Ollama terpasang (dari GET /api/tags).</summary>
public sealed record OllamaModelInfo(string Name, long SizeBytes);

/// <summary>Satu baris kurasi model untuk panel Instal Instan:
/// nama persis seperti yang dipakai API Ollama, label ukuran
/// perkiraan unduhan, dan catatan pemakaian.</summary>
public sealed record CuratedOllamaModel(string Name, string SizeLabel, string Note);

/// <summary>Keadaan Ollama di mesin pengguna — tiga keadaan yang
/// dibedakan UI Instal Instan.</summary>
public enum OllamaState
{
    /// <summary>Server menjawab /api/tags: siap dipakai.</summary>
    Running,
    /// <summary>Executable ditemukan di disk tetapi server tidak menjawab.</summary>
    InstalledNotRunning,
    /// <summary>Tidak ada server dan tidak ada executable yang ditemukan.</summary>
    NotInstalled,
}

/// <summary>Hasil probe keadaan Ollama: keadaan + daftar model
/// terpasang (hanya terisi saat Running) + jalur executable bila
/// ditemukan di disk.</summary>
public sealed record OllamaProbe(
    OllamaState State, IReadOnlyList<OllamaModelInfo> Models, string? ExecutablePath);

/// <summary>Satu potong progres unduh model (satu baris JSONL dari
/// POST /api/pull): status penyedia apa adanya, persen (null bila
/// baris tidak membawa completed/total), dan pesan galat penyedia
/// bila ada (baris {"error": …} menutup aliran dengan kegagalan).</summary>
public sealed record PullProgress(string Status, double? Percent, string? Error)
{
    /// <summary>Unduhan selesai (status akhir "success" dari Ollama).</summary>
    public bool IsSuccess => string.Equals(Status, "success", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Jembatan ke Ollama lokal untuk panel Instal Instan di dialog
/// Pengaturan Key &amp; Model (v0.7.11): probe status, daftar model
/// terpasang, unduh/hapus model dengan progres, pasang &amp; jalankan
/// Ollama. Alamat API adalah akar http://localhost:11434 (BUKAN
/// /v1 yang dipakai jalur terjemahan OpenAI-compatible). Semua
/// logika parsing murni &amp; statis agar teruji lewat stub
/// HttpMessageHandler dalam-proses; operasi proses (unduh
/// installer, Process.Start) terpisah dan tipis.
/// </summary>
public sealed class OllamaService
{
    /// <summary>Akar API manajemen Ollama (tanpa /v1).</summary>
    public const string ApiRoot = "http://localhost:11434";

    /// <summary>URL unduhan installer resmi Windows.</summary>
    public const string InstallerUrl = "https://ollama.com/download/OllamaSetup.exe";

    /// <summary>Daftar model kurasi untuk panel Instal Instan
    /// (nama persis Ollama; ukuran = perkiraan unduhan kuantisasi
    /// bawaan, bisa sedikit berbeda antar rilis model).</summary>
    public static IReadOnlyList<CuratedOllamaModel> CuratedModels { get; } = new[]
    {
        new CuratedOllamaModel("qwen2.5:7b", "±4,7 GB",
            "Saran utama — JA/KO/ZH→ID, cepat & pintar"),
        new CuratedOllamaModel("qwen2.5:14b", "±9,0 GB",
            "Lebih akurat & natural — PC RAM 16–32 GB"),
        new CuratedOllamaModel("qwen2.5:3b", "±1,9 GB",
            "Paling ringan — PC pas-pasan / coba-coba"),
        new CuratedOllamaModel("llama3.1:8b", "±4,7 GB",
            "Umum & aman — spesialis Inggris (Meta)"),
        new CuratedOllamaModel("gemma3:12b", "±8,1 GB",
            "Multibahasa dari Google — alternatif Qwen"),
    };

    private readonly HttpClient _http;
    private readonly string _apiRoot;
    private readonly Func<string?> _findExecutable;

    /// <param name="http">Klien HTTP (tes menyuntik stub lewat
    /// handler; produksi memakai klien baru bertimeout panjang untuk
    /// unduhan model).</param>
    /// <param name="apiRoot">Akar API (bawaan localhost:11434).</param>
    /// <param name="findExecutable">Pencari executable di disk
    /// (bawaan: PATH + lokasi pasang standar Windows); tes menyuntik
    /// tiruan agar pemetaan keadaan bisa disimulasikan.</param>
    public OllamaService(HttpClient? http = null, string? apiRoot = null,
        Func<string?>? findExecutable = null)
    {
        _http = http ?? new HttpClient();
        _apiRoot = (apiRoot ?? ApiRoot).TrimEnd('/');
        _findExecutable = findExecutable ?? FindExecutableOnDisk;
    }

    // ── Parsing murni (teruji) ──────────────────────────────────

    /// <summary>Parse isi JSON GET /api/tags menjadi daftar model.
    /// JSON rusak / tanpa field models → daftar kosong (bukan galat).</summary>
    public static IReadOnlyList<OllamaModelInfo> ParseTags(string json)
    {
        var result = new List<OllamaModelInfo>();
        if (string.IsNullOrWhiteSpace(json))
            return result;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("models", out var models)
                || models.ValueKind != JsonValueKind.Array)
                return result;
            foreach (var m in models.EnumerateArray())
            {
                if (!m.TryGetProperty("name", out var nameEl)
                    || nameEl.ValueKind != JsonValueKind.String)
                    continue;
                var name = nameEl.GetString();
                if (string.IsNullOrWhiteSpace(name))
                    continue;
                long size = m.TryGetProperty("size", out var s)
                    && s.ValueKind == JsonValueKind.Number
                    && s.TryGetInt64(out var v) ? v : 0;
                result.Add(new OllamaModelInfo(name, size));
            }
        }
        catch (JsonException)
        {
            return new List<OllamaModelInfo>();
        }
        return result;
    }

    /// <summary>Parse SATU baris JSONL progres POST /api/pull.
    /// Mengembalikan null untuk baris kosong/bukan JSON (dilewati
    /// pemanggil). Persen = completed/total × 100 bila keduanya ada
    /// dan total &gt; 0.</summary>
    public static PullProgress? ParsePullLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return null;
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            string? error = root.TryGetProperty("error", out var e)
                && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
            string status = root.TryGetProperty("status", out var st)
                && st.ValueKind == JsonValueKind.String
                ? st.GetString() ?? "" : "";
            double? percent = null;
            if (root.TryGetProperty("completed", out var c) && c.ValueKind == JsonValueKind.Number
                && root.TryGetProperty("total", out var t) && t.ValueKind == JsonValueKind.Number
                && c.TryGetDouble(out var cd) && t.TryGetDouble(out var td) && td > 0)
                percent = Math.Clamp(cd / td * 100.0, 0, 100);
            return new PullProgress(status, percent, error);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Pemetaan keadaan dari dua fakta probe: apakah
    /// /api/tags menjawab, dan apakah executable ditemukan di disk.</summary>
    public static OllamaState MapState(bool serverResponded, bool executableFound)
    {
        if (serverResponded) return OllamaState.Running;
        return executableFound ? OllamaState.InstalledNotRunning : OllamaState.NotInstalled;
    }

    /// <summary>Apakah nama model terpasang cocok dengan nama yang
    /// dicari — Ollama menganggap "llama3.1" alias dari
    /// "llama3.1:latest", sedangkan tag eksplisit harus persis.</summary>
    public static bool ModelMatches(string installedName, string wantedName)
    {
        if (string.Equals(installedName, wantedName, StringComparison.OrdinalIgnoreCase))
            return true;
        if (!wantedName.Contains(':')
            && string.Equals(installedName, wantedName + ":latest", StringComparison.OrdinalIgnoreCase))
            return true;
        if (!installedName.Contains(':')
            && string.Equals(installedName + ":latest", wantedName, StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

    // ── Lokasi folder model (v0.7.13) ───────────────────────────

    /// <summary>Folder model bawaan Ollama: ~/.ollama/models.</summary>
    public static string DefaultModelsDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".ollama", "models");

    /// <summary>Resolusi murni lokasi model efektif: variabel
    /// pengguna OLLAMA_MODELS menang, lalu variabel proses, kalau
    /// tidak folder bawaan. (Ollama sendiri membaca OLLAMA_MODELS
    /// saat server dimulai; nilai kosong/spasi dianggap tak ada.)</summary>
    public static string ResolveModelsDir(string? userEnv, string? processEnv)
    {
        if (!string.IsNullOrWhiteSpace(userEnv))
            return userEnv.Trim();
        if (!string.IsNullOrWhiteSpace(processEnv))
            return processEnv.Trim();
        return DefaultModelsDir;
    }

    /// <summary>Lokasi model Ollama yang berlaku di mesin ini.</summary>
    public static string EffectiveModelsDir => ResolveModelsDir(
        Environment.GetEnvironmentVariable("OLLAMA_MODELS", EnvironmentVariableTarget.User),
        Environment.GetEnvironmentVariable("OLLAMA_MODELS", EnvironmentVariableTarget.Process));

    /// <summary>Tetapkan OLLAMA_MODELS lingkup pengguna (Windows:
    /// ditulis ke registri pengguna; berlaku untuk proses Ollama
    /// yang dimulai SESUDAH ini). Keberhasilan DIVERIFIKASI dengan
    /// membaca balik: sistem yang tidak menyimpan variabel
    /// pengguna (mis. non-Windows) membalas false + pesan jujur,
    /// bukan diam-diam mengaku berhasil.</summary>
    public static bool TrySetUserModelsDir(string path, out string? error)
    {
        error = null;
        try
        {
            Environment.SetEnvironmentVariable(
                "OLLAMA_MODELS", path, EnvironmentVariableTarget.User);
            var back = Environment.GetEnvironmentVariable(
                "OLLAMA_MODELS", EnvironmentVariableTarget.User);
            if (back is not null && DataLocation.PathsEqual(back, path))
                return true;
            error = "Sistem ini tidak menyimpan variabel lingkungan " +
                    "pengguna (OLLAMA_MODELS), jadi lokasi model Ollama " +
                    "tidak bisa ditetapkan dari aplikasi di sistem ini.";
            return false;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>Format ukuran byte menjadi label ringkas id-ID
    /// (mis. "4,7 GB") untuk daftar model terpasang.</summary>
    public static string FormatSize(long bytes)
    {
        if (bytes <= 0) return "—";
        double gb = bytes / 1_000_000_000.0;
        if (gb >= 1)
            return gb.ToString("0.0", CultureInfo.GetCultureInfo("id-ID")) + " GB";
        double mb = bytes / 1_000_000.0;
        return mb.ToString("0", CultureInfo.GetCultureInfo("id-ID")) + " MB";
    }

    // ── Probe & operasi API ─────────────────────────────────────

    /// <summary>Probe lengkap: panggil /api/tags (timeout pendek)
    /// dan cari executable di disk; keadaan dipetakan lewat
    /// MapState. Kegagalan jaringan apa pun = server tidak menjawab
    /// (bukan pengecualian).</summary>
    public async Task<OllamaProbe> ProbeAsync(CancellationToken ct = default)
    {
        IReadOnlyList<OllamaModelInfo>? models = null;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            using var resp = await _http.GetAsync(
                _apiRoot + "/api/tags", timeout.Token).ConfigureAwait(false);
            if (resp.IsSuccessStatusCode)
            {
                var json = await resp.Content.ReadAsStringAsync(timeout.Token)
                    .ConfigureAwait(false);
                models = ParseTags(json);
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
        return new OllamaProbe(state, models ?? Array.Empty<OllamaModelInfo>(), exe);
    }

    /// <summary>Unduh satu model (POST /api/pull, aliran JSONL).
    /// Setiap potong progres dilaporkan lewat callback; baris galat
    /// penyedia → InvalidOperationException berpesan manusiawi.</summary>
    public async Task PullModelAsync(string modelName,
        Action<PullProgress>? onProgress = null, CancellationToken ct = default)
    {
        var payload = JsonSerializer.Serialize(new { name = modelName, stream = true });
        using var req = new HttpRequestMessage(HttpMethod.Post, _apiRoot + "/api/pull")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        using var resp = await _http.SendAsync(
            req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        string? line;
        while ((line = await reader.ReadLineAsync(ct).ConfigureAwait(false)) is not null)
        {
            var progress = ParsePullLine(line);
            if (progress is null)
                continue;
            if (progress.Error is { Length: > 0 } err)
                throw new InvalidOperationException($"Ollama menolak unduhan: {err}");
            onProgress?.Invoke(progress);
        }
    }

    /// <summary>Hapus satu model terpasang (DELETE /api/delete).</summary>
    public async Task DeleteModelAsync(string modelName, CancellationToken ct = default)
    {
        var payload = JsonSerializer.Serialize(new { name = modelName });
        using var req = new HttpRequestMessage(HttpMethod.Delete, _apiRoot + "/api/delete")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
    }

    /// <summary>Unduh installer resmi OllamaSetup.exe ke folder
    /// temp (unduhan terpisah dari HttpClient layanan karena
    /// tujuannya internet publik). Progres persen dilaporkan bila
    /// server mengirim Content-Length. Mengembalikan jalur berkas.</summary>
    public async Task<string> DownloadInstallerAsync(
        Action<double?>? onProgress = null, CancellationToken ct = default)
    {
        var dest = Path.Combine(Path.GetTempPath(), "OllamaSetup.exe");
        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        using var resp = await http.GetAsync(
            InstallerUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        long? total = resp.Content.Headers.ContentLength;
        await using var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var dst = new FileStream(dest, FileMode.Create, FileAccess.Write,
            FileShare.None, 81920, useAsync: true);
        var buffer = new byte[81920];
        long done = 0;
        int read;
        while ((read = await src.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            await dst.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            done += read;
            onProgress?.Invoke(total is > 0 ? done * 100.0 / total.Value : null);
        }
        return dest;
    }

    // ── Proses lokal ────────────────────────────────────────────

    private string? SafeFindExecutable()
    {
        try { return _findExecutable(); }
        catch { return null; }
    }

    /// <summary>Cari ollama.exe: PATH lebih dulu, lalu lokasi
    /// pasang standar Windows (%LOCALAPPDATA%\Programs\Ollama dan
    /// %ProgramFiles%\Ollama). Di non-Windows: PATH saja
    /// ("ollama"). Mengembalikan null bila tidak ditemukan.</summary>
    public static string? FindExecutableOnDisk()
    {
        string exeName = OperatingSystem.IsWindows() ? "ollama.exe" : "ollama";
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
                Path.Combine(local, "Programs", "Ollama", "ollama.exe"),
                Path.Combine(progFiles, "Ollama", "ollama.exe"),
            })
            {
                if (File.Exists(candidate)) return candidate;
            }
        }
        return null;
    }

    /// <summary>Jalankan executable Ollama (mode serve) tanpa
    /// jendela. Dipakai tombol "Jalankan Ollama" pada keadaan
    /// terpasang-tapi-mati.</summary>
    public static void StartOllama(string executablePath)
    {
        var psi = new ProcessStartInfo
        {
            FileName = executablePath,
            Arguments = "serve",
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        Process.Start(psi);
    }

    /// <summary>Jalankan installer Ollama yang sudah terunduh
    /// (wizard-nya tampil &amp; di tangan pengguna; UAC apa adanya).</summary>
    public static void RunInstaller(string installerPath)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = installerPath,
            UseShellExecute = true,
        });
    }

    /// <summary>Buka URL di browser bawaan sistem.</summary>
    public static void OpenInBrowser(string url)
    {
        Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
    }
}
