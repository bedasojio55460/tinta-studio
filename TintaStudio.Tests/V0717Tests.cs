using TintaStudio.Models;
using TintaStudio.Services;
using Xunit;

namespace TintaStudio.Tests;

/// <summary>
/// Tes v0.7.17 — perbaikan kerugian nyata dari uji PC pemilik:
/// deteksi ulang membuang terjemahan/typeset. (1) Pewarisan
/// geometri BlockCarryOver + pemetaan ulang operasi bersih;
/// (2) fungsi murni penentu konfirmasi deteksi ulang;
/// (3) bahasa deteksi tersimpan (bawaan Otomatis, bukan Jepang);
/// (4) cadangan berkas proyek .bak + pemulihan GetOrCreate.
/// </summary>
public sealed class V0717Tests : IDisposable
{
    private readonly string _configDir;

    public V0717Tests()
    {
        _configDir = Path.Combine(Path.GetTempPath(),
            "tinta-v0717-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_configDir);
        Environment.SetEnvironmentVariable("TINTA_CONFIG_DIR", _configDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_configDir, recursive: true); } catch { }
    }

    /// <summary>Runner menjalankan kelas tes secara paralel dan
    /// kelas lain juga mengatur TINTA_CONFIG_DIR — tegaskan env
    /// tepat sebelum tiap akses penyimpanan (pola seluruh suite).</summary>
    private void PinEnv() =>
        Environment.SetEnvironmentVariable("TINTA_CONFIG_DIR", _configDir);

    // ── Pembangun blok ────────────────────────────────────────────

    private static readonly DateTime Stamp =
        new(2026, 11, 2, 3, 4, 5, DateTimeKind.Utc);

    private static TextBlock OldBlock(
        int x, int y, int w, int h,
        string? translation = "Hasil terjemahan", bool typeset = true) => new()
        {
            X = x, Y = y, Width = w, Height = h,
            Language = "en", Text = "TEKS LAMA", Confidence = 0.9,
            Translation = translation,
            TranslatedBy = translation is null ? null : "Ollama (lokal) • qwen2.5:7b",
            TranslatedAt = translation is null ? null : Stamp,
            TranslationBackup = translation is null ? null : "Teks sebelum poles",
            TranslationBackupBy = translation is null ? null : "Groq • openai/gpt-oss-120b",
            Typeset = typeset
                ? new TypesetStyle { FontFamily = "Serif Uji", FontSize = 42, Bold = true }
                : null,
        };

    private static TextBlock NewBlock(int x, int y, int w, int h) => new()
    {
        X = x, Y = y, Width = w, Height = h,
        Language = "en", Text = "TEKS BARU", Confidence = 0.8,
    };

    // ── (1) Pewarisan ─────────────────────────────────────────────

    [Fact]
    public void CarryOver_ExactMatch_InheritsEverything()
    {
        var old = OldBlock(10, 20, 100, 60);
        var fresh = NewBlock(10, 20, 100, 60);
        var result = BlockCarryOver.Apply(
            new[] { old }, new[] { fresh }, Array.Empty<CleanOperation>());

        Assert.Equal(1, result.InheritedCount);
        Assert.Equal("Hasil terjemahan", fresh.Translation);
        Assert.Equal("Ollama (lokal) • qwen2.5:7b", fresh.TranslatedBy);
        Assert.Equal(Stamp, fresh.TranslatedAt);
        Assert.Equal("Teks sebelum poles", fresh.TranslationBackup);
        Assert.Equal("Groq • openai/gpt-oss-120b", fresh.TranslationBackupBy);
        // Typeset diwarisi sebagai SALINAN — bukan instans yang sama.
        Assert.NotNull(fresh.Typeset);
        Assert.NotSame(old.Typeset, fresh.Typeset);
        Assert.Equal("Serif Uji", fresh.Typeset!.FontFamily);
        Assert.Equal(42, fresh.Typeset.FontSize);
        Assert.True(fresh.Typeset.Bold);
        // Hasil OCR baru tidak disentuh.
        Assert.Equal("TEKS BARU", fresh.Text);
        Assert.Equal(0.8, fresh.Confidence);
        Assert.NotEqual(old.Id, fresh.Id);
        Assert.Equal(fresh.Id, result.IdMap[old.Id]);
    }

    [Fact]
    public void CarryOver_ShiftedPartialOverlap_Inherits()
    {
        // Geser 30px dari 100 → IoU 70/130 ≈ 0,54 ≥ 0,30.
        var old = OldBlock(0, 0, 100, 100);
        var fresh = NewBlock(30, 0, 100, 100);
        var result = BlockCarryOver.Apply(
            new[] { old }, new[] { fresh }, Array.Empty<CleanOperation>());
        Assert.Equal(1, result.InheritedCount);
        Assert.Equal("Hasil terjemahan", fresh.Translation);
    }

    [Fact]
    public void CarryOver_ContainmentLowIou_Inherits()
    {
        // Blok lama kecil di dalam blok baru raksasa: IoU ≈ 0,0025,
        // tetapi pusat blok lama berada di dalam blok baru.
        var old = OldBlock(40, 40, 20, 20);
        var fresh = NewBlock(0, 0, 400, 400);
        Assert.True(BlockCarryOver.Iou(old, fresh) < BlockCarryOver.MinIou);
        var result = BlockCarryOver.Apply(
            new[] { old }, new[] { fresh }, Array.Empty<CleanOperation>());
        Assert.Equal(1, result.InheritedCount);
        Assert.Equal("Hasil terjemahan", fresh.Translation);
    }

    [Fact]
    public void CarryOver_BelowThresholdNoContainment_NoInherit()
    {
        // Sentuhan sudut kecil: IoU ≈ 0,02 dan tak ada pusat yang
        // masuk kotak lawan → bukan pasangan.
        var old = OldBlock(0, 0, 100, 100);
        var fresh = NewBlock(80, 80, 100, 100);
        var result = BlockCarryOver.Apply(
            new[] { old }, new[] { fresh }, Array.Empty<CleanOperation>());
        Assert.Equal(0, result.InheritedCount);
        Assert.Null(fresh.Translation);
        Assert.Null(fresh.Typeset);
        Assert.Empty(result.IdMap);
    }

    [Fact]
    public void CarryOver_NewWithoutPartner_StaysEmpty()
    {
        var old = OldBlock(0, 0, 100, 100);
        var matched = NewBlock(0, 0, 100, 100);
        var lonely = NewBlock(500, 500, 80, 80);
        var result = BlockCarryOver.Apply(
            new[] { old }, new[] { matched, lonely },
            Array.Empty<CleanOperation>());
        Assert.Equal(1, result.InheritedCount);
        Assert.Equal("Hasil terjemahan", matched.Translation);
        Assert.Null(lonely.Translation);
        Assert.Null(lonely.Typeset);
        // Urutan blok baru dipertahankan apa adanya.
        Assert.Same(matched, result.Blocks[0]);
        Assert.Same(lonely, result.Blocks[1]);
    }

    [Fact]
    public void CarryOver_Competition_HigherIouWins()
    {
        var old = OldBlock(0, 0, 100, 100);
        var perfect = NewBlock(0, 0, 100, 100);      // IoU 1,0
        var partial = NewBlock(50, 0, 100, 100);     // IoU ≈ 0,33
        // Urutan daftar sengaja dibalik — yang menang ditentukan
        // IoU, bukan posisi.
        var result = BlockCarryOver.Apply(
            new[] { old }, new[] { partial, perfect },
            Array.Empty<CleanOperation>());
        Assert.Equal(1, result.InheritedCount);
        Assert.Equal("Hasil terjemahan", perfect.Translation);
        Assert.Null(partial.Translation);
        Assert.Equal(perfect.Id, result.IdMap[old.Id]);
    }

    [Fact]
    public void CarryOver_OldBlockSingleHeirOnly()
    {
        // Dua blok lama tumpang tindih memperebutkan satu blok baru:
        // hanya IoU tertinggi yang mewariskan.
        var strong = OldBlock(0, 0, 100, 100, translation: "Pemenang");
        var weak = OldBlock(10, 0, 100, 100, translation: "Kalah");
        var fresh = NewBlock(0, 0, 100, 100);
        var result = BlockCarryOver.Apply(
            new[] { weak, strong }, new[] { fresh },
            Array.Empty<CleanOperation>());
        Assert.Equal(1, result.InheritedCount);
        Assert.Equal("Pemenang", fresh.Translation);
    }

    // ── (1) Pemetaan operasi bersih ───────────────────────────────

    [Fact]
    public void CarryOver_CleanOps_RemappedAndOrphansDropped()
    {
        var heir = OldBlock(0, 0, 100, 100);
        var orphan = OldBlock(900, 900, 50, 50);
        var fresh = NewBlock(5, 5, 100, 100);
        var opOrphan = new CleanOperation
        {
            BlockId = orphan.Id, Mode = CleanMode.Solid, Seq = 1,
            AppliedUtc = Stamp,
        };
        var opHeir = new CleanOperation
        {
            BlockId = heir.Id, Mode = CleanMode.Inpaint,
            FillColor = "#ABCDEF", LineProtect = false, Seq = 2,
            AppliedUtc = Stamp,
        };
        var result = BlockCarryOver.Apply(
            new[] { heir, orphan }, new[] { fresh },
            new[] { opOrphan, opHeir });

        var op = Assert.Single(result.CleanOps);
        Assert.Equal(fresh.Id, op.BlockId);
        Assert.Equal(2, op.Seq);
        Assert.Equal(CleanMode.Inpaint, op.Mode);
        Assert.Equal("#ABCDEF", op.FillColor);
        Assert.False(op.LineProtect);
        Assert.Equal(Stamp, op.AppliedUtc);
        // Operasi lama tidak diubah (yang lolos adalah salinan).
        Assert.NotSame(opHeir, op);
        Assert.Equal(heir.Id, opHeir.BlockId);
    }

    [Fact]
    public void CarryOver_NoOps_StaysEmpty()
    {
        var result = BlockCarryOver.Apply(
            new[] { OldBlock(0, 0, 10, 10) },
            new[] { NewBlock(0, 0, 10, 10) },
            Array.Empty<CleanOperation>());
        Assert.Empty(result.CleanOps);
    }

    // ── (2) Penentu konfirmasi ────────────────────────────────────

    [Fact]
    public void RequiresConfirmation_TranslatedBlock_True()
    {
        Assert.True(BlockCarryOver.RequiresConfirmation(
            new[] { OldBlock(0, 0, 10, 10, translation: "Ada", typeset: false) }));
    }

    [Fact]
    public void RequiresConfirmation_TypesetOnlyBlock_True()
    {
        Assert.True(BlockCarryOver.RequiresConfirmation(
            new[] { OldBlock(0, 0, 10, 10, translation: null, typeset: true) }));
    }

    [Fact]
    public void RequiresConfirmation_PlainBlocks_False()
    {
        var plain = OldBlock(0, 0, 10, 10, translation: null, typeset: false);
        Assert.False(BlockCarryOver.RequiresConfirmation(new[] { plain }));
        Assert.False(BlockCarryOver.RequiresConfirmation(
            Array.Empty<TextBlock>()));
        // Terjemahan string kosong = belum diterjemahkan.
        var emptyTranslation = OldBlock(0, 0, 10, 10,
            translation: "", typeset: false);
        Assert.False(BlockCarryOver.RequiresConfirmation(
            new[] { emptyTranslation }));
    }

    [Fact]
    public void RequiresConfirmation_ChapterLevel()
    {
        var cleanPage = new ProjectPage
        {
            Index = 0,
            Blocks = { OldBlock(0, 0, 10, 10, translation: null, typeset: false) },
        };
        var workedPage = new ProjectPage
        {
            Index = 1,
            Blocks = { OldBlock(0, 0, 10, 10, translation: "Ada", typeset: false) },
        };
        Assert.False(BlockCarryOver.RequiresConfirmation(new[] { cleanPage }));
        Assert.True(BlockCarryOver.RequiresConfirmation(
            new[] { cleanPage, workedPage }));
    }

    // ── (3) Bahasa deteksi tersimpan ──────────────────────────────

    [Fact]
    public void DetectLanguage_FreshSettings_DefaultAuto()
    {
        var s = new AppSettings();
        Assert.Null(s.DetectLanguage);
        Assert.Same(OcrLanguage.Auto, s.EffectiveDetectLanguage);
        Assert.NotSame(OcrLanguage.Japanese, s.EffectiveDetectLanguage);
    }

    [Fact]
    public void DetectLanguage_SaveLoad_Roundtrip()
    {
        PinEnv();
        var s = AppConfig.LoadSettings();
        s.DetectLanguage = "en";
        AppConfig.SaveSettings(s);

        PinEnv();
        var loaded = AppConfig.LoadSettings();
        Assert.Equal("en", loaded.DetectLanguage);
        Assert.Same(OcrLanguage.English, loaded.EffectiveDetectLanguage);
    }

    [Fact]
    public void DetectLanguage_UnknownCode_FallsBackAuto()
    {
        var s = new AppSettings { DetectLanguage = "xx" };
        Assert.Same(OcrLanguage.Auto, s.EffectiveDetectLanguage);
        s.DetectLanguage = "ja";
        Assert.Same(OcrLanguage.Japanese, s.EffectiveDetectLanguage);
    }

    // ── (4) Cadangan berkas proyek ────────────────────────────────

    private Chapter Bab(string sourcePath) => new()
    {
        Title = "Bab Uji",
        SourcePath = sourcePath,
        Format = "ZIP (CBZ)",
    };

    [Fact]
    public void ProjectSave_Twice_BakHoldsPreviousVersion()
    {
        PinEnv();
        var chapter = Bab("/tmp/bab-v0717-bak.cbz");
        var (project, _) = ProjectStore.GetOrCreate(chapter);
        project.Name = "Versi Satu";
        PinEnv();
        ProjectStore.Save(project, chapter.SourcePath);
        project.Name = "Versi Dua";
        PinEnv();
        ProjectStore.Save(project, chapter.SourcePath);

        PinEnv();
        var path = ProjectStore.PathFor(chapter.SourcePath);
        Assert.True(File.Exists(path + ".bak"));
        Assert.Equal("Versi Satu", TintaProject.Load(path + ".bak").Name);
        Assert.Equal("Versi Dua", TintaProject.Load(path).Name);
        // Penulisan atomik tidak meninggalkan .tmp.
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void GetOrCreate_CorruptMain_RecoversFromBak()
    {
        PinEnv();
        var chapter = Bab("/tmp/bab-v0717-pulih.cbz");
        var (project, pc) = ProjectStore.GetOrCreate(chapter);
        var page = pc.PageAt(0);
        page.Blocks.Add(OldBlock(0, 0, 10, 10));
        PinEnv();
        ProjectStore.Save(project, chapter.SourcePath);
        PinEnv();
        ProjectStore.Save(project, chapter.SourcePath); // .bak = versi baik

        PinEnv();
        var path = ProjectStore.PathFor(chapter.SourcePath);
        File.WriteAllText(path, "{ ini bukan json proyek");

        PinEnv();
        var (recovered, recoveredChapter) = ProjectStore.GetOrCreate(chapter);
        var block = Assert.Single(recoveredChapter.Pages[0].Blocks);
        Assert.Equal("Hasil terjemahan", block.Translation);
        Assert.Same(recovered.Chapters[0], recoveredChapter);
    }

    [Fact]
    public void GetOrCreate_CorruptMain_NoBak_NewProject()
    {
        PinEnv();
        var chapter = Bab("/tmp/bab-v0717-rusak.cbz");
        PinEnv();
        var path = ProjectStore.PathFor(chapter.SourcePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "rusak total");

        PinEnv();
        var (_, pc) = ProjectStore.GetOrCreate(chapter);
        Assert.Equal(chapter.SourcePath, pc.SourcePath);
        // Pemulihan tidak menulis apa pun: .bak tetap tidak ada.
        Assert.False(File.Exists(path + ".bak"));
    }

    [Fact]
    public void GetOrCreate_TotalFailure_DoesNotTouchBak()
    {
        PinEnv();
        var chapter = Bab("/tmp/bab-v0717-gagal-total.cbz");
        PinEnv();
        var path = ProjectStore.PathFor(chapter.SourcePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "utama rusak");
        File.WriteAllText(path + ".bak", "cadangan ikut rusak");

        PinEnv();
        var (_, pc) = ProjectStore.GetOrCreate(chapter);
        Assert.Equal(chapter.SourcePath, pc.SourcePath);
        // Gagal total → proyek baru, dan .bak TIDAK ditimpa.
        Assert.Equal("cadangan ikut rusak", File.ReadAllText(path + ".bak"));
    }
}
