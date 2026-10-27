using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using TintaStudio.Models;

namespace TintaStudio.Services;

/// <summary>Keluarga protokol penyedia terjemahan.</summary>
public enum ProviderKind
{
    /// <summary>POST {base}/chat/completions dengan Bearer key.</summary>
    OpenAiCompatible,

    /// <summary>REST generateContent Google, header x-goog-api-key.</summary>
    Gemini,
}

/// <summary>Info statis satu penyedia terjemahan.</summary>
public sealed record ProviderInfo(
    string Id, string DisplayName, bool RequiresKey, ProviderKind Kind,
    string DefaultBaseUrl, string DefaultModel);

/// <summary>Bahasa tujuan terjemahan yang didukung.</summary>
public sealed record TargetLanguage(string Code, string DisplayName, string PromptName);

/// <summary>Pilihan satu jalan terjemahan (dari pengaturan aplikasi;
/// key TIDAK di sini — dibaca dari AppSecrets saat permintaan).
/// RequestDelay = jeda antar permintaan halaman &amp; sebelum
/// coba-ulang, dari pengaturan "Jeda antar halaman (detik)".</summary>
public sealed record TranslateOptions(
    ProviderInfo Provider, string BaseUrl, string Model, string TargetLanguage,
    TimeSpan RequestDelay = default);

/// <summary>Hasil terjemahan satu halaman: terjemahan per Id blok,
/// alasan gagal per Id blok, dan cap asal yang benar-benar dipakai.</summary>
public sealed record PageTranslationResult(
    IReadOnlyDictionary<string, string> Translations,
    IReadOnlyDictionary<string, string> Failures,
    string TranslatedBy,
    int? UsedKeyNumber);

/// <summary>Ringkasan jalan terjemahan satu bab.</summary>
public sealed record ChapterTranslationSummary(
    int PagesProcessed, int Translated, int Failed, int Skipped);

/// <summary>Kegagalan terjemahan yang dilaporkan apa adanya ke
/// pengguna (key kosong, HTTP error, jaringan, dst.).</summary>
public class TranslateException : Exception
{
    public TranslateException(string message) : base(message) { }
    public TranslateException(string message, Exception inner)
        : base(message, inner) { }
}

/// <summary>Semua key penyedia kena limit / tidak sah.</summary>
public sealed class AllKeysExhaustedException : TranslateException
{
    public AllKeysExhaustedException(string providerName)
        : base($"Semua key {providerName} kena limit atau tidak sah — " +
               "coba lagi nanti, atau tambah/periksa key di Pengaturan.")
    {
    }
}

/// <summary>Galat HTTP dari penyedia (internal: dasar keputusan rotasi).</summary>
internal sealed class ProviderHttpException : Exception
{
    public int StatusCode { get; }
    public string Body { get; }

    /// <summary>Durasi tunggu dari header Retry-After penyedia
    /// (bila dikirim) — mengalahkan durasi cooldown bawaan.</summary>
    public TimeSpan? RetryAfter { get; }

    public ProviderHttpException(int statusCode, string body,
        TimeSpan? retryAfter = null)
        : base($"HTTP {statusCode}")
    {
        StatusCode = statusCode;
        Body = body;
        RetryAfter = retryAfter;
    }

    /// <summary>429 atau penanda kuota habis (Gemini RESOURCE_EXHAUSTED).</summary>
    public bool IsRateLimit => StatusCode == 429
        || Body.Contains("RESOURCE_EXHAUSTED", StringComparison.OrdinalIgnoreCase);

    /// <summary>401/403: key tidak sah / ditolak.</summary>
    public bool IsAuthError => StatusCode == 401 || StatusCode == 403;
}

/// <summary>
/// Layanan terjemahan Tahap 4. Satu halaman diterjemahkan dalam SATU
/// prompt berisi semua blok bernomor (konteks dialog utuh), penyedia
/// membalas JSON ketat [{"id": n, "text": "..."}]. Penyedia BYOK:
/// Gemini (REST) + Groq/OpenRouter (kompatibel OpenAI); lokal:
/// Ollama/LM Studio (kompatibel OpenAI, tanpa key).
///
/// ROTASI KEY (BYOK): key dibaca dari AppSecrets (banyak key per
/// penyedia). Respons 429/kuota → key masuk cooldown (cap waktu di
/// keys.json) dan permintaan yang sama dicoba ulang dengan key
/// berikutnya; 401/403 → key ditandai buruk dan dilewati. Galat lain
/// (mis. 400) dilaporkan apa adanya tanpa rotasi. Semua key habis →
/// AllKeysExhaustedException.
/// </summary>
public sealed class TranslateService
{
    public const string GeminiId = "gemini";
    public const string GroqId = "groq";
    public const string OpenRouterId = "openrouter";
    public const string OllamaId = "ollama";
    public const string LmStudioId = "lmstudio";

    public static IReadOnlyList<ProviderInfo> Providers { get; } = new[]
    {
        new ProviderInfo(GeminiId, "Gemini", true, ProviderKind.Gemini,
            "https://generativelanguage.googleapis.com/v1beta", "gemini-2.5-flash"),
        new ProviderInfo(GroqId, "Groq", true, ProviderKind.OpenAiCompatible,
            "https://api.groq.com/openai/v1", "llama-3.3-70b-versatile"),
        new ProviderInfo(OpenRouterId, "OpenRouter", true, ProviderKind.OpenAiCompatible,
            "https://openrouter.ai/api/v1", "meta-llama/llama-3.3-70b-instruct:free"),
        new ProviderInfo(OllamaId, "Ollama (lokal)", false, ProviderKind.OpenAiCompatible,
            "http://localhost:11434/v1", "llama3.1"),
        new ProviderInfo(LmStudioId, "LM Studio (lokal)", false, ProviderKind.OpenAiCompatible,
            "http://localhost:1234/v1", ""),
    };

    public static IReadOnlyList<TargetLanguage> TargetLanguages { get; } = new[]
    {
        new TargetLanguage("id", "Indonesia", "Bahasa Indonesia"),
        new TargetLanguage("en", "Inggris", "English"),
    };

    public static ProviderInfo ProviderById(string id) =>
        Providers.FirstOrDefault(p => p.Id == id)
        ?? throw new ArgumentException($"Penyedia tidak dikenal: '{id}'.");

    public static TargetLanguage TargetByCode(string code) =>
        TargetLanguages.FirstOrDefault(t => t.Code == code)
        ?? throw new ArgumentException($"Bahasa tujuan tidak dikenal: '{code}'.");

    private readonly HttpClient _http;
    private readonly Func<TimeSpan, CancellationToken, Task> _delayer;
    private readonly TimeSpan _cooldown;

    /// <param name="http">Klien HTTP (tes menyuntik stub lewat handler/URL).</param>
    /// <param name="delayer">Fungsi jeda (bawaan Task.Delay) — dapat
    /// disuntik tes agar suite tidak lambat.</param>
    /// <param name="cooldown">Durasi cooldown key sesudah 429 bila
    /// penyedia tidak mengirim Retry-After (bawaan KeyPool 60 dtk;
    /// tes memakainya kecil, mis. 1 detik).</param>
    public TranslateService(HttpClient? http = null,
        Func<TimeSpan, CancellationToken, Task>? delayer = null,
        TimeSpan? cooldown = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(180) };
        _delayer = delayer ?? Task.Delay;
        _cooldown = cooldown ?? KeyPool.CooldownDuration;
    }

    /// <summary>
    /// Mode tunggu &amp; lanjut (dipakai jalan BAB): bila semua key
    /// sedang cooldown, pekerjaan TIDAK gagal — ia menjeda sampai
    /// key paling awal pulih lalu melanjutkan sendiri. Pembatalan
    /// (CancellationToken) tetap menghentikan dengan bersih.
    /// </summary>
    public bool WaitForKeyRecovery { get; set; }

    /// <summary>Callback status menunggu: (sisa waktu, nomor key
    /// yang ditunggu) — dipanggil berkala selama hitung mundur agar
    /// UI/CLI dapat menampilkan "Menunggu limit pulih… ±N dtk".</summary>
    public Action<TimeSpan, int>? WaitCallback { get; set; }

    private Task DelayAsync(TimeSpan delay, CancellationToken ct) =>
        delay > TimeSpan.Zero ? _delayer(delay, ct) : Task.CompletedTask;

    // ── Prompt ──────────────────────────────────────────────────────

    /// <summary>Bangun prompt konteks satu halaman: blok bernomor
    /// sesuai urutan yang diberikan (urutan baca), instruksi JSON ketat.</summary>
    public static string BuildPrompt(
        IReadOnlyList<(int Number, TextBlock Block)> numbered, string targetCode,
        bool retry = false)
    {
        var target = TargetByCode(targetCode);
        var sb = new StringBuilder();
        sb.Append("Kamu adalah penerjemah komik (manga/manhwa) profesional. ");
        sb.Append($"Terjemahkan teks dialog berikut ke {target.PromptName}. ");
        sb.Append("Semua blok berasal dari SATU halaman yang sama — pakai konteks " +
                  "antar-blok agar percakapan terasa natural dan nyambung. ");
        sb.Append("Jaga nada, sapaan, dan tingkat kesopanan dialog aslinya; jangan " +
                  "menambah penjelasan apa pun.\n\n");
        if (retry)
            sb.Append("Percobaan sebelumnya kehilangan sebagian blok. Terjemahkan " +
                      "HANYA blok bernomor berikut ini.\n\n");
        sb.Append("Teks sumber (format: #<nomor> [<kode bahasa>] <teks>):\n");
        foreach (var (number, block) in numbered)
        {
            sb.Append($"#{number} [{block.Language}] ");
            sb.Append(block.Text.Replace("\r\n", "\n"));
            sb.Append('\n');
        }
        sb.Append("\nBalas HANYA dengan JSON ketat tanpa teks lain, tanpa pagar kode, " +
                  "dalam format persis:\n");
        sb.Append("[{\"id\": <nomor>, \"text\": \"<terjemahan>\"}, ...]\n");
        sb.Append("Sertakan tepat satu entri untuk SETIAP nomor di atas, dengan " +
                  "nomor yang sama.");
        return sb.ToString();
    }

    // ── Parsing ─────────────────────────────────────────────────────

    /// <summary>
    /// Ekstrak peta nomor→terjemahan dari respons penyedia. Toleran
    /// pagar kode ``` dan teks pengantar; null bila tidak ada JSON
    /// larik yang terbaca sama sekali.
    /// </summary>
    public static Dictionary<int, string>? ParseTranslations(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        var text = raw.Trim();
        var fence = Regex.Match(text, "```(?:json)?\\s*(.*?)```",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);
        if (fence.Success)
            text = fence.Groups[1].Value.Trim();
        int start = text.IndexOf('[');
        int end = text.LastIndexOf(']');
        if (start < 0 || end <= start)
            return null;
        text = text[start..(end + 1)];
        try
        {
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return null;
            var map = new Dictionary<int, string>();
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object)
                    continue;
                if (!el.TryGetProperty("id", out var idEl))
                    continue;
                int id;
                if (idEl.ValueKind == JsonValueKind.Number)
                {
                    if (!idEl.TryGetInt32(out id)) continue;
                }
                else if (idEl.ValueKind == JsonValueKind.String)
                {
                    if (!int.TryParse(idEl.GetString(), out id)) continue;
                }
                else continue;
                if (!el.TryGetProperty("text", out var textEl)
                    || textEl.ValueKind != JsonValueKind.String)
                    continue;
                map[id] = textEl.GetString() ?? "";
            }
            return map;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // ── Terjemahan halaman ──────────────────────────────────────────

    /// <summary>
    /// Terjemahkan blok-blok satu halaman (sesuai urutan daftar =
    /// urutan baca). Blok yang tidak dikembalikan penyedia dicoba
    /// ulang SEKALI dengan prompt susulan khusus blok yang kurang;
    /// yang tetap hilang menjadi entri Failures dengan alasan.
    /// Galat tingkat permintaan (key/HTTP/jaringan) dilempar sebagai
    /// TranslateException — tidak ditelan.
    /// </summary>
    public async Task<PageTranslationResult> TranslatePageAsync(
        IReadOnlyList<TextBlock> blocks, TranslateOptions options,
        CancellationToken ct = default)
    {
        var by = Stamp(options, null);
        if (blocks.Count == 0)
            return new PageTranslationResult(
                new Dictionary<string, string>(), new Dictionary<string, string>(),
                by, null);

        var numbered = blocks.Select((b, i) => (Number: i + 1, Block: b)).ToList();
        var run = await ExecuteNumberedAsync(numbered,
            (items, retry) => BuildPrompt(items, options.TargetLanguage, retry),
            options, ct);
        return ToResult(numbered, run);
    }

    /// <summary>Hasil jalan prompt bernomor (kirim + coba-ulang sekali).</summary>
    private sealed record NumberedRun(
        Dictionary<int, string>? Map, string By, int? KeyNumber);

    /// <summary>
    /// Jalankan satu prompt bernomor: kirim, petakan respons; nomor
    /// yang hilang dicoba ulang SEKALI dengan prompt susulan (dengan
    /// jeda antar permintaan). Dipakai bersama oleh terjemahan
    /// dan poles.
    /// </summary>
    private async Task<NumberedRun> ExecuteNumberedAsync(
        IReadOnlyList<(int Number, TextBlock Block)> numbered,
        Func<IReadOnlyList<(int Number, TextBlock Block)>, bool, string> buildPrompt,
        TranslateOptions options, CancellationToken ct)
    {
        var prompt = buildPrompt(numbered, false);
        var (raw, keyNumber) = await SendChatAsync(options, prompt, ct);
        var by = Stamp(options, keyNumber);
        var map = ParseTranslations(raw);

        bool Missing(int n) => map is null
            || !map.TryGetValue(n, out var t)
            || string.IsNullOrWhiteSpace(t);

        var missing = numbered.Where(n => Missing(n.Number)).ToList();
        if (missing.Count > 0)
        {
            // Jeda antar permintaan juga berlaku sebelum coba-ulang.
            await DelayAsync(options.RequestDelay, ct);
            var retryPrompt = buildPrompt(missing, true);
            var (raw2, keyNumber2) = await SendChatAsync(options, retryPrompt, ct);
            if (keyNumber2 is not null)
                by = Stamp(options, keyNumber2);
            var map2 = ParseTranslations(raw2);
            if (map2 is not null)
            {
                map ??= new Dictionary<int, string>();
                foreach (var (k, v) in map2)
                    map[k] = v;
            }
        }
        return new NumberedRun(map, by, keyNumber);
    }

    private static PageTranslationResult ToResult(
        IReadOnlyList<(int Number, TextBlock Block)> numbered, NumberedRun run)
    {
        var translations = new Dictionary<string, string>();
        var failures = new Dictionary<string, string>();
        foreach (var (number, block) in numbered)
        {
            if (run.Map is not null
                && run.Map.TryGetValue(number, out var text)
                && !string.IsNullOrWhiteSpace(text))
            {
                translations[block.Id] = text.Trim();
            }
            else
            {
                failures[block.Id] = run.Map is null
                    ? "Respons penyedia tidak dapat dibaca sebagai JSON terjemahan."
                    : "Penyedia tidak mengembalikan terjemahan untuk blok ini " +
                      "(sudah dicoba ulang sekali).";
            }
        }
        return new PageTranslationResult(
            translations, failures, run.By, run.KeyNumber);
    }

    /// <summary>
    /// Terjemahkan SATU blok dengan konteks halamannya: seluruh
    /// blok halaman dikirim dalam prompt (dialog tetap natural),
    /// tetapi hanya hasil blok sasaran yang dikembalikan — untuk
    /// tombol "Terjemahkan blok ini" di UI. Rotasi key &amp; jeda
    /// berlaku sama seperti jalan sehalaman.
    /// </summary>
    public async Task<PageTranslationResult> TranslateSingleBlockAsync(
        IReadOnlyList<TextBlock> pageBlocks, TextBlock target,
        TranslateOptions options, CancellationToken ct = default)
    {
        var full = await TranslatePageAsync(pageBlocks, options, ct);
        var translations = new Dictionary<string, string>();
        var failures = new Dictionary<string, string>();
        if (full.Translations.TryGetValue(target.Id, out var text))
            translations[target.Id] = text;
        else if (full.Failures.TryGetValue(target.Id, out var reason))
            failures[target.Id] = reason;
        else
            failures[target.Id] = "Penyedia tidak mengembalikan blok ini.";
        return new PageTranslationResult(
            translations, failures, full.TranslatedBy, full.UsedKeyNumber);
    }

    // ── Poles AI ────────────────────────────────────────────────────

    /// <summary>Bangun prompt poles: per blok berisi teks SUMBER +
    /// terjemahan saat ini; minta versi yang lebih natural dengan
    /// makna/nada/panjang setia, format JSON bernomor yang sama.</summary>
    public static string BuildPolishPrompt(
        IReadOnlyList<(int Number, TextBlock Block)> numbered, string targetCode,
        bool retry = false)
    {
        var target = TargetByCode(targetCode);
        var sb = new StringBuilder();
        sb.Append("Kamu adalah penyunting terjemahan komik profesional. ");
        sb.Append($"Poles terjemahan berikut agar lebih natural dan enak dibaca " +
                  $"dalam {target.PromptName}. ");
        sb.Append("Tetap SETIA pada teks sumber: jaga makna, nada, sapaan, dan " +
                  "panjang dialog; JANGAN menambah atau mengurangi isi; jangan " +
                  "mengubah nama orang/tempat/istilah. ");
        sb.Append("Semua blok berasal dari SATU halaman yang sama — jaga " +
                  "keterkaitan dialog antar-blok.\n\n");
        if (retry)
            sb.Append("Percobaan sebelumnya kehilangan sebagian blok. Poles " +
                      "HANYA blok bernomor berikut ini.\n\n");
        sb.Append("Format masukan per blok: #<nomor> [<kode bahasa>] lalu baris " +
                  "SUMBER: dan TERJEMAHAN:\n");
        foreach (var (number, block) in numbered)
        {
            sb.Append($"#{number} [{block.Language}]\n");
            sb.Append($"SUMBER: {block.Text.Replace("\r\n", "\n")}\n");
            sb.Append($"TERJEMAHAN: {block.Translation?.Replace("\r\n", "\n")}\n");
        }
        sb.Append("\nBalas HANYA dengan JSON ketat tanpa teks lain, tanpa pagar " +
                  "kode, dalam format persis:\n");
        sb.Append("[{\"id\": <nomor>, \"text\": \"<terjemahan terpoles>\"}, ...]\n");
        sb.Append("Sertakan tepat satu entri untuk SETIAP nomor di atas.");
        return sb.ToString();
    }

    /// <summary>
    /// Poles blok-blok yang SUDAH punya terjemahan (blok kosong
    /// dilewati — tidak masuk prompt). Hasil menggantikan terjemahan
    /// lewat <see cref="ApplyPolish"/> yang menyimpan versi lama di
    /// TranslationBackup. Cap hasil: "Penyedia • model • poles".
    /// </summary>
    public async Task<PageTranslationResult> PolishPageAsync(
        IReadOnlyList<TextBlock> blocks, TranslateOptions options,
        CancellationToken ct = default)
    {
        var candidates = blocks
            .Where(b => !string.IsNullOrWhiteSpace(b.Translation))
            .ToList();
        if (candidates.Count == 0)
            return new PageTranslationResult(
                new Dictionary<string, string>(), new Dictionary<string, string>(),
                Stamp(options, null) + " • poles", null);
        var numbered = candidates.Select((b, i) => (Number: i + 1, Block: b)).ToList();
        var run = await ExecuteNumberedAsync(numbered,
            (items, retry) => BuildPolishPrompt(items, options.TargetLanguage, retry),
            options, ct);
        var result = ToResult(numbered, run);
        return result with { TranslatedBy = result.TranslatedBy + " • poles" };
    }

    /// <summary>
    /// Terapkan hasil poles: teks lama disimpan SATU tingkat di
    /// TranslationBackup (+ capnya) sebelum digantikan hasil poles.
    /// Kegagalan poles dicatat di TranslationError tanpa menyentuh
    /// terjemahan yang ada.
    /// </summary>
    public static void ApplyPolish(ProjectPage page, PageTranslationResult result)
    {
        var now = DateTime.UtcNow;
        foreach (var block in page.Blocks)
        {
            if (result.Translations.TryGetValue(block.Id, out var polished))
            {
                block.TranslationBackup = block.Translation;
                block.TranslationBackupBy = block.TranslatedBy;
                block.Translation = polished;
                block.TranslatedBy = result.TranslatedBy;
                block.TranslatedAt = now;
                block.TranslationError = null;
            }
            else if (result.Failures.TryGetValue(block.Id, out var reason))
            {
                block.TranslationError = $"Poles gagal: {reason}";
            }
        }
    }

    /// <summary>Urungkan poles terakhir satu blok: pulihkan teks +
    /// cap cadangan persis seperti sebelumnya. False bila tidak ada
    /// cadangan.</summary>
    public static bool UndoPolish(TextBlock block)
    {
        if (block.TranslationBackup is null)
            return false;
        block.Translation = block.TranslationBackup;
        block.TranslatedBy = block.TranslationBackupBy;
        block.TranslationBackup = null;
        block.TranslationBackupBy = null;
        block.TranslatedAt = DateTime.UtcNow;
        block.TranslationError = null;
        return true;
    }

    /// <summary>Ringkasan jalan poles satu bab.</summary>
    public sealed record ChapterPolishSummary(
        int PagesProcessed, int Polished, int Failed, int Skipped);

    /// <summary>
    /// Poles satu bab halaman demi halaman — aturan yang sama
    /// dengan jalan terjemahan bab: simpan per halaman lewat
    /// <paramref name="onPageDone"/>, jeda antar permintaan, mode
    /// tunggu key berlaku. Blok TANPA terjemahan dilewati; blok
    /// hasil MANUAL (TranslatedBy "Manual") TIDAK dipoles otomatis
    /// di sini (hanya lewat poles per blok/halaman yang eksplisit).
    /// </summary>
    public async Task<ChapterPolishSummary> PolishChapterAsync(
        ProjectChapter chapter, TranslateOptions options,
        IProgress<(int Done, int Total)>? progress = null,
        Action<ProjectPage>? onPageDone = null,
        CancellationToken ct = default)
    {
        int polished = 0, failed = 0, skipped = 0, done = 0;
        bool madeRequest = false;
        var pages = chapter.Pages.OrderBy(p => p.Index).ToList();
        foreach (var page in pages)
        {
            ct.ThrowIfCancellationRequested();
            var candidates = page.Blocks
                .Where(b => !string.IsNullOrWhiteSpace(b.Translation)
                            && b.TranslatedBy != "Manual")
                .ToList();
            skipped += page.Blocks.Count - candidates.Count;
            if (candidates.Count > 0)
            {
                if (madeRequest)
                    await DelayAsync(options.RequestDelay, ct);
                madeRequest = true;
                try
                {
                    var result = await PolishPageAsync(candidates, options, ct);
                    ApplyPolish(page, result);
                    polished += result.Translations.Count;
                    failed += result.Failures.Count;
                }
                catch (AllKeysExhaustedException ex)
                {
                    foreach (var b in candidates)
                        b.TranslationError = $"Poles gagal: {ex.Message}";
                    failed += candidates.Count;
                    onPageDone?.Invoke(page);
                    progress?.Report((++done, pages.Count));
                    throw;
                }
                catch (TranslateException ex)
                {
                    foreach (var b in candidates)
                        b.TranslationError = $"Poles gagal: {ex.Message}";
                    failed += candidates.Count;
                }
            }
            onPageDone?.Invoke(page);
            progress?.Report((++done, pages.Count));
        }
        return new ChapterPolishSummary(pages.Count, polished, failed, skipped);
    }

    // ── Terjemahan bab (simpan per halaman + lewati yang sudah ada) ──

    /// <summary>
    /// Terjemahkan satu bab halaman demi halaman. Perilaku simpan:
    /// <paramref name="onPageDone"/> dipanggil SESUDAH SETIAP halaman
    /// selesai (pemanggil menyimpan proyek di sana — jadi proses
    /// yang berhenti di tengah tetap meninggalkan halaman yang
    /// sudah jadi tersimpan). Blok yang SUDAH punya terjemahan
    /// DILEWATI (tidak dikirim ke penyedia, tidak membakar kuota);
    /// blok bertanda gagal justru dicoba lagi. Halaman yang gagal
    /// total (galat permintaan) menandai blok tertundanya gagal
    /// dengan alasan itu, lalu bab berlanjut ke halaman berikutnya;
    /// AllKeysExhaustedException menghentikan bab (percuma lanjut).
    /// </summary>
    public async Task<ChapterTranslationSummary> TranslateChapterAsync(
        ProjectChapter chapter, TranslateOptions options,
        IProgress<(int Done, int Total)>? progress = null,
        Action<ProjectPage>? onPageDone = null,
        CancellationToken ct = default)
    {
        int translated = 0, failed = 0, skipped = 0, done = 0;
        bool madeRequest = false;
        var pages = chapter.Pages.OrderBy(p => p.Index).ToList();
        foreach (var page in pages)
        {
            ct.ThrowIfCancellationRequested();
            var pending = page.Blocks
                .Where(b => string.IsNullOrWhiteSpace(b.Translation))
                .ToList();
            skipped += page.Blocks.Count - pending.Count;
            if (pending.Count > 0)
            {
                // Jeda antar halaman: hanya di antara permintaan
                // sungguhan (halaman yang terlewati penuh tidak
                // memicu jeda).
                if (madeRequest)
                    await DelayAsync(options.RequestDelay, ct);
                madeRequest = true;
                try
                {
                    var result = await TranslatePageAsync(pending, options, ct);
                    ApplyResult(page, result);
                    translated += result.Translations.Count;
                    failed += result.Failures.Count;
                }
                catch (AllKeysExhaustedException ex)
                {
                    MarkPageFailed(page, pending, ex.Message);
                    failed += pending.Count;
                    onPageDone?.Invoke(page);
                    progress?.Report((++done, pages.Count));
                    throw;
                }
                catch (TranslateException ex)
                {
                    MarkPageFailed(page, pending, ex.Message);
                    failed += pending.Count;
                }
            }
            onPageDone?.Invoke(page);
            progress?.Report((++done, pages.Count));
        }
        return new ChapterTranslationSummary(pages.Count, translated, failed, skipped);
    }

    /// <summary>Terapkan hasil halaman ke blok-blok proyek: sukses →
    /// Translation + cap; gagal → TranslationError (Translation lama
    /// yang kosong tetap kosong; yang sudah ada tidak dihapus).</summary>
    public static void ApplyResult(ProjectPage page, PageTranslationResult result)
    {
        var now = DateTime.UtcNow;
        foreach (var block in page.Blocks)
        {
            if (result.Translations.TryGetValue(block.Id, out var text))
            {
                block.Translation = text;
                block.TranslatedBy = result.TranslatedBy;
                block.TranslatedAt = now;
                block.TranslationError = null;
            }
            else if (result.Failures.TryGetValue(block.Id, out var reason))
            {
                block.TranslationError = reason;
            }
        }
    }

    private static void MarkPageFailed(
        ProjectPage page, IReadOnlyList<TextBlock> pending, string reason)
    {
        var ids = pending.Select(b => b.Id).ToHashSet();
        foreach (var block in page.Blocks)
            if (ids.Contains(block.Id))
                block.TranslationError = reason;
    }

    private static string Stamp(TranslateOptions options, int? keyNumber)
    {
        var model = string.IsNullOrWhiteSpace(options.Model)
            ? "(model bawaan server)"
            : options.Model;
        var stamp = $"{options.Provider.DisplayName} • {model}";
        if (keyNumber is not null)
            stamp += $" • key #{keyNumber}";
        return stamp;
    }

    // ── Lapisan HTTP + rotasi key ───────────────────────────────────

    /// <summary>
    /// Kirim satu prompt dengan rotasi key otomatis untuk penyedia
    /// BYOK. Mengembalikan teks jawaban + nomor key yang berhasil
    /// (null untuk penyedia lokal tanpa key).
    /// </summary>
    private async Task<(string Text, int? KeyNumber)> SendChatAsync(
        TranslateOptions options, string prompt, CancellationToken ct)
    {
        var provider = options.Provider;
        if (!provider.RequiresKey)
        {
            var text = await SendOnceAsync(options, null, prompt, ct);
            return (text, null);
        }

        var pool = new KeyPool(provider.Id);
        if (pool.Count == 0)
            throw new TranslateException(
                $"API key {provider.DisplayName} belum diisi — isi dulu lewat " +
                "Pengaturan Key & Model di halaman Terjemahan.");

        var tried = new HashSet<int>();
        while (true)
        {
            while (pool.TryNext(tried, out var index, out var key))
            {
                tried.Add(index);
                try
                {
                    var text = await SendOnceAsync(options, key, prompt, ct);
                    pool.MarkSuccess(index);
                    return (text, index + 1);
                }
                catch (ProviderHttpException ex) when (ex.IsRateLimit)
                {
                    // Retry-After penyedia mengalahkan cooldown bawaan.
                    pool.MarkCooldown(index, ex.RetryAfter ?? _cooldown);
                }
                catch (ProviderHttpException ex) when (ex.IsAuthError)
                {
                    pool.MarkBad(index);
                }
                catch (ProviderHttpException ex)
                {
                    // Galat non-limit lain (400, 500, …): laporkan apa
                    // adanya, TANPA rotasi.
                    throw ToTranslateException(options, ex);
                }
            }

            // Tidak ada kandidat tersedia. Mode tunggu (jalan bab):
            // bila ada key yang sedang cooldown (bukan buruk), jeda
            // sampai yang paling awal pulih, lalu coba lagi dari
            // blok yang tertunda ini — bab tidak gagal.
            if (WaitForKeyRecovery
                && pool.TryGetEarliestRecovery(out var until, out var waitIndex))
            {
                tried.Clear();
                while (true)
                {
                    var remaining = until - DateTime.UtcNow;
                    if (remaining <= TimeSpan.Zero)
                        break;
                    WaitCallback?.Invoke(remaining, waitIndex + 1);
                    await DelayAsync(
                        TimeSpan.FromSeconds(
                            Math.Min(1.0, remaining.TotalSeconds)), ct);
                }
                continue;
            }
            // Semua kandidat habis: kena limit/cooldown atau tidak sah.
            throw new AllKeysExhaustedException(provider.DisplayName);
        }
    }

    private static TranslateException ToTranslateException(
        TranslateOptions options, ProviderHttpException ex)
    {
        var snippet = ex.Body.Replace("\n", " ").Trim();
        if (snippet.Length > 220)
            snippet = snippet[..220] + "…";
        return new TranslateException(
            $"Penyedia {options.Provider.DisplayName} membalas HTTP " +
            $"{ex.StatusCode}: {snippet}");
    }

    /// <summary>Satu percobaan HTTP ke penyedia; mengembalikan teks
    /// jawaban model ("" bila struktur respons tak memuat teks).</summary>
    private async Task<string> SendOnceAsync(
        TranslateOptions options, string? apiKey, string prompt,
        CancellationToken ct)
    {
        var provider = options.Provider;
        string url;
        object payload;
        if (provider.Kind == ProviderKind.Gemini)
        {
            var model = string.IsNullOrWhiteSpace(options.Model)
                ? provider.DefaultModel : options.Model;
            url = options.BaseUrl.TrimEnd('/') +
                  $"/models/{model}:generateContent";
            payload = new
            {
                contents = new[]
                {
                    new { parts = new[] { new { text = prompt } } },
                },
                generationConfig = new { temperature = 0.3 },
            };
        }
        else
        {
            url = options.BaseUrl.TrimEnd('/') + "/chat/completions";
            payload = new
            {
                model = string.IsNullOrWhiteSpace(options.Model)
                    ? "local-model" : options.Model,
                temperature = 0.3,
                messages = new[]
                {
                    new { role = "user", content = prompt },
                },
            };
        }

        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(payload), Encoding.UTF8,
                "application/json"),
        };
        if (provider.Kind == ProviderKind.Gemini)
        {
            if (apiKey is not null)
                req.Headers.TryAddWithoutValidation("x-goog-api-key", apiKey);
        }
        else if (apiKey is not null)
        {
            req.Headers.TryAddWithoutValidation(
                "Authorization", $"Bearer {apiKey}");
        }

        HttpResponseMessage resp;
        try
        {
            resp = await _http.SendAsync(req, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new TranslateException(
                $"Tidak dapat menghubungi {provider.DisplayName} di " +
                $"{options.BaseUrl}: {ex.Message}", ex);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TranslateException(
                $"Permintaan ke {provider.DisplayName} kehabisan waktu " +
                "(timeout).");
        }

        using (resp)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
            {
                TimeSpan? retryAfter = null;
                var ra = resp.Headers.RetryAfter;
                if (ra?.Delta is { } delta)
                    retryAfter = delta;
                else if (ra?.Date is { } date)
                    retryAfter = date - DateTimeOffset.UtcNow;
                if (retryAfter is { } r && r < TimeSpan.Zero)
                    retryAfter = TimeSpan.Zero;
                throw new ProviderHttpException(
                    (int)resp.StatusCode, body, retryAfter);
            }
            return provider.Kind == ProviderKind.Gemini
                ? ExtractGeminiText(body)
                : ExtractOpenAiText(body);
        }
    }

    private static string ExtractOpenAiText(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("choices", out var choices)
                || choices.ValueKind != JsonValueKind.Array
                || choices.GetArrayLength() == 0)
                return "";
            var message = choices[0].GetProperty("message");
            if (!message.TryGetProperty("content", out var content))
                return "";
            if (content.ValueKind == JsonValueKind.String)
                return content.GetString() ?? "";
            if (content.ValueKind == JsonValueKind.Array)
            {
                var sb = new StringBuilder();
                foreach (var part in content.EnumerateArray())
                    if (part.TryGetProperty("text", out var t)
                        && t.ValueKind == JsonValueKind.String)
                        sb.Append(t.GetString());
                return sb.ToString();
            }
            return "";
        }
        catch (JsonException)
        {
            return "";
        }
    }

    private static string ExtractGeminiText(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("candidates", out var candidates)
                || candidates.ValueKind != JsonValueKind.Array
                || candidates.GetArrayLength() == 0)
                return "";
            var content = candidates[0].GetProperty("content");
            if (!content.TryGetProperty("parts", out var parts))
                return "";
            var sb = new StringBuilder();
            foreach (var part in parts.EnumerateArray())
                if (part.TryGetProperty("text", out var t)
                    && t.ValueKind == JsonValueKind.String)
                    sb.Append(t.GetString());
            return sb.ToString();
        }
        catch (JsonException)
        {
            return "";
        }
    }
}
