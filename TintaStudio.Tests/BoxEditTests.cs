using TintaStudio.Services;
using Xunit;

namespace TintaStudio.Tests;

/// <summary>
/// Tes matematika pegangan tarik kotak blok (v0.7.15): hit-test
/// pegangan (sudut > tepi > badan) dan penerapan seret (jangkar
/// tepi berlawanan diam, ukuran minimum dijepit). Murni — kanvas
/// Typesetting & Deteksi memakai fungsi yang sama.
/// Kotak acuan: (100, 200) ukuran 300×150, toleransi 10 px.
/// </summary>
public sealed class BoxEditTests
{
    private const int X = 100, Y = 200, W = 300, H = 150;
    private const double Tol = 10;

    [Fact]
    public void HitHandle_CornersWinOverEdges()
    {
        Assert.Equal(BoxHandle.TopLeft,
            BoxEdit.HitHandle(102, 198, X, Y, W, H, Tol));
        Assert.Equal(BoxHandle.TopRight,
            BoxEdit.HitHandle(398, 203, X, Y, W, H, Tol));
        Assert.Equal(BoxHandle.BottomLeft,
            BoxEdit.HitHandle(97, 348, X, Y, W, H, Tol));
        Assert.Equal(BoxHandle.BottomRight,
            BoxEdit.HitHandle(403, 352, X, Y, W, H, Tol));
    }

    [Fact]
    public void HitHandle_EdgesAndBodyAndOutside()
    {
        Assert.Equal(BoxHandle.Left,
            BoxEdit.HitHandle(95, 275, X, Y, W, H, Tol));
        Assert.Equal(BoxHandle.Right,
            BoxEdit.HitHandle(405, 275, X, Y, W, H, Tol));
        Assert.Equal(BoxHandle.Top,
            BoxEdit.HitHandle(250, 194, X, Y, W, H, Tol));
        Assert.Equal(BoxHandle.Bottom,
            BoxEdit.HitHandle(250, 356, X, Y, W, H, Tol));
        Assert.Equal(BoxHandle.Move,
            BoxEdit.HitHandle(250, 275, X, Y, W, H, Tol));
        Assert.Equal(BoxHandle.None,
            BoxEdit.HitHandle(600, 275, X, Y, W, H, Tol));
        Assert.Equal(BoxHandle.None,
            BoxEdit.HitHandle(250, 500, X, Y, W, H, Tol));
    }

    [Fact]
    public void HitHandle_OutsideButWithinToleranceOfCorner()
    {
        // Pegangan digambar di atas garis kotak — titik sedikit di
        // luar kotak dekat sudut tetap menangkap sudutnya.
        Assert.Equal(BoxHandle.TopLeft,
            BoxEdit.HitHandle(93, 193, X, Y, W, H, Tol));
        // Jauh di luar toleransi: tidak menangkap apa pun.
        Assert.Equal(BoxHandle.None,
            BoxEdit.HitHandle(80, 180, X, Y, W, H, Tol));
    }

    [Fact]
    public void ApplyDrag_MoveShiftsWholeBox()
    {
        var r = BoxEdit.ApplyDrag(BoxHandle.Move,
            X, Y, W, H, 250, 275, 290, 245);
        Assert.Equal((140, 170, 300, 150), r);
    }

    [Fact]
    public void ApplyDrag_RightAndBottomGrowFromAnchor()
    {
        var r = BoxEdit.ApplyDrag(BoxHandle.Right,
            X, Y, W, H, 400, 275, 460, 275);
        Assert.Equal((100, 200, 360, 150), r);
        var b = BoxEdit.ApplyDrag(BoxHandle.Bottom,
            X, Y, W, H, 250, 350, 250, 390);
        Assert.Equal((100, 200, 300, 190), b);
    }

    [Fact]
    public void ApplyDrag_LeftMovesEdgeKeepsRightAnchor()
    {
        // Tarik tepi kiri 40 px ke kiri: lebar +40, x −40, tepi
        // kanan diam di 400.
        var r = BoxEdit.ApplyDrag(BoxHandle.Left,
            X, Y, W, H, 100, 275, 60, 275);
        Assert.Equal((60, 200, 340, 150), r);
        Assert.Equal(400, r.X + r.W);
        // Tarik tepi atas ke bawah 30: tinggi −30, y +30, tepi
        // bawah diam di 350.
        var t = BoxEdit.ApplyDrag(BoxHandle.Top,
            X, Y, W, H, 250, 200, 250, 230);
        Assert.Equal((100, 230, 300, 120), t);
        Assert.Equal(350, t.Y + t.H);
    }

    [Fact]
    public void ApplyDrag_CornerCombinesBothAxes()
    {
        var r = BoxEdit.ApplyDrag(BoxHandle.BottomRight,
            X, Y, W, H, 400, 350, 430, 320);
        Assert.Equal((100, 200, 330, 120), r);
        var tl = BoxEdit.ApplyDrag(BoxHandle.TopLeft,
            X, Y, W, H, 100, 200, 80, 170);
        Assert.Equal((80, 170, 320, 180), tl);
    }

    [Fact]
    public void ApplyDrag_MinSizeClampsWithoutFlipping()
    {
        // Tarik tepi kanan jauh melewati tepi kiri: berhenti di
        // ukuran minimum, tidak membalik.
        var r = BoxEdit.ApplyDrag(BoxHandle.Right,
            X, Y, W, H, 400, 275, 0, 275);
        Assert.Equal((100, 200, BoxEdit.MinBoxSize, 150), r);
        // Tarik tepi kiri melewati tepi kanan: x berhenti sehingga
        // lebar = minimum dan tepi kanan tetap jangkar.
        var l = BoxEdit.ApplyDrag(BoxHandle.Left,
            X, Y, W, H, 100, 275, 999, 275);
        Assert.Equal(BoxEdit.MinBoxSize, l.W);
        Assert.Equal(400, l.X + l.W);
        // Hal yang sama untuk sumbu vertikal.
        var t = BoxEdit.ApplyDrag(BoxHandle.Top,
            X, Y, W, H, 250, 200, 250, 999);
        Assert.Equal(BoxEdit.MinBoxSize, t.H);
        Assert.Equal(350, t.Y + t.H);
    }
}
