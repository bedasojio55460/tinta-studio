using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using SkiaSharp;
using TintaStudio.Models;
using TintaStudio.Services;

namespace TintaStudio.ViewModels;

/// <summary>Alat aktif di kanvas halaman Bersihkan.</summary>
public enum CleanTool
{
    None,
    Eyedropper,
    Brush,
    Eraser,
}

/// <summary>
/// Baris daftar blok di halaman Bersihkan: mode per blok + tombol
/// terapkan. Pilihan mode lokal sampai "Terapkan" ditekan; status
/// dibaca dari operasi tersimpan halaman.
/// </summary>
public sealed class CleanBlockItemViewModel : ViewModelBase
{
    private readonly MainWindowViewModel _owner;
    private CleanMode _mode = CleanMode.Solid;
    private bool _isCleaned;
    private CleanMode? _appliedMode;

    public CleanBlockItemViewModel(TextBlock block, int number, MainWindowViewModel owner)
    {
        Block = block;
        Number = number;
        _owner = owner;
        ApplyCommand = new RelayCommand(() => _ = _owner.ApplyBlockCleanAsync(this));
    }

    public TextBlock Block { get; }
    public int Number { get; }
    public IReadOnlyList<CleanMode> Modes => MainWindowViewModel.CleanModeOptions;

    public string TextPreview => Block.Text.Replace("\n", " / ");
    public string Meta => $"{Block.Width}×{Block.Height} • {Block.Language}";

    public CleanMode Mode
    {
        get => _mode;
        set => Set(ref _mode, value);
    }

    public bool IsCleaned
    {
        get => _isCleaned;
        private set
        {
            if (Set(ref _isCleaned, value)) Raise(nameof(StatusText));
        }
    }

    public string StatusText => _appliedMode is { } m
        ? $"Sudah dibersihkan — {m}"
        : "Belum dibersihkan";

    public RelayCommand ApplyCommand { get; }

    public void RefreshFrom(ProjectPage page)
    {
        var op = page.CleanOps.FirstOrDefault(o => o.BlockId == Block.Id);
        _appliedMode = op?.Mode;
        if (op is not null)
            Mode = op.Mode;
        IsCleaned = op is not null;
        Raise(nameof(StatusText));
    }
}

/// <summary>
/// Bagian ViewModel untuk Tahap 3: keadaan bersih halaman aktif,
/// operasi per blok, pembersihan halaman/bab di latar (+ batal),
/// Urungkan, eyedropper, dan sapuan manual. Gambar bersih dibangun
/// CleanStore (cache dasar operasi) — halaman asli tak pernah diubah.
/// </summary>
public sealed partial class MainWindowViewModel
{
    private CleanService? _cleanService;
    private Bitmap? _cleanedPageBitmap;
    private bool _showOriginal;
    private bool _showCleanOverlay = true;
    private bool _isCleaning;
    private double _cleanProgress;
    private CancellationTokenSource? _cleanCts;
    private CleanMode _selectedCleanMode = CleanMode.Solid;
    private bool _useAutoFill = true;
    private string _cleanFillHex = "#FFFFFF";
    private CleanTool _cleanTool = CleanTool.None;
    private double _brushSize = 24;
    private int _cleanRefreshGen;

    public static IReadOnlyList<CleanMode> CleanModeOptions { get; } =
        Enum.GetValues<CleanMode>();

    public ObservableCollection<CleanBlockItemViewModel> CleanBlocks { get; } = new();

    private RelayCommand? _undoCleanCommand;
    public RelayCommand UndoCleanCmd => _undoCleanCommand ??=
        new RelayCommand(UndoClean, () => CurrentProjectPage() is { } p
            && CleanService.HasWork(p));

    private CleanService CleanSvc => _cleanService ??= new CleanService();

    public IReadOnlyList<CleanMode> CleanModes => CleanModeOptions;

    /// <summary>Status model inpaint untuk kartu di halaman Bersihkan.</summary>
    public ModelStatusViewModel? InpaintModelStatus =>
        ModelStatuses.FirstOrDefault(m => m.Entry.Id == ModelStore.InpaintId);

    public Bitmap? CleanedPageBitmap
    {
        get => _cleanedPageBitmap;
        private set
        {
            if (Set(ref _cleanedPageBitmap, value))
                Raise(nameof(CleanCanvasBitmap));
        }
    }

    /// <summary>Bitmap yang tampil di kanvas Bersihkan: asli saat
    /// toggle Sebelum/Sesudah menyala, selain itu hasil bersih
    /// (jatuh ke asli bila belum dibersihkan).</summary>
    public Bitmap? CleanCanvasBitmap =>
        ShowOriginal ? CurrentPageBitmap : CleanedPageBitmap ?? CurrentPageBitmap;

    public bool ShowOriginal
    {
        get => _showOriginal;
        set
        {
            if (Set(ref _showOriginal, value))
                Raise(nameof(CleanCanvasBitmap));
        }
    }

    public bool ShowCleanOverlay
    {
        get => _showCleanOverlay;
        set => Set(ref _showCleanOverlay, value);
    }

    public bool IsCleaning
    {
        get => _isCleaning;
        private set => Set(ref _isCleaning, value);
    }

    public double CleanProgress
    {
        get => _cleanProgress;
        private set => Set(ref _cleanProgress, value);
    }

    public CleanMode SelectedCleanMode
    {
        get => _selectedCleanMode;
        set => Set(ref _selectedCleanMode, value);
    }

    /// <summary>Warna isi Solid otomatis (sampel bubble) vs manual.</summary>
    public bool UseAutoFill
    {
        get => _useAutoFill;
        set => Set(ref _useAutoFill, value);
    }

    /// <summary>Warna isi manual / warna kuas, format "#RRGGBB".</summary>
    public string CleanFillHex
    {
        get => _cleanFillHex;
        set
        {
            if (Set(ref _cleanFillHex, value))
                Raise(nameof(CleanFillBrush));
        }
    }

    public Avalonia.Media.IBrush CleanFillBrush
    {
        get
        {
            var c = CleanService.ParseColor(_cleanFillHex);
            return new Avalonia.Media.SolidColorBrush(
                Avalonia.Media.Color.FromRgb(c.Red, c.Green, c.Blue));
        }
    }

    public CleanTool CleanTool
    {
        get => _cleanTool;
        set => Set(ref _cleanTool, value);
    }

    /// <summary>Ukuran kuas manual (diameter piksel halaman).</summary>
    public double BrushSize
    {
        get => _brushSize;
        set => Set(ref _brushSize, value);
    }

    /// <summary>Ringkasan pekerjaan bersih halaman aktif.</summary>
    public string CleanOpsInfo
    {
        get
        {
            var page = CurrentProjectPage();
            if (page is null)
                return "Belum ada bab yang dibuka.";
            int ops = page.CleanOps.Count, strokes = page.ManualStrokes.Count;
            if (ops == 0 && strokes == 0)
                return "Halaman ini belum dibersihkan.";
            var parts = new List<string>();
            if (ops > 0) parts.Add($"{ops} blok dibersihkan");
            if (strokes > 0) parts.Add($"{strokes} sapuan manual");
            return string.Join(" • ", parts) + ".";
        }
    }

    public string CleanBlocksHeader => $"BLOK TEKS — {CleanBlocks.Count}";

    private ProjectPage? CurrentProjectPage()
    {
        if (_projectChapter is null || SelectedPageIndex < 0)
            return null;
        return _projectChapter.Pages.FirstOrDefault(p => p.Index == SelectedPageIndex);
    }

    private string? FillChoice() => UseAutoFill ? null : CleanFillHex;

    /// <summary>Muat ulang keadaan bersih untuk halaman aktif
    /// (daftar blok + bitmap bersih dari cache).</summary>
    public void RefreshCleanStateForCurrentPage()
    {
        CleanBlocks.Clear();
        var page = CurrentProjectPage();
        if (page is not null)
        {
            int n = 0;
            foreach (var b in page.Blocks)
            {
                var item = new CleanBlockItemViewModel(b, ++n, this);
                item.RefreshFrom(page);
                CleanBlocks.Add(item);
            }
        }
        Raise(nameof(CleanBlocksHeader));
        Raise(nameof(CleanOpsInfo));
        UndoCleanCmd.RaiseCanExecuteChanged();
        _ = RefreshCleanedBitmapAsync();
    }

    public async Task RefreshCleanedBitmapAsync()
    {
        int gen = ++_cleanRefreshGen;
        var chapter = CurrentChapter;
        var projectChapter = _projectChapter;
        var page = CurrentProjectPage();
        if (chapter is null || projectChapter is null || page is null
            || !CleanService.HasWork(page))
        {
            CleanedPageBitmap = null;
            return;
        }
        try
        {
            var sk = await Task.Run(() =>
                CleanStore.GetCleanedPageBitmap(CleanSvc, chapter, projectChapter, page));
            if (gen != _cleanRefreshGen || sk is null)
            {
                sk?.Dispose();
                return;
            }
            using (sk)
            {
                using var img = SKImage.FromBitmap(sk);
                using var data = img.Encode(SKEncodedImageFormat.Png, 100);
                using var ms = new MemoryStream();
                data.SaveTo(ms);
                ms.Position = 0;
                var old = _cleanedPageBitmap;
                CleanedPageBitmap = new Bitmap(ms);
                old?.Dispose();
            }
        }
        catch (ModelMissingException)
        {
            CleanedPageBitmap = null;
            if (IsCleanActive)
                StatusText = "Model Inpaint LaMA belum diunduh — unduh lewat kartu " +
                             "Model di halaman ini untuk menampilkan hasil inpaint.";
        }
        catch (Exception ex)
        {
            CleanedPageBitmap = null;
            if (IsCleanActive)
                StatusText = $"Gagal membangun gambar bersih: {ex.Message}";
        }
    }

    /// <summary>Terapkan mode satu blok, simpan, segarkan tampilan.</summary>
    public async Task ApplyBlockCleanAsync(CleanBlockItemViewModel item)
    {
        var page = CurrentProjectPage();
        if (page is null || IsCleaning)
            return;
        if (item.Mode == CleanMode.Inpaint && !ModelStore.InpaintReady)
        {
            StatusText = "Model Inpaint LaMA belum diunduh — unduh dulu lewat " +
                         "kartu Model di halaman ini.";
            RefreshModelStatuses();
            Raise(nameof(InpaintModelStatus));
            return;
        }
        CleanService.SetBlockOperation(page, item.Block, item.Mode, FillChoice());
        SaveProject();
        RefreshCleanStateForCurrentPage();
        StatusText = $"Blok #{item.Number} dibersihkan ({item.Mode}).";
        await Task.CompletedTask;
    }

    public async Task CleanCurrentPageAsync()
    {
        var page = CurrentProjectPage();
        if (page is null || IsCleaning || CurrentChapter is null)
            return;
        if (page.Blocks.Count == 0)
        {
            StatusText = "Halaman ini belum punya blok teks — jalankan " +
                         "Deteksi & OCR dulu.";
            return;
        }
        if (!EnsureCleanReady())
            return;
        foreach (var block in page.Blocks)
            CleanService.SetBlockOperation(page, block, SelectedCleanMode, FillChoice());
        SaveProject();
        await RebuildCurrentPageCleanAsync(
            $"Membersihkan halaman {SelectedPageIndex + 1} ({SelectedCleanMode})…");
        RefreshCleanStateForCurrentPage();
    }

    public async Task CleanChapterAsync()
    {
        if (CurrentChapter is null || _projectChapter is null || IsCleaning)
            return;
        if (!EnsureCleanReady())
            return;
        IsCleaning = true;
        CleanProgress = 0;
        _cleanCts = new CancellationTokenSource();
        var ct = _cleanCts.Token;
        var chapter = CurrentChapter;
        var projectChapter = _projectChapter;
        try
        {
            int total = chapter.Pages.Count;
            for (int i = 0; i < total; i++)
            {
                ct.ThrowIfCancellationRequested();
                var page = projectChapter.PageAt(i);
                foreach (var block in page.Blocks)
                    CleanService.SetBlockOperation(page, block, SelectedCleanMode, FillChoice());
            }
            SaveProject();
            var progress = new Progress<string>(s =>
                Dispatcher.UIThread.Post(() => StatusText = s));
            for (int i = 0; i < total; i++)
            {
                ct.ThrowIfCancellationRequested();
                var page = projectChapter.PageAt(i);
                StatusText = $"Membersihkan bab… halaman {i + 1}/{total}";
                await Task.Run(() => CleanStore.GetCleanedPageBitmap(
                    CleanSvc, chapter, projectChapter, page, ct, progress), ct);
                CleanProgress = (double)(i + 1) / total;
            }
            StatusText = $"Bersihkan bab selesai: {total} halaman " +
                         $"({SelectedCleanMode}).";
        }
        catch (OperationCanceledException)
        {
            SaveProject();
            StatusText = "Pembersihan bab dibatalkan — hasil sebagian tersimpan.";
        }
        catch (Exception ex)
        {
            StatusText = $"Pembersihan bab gagal: {ex.Message}";
        }
        finally
        {
            IsCleaning = false;
            _cleanCts?.Dispose();
            _cleanCts = null;
            RefreshCleanStateForCurrentPage();
        }
    }

    private async Task RebuildCurrentPageCleanAsync(string busyText)
    {
        var chapter = CurrentChapter;
        var projectChapter = _projectChapter;
        var page = CurrentProjectPage();
        if (chapter is null || projectChapter is null || page is null)
            return;
        IsCleaning = true;
        CleanProgress = 0;
        _cleanCts = new CancellationTokenSource();
        var ct = _cleanCts.Token;
        StatusText = busyText;
        try
        {
            var progress = new Progress<string>(s =>
                Dispatcher.UIThread.Post(() => StatusText = s));
            await Task.Run(() => CleanStore.GetCleanedPageBitmap(
                CleanSvc, chapter, projectChapter, page, ct, progress), ct);
            CleanProgress = 1;
            StatusText = $"Halaman {SelectedPageIndex + 1} bersih " +
                         $"({page.CleanOps.Count} blok, {SelectedCleanMode}).";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Pembersihan dibatalkan.";
        }
        catch (Exception ex)
        {
            StatusText = $"Pembersihan gagal: {ex.Message}";
        }
        finally
        {
            IsCleaning = false;
            _cleanCts?.Dispose();
            _cleanCts = null;
        }
    }

    public void CancelClean() => _cleanCts?.Cancel();

    /// <summary>Urungkan pekerjaan bersih terakhir halaman aktif.</summary>
    public void UndoClean()
    {
        var page = CurrentProjectPage();
        if (page is null)
            return;
        if (CleanService.UndoLast(page))
        {
            SaveProject();
            RefreshCleanStateForCurrentPage();
            StatusText = "Pekerjaan bersih terakhir diurungkan.";
        }
    }

    /// <summary>Eyedropper: ambil warna piksel halaman asli pada (x, y).</summary>
    public void PickColorAt(int x, int y)
    {
        if (CurrentChapter is null || SelectedPageIndex < 0)
            return;
        var info = CurrentChapter.Pages[SelectedPageIndex];
        using var bmp = SKBitmap.Decode(info.WorkPath);
        if (bmp is null || x < 0 || y < 0 || x >= bmp.Width || y >= bmp.Height)
            return;
        var px = bmp.GetPixel(x, y);
        CleanFillHex = CleanService.ToHex(new SKColor(px.Red, px.Green, px.Blue));
        UseAutoFill = false;
        StatusText = $"Warna terpilih: {CleanFillHex} — dipakai untuk isi " +
                     "Solid manual & kuas.";
    }

    /// <summary>Catat sapuan manual yang baru digambar di kanvas.</summary>
    public void AddManualStroke(IReadOnlyList<(int X, int Y)> points, bool erased)
    {
        var page = CurrentProjectPage();
        if (page is null || points.Count == 0)
            return;
        var stroke = new BrushStroke
        {
            Erased = erased,
            Color = CleanFillHex,
            Radius = Math.Max(2, (int)Math.Round(BrushSize / 2)),
            Points = points.Select(p => new StrokePoint { X = p.X, Y = p.Y }).ToList(),
        };
        CleanService.AddStroke(page, stroke);
        SaveProject();
        RefreshCleanStateForCurrentPage();
        StatusText = erased
            ? "Sapuan penghapus diterapkan (piksel asli dikembalikan)."
            : "Sapuan kuas diterapkan.";
    }

    private bool EnsureCleanReady()
    {
        if (SelectedCleanMode == CleanMode.Inpaint && !ModelStore.InpaintReady)
        {
            StatusText = "Model Inpaint LaMA belum diunduh — unduh dulu lewat " +
                         "kartu Model di halaman ini.";
            RefreshModelStatuses();
            Raise(nameof(InpaintModelStatus));
            return false;
        }
        return true;
    }

    public void ShowClean()
    {
        IsLibraryActive = false;
        IsDetectActive = false;
        IsTranslateActive = false;
        IsCleanActive = true;
        RefreshModelStatuses();
        Raise(nameof(InpaintModelStatus));
        RefreshCleanStateForCurrentPage();
        Raise(nameof(ContextInfo));
    }
}
