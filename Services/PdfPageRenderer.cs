using PDFtoImage;
using SkiaSharp;

// PDFtoImage mendukung Windows/Linux/macOS — persis target Tinta Studio;
// peringatan CA1416 soal platform lain (Android/iOS) tidak relevan di sini.
#pragma warning disable CA1416

namespace TintaStudio.Services;

/// <summary>
/// Perender halaman PDF berbasis PDFium (lewat PDFtoImage) — dipilih di
/// Tahap 1 karena restore-nya bersih di Linux &amp; Windows dan biner
/// native-nya terbawa paket NuGet per-RID.
/// </summary>
internal static class PdfPageRenderer
{
    // Catatan: pada PDFtoImage 5.2, overload string berarti Base64 — jadi
    // selalu pakai overload byte[] dengan isi berkas yang dibaca sendiri.
    public static int GetPageCount(byte[] pdfBytes) => Conversion.GetPageCount(pdfBytes);

    public static (byte[] Png, int Width, int Height) RenderPagePng(byte[] pdfBytes, int pageIndex, int dpi)
    {
        using var ms = new MemoryStream();
        Conversion.SavePng(ms, pdfBytes, pageIndex, null, new RenderOptions(Dpi: dpi));
        var bytes = ms.ToArray();
        using var bmp = SKBitmap.Decode(bytes)
            ?? throw new InvalidDataException("hasil render PDF tidak terbaca");
        return (bytes, bmp.Width, bmp.Height);
    }
}
