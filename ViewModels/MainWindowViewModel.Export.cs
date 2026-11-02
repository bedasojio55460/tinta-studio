using TintaStudio.Models;
using TintaStudio.Services;

namespace TintaStudio.ViewModels;

/// <summary>
/// Bagian ViewModel untuk Tahap 6 (Ekspor): pilihan bentuk keluaran
/// (CBZ/PDF/Folder), format gambar (Asli/JPG/PNG/WebP), kualitas,
/// folder tujuan, dan jalannya ekspor bab terbuka menjadi halaman
/// final (jalur render yang sama dengan CLI --export/--render).
/// </summary>
public sealed partial class MainWindowViewModel
{
    private bool _isExportActive;
    private bool _isExporting;
    private ExportKind _exportKind = ExportKind.Cbz;
    private string _exportImageFormatChoice = "Asli";
    private decimal? _exportQualityValue = 90;
    private string _exportFolderPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "TintaStudio-Ekspor");
    private string _exportResultText = "";

    public bool IsExportActive
    {
        get => _isExportActive;
        internal set
        {
            if (Set(ref _isExportActive, value)) Raise(nameof(IsViewerActive));
        }
    }

    public void ShowExport()
    {
        IsLibraryActive = false;
        IsDetectActive = false;
        IsCleanActive = false;
        IsTranslateActive = false;
        IsTypesetActive = false;
        IsExportActive = true;
        Raise(nameof(ExportDestinationHint));
        Raise(nameof(ContextInfo));
    }

    public bool IsExporting
    {
        get => _isExporting;
        private set => Set(ref _isExporting, value);
    }

    // ─── Bentuk keluaran (tiga toggle saling lepas) ──────────────

    public bool ExportToCbz
    {
        get => _exportKind == ExportKind.Cbz;
        set { if (value) SetExportKind(ExportKind.Cbz); }
    }

    public bool ExportToPdf
    {
        get => _exportKind == ExportKind.Pdf;
        set { if (value) SetExportKind(ExportKind.Pdf); }
    }

    public bool ExportToFolder
    {
        get => _exportKind == ExportKind.Folder;
        set { if (value) SetExportKind(ExportKind.Folder); }
    }

    private void SetExportKind(ExportKind kind)
    {
        if (Set(ref _exportKind, kind))
        {
            Raise(nameof(ExportToCbz));
            Raise(nameof(ExportToPdf));
            Raise(nameof(ExportToFolder));
            Raise(nameof(ExportImageFormatEnabled));
            Raise(nameof(ExportDestinationHint));
        }
    }

    public ExportKind ExportKindChoice => _exportKind;

    // ─── Sambung potongan saat ekspor (v0.7.14) ──────────────────

    private bool _exportStitchSplits = true;

    /// <summary>Bila bab punya halaman yang dipotong: true =
    /// potongan disambung kembali menjadi strip utuh persis
    /// dimensi aslinya saat ekspor; false = ekspor per potongan
    /// apa adanya. Bawaan true (mengembalikan bentuk asal).</summary>
    public bool ExportStitchSplits
    {
        get => _exportStitchSplits;
        set => Set(ref _exportStitchSplits, value);
    }

    // ─── Format gambar & kualitas ────────────────────────────────

    public IReadOnlyList<string> ExportImageFormatOptions { get; } =
        new[] { "Asli", "JPG", "PNG", "WebP" };

    public string ExportImageFormatChoice
    {
        get => _exportImageFormatChoice;
        set => Set(ref _exportImageFormatChoice, value);
    }

    public ExportImageFormat ExportImageFormatParsed => _exportImageFormatChoice switch
    {
        "JPG" => ExportImageFormat.Jpg,
        "PNG" => ExportImageFormat.Png,
        "WebP" => ExportImageFormat.WebP,
        _ => ExportImageFormat.Original,
    };

    /// <summary>Format gambar tidak berlaku untuk PDF (halaman
    /// disematkan sebagai gambar oleh penulis PDF).</summary>
    public bool ExportImageFormatEnabled => _exportKind != ExportKind.Pdf;

    public decimal? ExportQualityValue
    {
        get => _exportQualityValue;
        set => Set(ref _exportQualityValue, value);
    }

    // ─── Tujuan & hasil ──────────────────────────────────────────

    public string ExportFolderPath
    {
        get => _exportFolderPath;
        set
        {
            if (Set(ref _exportFolderPath, value))
                Raise(nameof(ExportDestinationHint));
        }
    }

    public string ExportDestinationHint
    {
        get
        {
            var title = CurrentChapter is null
                ? "bab"
                : ExportService.SafeFileName(CurrentChapter.Title);
            return _exportKind switch
            {
                ExportKind.Folder =>
                    $"Hasil: folder \"{title}\" di dalam folder tujuan, " +
                    "satu berkas gambar per halaman.",
                ExportKind.Pdf =>
                    $"Hasil: berkas \"{title}.pdf\" di folder tujuan.",
                _ => $"Hasil: berkas \"{title}.cbz\" di folder tujuan " +
                     "(+ ComicInfo.xml bab bila ada).",
            };
        }
    }

    public string ExportResultText
    {
        get => _exportResultText;
        private set => Set(ref _exportResultText, value);
    }

    /// <summary>Ekspor bab terbuka menurut pilihan saat ini. Tujuan
    /// akhir dihitung dari folder tujuan + judul bab; berkas yang
    /// sudah ada tidak ditimpa (layanan memberi akhiran angka).</summary>
    public async Task ExportCurrentChapterAsync()
    {
        var chapter = CurrentChapter;
        var projectChapter = _projectChapter;
        if (chapter is null || projectChapter is null || IsExporting || IsBusy)
            return;
        IsExporting = true;
        ExportResultText = "";
        StatusText = $"Mengekspor {chapter.Title}…";
        try
        {
            var baseName = ExportService.SafeFileName(chapter.Title);
            string destination = _exportKind switch
            {
                ExportKind.Folder => Path.Combine(_exportFolderPath, baseName),
                ExportKind.Pdf => Path.Combine(_exportFolderPath, baseName + ".pdf"),
                _ => Path.Combine(_exportFolderPath, baseName + ".cbz"),
            };
            var options = new ExportOptions(
                _exportKind, ExportImageFormatParsed,
                (int)(_exportQualityValue ?? 90),
                StitchSplits: _exportStitchSplits);
            var result = await Task.Run(() => ExportService.ExportChapter(
                chapter, projectChapter, destination, options,
                CancellationToken.None));
            SaveProject();
            ExportResultText =
                $"Selesai: {result.PageCount} halaman → {result.OutputPath} " +
                $"({FormatBytes(result.TotalBytes)})";
            StatusText = $"Ekspor selesai: {result.OutputPath}";
        }
        catch (Exception ex)
        {
            ExportResultText = $"Gagal mengekspor: {ex.Message}";
            StatusText = $"Ekspor gagal: {ex.Message}";
        }
        finally
        {
            IsExporting = false;
        }
    }

    private static string FormatBytes(long bytes) => bytes switch
    {
        >= 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
        >= 1024 => $"{bytes / 1024.0:F1} KB",
        _ => $"{bytes} byte",
    };
}
