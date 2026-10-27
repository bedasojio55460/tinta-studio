using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using TintaStudio.Models;
using TintaStudio.Services;
using Xunit;
using Xunit.Abstractions;

namespace TintaStudio.Tests;

/// <summary>
/// Tes Tahap 4 — Terjemahan BYOK + lokal lewat STUB dalam-proses:
/// sebuah HttpMessageHandler tiruan yang menerima HttpRequestMessage
/// persis seperti yang akan dikirim ke penyedia (URL, header
/// Authorization / x-goog-api-key, badan JSON), memvalidasinya, dan
/// membalas deterministik (termasuk 429 + Retry-After).
///
/// Kenapa bukan server HttpListener loopback: kebijakan jaringan
/// sandbox VM ini (Direct network protocols: other_tcp = Deny)
/// menolak koneksi TCP keluar dari proses .NET — bahkan ke
/// 127.0.0.1 — dengan banner penolakan sebagai "status line".
/// Handler stub memvalidasi bentuk permintaan yang IDENTIK tanpa
/// soket; di mesin normal jalur soketnya sama (HttpClient standar).
/// Tes CLI yang memang butuh soket sungguhan di-skip jujur bila
/// probe loopback gagal (lihat Cli_Translate_ViaLoopbackStub).
/// Tidak ada key asli; tidak ada panggilan ke penyedia asli.
/// </summary>
public sealed class TranslateTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _configDir;

    public TranslateTests(ITestOutputHelper output)
    {
        _out = output;
        _configDir = Path.Combine(Path.GetTempPath(),
            "tinta-translate-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_configDir);
        Environment.SetEnvironmentVariable("TINTA_CONFIG_DIR", _configDir);
        AppSecrets.ResetCache();
    }

    public void Dispose()
    {
        AppSecrets.ResetCache();
        try { Directory.Delete(_configDir, recursive: true); } catch { }
    }

    // ── Stub handler ────────────────────────────────────────────

    private sealed record StubRequest(
        string Path, string? Authorization, string? GoogleKey,
        string Body, DateTimeOffset At);

    private sealed record StubResponse(int Status, string Body, int? RetryAfterSeconds = null);

    /// <summary>Tiruan penyedia di tingkat transport HTTP.</summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly object _gate = new();
        public List<StubRequest> Requests { get; } = new();
        public Func<StubRequest, StubResponse> Handler { get; set; } =
            _ => new StubResponse(500, "no handler");

        /// <summary>Base URL rekaan — tidak pernah tersentuh DNS/soket
        /// karena handler mencegat semua permintaan.</summary>
        public string BaseUrl => "http://stub.local/v1";

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? ""
                : await request.Content.ReadAsStringAsync(cancellationToken);
            string? auth = request.Headers.TryGetValues("Authorization", out var a)
                ? a.FirstOrDefault() : null;
            string? gk = request.Headers.TryGetValues("x-goog-api-key", out var g)
                ? g.FirstOrDefault() : null;
            var rec = new StubRequest(
                request.RequestUri?.AbsolutePath ?? "", auth, gk, body,
                DateTimeOffset.UtcNow);
            lock (_gate) Requests.Add(rec);
            var resp = Handler(rec);
            var msg = new HttpResponseMessage((HttpStatusCode)resp.Status)
            {
                Content = new StringContent(resp.Body, Encoding.UTF8,
                    "application/json"),
            };
            if (resp.RetryAfterSeconds is { } ra)
                msg.Headers.RetryAfter =
                    new System.Net.Http.Headers.RetryConditionHeaderValue(
                        TimeSpan.FromSeconds(ra));
            return msg;
        }

        public List<StubRequest> Snapshot()
        {
            lock (_gate) return Requests.ToList();
        }
    }

    /// <summary>Layanan dengan stub handler terpasang (transport
    /// dalam-memori; logika layanan &amp; bentuk permintaan asli).</summary>
    private static TranslateService Svc(StubHandler stub, TimeSpan? cooldown = null) =>
        new(new HttpClient(stub), cooldown: cooldown);

    // ── Pembantu stub ───────────────────────────────────────────

    private static string PromptOf(StubRequest req, ProviderKind kind)
    {
        using var doc = JsonDocument.Parse(req.Body);
        return kind == ProviderKind.Gemini
            ? doc.RootElement.GetProperty("contents")[0]
                .GetProperty("parts")[0].GetProperty("text").GetString() ?? ""
            : doc.RootElement.GetProperty("messages")[0]
                .GetProperty("content").GetString() ?? "";
    }

    private static List<int> BlockNumbers(string prompt) =>
        Regex.Matches(prompt, @"(?m)^#(\d+)")
            .Select(m => int.Parse(m.Groups[1].Value)).ToList();

    private static string TranslationsJson(IEnumerable<int> numbers,
        Func<int, string> text)
    {
        var items = numbers.Select(n => new { id = n, text = text(n) });
        return JsonSerializer.Serialize(items);
    }

    private static StubResponse OpenAiOk(string content) =>
        new(200, JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new { message = new { role = "assistant", content } },
            },
        }));

    private static StubResponse GeminiOk(string content) =>
        new(200, JsonSerializer.Serialize(new
        {
            candidates = new[]
            {
                new { content = new { parts = new[] { new { text = content } } } },
            },
        }));

    /// <summary>Handler sukses umum: balas semua nomor di prompt.</summary>
    private static Func<StubRequest, StubResponse> SuccessHandler(
        ProviderKind kind, Func<int, string>? text = null)
    {
        text ??= n => $"Terjemahan-{n}";
        return req =>
        {
            var numbers = BlockNumbers(PromptOf(req, kind));
            var json = TranslationsJson(numbers, text);
            return kind == ProviderKind.Gemini ? GeminiOk(json) : OpenAiOk(json);
        };
    }

    private static TextBlock Block(string text, string lang = "ja") => new()
    {
        Text = text,
        Language = lang,
        Confidence = 0.95,
        DetectionScore = 0.9,
    };

    private static TranslateOptions Options(ProviderInfo provider, string baseUrl,
        string model = "stub-model", TimeSpan? delay = null) =>
        new(provider, baseUrl, model, "id", delay ?? TimeSpan.Zero);

    private static ProviderInfo Provider(string id) => TranslateService.ProviderById(id);

    // ── Jalur sukses per bentuk penyedia ────────────────────────

    [Fact]
    public async Task OpenAi_Translate_MapsBlocks_Stamps_RoundTrips_NoKeyInProject()
    {
        AppSecrets.SetKeys("groq", new[] { "DUMMY-GROQ-1" });
        var stub = new StubHandler();
        stub.Handler = req =>
        {
            Assert.Equal("Bearer DUMMY-GROQ-1", req.Authorization);
            Assert.EndsWith("/chat/completions", req.Path);
            using var doc = JsonDocument.Parse(req.Body);
            Assert.Equal("stub-model",
                doc.RootElement.GetProperty("model").GetString());
            var prompt = PromptOf(req, ProviderKind.OpenAiCompatible);
            Assert.Contains("#1 [ja]", prompt);
            Assert.Contains("#3 [ja]", prompt);
            return OpenAiOk(TranslationsJson(new[] { 1, 2, 3 },
                n => $"Terjemahan-{n}"));
        };
        var blocks = new[] { Block("こんにちは"), Block("ありがとう"), Block("せかい") };
        var page = new ProjectPage { Index = 0 };
        page.Blocks.AddRange(blocks);

        var result = await Svc(stub).TranslatePageAsync(
            blocks, Options(Provider("groq"), stub.BaseUrl));
        TranslateService.ApplyResult(page, result);

        Assert.Equal(3, result.Translations.Count);
        Assert.Empty(result.Failures);
        Assert.Equal(1, result.UsedKeyNumber); // key #1 yang berhasil
        foreach (var b in blocks)
        {
            Assert.False(string.IsNullOrWhiteSpace(b.Translation));
            Assert.Equal("Groq • stub-model • key #1", b.TranslatedBy);
            Assert.NotNull(b.TranslatedAt);
            Assert.Null(b.TranslationError);
        }

        // Round-trip proyek v4 + key TIDAK PERNAH masuk berkas proyek.
        var project = new TintaProject { Name = "Uji" };
        var chapter = new ProjectChapter
        {
            SourcePath = "/tmp/bab.cbz", Title = "Bab",
            Format = "ZIP (CBZ)", PageCount = 1,
        };
        chapter.Pages.Add(page);
        project.Chapters.Add(chapter);
        var path = Path.Combine(_configDir, "proyek.tinta.json");
        project.Save(path);
        var raw = File.ReadAllText(path);
        Assert.DoesNotContain("DUMMY-GROQ-1", raw);
        var loaded = TintaProject.Load(path);
        Assert.Equal(TintaProject.CurrentVersion, loaded.Version);
        var loadedBlocks = loaded.Chapters[0].Pages[0].Blocks;
        Assert.Equal(blocks.Select(b => b.Translation),
                     loadedBlocks.Select(b => b.Translation));
        Assert.Equal("Groq • stub-model • key #1", loadedBlocks[0].TranslatedBy);
    }

    [Fact]
    public async Task Gemini_Translate_ValidatesRequestShape()
    {
        AppSecrets.SetKeys("gemini", new[] { "DUMMY-GEM-1" });
        var stub = new StubHandler();
        stub.Handler = req =>
        {
            Assert.Equal("DUMMY-GEM-1", req.GoogleKey);
            Assert.EndsWith("/models/stub-gem:generateContent", req.Path);
            using var doc = JsonDocument.Parse(req.Body);
            Assert.Equal(0.3, doc.RootElement.GetProperty("generationConfig")
                .GetProperty("temperature").GetDouble(), precision: 3);
            var numbers = BlockNumbers(PromptOf(req, ProviderKind.Gemini));
            return GeminiOk(TranslationsJson(numbers, n => $"Gem-{n}"));
        };
        var blocks = new[] { Block("こんにちは"), Block("せかい") };
        var result = await Svc(stub).TranslatePageAsync(blocks,
            Options(Provider("gemini"), stub.BaseUrl, "stub-gem"));
        Assert.Equal(2, result.Translations.Count);
        Assert.Equal("Gem-1", result.Translations[blocks[0].Id]);
        Assert.Equal("Gemini • stub-gem • key #1", result.TranslatedBy);
    }

    // ── Parsing kokoh ───────────────────────────────────────────

    [Fact]
    public async Task Parse_CodeFencedJson_StillWorks()
    {
        var stub = new StubHandler();
        stub.Handler = req =>
        {
            var numbers = BlockNumbers(PromptOf(req, ProviderKind.OpenAiCompatible));
            var json = TranslationsJson(numbers, n => $"Pagar-{n}");
            return OpenAiOk($"Baik, ini hasilnya:\n```json\n{json}\n```");
        };
        var blocks = new[] { Block("a"), Block("b") };
        var result = await Svc(stub).TranslatePageAsync(
            blocks, Options(Provider("ollama"), stub.BaseUrl));
        Assert.Equal(2, result.Translations.Count);
        Assert.Equal("Pagar-2", result.Translations[blocks[1].Id]);
    }

    [Fact]
    public async Task Parse_MissingBlock_RetriesOnlyMissing()
    {
        var stub = new StubHandler();
        int call = 0;
        stub.Handler = req =>
        {
            call++;
            var numbers = BlockNumbers(PromptOf(req, ProviderKind.OpenAiCompatible));
            if (call == 1)
            {
                Assert.Equal(new[] { 1, 2, 3 }, numbers);
                return OpenAiOk(TranslationsJson(new[] { 1, 3 },
                    n => $"Terjemahan-{n}"));
            }
            // Coba-ulang: HANYA nomor yang hilang (#2).
            Assert.Equal(new[] { 2 }, numbers);
            return OpenAiOk(TranslationsJson(new[] { 2 }, n => "Terjemahan-2"));
        };
        var blocks = new[] { Block("a"), Block("b"), Block("c") };
        var result = await Svc(stub).TranslatePageAsync(
            blocks, Options(Provider("ollama"), stub.BaseUrl));
        Assert.Equal(2, call);
        Assert.Equal(3, result.Translations.Count);
        Assert.Empty(result.Failures);
    }

    [Fact]
    public async Task Parse_Garbage_AllBlocksFailWithReason_NoCrash()
    {
        var stub = new StubHandler();
        stub.Handler = _ => OpenAiOk("Maaf, saya tidak bisa menerjemahkan itu.");
        var blocks = new[] { Block("a"), Block("b") };
        var page = new ProjectPage();
        page.Blocks.AddRange(blocks);
        var result = await Svc(stub).TranslatePageAsync(
            blocks, Options(Provider("ollama"), stub.BaseUrl));
        Assert.Empty(result.Translations);
        Assert.Equal(2, result.Failures.Count);
        TranslateService.ApplyResult(page, result);
        foreach (var b in blocks)
        {
            Assert.Null(b.Translation);
            Assert.Contains("tidak dapat dibaca", b.TranslationError);
        }
    }

    // ── Rotasi key ──────────────────────────────────────────────

    [Fact]
    public async Task Rotation_429FirstKey_SucceedsViaSecond_CoolingRecorded()
    {
        AppSecrets.SetKeys("groq", new[] { "LIMITKEY", "GOODKEY" });
        var stub = new StubHandler();
        stub.Handler = req =>
        {
            if (req.Authorization == "Bearer LIMITKEY")
                return new StubResponse(429, "{\"error\":{\"message\":\"rate limited\"}}");
            Assert.Equal("Bearer GOODKEY", req.Authorization);
            var numbers = BlockNumbers(PromptOf(req, ProviderKind.OpenAiCompatible));
            return OpenAiOk(TranslationsJson(numbers, n => $"OK-{n}"));
        };
        var service = Svc(stub);
        var blocks = new[] { Block("a") };
        var result = await service.TranslatePageAsync(
            blocks, Options(Provider("groq"), stub.BaseUrl));
        Assert.Single(result.Translations);
        Assert.Equal(2, result.UsedKeyNumber);
        Assert.Contains("key #2", result.TranslatedBy);

        var status = AppSecrets.GetStatus("groq");
        Assert.Equal(2, status.Count);
        Assert.Equal(1, status.Cooling);
        Assert.Equal(2, status.ActiveNumber);

        // Jalan berikutnya: key #1 masih cooldown → langsung key #2.
        int before = stub.Snapshot().Count;
        await service.TranslatePageAsync(
            new[] { Block("b") }, Options(Provider("groq"), stub.BaseUrl));
        var secondRun = stub.Snapshot().Skip(before).ToList();
        Assert.NotEmpty(secondRun);
        Assert.All(secondRun, r => Assert.Equal("Bearer GOODKEY", r.Authorization));
    }

    [Fact]
    public async Task Rotation_Gemini429_SucceedsViaSecondKey()
    {
        AppSecrets.SetKeys("gemini", new[] { "GEMLIMIT", "GEMGOOD" });
        var stub = new StubHandler();
        stub.Handler = req =>
        {
            if (req.GoogleKey == "GEMLIMIT")
                return new StubResponse(429,
                    "{\"error\":{\"status\":\"RESOURCE_EXHAUSTED\"}}");
            Assert.Equal("GEMGOOD", req.GoogleKey);
            var numbers = BlockNumbers(PromptOf(req, ProviderKind.Gemini));
            return GeminiOk(TranslationsJson(numbers, n => $"G-{n}"));
        };
        var result = await Svc(stub).TranslatePageAsync(
            new[] { Block("a") }, Options(Provider("gemini"), stub.BaseUrl));
        Assert.Single(result.Translations);
        Assert.Equal(2, result.UsedKeyNumber);
        Assert.Equal(1, AppSecrets.GetStatus("gemini").Cooling);
    }

    [Fact]
    public async Task Rotation_AllKeys429_HonestExhaustion()
    {
        AppSecrets.SetKeys("groq", new[] { "L1", "L2" });
        var stub = new StubHandler();
        stub.Handler = _ => new StubResponse(429, "{\"error\":\"quota\"}");
        var ex = await Assert.ThrowsAsync<AllKeysExhaustedException>(() =>
            Svc(stub).TranslatePageAsync(
                new[] { Block("a") }, Options(Provider("groq"), stub.BaseUrl)));
        Assert.Contains("Semua key", ex.Message);
        Assert.Equal(2, stub.Snapshot().Count); // kedua key dicoba
    }

    [Fact]
    public async Task Rotation_401BadKey_MarkedBadAndSkipped()
    {
        AppSecrets.SetKeys("openrouter", new[] { "BADKEY", "GOODKEY" });
        var stub = new StubHandler();
        stub.Handler = req =>
        {
            if (req.Authorization == "Bearer BADKEY")
                return new StubResponse(401, "{\"error\":\"invalid key\"}");
            var numbers = BlockNumbers(PromptOf(req, ProviderKind.OpenAiCompatible));
            return OpenAiOk(TranslationsJson(numbers, n => $"OR-{n}"));
        };
        var result = await Svc(stub).TranslatePageAsync(
            new[] { Block("a") }, Options(Provider("openrouter"), stub.BaseUrl));
        Assert.Single(result.Translations);
        Assert.Equal(2, result.UsedKeyNumber);
        Assert.Equal(1, AppSecrets.GetStatus("openrouter").Bad);
    }

    [Fact]
    public async Task NoRotation_On400_ReportedAsIs()
    {
        AppSecrets.SetKeys("groq", new[] { "K1", "K2" });
        var stub = new StubHandler();
        stub.Handler = _ => new StubResponse(400, "{\"error\":\"bad request\"}");
        var ex = await Assert.ThrowsAsync<TranslateException>(() =>
            Svc(stub).TranslatePageAsync(
                new[] { Block("a") }, Options(Provider("groq"), stub.BaseUrl)));
        Assert.IsNotType<AllKeysExhaustedException>(ex);
        Assert.Contains("HTTP 400", ex.Message);
        Assert.Single(stub.Snapshot()); // tanpa rotasi: satu permintaan saja
    }

    [Fact]
    public async Task MissingKey_ClearError_NoRequest()
    {
        var stub = new StubHandler();
        stub.Handler = SuccessHandler(ProviderKind.Gemini);
        var ex = await Assert.ThrowsAsync<TranslateException>(() =>
            Svc(stub).TranslatePageAsync(
                new[] { Block("a") }, Options(Provider("gemini"), stub.BaseUrl)));
        Assert.Contains("belum diisi", ex.Message);
        Assert.Empty(stub.Snapshot());
    }

    // ── Bab: lewati + simpan per halaman ────────────────────────

    [Fact]
    public async Task Chapter_SkipsTranslated_RetriesFailed_SavesPerPage()
    {
        var stub = new StubHandler();
        var requestedTexts = new List<string>();
        stub.Handler = req =>
        {
            var prompt = PromptOf(req, ProviderKind.OpenAiCompatible);
            lock (requestedTexts) requestedTexts.Add(prompt);
            var numbers = BlockNumbers(prompt);
            return OpenAiOk(TranslationsJson(numbers, n => $"Baru-{n}"));
        };
        var doneBlock = Block("SUMBER-A");
        doneBlock.Translation = "SudahAda";
        var pendingBlock = Block("SUMBER-B");
        var failedBlock = Block("SUMBER-C");
        failedBlock.TranslationError = "gagal sebelumnya";
        var chapter = new ProjectChapter { SourcePath = "/tmp/bab.cbz" };
        var p1 = chapter.PageAt(0);
        p1.Blocks.AddRange(new[] { doneBlock, pendingBlock });
        var p2 = chapter.PageAt(1);
        p2.Blocks.Add(failedBlock);

        int saves = 0;
        bool page1SavedWithB = false;
        var summary = await Svc(stub).TranslateChapterAsync(
            chapter, Options(Provider("ollama"), stub.BaseUrl),
            progress: null,
            onPageDone: page =>
            {
                saves++;
                if (page.Index == 0
                    && !string.IsNullOrWhiteSpace(pendingBlock.Translation))
                    page1SavedWithB = true;
            });

        Assert.Equal(2, saves); // simpan dipanggil per halaman
        Assert.True(page1SavedWithB);
        Assert.Equal(1, summary.Skipped);
        Assert.Equal(2, summary.Translated);
        Assert.Equal(0, summary.Failed);
        Assert.Equal("SudahAda", doneBlock.Translation); // tidak ditimpa
        Assert.False(string.IsNullOrWhiteSpace(pendingBlock.Translation));
        Assert.False(string.IsNullOrWhiteSpace(failedBlock.Translation));
        Assert.Null(failedBlock.TranslationError); // gagal lama teratasi
        var allPrompts = string.Join("\n", requestedTexts);
        Assert.DoesNotContain("SUMBER-A", allPrompts); // tidak membakar kuota
        Assert.Contains("SUMBER-B", allPrompts);
        Assert.Contains("SUMBER-C", allPrompts);
    }

    // ── Tunggu & lanjut + jeda ──────────────────────────────────

    [Fact]
    public async Task Chapter_WaitsForRecovery_RetryAfterHonored_Completes()
    {
        AppSecrets.SetKeys("groq", new[] { "ONLYKEY" });
        var stub = new StubHandler();
        int calls = 0;
        stub.Handler = req =>
        {
            if (Interlocked.Increment(ref calls) == 1)
                return new StubResponse(429, "{\"error\":\"quota\"}",
                    RetryAfterSeconds: 1);
            var numbers = BlockNumbers(PromptOf(req, ProviderKind.OpenAiCompatible));
            return OpenAiOk(TranslationsJson(numbers, n => $"Pulih-{n}"));
        };
        var waits = new List<(TimeSpan, int)>();
        // Cooldown suntik dibuat BESAR (30 dtk): bila Retry-After
        // tidak dihormati, tes ini akan jauh lebih lambat.
        var service = Svc(stub, TimeSpan.FromSeconds(30));
        service.WaitForKeyRecovery = true;
        service.WaitCallback = (remaining, key) =>
        {
            lock (waits) waits.Add((remaining, key));
        };
        var chapter = new ProjectChapter { SourcePath = "/tmp/bab.cbz" };
        var page = chapter.PageAt(0);
        var block = Block("a");
        page.Blocks.Add(block);

        var sw = Stopwatch.StartNew();
        var summary = await service.TranslateChapterAsync(
            chapter, Options(Provider("groq"), stub.BaseUrl));
        sw.Stop();

        Assert.Equal(1, summary.Translated);
        Assert.Equal("Pulih-1", block.Translation);
        Assert.NotEmpty(waits);
        Assert.Equal(1, waits[0].Item2); // menunggu key #1
        _out.WriteLine($"tunggu-lanjut selesai dalam {sw.Elapsed.TotalSeconds:F1} dtk");
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10),
            $"Retry-After 1 dtk harus dihormati (aktual {sw.Elapsed}).");
    }

    [Fact]
    public async Task Chapter_DelayBetweenPages_Applied()
    {
        var stub = new StubHandler();
        stub.Handler = SuccessHandler(ProviderKind.OpenAiCompatible);
        var chapter = new ProjectChapter { SourcePath = "/tmp/bab.cbz" };
        chapter.PageAt(0).Blocks.Add(Block("p1"));
        chapter.PageAt(1).Blocks.Add(Block("p2"));
        await Svc(stub).TranslateChapterAsync(chapter,
            Options(Provider("ollama"), stub.BaseUrl,
                delay: TimeSpan.FromSeconds(1)));
        var reqs = stub.Snapshot();
        Assert.Equal(2, reqs.Count);
        var gap = reqs[1].At - reqs[0].At;
        _out.WriteLine($"jeda antar halaman terukur: {gap.TotalSeconds:F2} dtk");
        Assert.True(gap >= TimeSpan.FromMilliseconds(850),
            $"Jeda 1 dtk harus diterapkan (terukur {gap}).");
    }

    [Fact]
    public async Task Page_RetryDelay_Applied()
    {
        var stub = new StubHandler();
        int call = 0;
        stub.Handler = req =>
        {
            call++;
            if (call == 1)
                return OpenAiOk(TranslationsJson(new[] { 1 }, n => "Satu"));
            return OpenAiOk(TranslationsJson(new[] { 2 }, n => "Dua"));
        };
        await Svc(stub).TranslatePageAsync(
            new[] { Block("a"), Block("b") },
            Options(Provider("ollama"), stub.BaseUrl,
                delay: TimeSpan.FromSeconds(1)));
        var reqs = stub.Snapshot();
        Assert.Equal(2, reqs.Count);
        var gap = reqs[1].At - reqs[0].At;
        Assert.True(gap >= TimeSpan.FromMilliseconds(850),
            $"Jeda sebelum coba-ulang harus diterapkan (terukur {gap}).");
    }

    // ── Mode per blok ───────────────────────────────────────────

    [Fact]
    public async Task SingleBlock_UsesPageContext_AppliesOnlyTarget()
    {
        var stub = new StubHandler();
        string? seenPrompt = null;
        stub.Handler = req =>
        {
            seenPrompt = PromptOf(req, ProviderKind.OpenAiCompatible);
            return SuccessHandler(ProviderKind.OpenAiCompatible)(req);
        };
        var b1 = Block("SATU");
        var b2 = Block("DUA");
        var b3 = Block("TIGA");
        var page = new ProjectPage();
        page.Blocks.AddRange(new[] { b1, b2, b3 });
        var result = await Svc(stub).TranslateSingleBlockAsync(
            page.Blocks, b2, Options(Provider("ollama"), stub.BaseUrl));
        Assert.Contains("SATU", seenPrompt);
        Assert.Contains("DUA", seenPrompt);
        Assert.Contains("TIGA", seenPrompt); // konteks halaman utuh
        Assert.Single(result.Translations);
        TranslateService.ApplyResult(page, result);
        Assert.Null(b1.Translation);
        Assert.Equal("Terjemahan-2", b2.Translation);
        Assert.Null(b3.Translation);
    }

    // ── Poles AI ────────────────────────────────────────────────

    [Fact]
    public async Task Polish_MapsResult_SetsBackup_UndoRestoresExactly()
    {
        AppSecrets.SetKeys("groq", new[] { "POLKEY" });
        var stub = new StubHandler();
        stub.Handler = req =>
        {
            var prompt = PromptOf(req, ProviderKind.OpenAiCompatible);
            Assert.Contains("SUMBER:", prompt);
            Assert.Contains("TERJEMAHAN: Kasar-1", prompt);
            var numbers = BlockNumbers(prompt);
            return OpenAiOk(TranslationsJson(numbers, n => $"Poles-{n}"));
        };
        var block = Block("こんにちは");
        block.Translation = "Kasar-1";
        block.TranslatedBy = "Groq • stub-model • key #1";
        var page = new ProjectPage();
        page.Blocks.Add(block);

        var result = await Svc(stub).PolishPageAsync(
            page.Blocks, Options(Provider("groq"), stub.BaseUrl));
        TranslateService.ApplyPolish(page, result);

        Assert.Equal("Poles-1", block.Translation);
        Assert.Equal("Kasar-1", block.TranslationBackup);
        Assert.Contains("poles", block.TranslatedBy);
        Assert.Contains("Groq", block.TranslatedBy);

        Assert.True(TranslateService.UndoPolish(block));
        Assert.Equal("Kasar-1", block.Translation);
        Assert.Equal("Groq • stub-model • key #1", block.TranslatedBy);
        Assert.Null(block.TranslationBackup);
        Assert.False(TranslateService.UndoPolish(block));
    }

    [Fact]
    public async Task Polish_SkipsBlocksWithoutTranslation()
    {
        var stub = new StubHandler();
        string? seenPrompt = null;
        stub.Handler = req =>
        {
            seenPrompt = PromptOf(req, ProviderKind.OpenAiCompatible);
            return SuccessHandler(ProviderKind.OpenAiCompatible,
                n => $"Halus-{n}")(req);
        };
        var withT = Block("ADA-SUMBER");
        withT.Translation = "Ada terjemahan";
        var withoutT = Block("KOSONG-SUMBER");
        var page = new ProjectPage();
        page.Blocks.AddRange(new[] { withT, withoutT });
        var result = await Svc(stub).PolishPageAsync(
            page.Blocks, Options(Provider("ollama"), stub.BaseUrl));
        Assert.Single(result.Translations);
        Assert.Contains("ADA-SUMBER", seenPrompt);
        Assert.DoesNotContain("KOSONG-SUMBER", seenPrompt);
        TranslateService.ApplyPolish(page, result);
        Assert.Equal("Halus-1", withT.Translation);
        Assert.Null(withoutT.Translation);
        Assert.Null(withoutT.TranslationError);
    }

    [Fact]
    public async Task PolishChapter_SkipsManualAndUntranslated()
    {
        var stub = new StubHandler();
        var prompts = new List<string>();
        stub.Handler = req =>
        {
            var prompt = PromptOf(req, ProviderKind.OpenAiCompatible);
            lock (prompts) prompts.Add(prompt);
            return SuccessHandler(ProviderKind.OpenAiCompatible,
                n => $"Halus-{n}")(req);
        };
        var auto = Block("OTOMATIS");
        auto.Translation = "Hasil mesin";
        auto.TranslatedBy = "Ollama (lokal) • stub-model";
        var manual = Block("TANGAN");
        manual.Translation = "Hasil tangan";
        manual.TranslatedBy = "Manual";
        var empty = Block("BELUM");
        var chapter = new ProjectChapter { SourcePath = "/tmp/bab.cbz" };
        var page = chapter.PageAt(0);
        page.Blocks.AddRange(new[] { auto, manual, empty });

        var summary = await Svc(stub).PolishChapterAsync(
            chapter, Options(Provider("ollama"), stub.BaseUrl));
        Assert.Equal(1, summary.Polished);
        Assert.Equal(2, summary.Skipped); // manual + kosong
        Assert.Equal("Halus-1", auto.Translation);
        Assert.Equal("Hasil tangan", manual.Translation); // tak disentuh
        var all = string.Join("\n", prompts);
        Assert.Contains("OTOMATIS", all);
        Assert.DoesNotContain("TANGAN", all);
    }

    // ── Proyek lama & CLI ───────────────────────────────────────

    [Fact]
    public void Project_LoadsV3_TranslationFieldsEmpty()
    {
        var path = Path.Combine(_configDir, "v3.tinta.json");
        File.WriteAllText(path, """
            {
              "format": "tinta-studio-project",
              "version": 3,
              "name": "Proyek v3",
              "chapters": [
                { "sourcePath": "/tmp/bab.cbz", "title": "Bab",
                  "format": "ZIP (CBZ)", "pageCount": 1,
                  "pages": [
                    { "index": 0, "blocks": [
                      { "id": "abc", "x": 1, "y": 2, "width": 30,
                        "height": 40, "direction": "horizontal",
                        "language": "ja", "text": "x",
                        "confidence": 0.5, "detectionScore": 0.6 }
                    ], "cleanOps": [], "manualStrokes": [], "cleanSeq": 0 }
                  ] }
              ]
            }
            """);
        var loaded = TintaProject.Load(path);
        Assert.Equal(3, loaded.Version);
        var block = loaded.Chapters[0].Pages[0].Blocks[0];
        Assert.Null(block.Translation);
        Assert.Null(block.TranslatedBy);
        Assert.Null(block.TranslationBackup);
    }

    [Fact]
    public void Cli_Translate_MissingKey_ClearError()
    {
        var testBin = Path.GetDirectoryName(typeof(TranslateTests).Assembly.Location)!;
        var config = new DirectoryInfo(testBin).Parent?.Name ?? "Debug";
        var root = Path.GetFullPath(Path.Combine(testBin, "..", "..", "..", ".."));
        var appDll = Path.Combine(root, "bin", config, "net10.0", "TintaStudio.dll");
        Skip.If(!File.Exists(appDll), $"DLL aplikasi belum dibangun: {appDll}");
        var dir = Path.Combine(Path.GetTempPath(),
            "tinta-cli-nokey-" + Guid.NewGuid().ToString("N"));
        using var pageBmp = MangaFixture.JapanesePage();
        var cbz = MangaFixture.MakeCbz(pageBmp, dir);
        var psi = new ProcessStartInfo("dotnet",
            $"\"{appDll}\" --translate \"{cbz}\" --provider gemini")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.Environment["TINTA_CONFIG_DIR"] = _configDir; // tanpa key apa pun
        using var proc = Process.Start(psi)!;
        var stdout = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit(120_000);
        _out.WriteLine(stdout + stderr);
        Assert.Equal(2, proc.ExitCode);
        Assert.Contains("belum diisi", stderr);
        Directory.Delete(dir, recursive: true);
    }

    /// <summary>Probe: apakah proses .NET di mesin ini boleh membuka
    /// koneksi TCP loopback sungguhan? (Di sandbox VM ini: tidak —
    /// kebijakan other_tcp Deny membalas banner penolakan.)</summary>
    private static bool LoopbackTcpFeasible()
    {
        try
        {
            var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            int port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            var listener = new HttpListener();
            listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            listener.Start();
            var serverTask = Task.Run(() =>
            {
                try
                {
                    var ctx = listener.GetContext();
                    var bytes = Encoding.UTF8.GetBytes("{}");
                    ctx.Response.ContentLength64 = bytes.Length;
                    ctx.Response.OutputStream.Write(bytes);
                    ctx.Response.Close();
                }
                catch { }
            });
            using var http = new HttpClient(
                new HttpClientHandler { UseProxy = false })
            { Timeout = TimeSpan.FromSeconds(4) };
            var resp = http.GetAsync($"http://127.0.0.1:{port}/")
                .GetAwaiter().GetResult();
            listener.Stop();
            serverTask.Wait(2000);
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    [SkippableFact]
    public void Cli_Translate_ViaLoopbackStub_SavesTranslations()
    {
        Skip.If(!LoopbackTcpFeasible(),
            "Koneksi TCP loopback dari proses .NET diblokir kebijakan " +
            "sandbox VM ini (other_tcp Deny) — jalur layanan terverifikasi " +
            "penuh via stub handler dalam-proses; CLI diuji pada jalur " +
            "key-kosong di sini dan stub loopback di mesin normal.");
        var missing = ModelStore.MissingFor(OcrLanguage.Japanese);
        Skip.If(missing.Count > 0,
            "Model OCR belum diunduh: " + string.Join(", ", missing.Select(m => m.Id)));
        var testBin = Path.GetDirectoryName(typeof(TranslateTests).Assembly.Location)!;
        var config = new DirectoryInfo(testBin).Parent?.Name ?? "Debug";
        var root = Path.GetFullPath(Path.Combine(testBin, "..", "..", "..", ".."));
        var appDll = Path.Combine(root, "bin", config, "net10.0", "TintaStudio.dll");
        Skip.If(!File.Exists(appDll), $"DLL aplikasi belum dibangun: {appDll}");

        // Server loopback sungguhan (hanya tercapai bila probe lolos).
        var listener = new HttpListener();
        var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();
        using var serverCts = new CancellationTokenSource();
        var serverTask = Task.Run(() =>
        {
            while (!serverCts.IsCancellationRequested)
            {
                HttpListenerContext ctx;
                try { ctx = listener.GetContext(); } catch { break; }
                try
                {
                    string body;
                    using (var reader = new StreamReader(
                               ctx.Request.InputStream, Encoding.UTF8))
                        body = reader.ReadToEnd();
                    using var doc = JsonDocument.Parse(body);
                    var prompt = doc.RootElement.GetProperty("messages")[0]
                        .GetProperty("content").GetString() ?? "";
                    var numbers = BlockNumbers(prompt);
                    var content = TranslationsJson(numbers, n => $"Terjemahan-{n}");
                    var payload = JsonSerializer.Serialize(new
                    {
                        choices = new[]
                        {
                            new { message = new { role = "assistant", content } },
                        },
                    });
                    var bytes = Encoding.UTF8.GetBytes(payload);
                    ctx.Response.ContentType = "application/json";
                    ctx.Response.ContentLength64 = bytes.Length;
                    ctx.Response.OutputStream.Write(bytes);
                    ctx.Response.Close();
                }
                catch { try { ctx.Response.Abort(); } catch { } }
            }
        });

        try
        {
            var settings = new AppSettings
            {
                Provider = "ollama",
                TargetLanguage = "id",
                RequestDelaySeconds = 0,
            };
            settings.Providers["ollama"] = new ProviderSettings
            {
                BaseUrl = $"http://127.0.0.1:{port}/v1",
                Model = "stub-model",
            };
            AppConfig.SaveSettings(settings);

            var dir = Path.Combine(Path.GetTempPath(),
                "tinta-cli-translate-" + Guid.NewGuid().ToString("N"));
            using var pageBmp = MangaFixture.JapanesePage();
            var cbz = MangaFixture.MakeCbz(pageBmp, dir);
            var psi = new ProcessStartInfo("dotnet",
                $"\"{appDll}\" --translate \"{cbz}\" --provider ollama")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            psi.Environment["TINTA_CONFIG_DIR"] = _configDir;
            using var proc = Process.Start(psi)!;
            var stdout = proc.StandardOutput.ReadToEnd();
            var stderr = proc.StandardError.ReadToEnd();
            proc.WaitForExit(300_000);
            _out.WriteLine(stdout);
            _out.WriteLine(stderr);
            Assert.Equal(0, proc.ExitCode);
            Assert.Contains("blok diterjemahkan", stdout);
            Assert.Contains("dilewati", stdout);

            var projectPath = ProjectStore.PathFor(cbz);
            Assert.True(File.Exists(projectPath), "Proyek harus tersimpan.");
            var loaded = TintaProject.Load(projectPath);
            var allBlocks = loaded.Chapters[0].Pages
                .SelectMany(p => p.Blocks).ToList();
            Assert.NotEmpty(allBlocks);
            Assert.All(allBlocks, b =>
                Assert.False(string.IsNullOrWhiteSpace(b.Translation)));
            Assert.All(allBlocks, b =>
                Assert.Contains("Ollama", b.TranslatedBy));
            Directory.Delete(dir, recursive: true);
        }
        finally
        {
            serverCts.Cancel();
            try { listener.Stop(); } catch { }
            try { serverTask.Wait(2000); } catch { }
            listener.Close();
        }
    }
}
