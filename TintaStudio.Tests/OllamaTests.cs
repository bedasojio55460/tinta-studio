using System.Net;
using System.Text;
using TintaStudio.Services;
using Xunit;
using Xunit.Abstractions;

namespace TintaStudio.Tests;

/// <summary>
/// Tes Instal Instan Ollama (v0.7.11): parsing murni /api/tags dan
/// progres pull JSONL, pemetaan tiga keadaan status dari probe
/// tersimulasi (HttpMessageHandler stub dalam-proses + pencari
/// executable tiruan — TANPA soket, sesuai kebijakan sandbox VM),
/// alur probe→keadaan lewat layanan asli di atas stub, progres
/// pull ujung-ke-ujung di tingkat transport, dan keutuhan daftar
/// kurasi.
/// </summary>
public sealed class OllamaTests
{
    private readonly ITestOutputHelper _out;

    public OllamaTests(ITestOutputHelper output) => _out = output;

    private const string TagsJson = """
        {
          "models": [
            {
              "name": "qwen2.5:7b",
              "model": "qwen2.5:7b",
              "modified_at": "2026-10-30T10:15:00Z",
              "size": 4683087332,
              "digest": "abc123",
              "details": { "family": "qwen2", "parameter_size": "7.6B", "quantization_level": "Q4_K_M" }
            },
            {
              "name": "llama3.1:latest",
              "model": "llama3.1:latest",
              "modified_at": "2026-10-29T09:00:00Z",
              "size": 4920753328,
              "digest": "def456",
              "details": { "family": "llama", "parameter_size": "8.0B", "quantization_level": "Q4_K_M" }
            }
          ]
        }
        """;

    // ── Stub transport ──────────────────────────────────────────

    /// <summary>Stub HTTP dalam-memori untuk API manajemen Ollama:
    /// rute per (method, path); tubuh respons bisa berupa aliran
    /// baris (untuk pull JSONL).</summary>
    private sealed class OllamaStubHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, string, HttpResponseMessage> Handler { get; set; } =
            (_, _) => new HttpResponseMessage(HttpStatusCode.InternalServerError);
        public List<(string Method, string Path, string Body)> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? ""
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request.Method.Method,
                request.RequestUri?.AbsolutePath ?? "", body));
            return Handler(request, body);
        }
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode code = HttpStatusCode.OK) =>
        new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage JsonLines(IEnumerable<string> lines) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(string.Join("\n", lines) + "\n",
                Encoding.UTF8, "application/x-ndjson"),
        };

    // ── Parsing /api/tags ───────────────────────────────────────

    [Fact]
    public void ParseTags_DuaModel_NamaDanUkuran()
    {
        var models = OllamaService.ParseTags(TagsJson);
        Assert.Equal(2, models.Count);
        Assert.Equal("qwen2.5:7b", models[0].Name);
        Assert.Equal(4683087332L, models[0].SizeBytes);
        Assert.Equal("llama3.1:latest", models[1].Name);
        Assert.Equal(4920753328L, models[1].SizeBytes);
    }

    [Fact]
    public void ParseTags_KosongAtauRusak_DaftarKosong()
    {
        Assert.Empty(OllamaService.ParseTags(""));
        Assert.Empty(OllamaService.ParseTags("   "));
        Assert.Empty(OllamaService.ParseTags("bukan json {"));
        Assert.Empty(OllamaService.ParseTags("{}"));
        Assert.Empty(OllamaService.ParseTags("""{"models": []}"""));
        Assert.Empty(OllamaService.ParseTags("""{"models": [{"size": 5}]}"""));
        // model tanpa size tetap terbaca dengan ukuran 0
        var one = OllamaService.ParseTags("""{"models": [{"name": "m:1"}]}""");
        Assert.Single(one);
        Assert.Equal(0L, one[0].SizeBytes);
    }

    // ── Parsing progres pull JSONL ──────────────────────────────

    [Fact]
    public void ParsePullLine_PersenDariCompletedTotal()
    {
        var p = OllamaService.ParsePullLine(
            """{"status":"pulling 9f6a","digest":"sha256:9f6a","total":4683087332,"completed":2341543666}""");
        Assert.NotNull(p);
        Assert.Equal("pulling 9f6a", p!.Status);
        Assert.NotNull(p.Percent);
        Assert.InRange(p.Percent!.Value, 49.9, 50.1);
        Assert.Null(p.Error);
        Assert.False(p.IsSuccess);

        var full = OllamaService.ParsePullLine(
            """{"status":"pulling x","total":200,"completed":200}""");
        Assert.Equal(100.0, full!.Percent!.Value);

        // completed melebihi total → dijepit ke 100
        var over = OllamaService.ParsePullLine(
            """{"status":"pulling x","total":200,"completed":250}""");
        Assert.Equal(100.0, over!.Percent!.Value);
    }

    [Fact]
    public void ParsePullLine_TanpaPersen_StatusDanSukses()
    {
        var verifying = OllamaService.ParsePullLine("""{"status":"verifying sha256 digest"}""");
        Assert.NotNull(verifying);
        Assert.Null(verifying!.Percent);

        var sukses = OllamaService.ParsePullLine("""{"status":"success"}""");
        Assert.True(sukses!.IsSuccess);

        // total 0 → persen tidak dihitung (hindari bagi-nol)
        var nol = OllamaService.ParsePullLine(
            """{"status":"pulling x","total":0,"completed":0}""");
        Assert.Null(nol!.Percent);
    }

    [Fact]
    public void ParsePullLine_GalatDanBarisSampah()
    {
        var err = OllamaService.ParsePullLine(
            """{"error":"pull model manifest: file does not exist"}""");
        Assert.NotNull(err);
        Assert.Contains("file does not exist", err!.Error);

        Assert.Null(OllamaService.ParsePullLine(""));
        Assert.Null(OllamaService.ParsePullLine("   "));
        Assert.Null(OllamaService.ParsePullLine("ini bukan json"));
    }

    // ── Pemetaan keadaan ────────────────────────────────────────

    [Fact]
    public void MapState_TigaKeadaan()
    {
        Assert.Equal(OllamaState.Running, OllamaService.MapState(true, false));
        Assert.Equal(OllamaState.Running, OllamaService.MapState(true, true));
        Assert.Equal(OllamaState.InstalledNotRunning, OllamaService.MapState(false, true));
        Assert.Equal(OllamaState.NotInstalled, OllamaService.MapState(false, false));
    }

    [Fact]
    public async Task Probe_ServerMenjawab_RunningDenganDaftarModel()
    {
        var stub = new OllamaStubHandler
        {
            Handler = (req, _) => req.RequestUri!.AbsolutePath == "/api/tags"
                ? Json(TagsJson)
                : new HttpResponseMessage(HttpStatusCode.NotFound),
        };
        var svc = new OllamaService(new HttpClient(stub),
            findExecutable: () => null);
        var probe = await svc.ProbeAsync();
        Assert.Equal(OllamaState.Running, probe.State);
        Assert.Equal(2, probe.Models.Count);
        Assert.Contains(probe.Models, m => m.Name == "qwen2.5:7b");
    }

    [Fact]
    public async Task Probe_ServerMati_ExecutableAda_TerpasangBelumJalan()
    {
        var stub = new OllamaStubHandler
        {
            Handler = (_, _) => throw new HttpRequestException("connection refused"),
        };
        var svc = new OllamaService(new HttpClient(stub),
            findExecutable: () => @"C:\Users\Uji\AppData\Local\Programs\Ollama\ollama.exe");
        var probe = await svc.ProbeAsync();
        Assert.Equal(OllamaState.InstalledNotRunning, probe.State);
        Assert.EndsWith("ollama.exe", probe.ExecutablePath);
        Assert.Empty(probe.Models);
    }

    [Fact]
    public async Task Probe_ServerMati_TanpaExecutable_BelumTerpasang()
    {
        var stub = new OllamaStubHandler
        {
            Handler = (_, _) => Json("""{"error":"tidak"}""",
                HttpStatusCode.InternalServerError),
        };
        var svc = new OllamaService(new HttpClient(stub), findExecutable: () => null);
        var probe = await svc.ProbeAsync();
        Assert.Equal(OllamaState.NotInstalled, probe.State);
        Assert.Null(probe.ExecutablePath);
    }

    // ── Alur pull lewat transport stub ──────────────────────────

    [Fact]
    public async Task PullModel_AliranJsonl_ProgresBerurutanDanTuntas()
    {
        var lines = new[]
        {
            """{"status":"pulling manifest"}""",
            """{"status":"pulling a1b2","digest":"sha256:a1b2","total":1000,"completed":250}""",
            """{"status":"pulling a1b2","digest":"sha256:a1b2","total":1000,"completed":1000}""",
            """{"status":"verifying sha256 digest"}""",
            """{"status":"success"}""",
        };
        var stub = new OllamaStubHandler { Handler = (_, _) => JsonLines(lines) };
        var svc = new OllamaService(new HttpClient(stub), findExecutable: () => null);
        var seen = new List<PullProgress>();
        await svc.PullModelAsync("qwen2.5:7b", seen.Add);

        Assert.Equal(5, seen.Count);
        Assert.Equal(25.0, seen[1].Percent!.Value);
        Assert.Equal(100.0, seen[2].Percent!.Value);
        Assert.True(seen[^1].IsSuccess);
        // permintaan benar-benar POST /api/pull berisi nama model
        var req = Assert.Single(stub.Requests);
        Assert.Equal("POST", req.Method);
        Assert.Equal("/api/pull", req.Path);
        Assert.Contains("qwen2.5:7b", req.Body);
    }

    [Fact]
    public async Task PullModel_BarisGalat_MelemparPesanManusiawi()
    {
        var stub = new OllamaStubHandler
        {
            Handler = (_, _) => JsonLines(new[]
            {
                """{"status":"pulling manifest"}""",
                """{"error":"pull model manifest: file does not exist"}""",
            }),
        };
        var svc = new OllamaService(new HttpClient(stub), findExecutable: () => null);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.PullModelAsync("model-ngawur:1b"));
        Assert.Contains("file does not exist", ex.Message);
    }

    [Fact]
    public async Task DeleteModel_MengirimDeleteKeApiDelete()
    {
        var stub = new OllamaStubHandler
        {
            Handler = (_, _) => new HttpResponseMessage(HttpStatusCode.OK),
        };
        var svc = new OllamaService(new HttpClient(stub), findExecutable: () => null);
        await svc.DeleteModelAsync("qwen2.5:3b");
        var req = Assert.Single(stub.Requests);
        Assert.Equal("DELETE", req.Method);
        Assert.Equal("/api/delete", req.Path);
        Assert.Contains("qwen2.5:3b", req.Body);
    }

    // ── Kurasi & pencocokan nama ────────────────────────────────

    [Fact]
    public void Kurasi_LimaModel_NamaDanLabelUkuranUtuh()
    {
        var curated = OllamaService.CuratedModels;
        Assert.Equal(5, curated.Count);
        Assert.Equal(
            new[] { "qwen2.5:7b", "qwen2.5:14b", "qwen2.5:3b", "llama3.1:8b", "gemma3:12b" },
            curated.Select(c => c.Name).ToArray());
        Assert.All(curated, c =>
        {
            Assert.False(string.IsNullOrWhiteSpace(c.SizeLabel));
            Assert.Contains("GB", c.SizeLabel);
            Assert.False(string.IsNullOrWhiteSpace(c.Note));
        });
        Assert.Contains("Saran utama", curated[0].Note);
        _out.WriteLine(string.Join(" | ", curated.Select(c => $"{c.Name} {c.SizeLabel}")));
    }

    [Fact]
    public void ModelMatches_AliasLatestDanTagPersis()
    {
        Assert.True(OllamaService.ModelMatches("llama3.1:latest", "llama3.1"));
        Assert.True(OllamaService.ModelMatches("llama3.1", "llama3.1:latest"));
        Assert.True(OllamaService.ModelMatches("qwen2.5:7b", "qwen2.5:7b"));
        Assert.False(OllamaService.ModelMatches("qwen2.5:7b", "qwen2.5:14b"));
        Assert.False(OllamaService.ModelMatches("llama3.1:latest", "llama3.1:8b"));
    }

    [Fact]
    public void FormatSize_LabelIndonesia()
    {
        Assert.Equal("4,7 GB", OllamaService.FormatSize(4_683_087_332));
        Assert.Equal("500 MB", OllamaService.FormatSize(500_000_000));
        Assert.Equal("—", OllamaService.FormatSize(0));
    }
}
