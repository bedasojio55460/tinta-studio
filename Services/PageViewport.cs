namespace TintaStudio.Services;

/// <summary>
/// Transformasi tampilan kanvas halaman (v0.7.14): skala + geser
/// (offset) antara koordinat piksel halaman dan koordinat layar.
/// MURNI matematika double — tanpa dependensi UI — agar bisa
/// dites langsung. Koordinat blok/data TIDAK pernah berubah oleh
/// kelas ini; ini murni urusan tampilan.
///
/// Konvensi: Scale = piksel layar per piksel halaman; Offset =
/// posisi sudut kiri-atas halaman di layar. "Pas Layar" = skala
/// fit (seluruh halaman terlihat, terpusat); "100%" = Scale 1.
/// </summary>
public sealed class PageViewport
{
    /// <summary>Skala maksimum absolut (800%).</summary>
    public const double MaxScale = 8.0;

    /// <summary>Fraksi tinggi viewport yang diisi tinggi blok
    /// saat zoom-ke-blok (skala enak dibaca).</summary>
    public const double BlockHeightFraction = 0.65;

    public double Scale { get; private set; }
    public double OffsetX { get; private set; }
    public double OffsetY { get; private set; }

    /// <summary>Sudah pernah di-fit ke viewport semasa?</summary>
    public bool HasLayout { get; private set; }

    public string PercentText => $"{Scale * 100:0}%";

    /// <summary>Skala pas-layar: halaman utuh muat di viewport.</summary>
    public static double FitScale(double viewW, double viewH, int pageW, int pageH) =>
        viewW <= 0 || viewH <= 0 || pageW <= 0 || pageH <= 0
            ? 0
            : Math.Min(viewW / pageW, viewH / pageH);

    /// <summary>Batas skala bawah: seperempat skala fit (atau
    /// 0,05 bila fit belum diketahui).</summary>
    private static double MinScale(double fit) => fit > 0 ? fit * 0.25 : 0.05;

    /// <summary>Pas Layar: skala fit + halaman terpusat.</summary>
    public void Fit(double viewW, double viewH, int pageW, int pageH)
    {
        double fit = FitScale(viewW, viewH, pageW, pageH);
        if (fit <= 0)
            return;
        Scale = fit;
        OffsetX = (viewW - pageW * Scale) / 2;
        OffsetY = (viewH - pageH * Scale) / 2;
        HasLayout = true;
    }

    /// <summary>Skala 100% (1 piksel halaman = 1 piksel layar),
    /// posisi dijepit/dipusatkan seperti sesudah geser.</summary>
    public void SetActualSize(double viewW, double viewH, int pageW, int pageH)
    {
        if (pageW <= 0 || pageH <= 0)
            return;
        // Pertahankan titik tengah viewport sedapat mungkin.
        var center = ScreenToPage(viewW / 2, viewH / 2);
        Scale = 1.0;
        OffsetX = viewW / 2 - center.X * Scale;
        OffsetY = viewH / 2 - center.Y * Scale;
        HasLayout = true;
        ClampOffsets(viewW, viewH, pageW, pageH);
    }

    /// <summary>Zoom dengan faktor pengali, berpusat di kursor:
    /// titik halaman di bawah kursor tetap di bawah kursor.</summary>
    public void ZoomAt(double cursorX, double cursorY, double factor,
        double viewW, double viewH, int pageW, int pageH)
    {
        if (Scale <= 0 || factor <= 0 || pageW <= 0 || pageH <= 0)
            return;
        double fit = FitScale(viewW, viewH, pageW, pageH);
        double newScale = Math.Clamp(Scale * factor, MinScale(fit), MaxScale);
        if (Math.Abs(newScale - Scale) < 1e-9)
            return;
        var anchor = ScreenToPage(cursorX, cursorY);
        Scale = newScale;
        OffsetX = cursorX - anchor.X * Scale;
        OffsetY = cursorY - anchor.Y * Scale;
        ClampOffsets(viewW, viewH, pageW, pageH);
    }

    /// <summary>Geser tampilan sebesar delta layar, lalu jepit.</summary>
    public void PanBy(double dx, double dy,
        double viewW, double viewH, int pageW, int pageH)
    {
        if (Scale <= 0)
            return;
        OffsetX += dx;
        OffsetY += dy;
        ClampOffsets(viewW, viewH, pageW, pageH);
    }

    /// <summary>
    /// Zoom ke satu kotak halaman (mis. blok terpilih): skala
    /// dipilih agar tinggi kotak ≈ <see cref="BlockHeightFraction"/>
    /// tinggi viewport (lebar ikut dibatasi 92% viewport), dijepit
    /// antara skala fit dan <see cref="MaxScale"/>; kotak dipusatkan.
    /// </summary>
    public void ZoomToRect(double rectX, double rectY, double rectW, double rectH,
        double viewW, double viewH, int pageW, int pageH)
    {
        double fit = FitScale(viewW, viewH, pageW, pageH);
        if (fit <= 0 || rectW <= 0 || rectH <= 0)
            return;
        double desired = Math.Min(
            viewH * BlockHeightFraction / rectH,
            viewW * 0.92 / rectW);
        Scale = Math.Clamp(desired, fit, MaxScale);
        OffsetX = viewW / 2 - (rectX + rectW / 2) * Scale;
        OffsetY = viewH / 2 - (rectY + rectH / 2) * Scale;
        HasLayout = true;
        ClampOffsets(viewW, viewH, pageW, pageH);
    }

    public (double X, double Y) PageToScreen(double x, double y) =>
        (OffsetX + x * Scale, OffsetY + y * Scale);

    public (double X, double Y) ScreenToPage(double x, double y) =>
        Scale <= 0 ? (0, 0) : ((x - OffsetX) / Scale, (y - OffsetY) / Scale);

    /// <summary>Jepit offset: halaman yang lebih kecil dari
    /// viewport pada satu sumbu dipusatkan di sumbu itu; yang
    /// lebih besar tidak boleh menyisakan ruang kosong di tepi.</summary>
    public void ClampOffsets(double viewW, double viewH, int pageW, int pageH)
    {
        if (Scale <= 0 || pageW <= 0 || pageH <= 0)
            return;
        double dispW = pageW * Scale, dispH = pageH * Scale;
        OffsetX = dispW <= viewW
            ? (viewW - dispW) / 2
            : Math.Clamp(OffsetX, viewW - dispW, 0);
        OffsetY = dispH <= viewH
            ? (viewH - dispH) / 2
            : Math.Clamp(OffsetY, viewH - dispH, 0);
    }
}
