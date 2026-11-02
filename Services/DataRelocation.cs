namespace TintaStudio.Services;

/// <summary>Satu rencana relokasi data aplikasi utuh: rencana
/// pindah mesin (DataMover) + akar tujuan + apakah ini
/// pengembalian ke bawaan (penanda dihapus) atau pemindahan ke
/// akar baru (penanda ditulis) + jalur berkas penanda yang
/// dikelola rencana ini.</summary>
public sealed record RelocationPlan(
    MovePlan Move, string NewDataRoot, bool ReturnToDefault, string MarkerPath);

/// <summary>
/// Orkestrasi "Lokasi Data &amp; Model" (v0.7.13): bangun rencana
/// pindah dari resolver yang berlaku (DataLocation.Effective* —
/// penanda diterapkan) ke akar yang dipilih pengguna, jalankan
/// lewat DataMover, dan HANYA sesudah eksekusi selesai penuh
/// (MoveResult.Completed) tulis/hapus penanda. Urutan ini
/// menjamin resolver tidak pernah menunjuk folder yang isinya
/// belum sampai. Sisa terkunci (Leftovers) tidak menghalangi
/// penanda: salinannya sah di tujuan, sisa lamanya dilaporkan UI
/// dan kelak tertangkap baris "lokasi lama" di Hapus Bersih.
///
/// Catatan: sumber rencana diambil dari resolver penanda
/// (DataLocation), bukan AppConfig/ModelStore — override env
/// pengembang memang mengabaikan penanda, jadi relokasi bekerja
/// pada lapisan penanda itu sendiri. Di pemakaian nyata (tanpa
/// env) keduanya identik. Varian berparameter eksplisit adalah
/// jahitan tes agar alur penuh berjalan di folder temp tanpa
/// menyentuh profil asli.
/// </summary>
public static class DataRelocation
{
    /// <summary>Rencana pindah ke akar baru pilihan pengguna:
    /// folder data → &lt;akar&gt;/config, cache → &lt;akar&gt;/cache.</summary>
    public static RelocationPlan BuildMovePlan(string destinationRoot)
        => BuildMovePlan(
            DataLocation.EffectiveConfigDir,
            DataLocation.EffectiveCacheRootDir,
            destinationRoot, DataLocation.MarkerPath);

    /// <summary>Rencana pindah dari dua folder sumber eksplisit
    /// ke akar baru (dipakai tes).</summary>
    public static RelocationPlan BuildMovePlan(
        string sourceConfigDir, string sourceCacheRootDir,
        string destinationRoot, string markerPath)
    {
        var dest = DataLocation.DirsForRoot(destinationRoot);
        var move = DataMover.BuildPlan(new[]
        {
            ("Folder data aplikasi", sourceConfigDir, dest.ConfigDir),
            ("Folder cache model", sourceCacheRootDir, dest.CacheRootDir),
        });
        return new RelocationPlan(
            move, Path.GetFullPath(destinationRoot), false, markerPath);
    }

    /// <summary>Rencana kembali ke bawaan: folder efektif →
    /// folder bawaan; penanda dihapus sesudah berhasil.</summary>
    public static RelocationPlan BuildReturnPlan()
        => BuildReturnPlan(
            DataLocation.EffectiveConfigDir,
            DataLocation.EffectiveCacheRootDir,
            DataLocation.DefaultConfigDir,
            DataLocation.DefaultCacheRootDir,
            DataLocation.MarkerPath);

    /// <summary>Rencana kembali ke bawaan dari folder eksplisit
    /// (dipakai tes).</summary>
    public static RelocationPlan BuildReturnPlan(
        string sourceConfigDir, string sourceCacheRootDir,
        string defaultConfigDir, string defaultCacheRootDir,
        string markerPath)
    {
        var move = DataMover.BuildPlan(new[]
        {
            ("Folder data aplikasi", sourceConfigDir, defaultConfigDir),
            ("Folder cache model", sourceCacheRootDir, defaultCacheRootDir),
        });
        return new RelocationPlan(move, "", true, markerPath);
    }

    /// <summary>Jalankan rencana + kelola penanda sesuai hasil:
    /// Completed → tulis penanda akar baru / hapus penanda (untuk
    /// kembali ke bawaan). Tidak Completed (galat/batal) → penanda
    /// tidak disentuh; resolver tetap menunjuk lokasi lama.</summary>
    public static async Task<MoveResult> ExecuteAsync(
        RelocationPlan plan, IProgress<MoveProgress>? progress = null,
        CancellationToken ct = default)
    {
        var result = await DataMover.ExecuteAsync(plan.Move, progress, ct)
            .ConfigureAwait(false);
        if (result.Completed)
        {
            if (plan.ReturnToDefault)
                DataLocation.DeleteMarker(plan.MarkerPath);
            else
                DataLocation.WriteMarker(plan.MarkerPath, plan.NewDataRoot);
        }
        return result;
    }
}
