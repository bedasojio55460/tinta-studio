using TintaStudio.Services;
using Xunit;
using Xunit.Abstractions;

namespace TintaStudio.Tests;

/// <summary>
/// Tes Hapus Bersih (v0.7.11): pembangun rencana dari dua akar
/// (label + jalur persis + keberadaan), format ukuran id-ID, dan
/// perhitungan ukuran folder dari pohon berkas contoh di direktori
/// temp — plus eksekusi pada pohon temp (target ada terhapus,
/// target tidak ada = "sudah bersih", bukan galat). Semua di
/// folder temp sendiri; data asli tidak pernah disentuh.
/// </summary>
public sealed class DataWipeTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _root;

    public DataWipeTests(ITestOutputHelper output)
    {
        _out = output;
        _root = Path.Combine(Path.GetTempPath(),
            "tinta-wipe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private string WriteFile(string relPath, int bytes)
    {
        var full = Path.Combine(_root, relPath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, new byte[bytes]);
        return full;
    }

    [Fact]
    public void BuildPlan_DuaTarget_LabelJalurDanKeberadaan()
    {
        var config = Path.Combine(_root, "config");
        var cache = Path.Combine(_root, "cache");
        Directory.CreateDirectory(config); // cache sengaja tidak dibuat

        var plan = DataWipe.BuildPlan(config, cache);

        Assert.Equal(2, plan.Targets.Count);
        Assert.Equal(config, plan.Targets[0].Path);
        Assert.Contains("Folder data aplikasi", plan.Targets[0].Label);
        Assert.Contains("key API", plan.Targets[0].Label);
        Assert.True(plan.Targets[0].Exists);
        Assert.Equal(cache, plan.Targets[1].Path);
        Assert.Contains("Folder cache model", plan.Targets[1].Label);
        Assert.False(plan.Targets[1].Exists);
        Assert.Equal(0, plan.Targets[1].SizeBytes);
        Assert.Equal(1, plan.ExistingCount);
        _out.WriteLine(string.Join(" | ", plan.Targets.Select(t => $"{t.Path} ({t.Exists})")));
    }

    [Fact]
    public void BuildPlan_ResolverAplikasi_TidakHardcode()
    {
        // Rencana bawaan harus menunjuk persis ke resolver yang
        // dipakai AppConfig dan ModelStore (sumber tunggal).
        var plan = DataWipe.BuildPlan();
        Assert.Equal(AppConfig.ConfigDir, plan.Targets[0].Path);
        Assert.Equal(ModelStore.CacheRootDir, plan.Targets[1].Path);
        Assert.EndsWith(Path.Combine(".cache", "tinta-studio"), plan.Targets[1].Path);
    }

    [Fact]
    public void DirectorySize_PohonContoh_TotalPersis()
    {
        WriteFile("a.bin", 1000);
        WriteFile(Path.Combine("sub", "b.bin"), 2000);
        WriteFile(Path.Combine("sub", "dalam", "c.bin"), 3000);
        WriteFile(Path.Combine("sub", "kosong.txt"), 0);

        var plan = DataWipe.BuildPlan(_root, Path.Combine(_root, "tidak-ada"));
        Assert.Equal(6000, plan.Targets[0].SizeBytes);
        Assert.Equal(6000, plan.TotalBytes);
        Assert.Equal(6000, DataWipe.DirectorySize(_root));
        Assert.Equal(0, DataWipe.DirectorySize(Path.Combine(_root, "tidak-ada")));
    }

    [Fact]
    public void FormatSize_SkalaId()
    {
        Assert.Equal("0 MB", DataWipe.FormatSize(0));
        Assert.Equal("0,0 MB", DataWipe.FormatSize(1024));
        Assert.Equal("12,3 MB", DataWipe.FormatSize((long)(12.3 * 1_048_576)));
        Assert.Equal("1,25 GB", DataWipe.FormatSize((long)(1.25 * 1_073_741_824)));
        Assert.Equal("2,00 GB", DataWipe.FormatSize(2L * 1_073_741_824));
    }

    [Fact]
    public void Execute_AdaTerhapus_TidakAdaSudahBersih()
    {
        var config = Path.Combine(_root, "config");
        var cache = Path.Combine(_root, "cache");
        WriteFile(Path.Combine("config", "keys.json"), 111);
        WriteFile(Path.Combine("config", "projects", "bab.json"), 222);
        // cache tidak dibuat → target "sudah bersih"

        var plan = DataWipe.BuildPlan(config, cache);
        Assert.Equal(333, plan.TotalBytes);

        var result = DataWipe.Execute(plan);
        Assert.Single(result.Deleted);
        Assert.Single(result.AlreadyAbsent);
        Assert.Empty(result.Errors);
        Assert.Equal(333, result.FreedBytes);
        Assert.False(Directory.Exists(config));

        // Eksekusi kedua pada rencana yang sama: semua sudah bersih.
        var again = DataWipe.Execute(plan);
        Assert.Empty(again.Deleted);
        Assert.Equal(2, again.AlreadyAbsent.Count);
        Assert.Empty(again.Errors);
        Assert.Equal(0, again.FreedBytes);
    }
}
