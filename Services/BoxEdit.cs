namespace TintaStudio.Services;

/// <summary>
/// Pegangan tarik pada kotak blok terpilih di kanvas (v0.7.15):
/// 4 sudut + 4 tepi untuk mengubah ukuran, badan kotak untuk
/// memindah. Keluhan pemilik: kotak blok cuma bisa digeser —
/// mengubah lebar/tinggi harus lewat angka di panel. Kini tepi
/// dan sudut bisa ditarik langsung di kanvas Typesetting maupun
/// Deteksi &amp; OCR.
/// </summary>
public enum BoxHandle
{
    None,
    Move,
    Left,
    Right,
    Top,
    Bottom,
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
}

/// <summary>
/// Matematika sunting kotak di kanvas — MURNI (koordinat piksel
/// halaman, tanpa dependensi UI) agar bisa dites langsung.
/// Toleransi tangkap pegangan dinyatakan dalam piksel halaman;
/// view membaginya dengan skala viewport agar pegangan terasa
/// tetap ±7 px di layar pada zoom berapa pun.
/// </summary>
public static class BoxEdit
{
    /// <summary>Ukuran minimum kotak hasil tarik (piksel halaman).</summary>
    public const int MinBoxSize = 16;

    /// <summary>
    /// Titik jangkar pegangan sebagai fraksi (x, y) dari kotak:
    /// sudut-sudut lalu titik tengah tiap tepi — urutan gambar
    /// pegangan di overlay.
    /// </summary>
    public static IReadOnlyList<(double Fx, double Fy)> HandleAnchors { get; } =
        new[]
        {
            (0.0, 0.0), (0.5, 0.0), (1.0, 0.0),
            (0.0, 0.5),             (1.0, 0.5),
            (0.0, 1.0), (0.5, 1.0), (1.0, 1.0),
        };

    /// <summary>
    /// Uji titik (piksel halaman) terhadap pegangan kotak
    /// (x, y, w, h): sudut didahulukan, lalu tepi, lalu badan
    /// (Move), selain itu None. Titik di luar kotak tapi dalam
    /// toleransi dari sudut/tepi tetap menangkap pegangannya —
    /// pegangan digambar di atas garis kotak dan harus mudah
    /// ditangkap.
    /// </summary>
    public static BoxHandle HitHandle(
        int px, int py, int x, int y, int w, int h, double tol)
    {
        bool nearL = Math.Abs(px - x) <= tol;
        bool nearR = Math.Abs(px - (x + w)) <= tol;
        bool nearT = Math.Abs(py - y) <= tol;
        bool nearB = Math.Abs(py - (y + h)) <= tol;
        bool inX = px >= x - tol && px <= x + w + tol;
        bool inY = py >= y - tol && py <= y + h + tol;
        if (nearL && nearT) return BoxHandle.TopLeft;
        if (nearR && nearT) return BoxHandle.TopRight;
        if (nearL && nearB) return BoxHandle.BottomLeft;
        if (nearR && nearB) return BoxHandle.BottomRight;
        if (nearL && inY) return BoxHandle.Left;
        if (nearR && inY) return BoxHandle.Right;
        if (nearT && inX) return BoxHandle.Top;
        if (nearB && inX) return BoxHandle.Bottom;
        if (px >= x && px < x + w && py >= y && py < y + h)
            return BoxHandle.Move;
        return BoxHandle.None;
    }

    /// <summary>
    /// Terapkan seret: kotak awal (sx, sy, sw, sh) + titik awal
    /// seret (startPx, startPy) + titik kini (px, py) + pegangan →
    /// kotak baru. Tepi yang berlawanan diam (jangkar); ukuran
    /// dijepit minimum <see cref="MinBoxSize"/> — menarik tepi
    /// melewati jangkar tidak membalik kotak, cukup berhenti di
    /// ukuran minimum.
    /// </summary>
    public static (int X, int Y, int W, int H) ApplyDrag(
        BoxHandle handle,
        int sx, int sy, int sw, int sh,
        int startPx, int startPy, int px, int py)
    {
        int dx = px - startPx, dy = py - startPy;
        int x = sx, y = sy, w = sw, h = sh;
        if (handle == BoxHandle.Move)
            return (sx + dx, sy + dy, sw, sh);
        if (handle is BoxHandle.Right or BoxHandle.TopRight or BoxHandle.BottomRight)
            w = Math.Max(MinBoxSize, sw + dx);
        if (handle is BoxHandle.Left or BoxHandle.TopLeft or BoxHandle.BottomLeft)
        {
            w = Math.Max(MinBoxSize, sw - dx);
            x = sx + (sw - w);
        }
        if (handle is BoxHandle.Bottom or BoxHandle.BottomLeft or BoxHandle.BottomRight)
            h = Math.Max(MinBoxSize, sh + dy);
        if (handle is BoxHandle.Top or BoxHandle.TopLeft or BoxHandle.TopRight)
        {
            h = Math.Max(MinBoxSize, sh - dy);
            y = sy + (sh - h);
        }
        return (x, y, w, h);
    }
}
