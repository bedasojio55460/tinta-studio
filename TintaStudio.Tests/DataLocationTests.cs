using TintaStudio.Services;
using Xunit;
using Xunit.Abstractions;

namespace TintaStudio.Tests;

/// <summary>
/// Tes Lokasi Data &amp; Model (v0.7.13): penanda lokasi
/// (baca/tulis/rusak → bawaan), resolusi murni, validasi tujuan,
/// rencana + eksekusi pindah (rename satu volume dan salin lintas
/// volume yang dipaksakan), alur relokasi penuh lewat jahitan
/// parameter eksplisit, rencana Hapus Bersih gabungan (lokasi
/// berlaku + lokasi lama yatim + penanda terakhir, tanpa hitung
/// ganda), keputusan penawaran jalan pertama, dan resolusi folder
/// model Ollama. Semua di folder temp sendiri; profil asli dan
/// variabel lingkungan tidak pernah disentuh.
/// </summary>
public sealed class DataLocationTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _root;

    public DataLocationTests(ITestOutputHelper output)
    {
        _out = output;
        _root = Path.Combine(Path.GetTempPath(),
            "tinta-loc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private string WriteFile(string baseDir, string relPath, int bytes)
    {
        var full = Path.Combine(baseDir, relPath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, new byte[bytes]);
        return full;
    }

    // ── Penanda ─────────────────────────────────────────────────

    [Fact]
    public void Marker_TidakAda_Null()
    {
        Assert.Null(DataLocation.ReadMarker(Path.Combine(_root, "location.json")));
    }

    [Fact]
    public void Marker_TulisBaca_Roundtrip()
    {
        var markerPath = Path.Combine(_root, "bawaan", "location.json");
        var dataRoot = Path.Combine(_root, "Data Tinta");
        DataLocation.WriteMarker(markerPath, dataRoot);

        var marker = DataLocation.ReadMarker(markerPath);
        Assert.NotNull(marker);
        Assert.Equal(1, marker!.Version);
        Assert.Equal(Path.GetFullPath(dataRoot), marker.DataRoot);

        DataLocation.DeleteMarker(markerPath);
        Assert.Null(DataLocation.ReadMarker(markerPath));
        // Hapus kedua kali bukan galat.
        DataLocation.DeleteMarker(markerPath);
    }

    [Fact]
    public void Marker_RusakAtauTidakSah_JatuhKeNull()
    {
        var markerPath = Path.Combine(_root, "location.json");

        File.WriteAllText(markerPath, "{ ini bukan json");
        Assert.Null(DataLocation.ReadMarker(markerPath));

        File.WriteAllText(markerPath, "{\"version\":1,\"dataRoot\":\"\"}");
        Assert.Null(DataLocation.ReadMarker(markerPath));

        // Akar relatif tidak sah (harus mutlak).
        File.WriteAllText(markerPath, "{\"version\":1,\"dataRoot\":\"relatif/saja\"}");
        Assert.Null(DataLocation.ReadMarker(markerPath));

        File.WriteAllText(markerPath, "{\"version\":0,\"dataRoot\":\"" +
            Path.GetFullPath(_root).Replace("\\", "\\\\") + "\"}");
        Assert.Null(DataLocation.ReadMarker(markerPath));
    }

    [Fact]
    public void Resolve_DenganDanTanpaPenanda()
    {
        var defCfg = Path.Combine(_root, "def", "config");
        var defCache = Path.Combine(_root, "def", "cache");
        var root = Path.Combine(_root, "Pindahan");
        var marker = new LocationMarker { Version = 1, DataRoot = root };

        Assert.Equal(defCfg, DataLocation.ResolveConfigDir(null, defCfg));
        Assert.Equal(defCache, DataLocation.ResolveCacheRootDir(null, defCache));
        Assert.Equal(Path.Combine(root, "config"),
            DataLocation.ResolveConfigDir(marker, defCfg));
        Assert.Equal(Path.Combine(root, "cache"),
            DataLocation.ResolveCacheRootDir(marker, defCache));
    }

    [Fact]
    public void PathsEqual_DanIsInside()
    {
        var a = Path.Combine(_root, "satu");
        Assert.True(DataLocation.PathsEqual(a, a + Path.DirectorySeparatorChar));
        Assert.False(DataLocation.PathsEqual(a, Path.Combine(_root, "dua")));
        Assert.True(DataLocation.IsInside(Path.Combine(a, "anak", "cucu"), a));
        Assert.False(DataLocation.IsInside(a, a)); // kesamaan bukan "di dalam"
        Assert.False(DataLocation.IsInside(Path.Combine(_root, "saturnus"), a));
    }

    // ── Validasi tujuan ─────────────────────────────────────────

    [Fact]
    public void ValidateDestination_KasusSahDanTidak()
    {
        var srcCfg = Path.Combine(_root, "asal", "config");
        var srcCache = Path.Combine(_root, "asal", "cache");
        var sources = new[] { srcCfg, srcCache };

        Assert.Null(DataMover.ValidateDestination(
            Path.Combine(_root, "tujuan"), sources));
        Assert.NotNull(DataMover.ValidateDestination("", sources));
        Assert.NotNull(DataMover.ValidateDestination("relatif/saja", sources));
        // Sama dengan sumber.
        Assert.NotNull(DataMover.ValidateDestination(
            Path.Combine(_root, "asal"), sources)); // <asal>/config == sumber
        // Di dalam sumber.
        Assert.NotNull(DataMover.ValidateDestination(
            Path.Combine(srcCfg, "di-dalam"), sources));
        // Memuat sumber.
        Assert.NotNull(DataMover.ValidateDestination(_root, sources));
    }

    // ── Rencana + eksekusi pindah ───────────────────────────────

    [Fact]
    public void BuildPlan_UkuranLiveDanSatuVolume()
    {
        var src = Path.Combine(_root, "src");
        WriteFile(src, "a.bin", 1000);
        WriteFile(src, Path.Combine("sub", "b.bin"), 2000);
        var dst = Path.Combine(_root, "dst");

        var plan = DataMover.BuildPlan(new[] { ("Satu", src, dst) });
        var unit = Assert.Single(plan.Units);
        Assert.True(unit.Exists);
        Assert.Equal(3000, unit.SizeBytes);
        Assert.Equal(3000, plan.TotalBytes);
        Assert.True(unit.SameVolume); // akar temp yang sama

        var missing = DataMover.BuildPlan(new[]
        {
            ("Dua", Path.Combine(_root, "belum-ada"), dst),
        });
        Assert.False(missing.Units[0].Exists);
        Assert.Equal(0, missing.Units[0].SizeBytes);
    }

    [Fact]
    public async Task Execute_RenameSatuVolume_BerkasBerpindah()
    {
        var src = Path.Combine(_root, "src");
        WriteFile(src, "settings.json", 111);
        WriteFile(src, Path.Combine("projects", "bab.json"), 222);
        var dst = Path.Combine(_root, "dst");

        var plan = DataMover.BuildPlan(new[] { ("Data", src, dst) });
        var result = await DataMover.ExecuteAsync(plan);

        Assert.True(result.Completed);
        Assert.False(result.Cancelled);
        Assert.Empty(result.Errors);
        Assert.Equal(333, result.BytesMoved);
        Assert.False(Directory.Exists(src));
        Assert.Equal(111, new FileInfo(Path.Combine(dst, "settings.json")).Length);
        Assert.Equal(222, new FileInfo(
            Path.Combine(dst, "projects", "bab.json")).Length);
    }

    [Fact]
    public async Task Execute_SalinLintasVolume_VerifikasiDanHapusSumber()
    {
        var src = Path.Combine(_root, "src");
        var f1 = WriteFile(src, "model.onnx", 5000);
        WriteFile(src, Path.Combine("sub", "dict.txt"), 300);
        var dst = Path.Combine(_root, "dst");
        // Paksa jalur salin: unit dibangun manual dengan SameVolume=false.
        var unit = new MoveUnit("Model", src, dst, true, 5300, false);
        var plan = new MovePlan(new[] { unit });

        var result = await DataMover.ExecuteAsync(plan);

        Assert.True(result.Completed);
        Assert.Empty(result.Errors);
        Assert.Empty(result.Leftovers);
        Assert.Equal(5300, result.BytesMoved);
        Assert.False(File.Exists(f1));
        Assert.False(Directory.Exists(src));
        Assert.Equal(5000, new FileInfo(Path.Combine(dst, "model.onnx")).Length);
        Assert.Equal(300, new FileInfo(
            Path.Combine(dst, "sub", "dict.txt")).Length);
    }

    [Fact]
    public async Task Execute_SumberBelumAda_TujuanDisiapkanKosong()
    {
        var dst = Path.Combine(_root, "dst");
        var plan = DataMover.BuildPlan(new[]
        {
            ("Data", Path.Combine(_root, "belum-ada"), dst),
        });
        var result = await DataMover.ExecuteAsync(plan);
        Assert.True(result.Completed);
        Assert.True(Directory.Exists(dst));
        Assert.Equal(0, result.BytesMoved);
    }

    [Fact]
    public async Task Execute_TujuanTerhalangBerkas_GagalTerlaporkan()
    {
        var src = Path.Combine(_root, "src");
        WriteFile(src, "a.bin", 100);
        var dst = Path.Combine(_root, "dst");
        File.WriteAllText(dst, "penghalang"); // tujuan adalah BERKAS
        var unit = new MoveUnit("Data", src, dst, true, 100, false);
        var result = await DataMover.ExecuteAsync(new MovePlan(new[] { unit }));

        Assert.False(result.Completed);
        Assert.NotEmpty(result.Errors);
        // Sumber tidak disentuh karena salin gagal.
        Assert.True(File.Exists(Path.Combine(src, "a.bin")));
        _out.WriteLine(string.Join(" | ", result.Errors));
    }

    [Fact]
    public async Task Execute_Batal_TidakSelesai()
    {
        var src = Path.Combine(_root, "src");
        for (int i = 0; i < 20; i++)
            WriteFile(src, $"f{i}.bin", 1024);
        var dst = Path.Combine(_root, "dst");
        var unit = new MoveUnit("Data", src, dst, true, 20 * 1024, false);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var result = await DataMover.ExecuteAsync(
            new MovePlan(new[] { unit }), null, cts.Token);
        Assert.True(result.Cancelled);
        Assert.False(result.Completed);
    }

    // ── Alur relokasi penuh (jahitan eksplisit) ─────────────────

    [Fact]
    public async Task Relokasi_PindahLaluKembali_PenandaMengikuti()
    {
        var defCfg = Path.Combine(_root, "bawaan", "config-tinta");
        var defCache = Path.Combine(_root, "bawaan", "cache-tinta");
        var markerPath = Path.Combine(defCfg, "location.json");
        WriteFile(defCfg, "settings.json", 100);
        WriteFile(defCfg, "keys.json", 50);
        WriteFile(defCache, Path.Combine("models", "rec.onnx"), 900);
        var newRoot = Path.Combine(_root, "D-Data", "TintaStudio");

        // PINDAH
        var plan = DataRelocation.BuildMovePlan(defCfg, defCache, newRoot, markerPath);
        Assert.Equal(Path.Combine(newRoot, "config"), plan.Move.Units[0].DestinationDir);
        Assert.Equal(Path.Combine(newRoot, "cache"), plan.Move.Units[1].DestinationDir);
        var moved = await DataRelocation.ExecuteAsync(plan);
        Assert.True(moved.Completed);
        Assert.True(File.Exists(Path.Combine(newRoot, "config", "settings.json")));
        Assert.True(File.Exists(Path.Combine(newRoot, "cache", "models", "rec.onnx")));
        var marker = DataLocation.ReadMarker(markerPath);
        Assert.NotNull(marker);
        Assert.Equal(Path.GetFullPath(newRoot), marker!.DataRoot);
        Assert.Equal(Path.Combine(newRoot, "config"),
            DataLocation.ResolveConfigDir(marker, defCfg));

        // KEMBALI
        var back = DataRelocation.BuildReturnPlan(
            Path.Combine(newRoot, "config"), Path.Combine(newRoot, "cache"),
            defCfg, defCache, markerPath);
        var returned = await DataRelocation.ExecuteAsync(back);
        Assert.True(returned.Completed);
        Assert.True(File.Exists(Path.Combine(defCfg, "settings.json")));
        Assert.True(File.Exists(Path.Combine(defCache, "models", "rec.onnx")));
        Assert.Null(DataLocation.ReadMarker(markerPath));
    }

    // ── Hapus Bersih gabungan ───────────────────────────────────

    [Fact]
    public void WipePlan_Gabungan_LokasiLamaDanPenandaTanpaHitungGanda()
    {
        var effCfg = Path.Combine(_root, "baru", "config");
        var effCache = Path.Combine(_root, "baru", "cache");
        var defCfg = Path.Combine(_root, "bawaan", "config-tinta");
        var defCache = Path.Combine(_root, "bawaan", "cache-tinta");
        WriteFile(effCfg, "settings.json", 100);
        WriteFile(effCache, "model.onnx", 1000);
        WriteFile(defCfg, "sisa.json", 40);
        WriteFile(defCache, "sisa.onnx", 60);
        var markerPath = Path.Combine(defCfg, "location.json");
        DataLocation.WriteMarker(markerPath, Path.Combine(_root, "baru"));
        long markerBytes = new FileInfo(markerPath).Length;

        var plan = DataWipe.BuildPlan(effCfg, effCache, defCfg, defCache, markerPath);

        Assert.Equal(5, plan.Targets.Count);
        Assert.Equal(effCfg, plan.Targets[0].Path);
        Assert.Equal(effCache, plan.Targets[1].Path);
        Assert.Equal(defCfg, plan.Targets[2].Path);
        Assert.Contains("lokasi lama", plan.Targets[2].Label);
        Assert.Equal(40, plan.Targets[2].SizeBytes); // penanda dikeluarkan
        Assert.Equal(defCache, plan.Targets[3].Path);
        Assert.Equal(60, plan.Targets[3].SizeBytes);
        Assert.True(plan.Targets[4].IsFile);
        Assert.Equal(markerPath, plan.Targets[4].Path);
        Assert.Equal(markerBytes, plan.Targets[4].SizeBytes);
        // Total = semua byte nyata tepat sekali.
        Assert.Equal(100 + 1000 + 40 + 60 + markerBytes, plan.TotalBytes);
    }

    [Fact]
    public void WipePlan_Gabungan_LokasiLamaKosong_TidakJadiBaris()
    {
        var effCfg = Path.Combine(_root, "baru", "config");
        var defCfg = Path.Combine(_root, "bawaan", "config-tinta");
        var defCache = Path.Combine(_root, "bawaan", "cache-tinta");
        WriteFile(effCfg, "settings.json", 10);
        Directory.CreateDirectory(defCache); // ada tapi kosong → bukan baris
        var markerPath = Path.Combine(defCfg, "location.json");
        DataLocation.WriteMarker(markerPath, Path.Combine(_root, "baru"));
        // defCfg hanya berisi penanda → bukan baris lokasi lama.

        var plan = DataWipe.BuildPlan(
            effCfg, Path.Combine(_root, "baru", "cache"),
            defCfg, defCache, markerPath);

        Assert.Equal(3, plan.Targets.Count); // 2 berlaku + penanda
        Assert.True(plan.Targets[2].IsFile);
    }

    [Fact]
    public void WipePlan_TanpaPenanda_TetapDuaTarget()
    {
        var plan = DataWipe.BuildPlan(
            Path.Combine(_root, "a"), Path.Combine(_root, "b"),
            Path.Combine(_root, "c"), Path.Combine(_root, "d"), null);
        Assert.Equal(2, plan.Targets.Count);
        Assert.All(plan.Targets, t => Assert.False(t.IsFile));
    }

    [Fact]
    public void WipeExecute_Gabungan_PenandaIkutTerhapusTerakhir()
    {
        var effCfg = Path.Combine(_root, "baru", "config");
        var defCfg = Path.Combine(_root, "bawaan", "config-tinta");
        WriteFile(effCfg, "settings.json", 10);
        WriteFile(defCfg, "sisa.json", 20);
        var markerPath = Path.Combine(defCfg, "location.json");
        DataLocation.WriteMarker(markerPath, Path.Combine(_root, "baru"));

        var plan = DataWipe.BuildPlan(
            effCfg, Path.Combine(_root, "baru", "cache"),
            defCfg, Path.Combine(_root, "bawaan", "cache-tinta"), markerPath);
        var result = DataWipe.Execute(plan);

        Assert.Empty(result.Errors);
        Assert.False(Directory.Exists(effCfg));
        Assert.False(Directory.Exists(defCfg));
        Assert.False(File.Exists(markerPath));
        // Penanda gugur bersama folder induknya (baris lokasi lama
        // dieksekusi sebelum giliran penanda): di hasil ia tercatat
        // terhapus ATAU sudah-tidak-ada — keadaan akhir yang
        // penting: berkasnya musnah dan resolver kembali ke bawaan.
        Assert.Contains(result.Deleted.Concat(result.AlreadyAbsent),
            t => t.IsFile);
    }

    // ── Penawaran jalan pertama ─────────────────────────────────

    [Fact]
    public void FirstRunOffer_MatriksKeputusan()
    {
        long ambang = FirstRunOffer.NearlyEmptyThresholdBytes;
        Assert.True(FirstRunOffer.ShouldOffer(false, false, 0));
        Assert.True(FirstRunOffer.ShouldOffer(false, false, ambang));
        Assert.False(FirstRunOffer.ShouldOffer(false, false, ambang + 1));
        Assert.False(FirstRunOffer.ShouldOffer(true, false, 0));  // sudah pindahan
        Assert.False(FirstRunOffer.ShouldOffer(false, true, 0));  // sudah ditawari
        Assert.False(FirstRunOffer.ShouldOffer(true, true, ambang + 1));
    }

    // ── Lokasi model Ollama ─────────────────────────────────────

    [Fact]
    public void OllamaModelsDir_ResolusiBerjenjang()
    {
        var user = Path.Combine(_root, "user-models");
        var proc = Path.Combine(_root, "proc-models");
        Assert.Equal(user, OllamaService.ResolveModelsDir(user, proc));
        Assert.Equal(proc, OllamaService.ResolveModelsDir(null, proc));
        Assert.Equal(proc, OllamaService.ResolveModelsDir("   ", proc));
        Assert.Equal(OllamaService.DefaultModelsDir,
            OllamaService.ResolveModelsDir(null, null));
        Assert.EndsWith(Path.Combine(".ollama", "models"),
            OllamaService.DefaultModelsDir);
        // DataWipe menunjuk lokasi efektif yang sama.
        Assert.Equal(OllamaService.EffectiveModelsDir, DataWipe.OllamaModelsDir);
    }

    [Fact]
    public void OllamaSetUserModelsDir_KontrakJujur()
    {
        // Di sistem yang menyimpan variabel pengguna (Windows):
        // true + terbaca balik. Di sistem yang tidak (VM ini):
        // false + pesan galat terisi. Keduanya kontrak yang sah —
        // yang tidak boleh: mengaku berhasil diam-diam.
        var target = Path.Combine(_root, "models-baru");
        var before = Environment.GetEnvironmentVariable(
            "OLLAMA_MODELS", EnvironmentVariableTarget.User);
        try
        {
            bool ok = OllamaService.TrySetUserModelsDir(target, out var error);
            if (ok)
            {
                Assert.Equal(target, Environment.GetEnvironmentVariable(
                    "OLLAMA_MODELS", EnvironmentVariableTarget.User));
            }
            else
            {
                Assert.False(string.IsNullOrWhiteSpace(error));
                _out.WriteLine("Variabel pengguna tidak tersimpan di sistem ini: " + error);
            }
        }
        finally
        {
            try
            {
                Environment.SetEnvironmentVariable(
                    "OLLAMA_MODELS", before, EnvironmentVariableTarget.User);
            }
            catch { }
        }
    }

    [Fact]
    public void FreeSpaceFor_JalurTemp_AdaNilai()
    {
        var free = DataMover.FreeSpaceFor(_root);
        Assert.NotNull(free);
        Assert.True(free!.Value > 0);
    }
}
