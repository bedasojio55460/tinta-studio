using TintaStudio.Models;

namespace TintaStudio.Services;

/// <summary>
/// Pewarisan hasil kerja saat DETEKSI ULANG (v0.7.17, keluhan
/// pemilik: menekan Deteksi &amp; OCR pada halaman yang sudah
/// diterjemahkan membuang semua terjemahan/typeset lama).
///
/// Deteksi ulang menghasilkan blok BARU (Id, teks OCR, bahasa,
/// keyakinan semua baru dari gambar). Fungsi murni ini memasangkan
/// tiap blok baru dengan blok LAMA yang wilayahnya paling cocok,
/// lalu mewariskan pekerjaan pengguna dari blok lama: terjemahan
/// (+ cap asal &amp; waktu), cadangan poles, dan gaya typeset
/// (salinan dalam). Operasi bersih lama dipetakan ulang ke Id blok
/// pewarisnya; operasi yang bloknya tidak punya pewaris dibuang
/// (kotaknya sudah tidak ada — membiarkannya yatim hanya akan
/// menggambar ulang hapusan di tempat yang salah).
///
/// Aturan pasangan: kecocokan geometri terbaik menurut IoU;
/// diterima bila IoU ≥ <see cref="MinIou"/> ATAU pusat salah satu
/// kotak berada di dalam kotak lainnya (menangkap kotak yang
/// menyusut/mengembang jauh — IoU-nya kecil padahal jelas blok
/// yang sama). Tiap blok lama hanya boleh diwarisi SATU blok baru:
/// pencocokan rakus menurut IoU menurun.
/// </summary>
public static class BlockCarryOver
{
    /// <summary>Ambang IoU minimum pasangan pewarisan.</summary>
    public const double MinIou = 0.30;

    /// <summary>Hasil pewarisan satu halaman.</summary>
    public sealed record Result(
        IReadOnlyList<TextBlock> Blocks,
        IReadOnlyList<CleanOperation> CleanOps,
        IReadOnlyDictionary<string, string> IdMap,
        int InheritedCount);

    /// <summary>
    /// Terapkan pewarisan: blok baru (instans yang sama, di tempat)
    /// menerima warisan dari pasangan lamanya; daftar yang
    /// dikembalikan adalah blok baru dalam urutan aslinya. Blok
    /// lama &amp; operasi lama TIDAK diubah — operasi yang lolos
    /// adalah salinan dengan BlockId baru (Seq &amp; urutan sama).
    /// </summary>
    public static Result Apply(
        IReadOnlyList<TextBlock> oldBlocks,
        IReadOnlyList<TextBlock> newBlocks,
        IReadOnlyList<CleanOperation> oldOps)
    {
        // Semua pasangan calon (lama, baru) yang lolos aturan,
        // diurutkan IoU menurun — seri diputuskan oleh indeks agar
        // deterministik.
        var candidates = new List<(int OldIdx, int NewIdx, double Iou)>();
        for (int o = 0; o < oldBlocks.Count; o++)
        {
            for (int n = 0; n < newBlocks.Count; n++)
            {
                double iou = Iou(oldBlocks[o], newBlocks[n]);
                if (iou >= MinIou
                    || CenterInside(newBlocks[n], oldBlocks[o])
                    || CenterInside(oldBlocks[o], newBlocks[n]))
                    candidates.Add((o, n, iou));
            }
        }
        candidates.Sort((a, b) =>
        {
            int byIou = b.Iou.CompareTo(a.Iou);
            if (byIou != 0) return byIou;
            int byOld = a.OldIdx.CompareTo(b.OldIdx);
            return byOld != 0 ? byOld : a.NewIdx.CompareTo(b.NewIdx);
        });

        var usedOld = new HashSet<int>();
        var usedNew = new HashSet<int>();
        var idMap = new Dictionary<string, string>();
        int inherited = 0;
        foreach (var (o, n, _) in candidates)
        {
            if (usedOld.Contains(o) || usedNew.Contains(n))
                continue;
            usedOld.Add(o);
            usedNew.Add(n);
            Inherit(oldBlocks[o], newBlocks[n]);
            idMap[oldBlocks[o].Id] = newBlocks[n].Id;
            inherited++;
        }

        var ops = new List<CleanOperation>();
        foreach (var op in oldOps)
        {
            if (!idMap.TryGetValue(op.BlockId, out var newId))
                continue; // blok lama yatim — operasinya ikut dibuang
            ops.Add(new CleanOperation
            {
                BlockId = newId,
                Mode = op.Mode,
                FillColor = op.FillColor,
                LineProtect = op.LineProtect,
                Seq = op.Seq,
                AppliedUtc = op.AppliedUtc,
            });
        }
        return new Result(newBlocks, ops, idMap, inherited);
    }

    /// <summary>Salin pekerjaan pengguna dari blok lama ke blok
    /// baru. Text/Language/Confidence/dll blok baru TIDAK disentuh
    /// — itu hasil OCR yang baru.</summary>
    private static void Inherit(TextBlock from, TextBlock to)
    {
        to.Translation = from.Translation;
        to.TranslatedBy = from.TranslatedBy;
        to.TranslatedAt = from.TranslatedAt;
        to.TranslationBackup = from.TranslationBackup;
        to.TranslationBackupBy = from.TranslationBackupBy;
        to.Typeset = from.Typeset?.Clone();
    }

    /// <summary>IoU dua kotak blok (0 bila tidak bersinggungan).</summary>
    internal static double Iou(TextBlock a, TextBlock b)
    {
        int x1 = Math.Max(a.X, b.X), y1 = Math.Max(a.Y, b.Y);
        int x2 = Math.Min(a.X + a.Width, b.X + b.Width);
        int y2 = Math.Min(a.Y + a.Height, b.Y + b.Height);
        if (x2 <= x1 || y2 <= y1)
            return 0;
        double inter = (double)(x2 - x1) * (y2 - y1);
        double union = (double)a.Width * a.Height
            + (double)b.Width * b.Height - inter;
        return union <= 0 ? 0 : inter / union;
    }

    /// <summary>True bila pusat kotak <paramref name="inner"/>
    /// berada di dalam kotak <paramref name="outer"/>.</summary>
    internal static bool CenterInside(TextBlock inner, TextBlock outer)
    {
        double cx = inner.X + inner.Width / 2.0;
        double cy = inner.Y + inner.Height / 2.0;
        return cx >= outer.X && cx < outer.X + outer.Width
            && cy >= outer.Y && cy < outer.Y + outer.Height;
    }

    /// <summary>
    /// Penentu konfirmasi deteksi ulang (fungsi murni): true bila
    /// ada blok yang sudah membawa pekerjaan pengguna — terjemahan
    /// (teks tidak kosong) ATAU gaya typeset. Blok hasil OCR polos
    /// tidak memicu konfirmasi.
    /// </summary>
    public static bool RequiresConfirmation(IEnumerable<TextBlock> blocks) =>
        blocks.Any(b => !string.IsNullOrEmpty(b.Translation)
                        || b.Typeset is not null);

    /// <summary>Varian satu bab: true bila halaman mana pun di bab
    /// memicu <see cref="RequiresConfirmation(IEnumerable{TextBlock})"/>.</summary>
    public static bool RequiresConfirmation(IEnumerable<ProjectPage> pages) =>
        pages.Any(p => RequiresConfirmation(p.Blocks));
}
