using SkiaSharp;
using TintaStudio.Models;
using TintaStudio.Services;
using Xunit;

namespace TintaStudio.Tests;

/// <summary>
/// Tes pemotongan halaman panjang (v0.7.14, skema proyek v7):
/// pencarian titik potong di celah minim tinta, tata letak
/// efektif, pemetaan blok saat potong &amp; sambung, muat-balik
/// berkas v6, dan layanan ujung-ke-ujung pada strip sintetis
/// (termasuk ekspor tersambung kembali ke dimensi asal).
/// </summary>
public sealed class PageSplitTests
{
    private static double[] DensityWithGaps(int height, params (int From, int To)[] gaps)
    {
        var d = new double[height];
        Array.Fill(d, 0.05);
        foreach (var (from, to) in gaps)
            for (int y = from; y <= to && y < height; y++)
                d[y] = 0;
        return d;
    }

    [Fact]
    public void FindCutRows_GapsNearTarget_CutsLandInGaps()
    {
        var density = DensityWithGaps(5000, (1550, 1650), (3150, 3250), (4700, 4800));
        var cuts = PageSplitMath.FindCutRows(density);
        Assert.Equal(new[] { 1600, 3200 }, cuts.ToArray());
        foreach (var cut in cuts)
            Assert.Equal(0, density[cut]);
    }

    [Fact]
    public void FindCutRows_OffCenterGap_CutAtGapEdgeNearestTarget()
    {
        // Celah hanya di 1300–1400; sasaran 1600, jendela ±400 →
        // baris celah terdekat dengan sasaran = 1400.
        var density = DensityWithGaps(5000, (1300, 1400), (3150, 3250));
        var cuts = PageSplitMath.FindCutRows(density);
        Assert.Equal(1400, cuts[0]);
        Assert.Equal(0, density[cuts[0]]);
    }

    [Fact]
    public void FindCutRows_NoGaps_ExactTargetCuts()
    {
        var density = DensityWithGaps(5000);
        var cuts = PageSplitMath.FindCutRows(density);
        Assert.Equal(new[] { 1600, 3200 }, cuts.ToArray());
    }

    [Fact]
    public void FindCutRows_ShortPage_NoCuts()
    {
        Assert.Empty(PageSplitMath.FindCutRows(DensityWithGaps(875)));
        Assert.Empty(PageSplitMath.FindCutRows(DensityWithGaps(2000)));
    }

    [Fact]
    public void BuildSlices_SumsToPageHeight()
    {
        var slices = PageSplitMath.BuildSlices(5000, new[] { 1600, 3200 });
        Assert.Equal(new[] { (0, 1600), (1600, 1600), (3200, 1800) }, slices.ToArray());
        Assert.Equal(5000, slices.Sum(s => s.Height));
    }

    [Fact]
    public void RowInkDensity_BlackBand_HighOnlyThere()
    {
        using var page = new SKBitmap(100, 300);
        using (var canvas = new SKCanvas(page))
        {
            canvas.Clear(SKColors.White);
            canvas.DrawRect(new SKRect(0, 100, 100, 112), new SKPaint { Color = SKColors.Black });
        }
        var density = PageSplitMath.RowInkDensity(page);
        Assert.Equal(0, density[50]);
        Assert.True(density[105] > 0.9, $"Densitas pita hitam {density[105]:F2}");
        Assert.Equal(0, density[200]);
    }

    [Fact]
    public void ExpandLayout_GroupExpandsInPlace()
    {
        var groups = new List<PageSplitGroup>
        {
            new() { ParentIndex = 1, Slices = new()
            {
                new PageSlice { OffsetY = 0, Height = 1600 },
                new PageSlice { OffsetY = 1600, Height = 900 },
            } },
        };
        var layout = PageSplitMath.ExpandLayout(3, groups);
        Assert.Equal(4, layout.Count);
        Assert.Equal((0, -1), (layout[0].ParentIndex, layout[0].SliceIndex));
        Assert.Equal((1, 0), (layout[1].ParentIndex, layout[1].SliceIndex));
        Assert.Equal((1, 1), (layout[2].ParentIndex, layout[2].SliceIndex));
        Assert.Equal(1600, layout[2].OffsetY);
        Assert.Equal((2, -1), (layout[3].ParentIndex, layout[3].SliceIndex));
    }

    private static ProjectChapter ChapterWithStrip()
    {
        var chapter = new ProjectChapter
        {
            SourcePath = "/tmp/strip.cbz", Title = "Strip", Format = "Folder", PageCount = 3,
        };
        var p0 = chapter.PageAt(0);
        p0.Blocks.Add(new TextBlock { X = 5, Y = 50, Width = 50, Height = 20, Text = "halaman-0" });
        var parent = chapter.PageAt(1);
        parent.Blocks.Add(new TextBlock { Id = "atas", X = 10, Y = 100, Width = 80, Height = 40, Text = "atas" });
        parent.Blocks.Add(new TextBlock { Id = "bawah", X = 10, Y = 5000, Width = 80, Height = 40, Text = "bawah" });
        parent.CleanOps.Add(new CleanOperation { BlockId = "bawah", Mode = CleanMode.Solid, Seq = 1 });
        parent.ManualStrokes.Add(new BrushStroke
        {
            Seq = 2,
            Points = new() { new StrokePoint { X = 3, Y = 1700 } },
        });
        parent.CleanSeq = 2;
        chapter.PageAt(2).Blocks.Add(
            new TextBlock { X = 5, Y = 60, Width = 50, Height = 20, Text = "halaman-2" });
        return chapter;
    }

    [Fact]
    public void ApplySplit_RedistributesBlocksStrokesOpsAndShiftsIndices()
    {
        var chapter = ChapterWithStrip();
        var slices = new[] { (0, 1600), (1600, 4400) };
        int first = PageSplitMath.ApplySplit(chapter, parentIndex: 1, slices);
        Assert.Equal(1, first);
        Assert.Equal(new[] { 0, 1, 2, 3 },
            chapter.Pages.Select(p => p.Index).OrderBy(i => i).ToArray());

        var slice0 = chapter.Pages.First(p => p.Index == 1);
        var atas = Assert.Single(slice0.Blocks);
        Assert.Equal(100, atas.Y);

        var slice1 = chapter.Pages.First(p => p.Index == 2);
        var bawah = Assert.Single(slice1.Blocks);
        Assert.Equal(5000 - 1600, bawah.Y); // relatif terhadap potongan
        Assert.Single(slice1.CleanOps);     // operasi mengikuti bloknya
        var stroke = Assert.Single(slice1.ManualStrokes);
        Assert.Equal(1700 - 1600, stroke.Points[0].Y);

        var last = chapter.Pages.First(p => p.Index == 3);
        Assert.Equal("halaman-2", Assert.Single(last.Blocks).Text);
        var group = Assert.Single(chapter.PageSplits);
        Assert.Equal(1, group.ParentIndex);
        Assert.Equal(2, group.Slices.Count);
    }

    [Fact]
    public void ApplyRejoin_RestoresParentCoordinatesAndIndices()
    {
        var chapter = ChapterWithStrip();
        PageSplitMath.ApplySplit(chapter, 1, new[] { (0, 1600), (1600, 4400) });
        // Suntingan di potongan: blok baru di potongan kedua.
        var slice1 = chapter.Pages.First(p => p.Index == 2);
        slice1.Blocks.Add(new TextBlock { X = 1, Y = 10, Width = 10, Height = 10, Text = "baru" });

        int index = PageSplitMath.ApplyRejoin(chapter, 1);
        Assert.Equal(1, index);
        Assert.Empty(chapter.PageSplits);
        Assert.Equal(new[] { 0, 1, 2 },
            chapter.Pages.Select(p => p.Index).OrderBy(i => i).ToArray());
        var parent = chapter.Pages.First(p => p.Index == 1);
        Assert.Equal(3, parent.Blocks.Count);
        Assert.Equal(100, parent.Blocks.Single(b => b.Text == "atas").Y);
        Assert.Equal(5000, parent.Blocks.Single(b => b.Text == "bawah").Y);
        Assert.Equal(1600 + 10, parent.Blocks.Single(b => b.Text == "baru").Y);
        Assert.Equal(1700, Assert.Single(parent.ManualStrokes).Points[0].Y);
        Assert.Single(parent.CleanOps);
    }

    [Fact]
    public void Project_V7_RoundTripsSplits()
    {
        var dir = Path.Combine(Path.GetTempPath(),
            "tinta-split-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "p.tinta.json");
        var project = new TintaProject { Name = "Uji Potong" };
        var chapter = ChapterWithStrip();
        PageSplitMath.ApplySplit(chapter, 1, new[] { (0, 1600), (1600, 4400) });
        project.Chapters.Add(chapter);
        project.Save(path);

        var loaded = TintaProject.Load(path);
        Assert.Equal(TintaProject.CurrentVersion, loaded.Version);
        var group = Assert.Single(loaded.Chapters[0].PageSplits);
        Assert.Equal(1, group.ParentIndex);
        Assert.Equal(4400, group.Slices[1].Height);
        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void Project_LoadsV6File_NoSplits()
    {
        var dir = Path.Combine(Path.GetTempPath(),
            "tinta-split-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "lama.tinta.json");
        File.WriteAllText(path, """
            {
              "format": "tinta-studio-project",
              "version": 6,
              "name": "Proyek v6",
              "chapters": [
                { "sourcePath": "/tmp/bab.cbz", "title": "Bab",
                  "format": "ZIP (CBZ)", "pageCount": 2,
                  "pages": [ { "index": 0, "blocks": [], "cleanOps": [],
                               "manualStrokes": [], "cleanSeq": 0 } ] }
              ]
            }
            """);
        var loaded = TintaProject.Load(path);
        Assert.Equal(6, loaded.Version);
        Assert.NotNull(loaded.Chapters[0].PageSplits);
        Assert.Empty(loaded.Chapters[0].PageSplits);
        Directory.Delete(dir, recursive: true);
    }

    // ─── Layanan ujung-ke-ujung pada strip sintetis ─────────────────

    private static (Chapter Chapter, string Dir) MakeStripChapter()
    {
        var dir = Path.Combine(Path.GetTempPath(),
            "tinta-strip-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        // Strip 200×5000: panel gelap dengan celah putih lebar di
        // sekitar baris sasaran potong (1600/3200) — titik potong
        // harus jatuh di celah.
        using var page = new SKBitmap(200, 5000);
        using (var canvas = new SKCanvas(page))
        {
            canvas.Clear(SKColors.White);
            using var paint = new SKPaint { Color = SKColors.Black };
            canvas.DrawRect(new SKRect(10, 100, 190, 1400), paint);
            canvas.DrawRect(new SKRect(10, 1800, 190, 3000), paint);
            canvas.DrawRect(new SKRect(10, 3400, 190, 4900), paint);
        }
        var work = Path.Combine(dir, "page-001.png");
        using (var fs = File.Create(work))
        using (var data = SKImage.FromBitmap(page).Encode(SKEncodedImageFormat.Png, 100))
            data.SaveTo(fs);
        var chapter = new Chapter
        {
            Title = "Strip Uji",
            SourcePath = Path.Combine(dir, "strip.cbz"),
            Format = "Folder",
            Pages = new[]
            {
                new ComicPage
                {
                    Index = 0, SourceName = "001.png", SourceFormat = "PNG",
                    Width = 200, Height = 5000, WorkPath = work,
                },
            },
        };
        return (chapter, dir);
    }

    [Fact]
    public void SplitService_Strip_EndToEndAndStitchedExportRestoresSize()
    {
        var (chapter, dir) = MakeStripChapter();
        try
        {
            var projectChapter = new ProjectChapter
            {
                SourcePath = chapter.SourcePath, Title = chapter.Title,
                Format = chapter.Format, PageCount = 1,
            };
            var (first, slices) = ChapterSplitService.SplitPage(chapter, projectChapter, 0);
            Assert.Equal(0, first);
            Assert.True(slices.Count >= 3, $"Potongan hanya {slices.Count}.");
            Assert.Equal(5000, slices.Sum(s => s.Height));
            // Titik potong di celah putih (densitas baris potong = 0 tinta panel).
            using (var bmp = SKBitmap.Decode(chapter.Pages[0].WorkPath))
            {
                var density = PageSplitMath.RowInkDensity(bmp!);
                foreach (var s in slices.Skip(1))
                    Assert.True(density[s.OffsetY] <= PageSplitMath.InkGapThreshold,
                        $"Potong di y={s.OffsetY} densitas {density[s.OffsetY]:F3} — membelah panel?");
            }
            for (int si = 0; si < slices.Count; si++)
            {
                var path = ChapterSplitService.SlicePath(chapter.SourcePath, 0, si);
                Assert.True(File.Exists(path), $"Berkas potongan hilang: {path}");
            }

            var effective = ChapterSplitService.BuildEffectiveChapter(chapter, projectChapter);
            Assert.Equal(slices.Count, effective.Pages.Count);

            // Ekspor tersambung → satu halaman persis 200×5000.
            var outDir = Path.Combine(dir, "out-stitch");
            var stitched = ExportService.ExportChapter(
                effective, projectChapter, outDir,
                new ExportOptions(ExportKind.Folder, ExportImageFormat.Png,
                    90, StitchSplits: true));
            Assert.Equal(1, stitched.PageCount);
            using (var img = SKBitmap.Decode(stitched.Files[0]))
            {
                Assert.NotNull(img);
                Assert.Equal((200, 5000), (img!.Width, img.Height));
            }

            // Ekspor per potongan → sebanyak potongan.
            var outDir2 = Path.Combine(dir, "out-plain");
            var plain = ExportService.ExportChapter(
                effective, projectChapter, outDir2,
                new ExportOptions(ExportKind.Folder, ExportImageFormat.Png,
                    90, StitchSplits: false));
            Assert.Equal(slices.Count, plain.PageCount);

            // Sambung lagi → bab efektif kembali 1 halaman utuh.
            ChapterSplitService.RejoinPage(chapter, projectChapter, 0);
            Assert.Empty(projectChapter.PageSplits);
            var restored = ChapterSplitService.BuildEffectiveChapter(chapter, projectChapter);
            Assert.Single(restored.Pages);
            Assert.Equal(5000, restored.Pages[0].Height);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
