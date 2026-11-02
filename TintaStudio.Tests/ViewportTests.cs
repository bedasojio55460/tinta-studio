using TintaStudio.Services;
using Xunit;

namespace TintaStudio.Tests;

/// <summary>
/// Tes transformasi viewport zoom/pan (v0.7.14): pas layar, zoom
/// berpusat di kursor, jepit geser, dan zoom-ke-blok. Murni
/// matematika — permukaan UI (Typesetting/Deteksi/Penampil)
/// memakai kelas yang sama.
/// </summary>
public sealed class ViewportTests
{
    [Fact]
    public void Fit_CentersPageInViewport()
    {
        var vp = new PageViewport();
        vp.Fit(viewW: 800, viewH: 600, pageW: 400, pageH: 400);
        Assert.Equal(1.5, vp.Scale, precision: 6);
        Assert.Equal(100, vp.OffsetX, precision: 6); // (800 − 600)/2
        Assert.Equal(0, vp.OffsetY, precision: 6);
        Assert.Equal("150%", vp.PercentText);
        var center = vp.PageToScreen(200, 200);
        Assert.Equal(400, center.X, precision: 6);
        Assert.Equal(300, center.Y, precision: 6);
    }

    [Fact]
    public void ZoomAt_KeepsPagePointUnderCursor()
    {
        var vp = new PageViewport();
        vp.Fit(500, 500, 1000, 1000); // skala 0,5, offset 0,0
        var before = vp.ScreenToPage(250, 250);
        Assert.Equal(500, before.X, precision: 6);
        Assert.Equal(500, before.Y, precision: 6);
        vp.ZoomAt(250, 250, factor: 2.0, 500, 500, 1000, 1000);
        Assert.Equal(1.0, vp.Scale, precision: 6);
        var after = vp.PageToScreen(before.X, before.Y);
        Assert.Equal(250, after.X, precision: 6);
        Assert.Equal(250, after.Y, precision: 6);
    }

    [Fact]
    public void ZoomAt_ClampsToMaxScale()
    {
        var vp = new PageViewport();
        vp.Fit(500, 500, 1000, 1000);
        for (int i = 0; i < 20; i++)
            vp.ZoomAt(250, 250, 2.0, 500, 500, 1000, 1000);
        Assert.Equal(PageViewport.MaxScale, vp.Scale, precision: 6);
    }

    [Fact]
    public void PanBy_PageSmallerThanViewport_StaysCentered()
    {
        var vp = new PageViewport();
        vp.Fit(800, 600, 400, 400); // tampil 600×600 di viewport 800×600
        vp.PanBy(120, 80, 800, 600, 400, 400);
        Assert.Equal(100, vp.OffsetX, precision: 6);
        Assert.Equal(0, vp.OffsetY, precision: 6);
    }

    [Fact]
    public void PanBy_ZoomedPage_ClampsAtEdges()
    {
        var vp = new PageViewport();
        vp.Fit(500, 500, 1000, 1000);
        vp.ZoomAt(250, 250, 4.0, 500, 500, 1000, 1000); // skala 2 → tampil 2000
        vp.PanBy(100000, 100000, 500, 500, 1000, 1000);
        Assert.Equal(0, vp.OffsetX, precision: 6); // tidak boleh lewat tepi kiri/atas
        Assert.Equal(0, vp.OffsetY, precision: 6);
        vp.PanBy(-100000, -100000, 500, 500, 1000, 1000);
        Assert.Equal(500 - 2000, vp.OffsetX, precision: 6);
        Assert.Equal(500 - 2000, vp.OffsetY, precision: 6);
    }

    [Fact]
    public void ZoomToRect_BlockFillsTargetFractionAndCenters()
    {
        var vp = new PageViewport();
        // Halaman 1000×2000 di viewport 800×600 (fit = 0,3).
        vp.ZoomToRect(rectX: 100, rectY: 1000, rectW: 100, rectH: 100,
            viewW: 800, viewH: 600, pageW: 1000, pageH: 2000);
        double screenH = 100 * vp.Scale;
        Assert.Equal(600 * PageViewport.BlockHeightFraction, screenH, precision: 4);
        var center = vp.PageToScreen(150, 1050);
        Assert.Equal(400, center.X, precision: 4);
        Assert.Equal(300, center.Y, precision: 4);
        Assert.True(vp.Scale >= 0.3); // tidak pernah di bawah fit
    }

    [Fact]
    public void ZoomToRect_HugeBlock_StaysAtFit()
    {
        var vp = new PageViewport();
        vp.ZoomToRect(0, 0, 1000, 1900, 800, 600, 1000, 2000);
        Assert.Equal(0.3, vp.Scale, precision: 6); // sebesar fit, tidak diperkecil lagi
    }

    [Fact]
    public void ScreenPage_RoundTrips()
    {
        var vp = new PageViewport();
        vp.Fit(640, 480, 900, 15412); // strip tinggi: fit sangat kecil
        vp.ZoomAt(320, 240, 3.0, 640, 480, 900, 15412);
        var page = vp.ScreenToPage(123, 456);
        var back = vp.PageToScreen(page.X, page.Y);
        Assert.Equal(123, back.X, precision: 4);
        Assert.Equal(456, back.Y, precision: 4);
    }
}
