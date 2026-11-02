using System.Text.Json;
using TintaStudio.Models;
using TintaStudio.Services;
using Xunit;

namespace TintaStudio.Tests;

/// <summary>
/// Tes v0.7.16 — tiga perbaikan dari uji PC pemilik:
/// (1) model bawaan Groq yang dimatikan diganti + migrasi
/// pengaturan lama (dan OpenRouter :free yang terbukti hilang
/// dari katalog publik; Gemini diaudit masih hidup, tidak
/// berubah); (2) fungsi murni keputusan peringatan "halaman
/// belum dibersihkan" di Typesetting + cek halaman bersih
/// tersimpan; (3) logika keadaan toggle "Kotak" Typesetting.
/// </summary>
public sealed class V0716Tests : IDisposable
{
    private readonly string _configDir;

    public V0716Tests()
    {
        _configDir = Path.Combine(Path.GetTempPath(),
            "tinta-v0716-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_configDir);
        Environment.SetEnvironmentVariable("TINTA_CONFIG_DIR", _configDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_configDir, recursive: true); } catch { }
    }

    // ── (1) Bawaan penyedia ───────────────────────────────────────

    [Fact]
    public void Groq_Default_Is_GptOss120b()
    {
        // llama-3.3-70b-versatile dimatikan Groq untuk tier
        // gratis/developer per 2026-08-16; pengganti resminya
        // openai/gpt-oss-120b (halaman deprecation Groq).
        Assert.Equal("openai/gpt-oss-120b",
            TranslateService.ProviderById(TranslateService.GroqId).DefaultModel);
    }

    [Fact]
    public void OpenRouter_Default_Is_Living_Free_Model()
    {
        // meta-llama/llama-3.3-70b-instruct:free TIDAK ada lagi di
        // katalog publik OpenRouter (diperiksa 2026-11-02);
        // google/gemma-4-31b-it:free ada dan benar-benar gratis.
        Assert.Equal("google/gemma-4-31b-it:free",
            TranslateService.ProviderById(TranslateService.OpenRouterId).DefaultModel);
    }

    [Fact]
    public void Gemini_Default_Unchanged_StillAlive()
    {
        // Diaudit 2026-11-02: gemini-2.5-flash masih terdaftar di
        // halaman model resmi Google AI → bawaan tidak berubah
        // dan tidak masuk daftar model mati.
        Assert.Equal("gemini-2.5-flash",
            TranslateService.ProviderById(TranslateService.GeminiId).DefaultModel);
        Assert.Null(TranslateService.ReplacementForRetired(
            TranslateService.GeminiId, "gemini-2.5-flash"));
    }

    [Fact]
    public void FreshSettings_UseLivingDefaults()
    {
        var s = new AppSettings();
        s.EnsureDefaults();
        Assert.Equal("openai/gpt-oss-120b",
            s.For(TranslateService.GroqId).Model);
        Assert.Equal("google/gemma-4-31b-it:free",
            s.For(TranslateService.OpenRouterId).Model);
    }

    // ── (1) Migrasi model mati ────────────────────────────────────

    [Fact]
    public void Migration_ReplacesDeadGroqModel()
    {
        var s = new AppSettings();
        s.Providers[TranslateService.GroqId] = new ProviderSettings
        {
            Model = "llama-3.3-70b-versatile",
        };
        s.EnsureDefaults();
        Assert.Equal("openai/gpt-oss-120b",
            s.For(TranslateService.GroqId).Model);
    }

    [Fact]
    public void Migration_ReplacesDeadOpenRouterModel()
    {
        var s = new AppSettings();
        s.Providers[TranslateService.OpenRouterId] = new ProviderSettings
        {
            Model = "meta-llama/llama-3.3-70b-instruct:free",
        };
        s.EnsureDefaults();
        Assert.Equal("google/gemma-4-31b-it:free",
            s.For(TranslateService.OpenRouterId).Model);
    }

    [Fact]
    public void Migration_LeavesCustomModelsUntouched()
    {
        var s = new AppSettings();
        s.Providers[TranslateService.GroqId] = new ProviderSettings
        {
            // Model kustom yang mirip tapi BUKAN ID mati persis.
            BaseUrl = "https://contoh.invalid/v1",
            Model = "llama-3.3-70b-versatile-lokal",
        };
        s.Providers[TranslateService.OpenRouterId] = new ProviderSettings
        {
            // Varian berbayar yang masih hidup — bukan ID mati.
            Model = "meta-llama/llama-3.3-70b-instruct",
        };
        // ID mati OpenRouter di entri PENYEDIA LAIN tidak
        // dimigrasi: pasangan (penyedia, model) yang menentukan.
        s.Providers[TranslateService.GeminiId] = new ProviderSettings
        {
            Model = "meta-llama/llama-3.3-70b-instruct:free",
        };
        s.EnsureDefaults();
        var groq = s.For(TranslateService.GroqId);
        Assert.Equal("llama-3.3-70b-versatile-lokal", groq.Model);
        Assert.Equal("https://contoh.invalid/v1", groq.BaseUrl);
        Assert.Equal("meta-llama/llama-3.3-70b-instruct",
            s.For(TranslateService.OpenRouterId).Model);
        Assert.Equal("meta-llama/llama-3.3-70b-instruct:free",
            s.For(TranslateService.GeminiId).Model);
    }

    [Fact]
    public void Migration_IsIdempotent()
    {
        var s = new AppSettings();
        s.Providers[TranslateService.GroqId] = new ProviderSettings
        {
            Model = "llama-3.3-70b-versatile",
        };
        Assert.Equal(1, s.MigrateRetiredModels());
        Assert.Equal(0, s.MigrateRetiredModels());
        Assert.Equal("openai/gpt-oss-120b",
            s.Providers[TranslateService.GroqId].Model);
    }

    [Fact]
    public void LoadSettings_OldFileWithDeadModels_Migrates()
    {
        // Berkas pengaturan gaya rilis lama: hanya dua penyedia,
        // keduanya menyimpan model yang sudah mati.
        var old = new
        {
            version = 1,
            provider = "groq",
            providers = new Dictionary<string, object>
            {
                ["groq"] = new { model = "llama-3.3-70b-versatile" },
                ["openrouter"] = new
                {
                    model = "meta-llama/llama-3.3-70b-instruct:free",
                },
            },
        };
        File.WriteAllText(AppConfig.SettingsPath,
            JsonSerializer.Serialize(old));

        var loaded = AppConfig.LoadSettings();
        Assert.Equal("openai/gpt-oss-120b",
            loaded.For(TranslateService.GroqId).Model);
        Assert.Equal("google/gemma-4-31b-it:free",
            loaded.For(TranslateService.OpenRouterId).Model);
        // Penyedia aktif warisan tetap terbaca.
        Assert.Equal("groq", loaded.Provider);

        // Dan hasil migrasi bertahan lewat simpan berikutnya.
        AppConfig.SaveSettings(loaded);
        var raw = File.ReadAllText(AppConfig.SettingsPath);
        Assert.Contains("openai/gpt-oss-120b", raw);
        Assert.DoesNotContain("llama-3.3-70b-versatile", raw);
        Assert.DoesNotContain("llama-3.3-70b-instruct:free", raw);
    }

    // ── (2) Peringatan halaman belum dibersihkan ─────────────────

    [Theory]
    // Tanpa blok: tidak ada yang tertimpa → tidak tampil.
    [InlineData(false, false, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, false, true, false)]
    // Sudah ada pekerjaan bersih → tidak tampil.
    [InlineData(true, true, false, false)]
    [InlineData(true, true, true, false)]
    // Belum ada ops tapi halaman bersih tersimpan → tidak tampil.
    [InlineData(true, false, true, false)]
    // Ada blok + belum bersih sama sekali → TAMPIL.
    [InlineData(true, false, false, true)]
    public void ShouldWarnPageNotCleaned_Matrix(
        bool hasBlocks, bool hasCleanWork, bool hasStored, bool expected)
    {
        Assert.Equal(expected, TypesetService.ShouldWarnPageNotCleaned(
            hasBlocks, hasCleanWork, hasStored));
    }

    [Fact]
    public void CleanStore_StoredPage_Check()
    {
        var chapter = new Chapter
        {
            Title = "Bab Uji",
            SourcePath = "/tmp/bab-v0716.cbz",
            Format = "ZIP (CBZ)",
        };
        var page = new ProjectPage { Index = 3 };
        // Runner menjalankan kelas tes secara paralel dan kelas
        // lain juga mengatur TINTA_CONFIG_DIR (pola yang sama di
        // seluruh suite) — tegaskan env TEPAT sebelum tiap akses
        // CleanStore agar jendela balapan sekecil mungkin.
        Environment.SetEnvironmentVariable("TINTA_CONFIG_DIR", _configDir);
        Assert.False(CleanStore.HasStoredCleanedPage(chapter, page));

        // Tanam berkas pola persis seperti yang ditulis
        // GetCleanedPageBitmap, lalu cek lagi.
        Environment.SetEnvironmentVariable("TINTA_CONFIG_DIR", _configDir);
        var dir = Path.Combine(CleanStore.StoreDir,
            Path.GetFileNameWithoutExtension(
                ProjectStore.PathFor(chapter.SourcePath)));
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "p3-0123456789abcdef.png"),
            new byte[] { 1, 2, 3 });
        Environment.SetEnvironmentVariable("TINTA_CONFIG_DIR", _configDir);
        Assert.True(CleanStore.HasStoredCleanedPage(chapter, page));
        // Indeks lain tetap tidak punya.
        Assert.False(CleanStore.HasStoredCleanedPage(
            chapter, new ProjectPage { Index = 4 }));
    }

    // ── (3) Toggle "Kotak" Typesetting ────────────────────────────

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(false, false, false)]
    public void BoxToggle_StateLogic(
        bool showBoxes, bool framesVisible, bool editingEnabled)
    {
        Assert.Equal(framesVisible,
            TypesetService.BlockFramesVisible(showBoxes));
        Assert.Equal(editingEnabled,
            TypesetService.BlockEditingEnabled(showBoxes));
    }
}
