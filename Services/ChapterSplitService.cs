using System.Security.Cryptography;
using System.Text;
using SkiaSharp;
using TintaStudio.Models;

namespace TintaStudio.Services;

/// <summary>
/// Operasi berkas/gambar pemotongan halaman panjang (v0.7.14):
/// menulis gambar potongan, membangun bab efektif (daftar halaman
/// yang tampil) dari bab impor asli + provenans proyek, dan
/// menjalankan potong/sambung ujung-ke-ujung di tingkat model.
/// Matematika murninya di PageSplitMath.
///
/// Gambar potongan disimpan sebagai PNG di folder kerja bab yang
/// sama turunannya dengan salinan kerja impor
/// (…/tinta-studio/chapters/&lt;hash&gt;/slices/) dan dibuat ulang
/// secara deterministik dari halaman kerja induk bila hilang —
/// berkas sumber pengguna tidak pernah diubah.
/// </summary>
public static class ChapterSplitService
{
    /// <summary>Folder potongan untuk satu sumber bab. Rumus hash
    /// sama dengan folder kerja impor di ViewModel (SHA-256 jalur
    /// penuh sumber, 8 byte pertama).</summary>
    public static string SlicesDirFor(string sourcePath)
    {
        var full = Path.GetFullPath(sourcePath);
        var hash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(full)), 0, 8).ToLowerInvariant();
        return Path.Combine(
            Path.GetTempPath(), "tinta-studio", "chapters", hash, "slices");
    }

    public static string SliceFileName(int parentIndex, int sliceIndex) =>
        $"p{parentIndex:D3}-s{sliceIndex:D2}.png";

    public static string SlicePath(string sourcePath, int parentIndex, int sliceIndex) =>
        Path.Combine(SlicesDirFor(sourcePath), SliceFileName(parentIndex, sliceIndex));

    /// <summary>Potong satu bitmap halaman menjadi bitmap potongan
    /// (salinan piksel persis, tanpa skala ulang).</summary>
    public static SKBitmap CropSlice(SKBitmap page, int offsetY, int height)
    {
        var slice = new SKBitmap(page.Width, height, page.ColorType, page.AlphaType);
        using var canvas = new SKCanvas(slice);
        canvas.DrawBitmap(page, 0, -offsetY);
        return slice;
    }

    /// <summary>Tulis semua berkas potongan untuk satu grup dari
    /// bitmap induk (menimpa yang ada — dipakai saat memotong).</summary>
    public static void WriteSliceFiles(
        string sourcePath, int parentIndex, SKBitmap parentBitmap,
        IReadOnlyList<(int OffsetY, int Height)> slices)
    {
        Directory.CreateDirectory(SlicesDirFor(sourcePath));
        for (int s = 0; s < slices.Count; s++)
        {
            using var slice = CropSlice(parentBitmap, slices[s].OffsetY, slices[s].Height);
            var path = SlicePath(sourcePath, parentIndex, s);
            using var fs = File.Create(path);
            using var data = SKImage.FromBitmap(slice).Encode(SKEncodedImageFormat.Png, 100);
            data.SaveTo(fs);
        }
    }

    /// <summary>Pastikan berkas potongan semua grup tersedia dan
    /// berdimensi benar; yang hilang/rusak dibuat ulang dari
    /// halaman kerja induk.</summary>
    public static void EnsureSliceFiles(Chapter original, ProjectChapter project)
    {
        foreach (var group in project.PageSplits)
        {
            if (group.ParentIndex < 0 || group.ParentIndex >= original.Pages.Count)
                continue;
            var parent = original.Pages[group.ParentIndex];
            bool needsWork = group.Slices
                .Select((slice, s) => (slice, s))
                .Any(t =>
                {
                    var path = SlicePath(original.SourcePath, group.ParentIndex, t.s);
                    if (!File.Exists(path))
                        return true;
                    using var info = SKBitmap.Decode(path);
                    return info is null
                        || info.Width != parent.Width
                        || info.Height != t.slice.Height;
                });
            if (!needsWork)
                continue;
            using var bmp = SKBitmap.Decode(parent.WorkPath)
                ?? throw new InvalidDataException(
                    $"Halaman kerja induk tidak terbaca: {parent.WorkPath}");
            WriteSliceFiles(original.SourcePath, group.ParentIndex, bmp,
                group.Slices.Select(s => (s.OffsetY, s.Height)).ToList());
        }
    }

    /// <summary>
    /// Bangun bab EFEKTIF yang tampil dari bab impor asli +
    /// provenans: tanpa grup → bab asli apa adanya; dengan grup →
    /// bab baru yang halamannya mengembang (potongan menggantikan
    /// induknya, penomoran mengikuti urutan efektif).
    /// </summary>
    public static Chapter BuildEffectiveChapter(Chapter original, ProjectChapter project)
    {
        if (project.PageSplits.Count == 0)
            return original;
        EnsureSliceFiles(original, project);
        var layout = PageSplitMath.ExpandLayout(original.Pages.Count, project.PageSplits);
        var pages = new List<ComicPage>();
        for (int i = 0; i < layout.Count; i++)
        {
            var entry = layout[i];
            var parent = original.Pages[entry.ParentIndex];
            if (entry.SliceIndex < 0)
            {
                pages.Add(new ComicPage
                {
                    Index = i,
                    SourceName = parent.SourceName,
                    SourceFormat = parent.SourceFormat,
                    Width = parent.Width,
                    Height = parent.Height,
                    WorkPath = parent.WorkPath,
                });
            }
            else
            {
                int total = project.PageSplits
                    .First(g => g.ParentIndex == entry.ParentIndex).Slices.Count;
                pages.Add(new ComicPage
                {
                    Index = i,
                    SourceName = $"{parent.SourceName} — potongan {entry.SliceIndex + 1}/{total}",
                    SourceFormat = "PNG",
                    Width = parent.Width,
                    Height = entry.Height,
                    WorkPath = SlicePath(original.SourcePath, entry.ParentIndex, entry.SliceIndex),
                });
            }
        }
        return new Chapter
        {
            Title = original.Title,
            SourcePath = original.SourcePath,
            Format = original.Format,
            Series = original.Series,
            Pages = pages,
            Skipped = original.Skipped,
        };
    }

    /// <summary>
    /// Potong halaman induk (indeks ASLI) pada bab terbuka: hitung
    /// densitas dari bitmap halaman kerja induk, cari titik potong,
    /// tulis berkas potongan, lalu terapkan ke model proyek.
    /// Mengembalikan (indeks efektif potongan pertama, potongan).
    /// </summary>
    public static (int FirstSliceEffective, IReadOnlyList<(int OffsetY, int Height)> Slices)
        SplitPage(Chapter original, ProjectChapter project, int parentIndex)
    {
        if (parentIndex < 0 || parentIndex >= original.Pages.Count)
            throw new ArgumentOutOfRangeException(nameof(parentIndex));
        if (project.PageSplits.Any(g => g.ParentIndex == parentIndex))
            throw new InvalidOperationException("Halaman ini sudah dipotong.");
        var parent = original.Pages[parentIndex];
        using var bmp = SKBitmap.Decode(parent.WorkPath)
            ?? throw new InvalidDataException(
                $"Halaman kerja tidak terbaca: {parent.WorkPath}");
        var density = PageSplitMath.RowInkDensity(bmp);
        var cuts = PageSplitMath.FindCutRows(density);
        var slices = PageSplitMath.BuildSlices(bmp.Height, cuts);
        if (slices.Count < 2)
            throw new InvalidOperationException(
                "Halaman ini tidak cukup tinggi untuk dipotong.");
        WriteSliceFiles(original.SourcePath, parentIndex, bmp, slices);
        int first = PageSplitMath.ApplySplit(project, parentIndex, slices);
        return (first, slices);
    }

    /// <summary>Sambung lagi: gabungkan potongan halaman induk di
    /// model proyek (blok dipetakan balik) + hapus berkas potongan.
    /// Mengembalikan indeks efektif halaman induk sesudahnya.</summary>
    public static int RejoinPage(Chapter original, ProjectChapter project, int parentIndex)
    {
        int index = PageSplitMath.ApplyRejoin(project, parentIndex);
        var dir = SlicesDirFor(original.SourcePath);
        if (Directory.Exists(dir))
        {
            foreach (var file in Directory.GetFiles(
                dir, $"p{parentIndex:D3}-s*.png"))
                File.Delete(file);
        }
        return index;
    }
}
