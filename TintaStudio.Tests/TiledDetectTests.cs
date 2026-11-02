using TintaStudio.Services;
using Xunit;

namespace TintaStudio.Tests;

/// <summary>
/// Tes deteksi berpetak (v0.7.14): matematika petak, pemetaan
/// koordinat, dan penggabungan duplikat antar-petak — semua murni
/// (tanpa model). Bukti model asli pada strip webtoon sungguhan
/// dicatat di project-state (halaman 003 contoh pemilik:
/// 4 blok sampah → 10 blok bicara).
/// </summary>
public sealed class TiledDetectTests
{
    [Fact]
    public void NeedsTiling_AspectThreshold()
    {
        Assert.False(TileMath.NeedsTiling(900, 875));     // halaman contoh pemilik yang pendek
        Assert.False(TileMath.NeedsTiling(900, 1800));    // tepat 2,0 — belum melewati ambang
        Assert.True(TileMath.NeedsTiling(900, 1801));
        Assert.True(TileMath.NeedsTiling(900, 15412));    // strip contoh pemilik
        Assert.False(TileMath.NeedsTiling(1532, 1024));   // halaman lebar biasa
        Assert.False(TileMath.NeedsTiling(0, 100));
    }

    [Fact]
    public void ComputeTiles_LongStrip_CoversWithOverlapAndClosesAtBottom()
    {
        var tiles = TileMath.ComputeTiles(900, 15412);
        // tileH = 1,25 × 900 = 1125; overlap 225; langkah 900.
        Assert.Equal(17, tiles.Count);
        Assert.Equal((0, 1125), tiles[0]);
        var last = tiles[^1];
        Assert.Equal(15412, last.Y + last.Height); // menutup ujung bawah persis
        for (int i = 1; i < tiles.Count; i++)
        {
            int prevEnd = tiles[i - 1].Y + tiles[i - 1].Height;
            Assert.True(tiles[i].Y < prevEnd,
                $"Petak {i} tidak bertumpang dengan petak sebelumnya.");
            Assert.Equal(225, prevEnd - tiles[i].Y);
        }
    }

    [Fact]
    public void ComputeTiles_NormalPage_SingleTile()
    {
        var tiles = TileMath.ComputeTiles(900, 875);
        Assert.Single(tiles);
        Assert.Equal((0, 875), tiles[0]);
    }

    [Fact]
    public void MapToPage_ShiftsByTileOffsetAndClamps()
    {
        var box = new DetectedBox(10, 100, 50, 60, DetectedClass.TextBubble, 0.9);
        var mapped = TileMath.MapToPage(box, tileY: 900, pageWidth: 900, pageHeight: 15412);
        Assert.NotNull(mapped);
        Assert.Equal((10, 1000, 50, 60),
            (mapped!.X, mapped.Y, mapped.Width, mapped.Height));

        // Kotak yang meluber ujung bawah halaman dijepit
        // (petak terakhir mulai 14400, tingginya 1012).
        var edge = new DetectedBox(0, 950, 100, 100, DetectedClass.TextFree, 0.8);
        var clamped = TileMath.MapToPage(edge, tileY: 14400, pageWidth: 900, pageHeight: 15412);
        Assert.NotNull(clamped);
        Assert.Equal(15412, clamped!.Y + clamped.Height);
    }

    [Fact]
    public void Merge_TileDuplicates_KeepsHighestScorePerClass()
    {
        var boxes = new List<DetectedBox>
        {
            // Wilayah sama dari dua petak (kelas sama): satu dibuang.
            new(100, 1000, 200, 150, DetectedClass.TextBubble, 0.80),
            new(102, 1002, 200, 150, DetectedClass.TextBubble, 0.92),
            // Kotak bubble yang menaungi kotak teks (IoU tinggi tapi
            // beda kelas): keduanya sah, tidak digabung.
            new(90, 990, 300, 300, DetectedClass.Bubble, 0.70),
            // Kotak jauh berbeda: tetap.
            new(100, 5000, 200, 150, DetectedClass.TextBubble, 0.60),
        };
        var merged = TileMath.Merge(boxes);
        Assert.Equal(3, merged.Count);
        var text = merged.Where(b => b.Class == DetectedClass.TextBubble).ToList();
        Assert.Equal(2, text.Count);
        Assert.Contains(text, b => b.Score == 0.92);
        Assert.Contains(merged, b => b.Class == DetectedClass.Bubble);
    }
}
