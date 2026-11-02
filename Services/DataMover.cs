using System.Security.Cryptography;

namespace TintaStudio.Services;

/// <summary>Satu pasangan folder dalam rencana pindah: label
/// manusiawi, sumber, tujuan, keberadaan + ukuran live sumber,
/// dan apakah keduanya di volume yang sama (rename atomik
/// dimungkinkan) atau lintas volume (salin–verifikasi–hapus).</summary>
public sealed record MoveUnit(
    string Label, string SourceDir, string DestinationDir,
    bool Exists, long SizeBytes, bool SameVolume);

/// <summary>Rencana pindah: daftar unit + total ukuran sumber.</summary>
public sealed record MovePlan(IReadOnlyList<MoveUnit> Units)
{
    public long TotalBytes => Units.Sum(u => u.SizeBytes);
}

/// <summary>Satu laporan progres pemindahan (agregat seluruh
/// rencana): berkas yang sedang diproses + byte/berkas selesai
/// dari total. TotalBytes 0 (semua sumber kosong) → persen tak
/// terdefinisi; UI menampilkan hitungan berkas saja.</summary>
public sealed record MoveProgress(
    string CurrentPath, long BytesDone, long TotalBytes, int FilesDone, int FilesTotal)
{
    public double? Percent => TotalBytes > 0
        ? Math.Clamp(BytesDone * 100.0 / TotalBytes, 0, 100)
        : null;
}

/// <summary>Hasil eksekusi pindah. Completed = setiap berkas
/// berhasil tersalin/berpindah DAN terverifikasi (syarat penanda
/// lokasi boleh ditulis). Leftovers = berkas sumber yang SUDAH
/// tersalin sah ke tujuan tetapi gagal dihapus dari sumber
/// (terkunci proses berjalan) — bukan kegagalan pindah; sisanya
/// tetap di lokasi lama dan kelak muncul sebagai baris "lokasi
/// lama" di Hapus Bersih. Errors = kegagalan salin/verifikasi per
/// berkas (Completed=false; tujuan mungkin berisi salinan sebagian
/// — menjalankan pindah lagi menimpanya sampai lengkap).</summary>
public sealed record MoveResult(
    bool Completed,
    bool Cancelled,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Leftovers,
    long BytesMoved);

/// <summary>
/// Mesin pindah folder data (v0.7.13): satu volume → rename
/// (Directory.Move; jatuh ke salin bila ditolak sistem), lintas
/// volume → salin + verifikasi (panjang + SHA-256) + hapus sumber,
/// per berkas, async dengan progres dan pembatalan. Logika rencana
/// murni/statis agar teruji; eksekusi berkas tipis di atasnya.
/// Dipakai pemindahan data aplikasi (DataRelocation) dan
/// pemindahan isi folder model Ollama.
/// </summary>
public static class DataMover
{
    /// <summary>Apakah dua jalur berada di volume yang sama —
    /// heuristik akar jalur (huruf drive / share UNC / "/").
    /// Bukan jaminan perangkat, hanya pemilih strategi awal;
    /// kegagalan rename selalu jatuh ke jalur salin.</summary>
    public static bool SameVolume(string pathA, string pathB)
    {
        try
        {
            var rootA = Path.GetPathRoot(Path.GetFullPath(pathA)) ?? "";
            var rootB = Path.GetPathRoot(Path.GetFullPath(pathB)) ?? "";
            return string.Equals(rootA, rootB, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Validasi folder tujuan pindah data aplikasi:
    /// harus jalur mutlak, tidak sama dengan / di dalam / memuat
    /// folder sumber mana pun (mencegah rekursi pindah ke dalam
    /// dirinya). Mengembalikan pesan galat manusiawi, atau null
    /// bila sah. Murni — dipakai UI sebelum rencana dibangun dan
    /// oleh tes.</summary>
    public static string? ValidateDestination(
        string destinationRoot, IReadOnlyList<string> sourceDirs)
    {
        var dest = (destinationRoot ?? "").Trim();
        if (dest.Length == 0 || !Path.IsPathFullyQualified(dest))
            return "Folder tujuan harus berupa jalur lengkap (mis. D:\\Data\\TintaStudio).";
        var derived = DataLocation.DirsForRoot(dest);
        var destDirs = new List<string> { dest, derived.ConfigDir, derived.CacheRootDir };
        foreach (var src in sourceDirs)
        {
            foreach (var dd in destDirs)
            {
                if (DataLocation.PathsEqual(dd, src)
                    || DataLocation.IsInside(dd, src)
                    || DataLocation.IsInside(src, dd))
                    return "Folder tujuan tidak boleh sama dengan, di dalam, " +
                           "atau memuat folder data yang sekarang " +
                           $"({src}). Pilih folder lain yang terpisah.";
            }
        }
        return null;
    }

    /// <summary>Ruang bebas (byte) pada volume yang menampung
    /// jalur ini — dihitung dari leluhur terdekat yang sudah ada.
    /// Null bila tidak bisa ditentukan.</summary>
    public static long? FreeSpaceFor(string path)
    {
        try
        {
            var probe = Path.GetFullPath(path);
            while (!Directory.Exists(probe))
            {
                var parent = Path.GetDirectoryName(probe);
                if (string.IsNullOrEmpty(parent) || parent == probe)
                    return null;
                probe = parent;
            }
            var root = Path.GetPathRoot(probe);
            if (string.IsNullOrEmpty(root))
                return null;
            return new DriveInfo(root).AvailableFreeSpace;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Bangun rencana dari pasangan (label, sumber,
    /// tujuan): ukuran live per sumber + strategi per pasangan.</summary>
    public static MovePlan BuildPlan(
        IReadOnlyList<(string Label, string SourceDir, string DestinationDir)> pairs)
    {
        var units = new List<MoveUnit>();
        foreach (var (label, src, dst) in pairs)
        {
            bool exists = Directory.Exists(src);
            units.Add(new MoveUnit(
                label, src, dst, exists,
                exists ? DataWipe.DirectorySize(src) : 0,
                SameVolume(src, dst)));
        }
        return new MovePlan(units);
    }

    /// <summary>Eksekusi rencana secara async. Batal lewat token:
    /// berhenti di antara berkas; Completed=false, Cancelled=true;
    /// penanda tidak ditulis pemanggil.</summary>
    public static async Task<MoveResult> ExecuteAsync(
        MovePlan plan, IProgress<MoveProgress>? progress = null,
        CancellationToken ct = default)
    {
        var errors = new List<string>();
        var leftovers = new List<string>();
        long bytesMoved = 0;
        int filesTotal = plan.Units
            .Where(u => u.Exists && !DataLocation.PathsEqual(u.SourceDir, u.DestinationDir))
            .Sum(u => CountFiles(u.SourceDir));
        int filesDone = 0;
        long bytesTotal = plan.TotalBytes;

        void Report(string current) => progress?.Report(
            new MoveProgress(current, bytesMoved, bytesTotal, filesDone, filesTotal));

        try
        {
            foreach (var unit in plan.Units)
            {
                ct.ThrowIfCancellationRequested();
                if (!unit.Exists || !Directory.Exists(unit.SourceDir))
                {
                    // Sumber belum ada: siapkan folder tujuan kosong
                    // agar resolver punya rumah di lokasi baru.
                    try { Directory.CreateDirectory(unit.DestinationDir); }
                    catch (Exception ex)
                    {
                        errors.Add($"{unit.DestinationDir}: {ex.Message}");
                    }
                    continue;
                }
                if (DataLocation.PathsEqual(unit.SourceDir, unit.DestinationDir))
                    continue; // sudah di tempatnya — bukan pekerjaan

                bool renamed = false;
                if (unit.SameVolume && DestinationIsClear(unit.DestinationDir))
                {
                    try
                    {
                        var parent = Path.GetDirectoryName(
                            Path.GetFullPath(unit.DestinationDir));
                        if (!string.IsNullOrEmpty(parent))
                            Directory.CreateDirectory(parent);
                        Directory.Move(unit.SourceDir, unit.DestinationDir);
                        renamed = true;
                    }
                    catch (Exception)
                    {
                        renamed = false; // jatuh ke jalur salin di bawah
                    }
                }
                if (renamed)
                {
                    bytesMoved += unit.SizeBytes;
                    filesDone += CountFiles(unit.DestinationDir);
                    Report(unit.DestinationDir);
                    continue;
                }

                // Jalur salin–verifikasi–hapus, per berkas.
                var files = Directory.EnumerateFiles(
                    unit.SourceDir, "*", SearchOption.AllDirectories).ToList();
                foreach (var srcFile in files)
                {
                    ct.ThrowIfCancellationRequested();
                    var rel = Path.GetRelativePath(unit.SourceDir, srcFile);
                    var dstFile = Path.Combine(unit.DestinationDir, rel);
                    Report(srcFile);
                    try
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(dstFile)!);
                        await CopyVerifiedAsync(srcFile, dstFile, ct)
                            .ConfigureAwait(false);
                        long len = new FileInfo(srcFile).Length;
                        try
                        {
                            File.Delete(srcFile);
                        }
                        catch (Exception)
                        {
                            // Tersalin sah tapi sumber terkunci:
                            // sisa dilaporkan, pindah tetap sah.
                            leftovers.Add(srcFile);
                        }
                        bytesMoved += len;
                        filesDone++;
                        Report(dstFile);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"{srcFile} → {dstFile}: {ex.Message}");
                    }
                }
                // Bersihkan kerangka folder sumber yang sudah kosong
                // (yang masih berisi sisa terkunci dibiarkan).
                PruneEmptyDirs(unit.SourceDir);
            }
        }
        catch (OperationCanceledException)
        {
            return new MoveResult(false, true, errors, leftovers, bytesMoved);
        }

        return new MoveResult(
            errors.Count == 0, false, errors, leftovers, bytesMoved);
    }

    /// <summary>Tujuan "bersih" untuk rename: belum ada, atau
    /// folder kosong yang boleh disingkirkan dulu.</summary>
    private static bool DestinationIsClear(string destinationDir)
    {
        if (!Directory.Exists(destinationDir))
            return !File.Exists(destinationDir);
        try
        {
            if (!Directory.EnumerateFileSystemEntries(destinationDir).Any())
            {
                Directory.Delete(destinationDir);
                return true;
            }
        }
        catch { /* tidak bisa disingkirkan → jalur salin */ }
        return false;
    }

    private static int CountFiles(string dir)
    {
        try { return Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Count(); }
        catch { return 0; }
    }

    /// <summary>Salin satu berkas + verifikasi: panjang sama DAN
    /// SHA-256 sama. Hash sumber dihitung sambil menyalin; hash
    /// tujuan dihitung dari berkas hasil. Ketidakcocokan →
    /// IOException (tercatat sebagai galat per berkas).</summary>
    private static async Task CopyVerifiedAsync(
        string sourceFile, string destinationFile, CancellationToken ct)
    {
        byte[] sourceHash;
        await using (var src = new FileStream(sourceFile, FileMode.Open,
                         FileAccess.Read, FileShare.Read, 81920, useAsync: true))
        await using (var dst = new FileStream(destinationFile, FileMode.Create,
                         FileAccess.Write, FileShare.None, 81920, useAsync: true))
        {
            using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[81920];
            int read;
            while ((read = await src.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                hasher.AppendData(buffer, 0, read);
                await dst.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            }
            sourceHash = hasher.GetHashAndReset();
        }
        var srcInfo = new FileInfo(sourceFile);
        var dstInfo = new FileInfo(destinationFile);
        if (srcInfo.Length != dstInfo.Length)
            throw new IOException(
                $"Verifikasi gagal: ukuran salinan berbeda ({dstInfo.Length} dari {srcInfo.Length} byte).");
        byte[] destHash;
        await using (var dst = new FileStream(destinationFile, FileMode.Open,
                         FileAccess.Read, FileShare.Read, 81920, useAsync: true))
        {
            destHash = await SHA256.HashDataAsync(dst, ct).ConfigureAwait(false);
        }
        if (!sourceHash.AsSpan().SequenceEqual(destHash))
            throw new IOException("Verifikasi gagal: hash salinan tidak cocok.");
    }

    /// <summary>Hapus folder-folder kosong di dalam (dan termasuk)
    /// akar ini, dari yang terdalam. Folder yang masih berisi
    /// berkas — sisa terkunci atau galat — sengaja dibiarkan.</summary>
    private static void PruneEmptyDirs(string root)
    {
        try
        {
            if (!Directory.Exists(root))
                return;
            foreach (var dir in Directory.EnumerateDirectories(
                         root, "*", SearchOption.AllDirectories)
                     .OrderByDescending(d => d.Length))
            {
                try
                {
                    if (!Directory.EnumerateFileSystemEntries(dir).Any())
                        Directory.Delete(dir);
                }
                catch { /* dibiarkan */ }
            }
            try
            {
                if (!Directory.EnumerateFileSystemEntries(root).Any())
                    Directory.Delete(root);
            }
            catch { /* dibiarkan */ }
        }
        catch { /* kerangka sumber apa adanya */ }
    }
}
