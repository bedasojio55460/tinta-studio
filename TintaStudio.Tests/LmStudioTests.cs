using System.Net;
using System.Text;
using TintaStudio.Services;
using Xunit;
using Xunit.Abstractions;

namespace TintaStudio.Tests;

/// <summary>
/// Tes status LM Studio (v0.7.13): parsing daftar model dari
/// contoh JSON GET /v1/models (format kompatibel OpenAI),
/// normalisasi Base URL, pemetaan tiga keadaan, dan alur probe
/// ujung-ke-ujung lewat HttpMessageHandler stub dalam-proses +
/// pencari executable tiruan — TANPA soket, sesuai kebijakan
/// sandbox VM (mengikuti pola OllamaTests).
/// </summary>
public sealed class LmStudioTests
{
    private readonly ITestOutputHelper _out;

    public LmStudioTests(ITestOutputHelper output) => _out = output;

    private const string ModelsJson = """
        {
          "data": [
            { "id": "qwen2.5-7b-instruct", "object": "model", "owned_by": "organization" },
            { "id": "llama-3.1-8b-instruct", "object": "model", "owned_by": "organization" }
          ],
          "object": "list"
        }
        """;

    // ── Stub transport ──────────────────────────────────────────

    /// <summary>Stub HTTP dalam-memori: membalas lewat fungsi yang
    /// dipasang tes (atau melempar untuk mensimulasikan jaringan
    /// mati) dan mencatat URL permintaan terakhir.</summary>
    private sealed class LmStubHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage> Handler { get; set; } =
            _ => new HttpResponseMessage(HttpStatusCode.InternalServerError);
        public string? LastUrl { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastUrl = request.RequestUri?.ToString();
            return Task.FromResult(Handler(request));
        }
    }

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    // ── Parsing murni ───────────────────────────────────────────

    [Fact]
    public void ParseModels_ContohJson_DaftarIdBerurutan()
    {
        var models = LmStudioService.ParseModels(ModelsJson);
        Assert.Equal(2, models.Count);
        Assert.Equal("qwen2.5-7b-instruct", models[0].Id);
        Assert.Equal("llama-3.1-8b-instruct", models[1].Id);
    }

    [Fact]
    public void ParseModels_KosongAtauRusak_DaftarKosong()
    {
        Assert.Empty(LmStudioService.ParseModels("{\"data\":[]}"));
        Assert.Empty(LmStudioService.ParseModels("{}"));
        Assert.Empty(LmStudioService.ParseModels("bukan json"));
        Assert.Empty(LmStudioService.ParseModels(""));
        // Entri tanpa id dilewati, entri sah tetap masuk.
        var campur = LmStudioService.ParseModels(
            "{\"data\":[{\"object\":\"model\"},{\"id\":\"sah\"}]}");
        var satu = Assert.Single(campur);
        Assert.Equal("sah", satu.Id);
    }

    [Fact]
    public void NormalizeBase_BawaanDanPotongMiring()
    {
        Assert.Equal("http://localhost:1234/v1", LmStudioService.NormalizeBase(null));
        Assert.Equal("http://localhost:1234/v1", LmStudioService.NormalizeBase("  "));
        Assert.Equal("http://localhost:1234/v1",
            LmStudioService.NormalizeBase("http://localhost:1234/v1/"));
        Assert.Equal("http://192.168.1.9:1234/v1",
            LmStudioService.NormalizeBase("http://192.168.1.9:1234/v1"));
    }

    [Fact]
    public void MapState_TigaKeadaan()
    {
        Assert.Equal(LmStudioState.Running, LmStudioService.MapState(true, false));
        Assert.Equal(LmStudioState.Running, LmStudioService.MapState(true, true));
        Assert.Equal(LmStudioState.InstalledNotRunning,
            LmStudioService.MapState(false, true));
        Assert.Equal(LmStudioState.NotInstalled,
            LmStudioService.MapState(false, false));
    }

    // ── Probe lewat stub ────────────────────────────────────────

    [Fact]
    public async Task Probe_ServerMenjawab_RunningDenganModel()
    {
        var stub = new LmStubHandler { Handler = _ => Json(ModelsJson) };
        var svc = new LmStudioService(new HttpClient(stub), () => null);

        var probe = await svc.ProbeAsync("http://localhost:1234/v1");

        Assert.Equal(LmStudioState.Running, probe.State);
        Assert.Equal(2, probe.Models.Count);
        Assert.Equal("http://localhost:1234/v1/models", stub.LastUrl);
        Assert.Null(probe.ExecutablePath);
    }

    [Fact]
    public async Task Probe_BaseUrlSuntingan_DihormatiDanDinormalisasi()
    {
        var stub = new LmStubHandler { Handler = _ => Json("{\"data\":[]}") };
        var svc = new LmStudioService(new HttpClient(stub), () => null);

        var probe = await svc.ProbeAsync("http://localhost:9999/v1/");

        Assert.Equal(LmStudioState.Running, probe.State);
        Assert.Empty(probe.Models); // server jalan, belum ada model dimuat
        Assert.Equal("http://localhost:9999/v1/models", stub.LastUrl);
    }

    [Fact]
    public async Task Probe_JaringanMati_AdaExecutable_TerpasangTapiMati()
    {
        var stub = new LmStubHandler
        {
            Handler = _ => throw new HttpRequestException("connection refused"),
        };
        var svc = new LmStudioService(new HttpClient(stub),
            () => "C:\\Users\\X\\AppData\\Local\\Programs\\LM Studio\\LM Studio.exe");

        var probe = await svc.ProbeAsync(null);

        Assert.Equal(LmStudioState.InstalledNotRunning, probe.State);
        Assert.NotNull(probe.ExecutablePath);
        Assert.Empty(probe.Models);
    }

    [Fact]
    public async Task Probe_StatusGalat_TanpaExecutable_BelumTerpasang()
    {
        var stub = new LmStubHandler(); // bawaan stub: 500
        var svc = new LmStudioService(new HttpClient(stub), () => null);

        var probe = await svc.ProbeAsync(null);

        Assert.Equal(LmStudioState.NotInstalled, probe.State);
        _out.WriteLine("probe: " + probe.State);
    }
}
