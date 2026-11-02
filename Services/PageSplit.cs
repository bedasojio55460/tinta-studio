using SkiaSharp;
using TintaStudio.Models;

namespace TintaStudio.Services;

/// <summary>
/// Pemotongan halaman panjang (v0.7.14) — bagian MURNI: densitas
/// tinta per baris, pencarian titik potong di celah minim tinta,
/// susunan potongan, tata letak halaman efektif dari provenans,
/// dan pemetaan ulang blok/sapuan saat potong &amp; sambung.
/// Operasi berkas/gambar hidup di ChapterSplitService.
///
/// Prinsip: pemotongan adalah aksi eksplisit pengguna. Titik
/// potong dicari di pita horizontal minim tinta (celah kosong
/// antar panel) paling dekat dengan tinggi sasaran, agar bubble
/// tidak terbelah; bila tidak ada celah dalam jendela pencarian,
/// potongan jatuh persis di tinggi sasaran.
/// </summary>
public static class PageSplitMath
{
    /// <summary>Tinggi sasaran tiap potongan (piksel).</summary>
    public const int TargetSliceHeight = 1600;

    /// <summary>Jendela pencarian celah di sekitar tinggi sasaran
    /// (± piksel).</summary>
    public const int CutSearchWindow = 400;

    /// <summary>Densitas tinta baris di bawah ini dianggap celah
    /// kosong (0,2% piksel sampel gelap).</summary>
    public const double InkGapThreshold = 0.002;

    /// <summary>Sisa ekor di bawah ini tidak dipotong lagi —
    /// potongan terakhir memanjang menelannya.</summary>
    public const int MinTailHeight = 400;

    /// <summary>Ambang gelap piksel tinta (saluran minimum &lt;
    /// ini) — keluarga ambang yang sama dengan penghalang gelap
    /// estimator bubble.</summary>
    public const int InkChannelThreshold = 140;

    /// <summary>Rujukan satu entri daftar halaman efektif:
    /// halaman asli ke-ParentIndex; SliceIndex −1 = halaman utuh
    /// (bukan potongan); selain itu indeks potongan di grupnya,
    /// dengan OffsetY/Height dari provenans (Height −1 = utuh).</summary>
    public sealed record LayoutEntry(
        int ParentIndex, int SliceIndex, int OffsetY, int Height);

    /// <summary>
    /// Densitas tinta per baris: fraksi piksel sampel yang gelap.
    /// Kolom disampel tiap <paramref name="columnStep"/> piksel
    /// (cukup untuk mencari celah; jauh lebih murah dari penuh).
    /// </summary>
    public static double[] RowInkDensity(SKBitmap page, int columnStep = 3)
    {
        var density = new double[page.Height];
        if (page.Width <= 0 || page.Height <= 0)
            return density;
        int step = Math.Max(1, columnStep);
        int samples = (page.Width + step - 1) / step;
        for (int y = 0; y < page.Height; y++)
        {
            int dark = 0;
            for (int x = 0; x < page.Width; x += step)
            {
                var px = page.GetPixel(x, y);
                int min = Math.Min(px.Red, Math.Min(px.Green, px.Blue));
                if (min < InkChannelThreshold)
                    dark++;
            }
            density[y] = samples > 0 ? (double)dark / samples : 0;
        }
        return density;
    }

    /// <summary>
    /// Cari baris-baris potong (Y awal potongan berikutnya) dari
    /// densitas baris: berurutan dari atas, tiap potongan
    /// ≈ <paramref name="target"/> piksel; di jendela
    /// ±<paramref name="window"/> di sekitar sasaran dipilih baris
    /// berdensitas minimum — dipakai sebagai titik potong HANYA
    /// bila densitasnya ≤ ambang celah; selain itu potong persis
    /// di sasaran. Berhenti bila sisa ≤ target + ekor minimum.
    /// </summary>
    public static IReadOnlyList<int> FindCutRows(
        IReadOnlyList<double> density,
        int target = TargetSliceHeight, int window = CutSearchWindow)
    {
        var cuts = new List<int>();
        int height = density.Count;
        if (height <= 0 || target <= 0)
            return cuts;
        int start = 0;
        while (height - start > target + MinTailHeight)
        {
            int desired = start + target;
            int lo = Math.Max(start + 1, desired - window);
            int hi = Math.Min(height - 1, desired + window);
            int bestY = -1;
            double bestDensity = double.MaxValue;
            int bestDistance = int.MaxValue;
            for (int y = lo; y <= hi; y++)
            {
                double d = density[y];
                int dist = Math.Abs(y - desired);
                if (d < bestDensity - 1e-12
                    || (Math.Abs(d - bestDensity) <= 1e-12 && dist < bestDistance))
                {
                    bestDensity = d;
                    bestY = y;
                    bestDistance = dist;
                }
            }
            int cut = bestY > start && bestDensity <= InkGapThreshold ? bestY : desired;
            cuts.Add(cut);
            start = cut;
        }
        return cuts;
    }

    /// <summary>Susun potongan (offset + tinggi) dari tinggi
    /// halaman + baris potong; gabungan tinggi = tinggi halaman.</summary>
    public static IReadOnlyList<(int OffsetY, int Height)> BuildSlices(
        int pageHeight, IReadOnlyList<int> cuts)
    {
        var slices = new List<(int OffsetY, int Height)>();
        int start = 0;
        foreach (var cut in cuts)
        {
            if (cut <= start || cut >= pageHeight)
                continue;
            slices.Add((start, cut - start));
            start = cut;
        }
        if (pageHeight > start)
            slices.Add((start, pageHeight - start));
        return slices;
    }

    /// <summary>
    /// Turunkan daftar halaman EFEKTIF dari jumlah halaman asli +
    /// grup potongan: halaman tanpa grup = satu entri utuh; halaman
    /// bergrup = satu entri per potongan, berurutan.
    /// </summary>
    public static IReadOnlyList<LayoutEntry> ExpandLayout(
        int originalCount, IReadOnlyList<PageSplitGroup> groups)
    {
        var byParent = new Dictionary<int, PageSplitGroup>();
        foreach (var g in groups)
            if (g.Slices.Count > 0)
                byParent[g.ParentIndex] = g;
        var layout = new List<LayoutEntry>();
        for (int i = 0; i < originalCount; i++)
        {
            if (byParent.TryGetValue(i, out var g))
            {
                for (int s = 0; s < g.Slices.Count; s++)
                    layout.Add(new LayoutEntry(
                        i, s, g.Slices[s].OffsetY, g.Slices[s].Height));
            }
            else
            {
                layout.Add(new LayoutEntry(i, -1, 0, -1));
            }
        }
        return layout;
    }

    /// <summary>
    /// Terapkan pemotongan ke daftar halaman proyek: entri halaman
    /// induk (pada indeks efektifnya saat ini) digantikan entri
    /// potongan — blok induk dibagikan ke potongan menurut pusat Y
    /// blok (koordinat Y blok/sapuan digeser relatif terhadap
    /// offset potongannya; operasi bersih mengikuti bloknya lewat
    /// BlockId); entri sesudahnya bergeser nomor. Mengembalikan
    /// indeks efektif potongan pertama. Grup provenans ditambahkan
    /// ke bab. MURNI terhadap model (tanpa berkas).
    /// </summary>
    public static int ApplySplit(ProjectChapter chapter, int parentIndex,
        IReadOnlyList<(int OffsetY, int Height)> slices)
    {
        if (slices.Count < 2)
            throw new ArgumentException(
                "Pemotongan harus menghasilkan ≥2 potongan.", nameof(slices));
        var layout = ExpandLayout(
            Math.Max(chapter.PageCount, parentIndex + 1), chapter.PageSplits);
        int effective = -1;
        for (int i = 0; i < layout.Count; i++)
            if (layout[i].ParentIndex == parentIndex && layout[i].SliceIndex <= 0)
            { effective = i; break; }
        if (effective < 0)
            throw new InvalidOperationException(
                $"Halaman induk {parentIndex} tidak ditemukan di tata letak.");

        var parent = chapter.Pages.FirstOrDefault(p => p.Index == effective);
        var slicePages = new List<ProjectPage>();
        for (int s = 0; s < slices.Count; s++)
            slicePages.Add(new ProjectPage { Index = effective + s });

        if (parent is not null)
        {
            foreach (var block in parent.Blocks)
            {
                int centerY = block.Y + block.Height / 2;
                int s = SliceIndexFor(slices, centerY);
                var copy = CloneBlock(block);
                copy.Y = block.Y - slices[s].OffsetY;
                slicePages[s].Blocks.Add(copy);
            }
            var blockSlice = new Dictionary<string, int>();
            for (int s = 0; s < slicePages.Count; s++)
                foreach (var b in slicePages[s].Blocks)
                    blockSlice[b.Id] = s;
            foreach (var op in parent.CleanOps)
            {
                // Operasi bersih mengikuti bloknya; operasi yatim
                // (blok tak dikenal) jatuh ke potongan pertama.
                int s = blockSlice.TryGetValue(op.BlockId, out var idx) ? idx : 0;
                slicePages[s].CleanOps.Add(CloneOp(op));
            }
            foreach (var stroke in parent.ManualStrokes)
            {
                int s = stroke.Points.Count > 0
                    ? SliceIndexFor(slices, stroke.Points[0].Y) : 0;
                var copy = CloneStroke(stroke);
                foreach (var pt in copy.Points)
                    pt.Y -= slices[s].OffsetY;
                slicePages[s].ManualStrokes.Add(copy);
            }
            for (int s = 0; s < slicePages.Count; s++)
                slicePages[s].CleanSeq = parent.CleanSeq;
        }

        // Geser nomor entri sesudah induk, tukar induk ↔ potongan.
        chapter.Pages.RemoveAll(p => p.Index == effective);
        foreach (var p in chapter.Pages)
            if (p.Index > effective)
                p.Index += slices.Count - 1;
        chapter.Pages.AddRange(slicePages);
        chapter.Pages.Sort((a, b) => a.Index.CompareTo(b.Index));

        chapter.PageSplits.RemoveAll(g => g.ParentIndex == parentIndex);
        chapter.PageSplits.Add(new PageSplitGroup
        {
            ParentIndex = parentIndex,
            Slices = slices.Select(s => new PageSlice
            { OffsetY = s.OffsetY, Height = s.Height }).ToList(),
        });
        chapter.PageSplits.Sort((a, b) => a.ParentIndex.CompareTo(b.ParentIndex));
        return effective;
    }

    /// <summary>
    /// Batalkan pemotongan (Sambung Lagi): entri potongan digabung
    /// kembali jadi satu entri induk — blok/sapuan dipetakan balik
    /// ke koordinat induk lewat offset potongan (Y + offset),
    /// operasi bersih digabung dengan Seq dinomori ulang berurutan
    /// agar Urungkan tetap masuk akal; entri sesudahnya bergeser
    /// mundur; grup provenans dihapus. Mengembalikan indeks
    /// efektif halaman induk sesudah penyambungan.
    /// </summary>
    public static int ApplyRejoin(ProjectChapter chapter, int parentIndex)
    {
        var group = chapter.PageSplits.FirstOrDefault(g => g.ParentIndex == parentIndex)
            ?? throw new InvalidOperationException(
                $"Halaman {parentIndex} tidak punya potongan untuk disambung.");
        var layout = ExpandLayout(
            Math.Max(chapter.PageCount, parentIndex + 1), chapter.PageSplits);
        var sliceEffective = new List<int>();
        for (int i = 0; i < layout.Count; i++)
            if (layout[i].ParentIndex == parentIndex && layout[i].SliceIndex >= 0)
                sliceEffective.Add(i);
        if (sliceEffective.Count == 0)
            throw new InvalidOperationException(
                "Tata letak tidak memuat potongan halaman ini.");
        int first = sliceEffective[0];

        var merged = new ProjectPage { Index = first };
        int seq = 0;
        for (int s = 0; s < group.Slices.Count; s++)
        {
            int eff = sliceEffective[s];
            int offset = group.Slices[s].OffsetY;
            var page = chapter.Pages.FirstOrDefault(p => p.Index == eff);
            if (page is null)
                continue;
            foreach (var block in page.Blocks)
            {
                var copy = CloneBlock(block);
                copy.Y = block.Y + offset;
                merged.Blocks.Add(copy);
            }
            foreach (var op in page.CleanOps)
            {
                var copy = CloneOp(op);
                copy.Seq = ++seq;
                merged.CleanOps.Add(copy);
            }
            foreach (var stroke in page.ManualStrokes)
            {
                var copy = CloneStroke(stroke);
                foreach (var pt in copy.Points)
                    pt.Y += offset;
                copy.Seq = ++seq;
                merged.ManualStrokes.Add(copy);
            }
            merged.CleanSeq = Math.Max(merged.CleanSeq, page.CleanSeq);
        }
        merged.CleanSeq = Math.Max(merged.CleanSeq, seq);

        var remove = new HashSet<int>(sliceEffective);
        chapter.Pages.RemoveAll(p => remove.Contains(p.Index));
        foreach (var p in chapter.Pages)
            if (p.Index > first)
                p.Index -= group.Slices.Count - 1;
        chapter.Pages.Add(merged);
        chapter.Pages.Sort((a, b) => a.Index.CompareTo(b.Index));
        chapter.PageSplits.Remove(group);
        return first;
    }

    private static int SliceIndexFor(
        IReadOnlyList<(int OffsetY, int Height)> slices, int y)
    {
        for (int s = 0; s < slices.Count; s++)
            if (y < slices[s].OffsetY + slices[s].Height)
                return s;
        return slices.Count - 1;
    }

    internal static TextBlock CloneBlock(TextBlock b) => new()
    {
        Id = b.Id, X = b.X, Y = b.Y, Width = b.Width, Height = b.Height,
        Direction = b.Direction, Language = b.Language, Text = b.Text,
        Confidence = b.Confidence, DetectionScore = b.DetectionScore,
        Translation = b.Translation, TranslatedBy = b.TranslatedBy,
        TranslatedAt = b.TranslatedAt, TranslationError = b.TranslationError,
        TranslationBackup = b.TranslationBackup,
        TranslationBackupBy = b.TranslationBackupBy,
        Typeset = b.Typeset?.Clone(),
    };

    internal static CleanOperation CloneOp(CleanOperation op) => new()
    {
        BlockId = op.BlockId, Mode = op.Mode, FillColor = op.FillColor,
        LineProtect = op.LineProtect, Seq = op.Seq, AppliedUtc = op.AppliedUtc,
    };

    internal static BrushStroke CloneStroke(BrushStroke s) => new()
    {
        Kind = s.Kind, Erased = s.Erased, LineProtect = s.LineProtect,
        Color = s.Color, Radius = s.Radius, Seq = s.Seq,
        Points = s.Points.Select(p => new StrokePoint { X = p.X, Y = p.Y }).ToList(),
    };
}
