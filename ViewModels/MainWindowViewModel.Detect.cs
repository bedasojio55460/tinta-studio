using System.Collections.ObjectModel;
using Avalonia.Threading;
using SkiaSharp;
using TintaStudio.Models;
using TintaStudio.Services;

namespace TintaStudio.ViewModels;

/// <summary>Status satu model pada kartu Model (halaman Deteksi &amp; OCR).</summary>
public sealed class ModelStatusViewModel : ViewModelBase
{
    private readonly MainWindowViewModel _owner;
    private bool _isDownloading;
    private double _downloadProgress;
    private string _statusText = "";

    public ModelStatusViewModel(ModelEntry entry, MainWindowViewModel owner)
    {
        Entry = entry;
        _owner = owner;
        DownloadCommand = new RelayCommand(() => _ = _owner.DownloadModelAsync(this));
        Refresh();
    }

    public ModelEntry Entry { get; }
    public string Name => Entry.DisplayName;
    public string Role => Entry.Role;
    public bool IsReady { get; private set; }

    public string StatusText
    {
        get => _statusText;
        private set => Set(ref _statusText, value);
    }

    public bool IsDownloading
    {
        get => _isDownloading;
        set => Set(ref _isDownloading, value);
    }

    public double DownloadProgress
    {
        get => _downloadProgress;
        set => Set(ref _downloadProgress, value);
    }

    public RelayCommand DownloadCommand { get; }

    public void Refresh()
    {
        IsReady = ModelStore.IsDownloaded(Entry);
        Raise(nameof(IsReady));
        long mb = Entry.Files.Sum(f => f.SizeBytes);
        StatusText = IsReady
            ? "Siap"
            : $"Belum diunduh — {mb / 1_000_000.0:F1} MB";
    }

    public void SetError(string message) => StatusText = message;
}

/// <summary>
/// Bagian ViewModel untuk Tahap 2: status model, jalannya
/// deteksi/OCR (latar + batal), blok halaman aktif, dan suntingan
/// teks blok yang tersimpan ke proyek.
/// </summary>
public sealed partial class MainWindowViewModel
{
    private TintaProject? _project;
    private ProjectChapter? _projectChapter;
    private DetectOcrPipeline? _pipeline;

    private TextBlock? _selectedBlock;

    /// <summary>Bahasa awal = Otomatis; konstruktor menimpanya
    /// dengan pilihan terakhir pengguna dari pengaturan
    /// (v0.7.17 — sebelumnya selalu Jepang).</summary>
    private OcrLanguage _selectedLanguage = OcrLanguage.Auto;
    private bool _showOverlay = true;
    private bool _isDetecting;
    private double _detectProgress;
    private CancellationTokenSource? _detectCts;

    public ObservableCollection<TextBlock> Blocks { get; } = new();
    public ObservableCollection<ModelStatusViewModel> ModelStatuses { get; } = new();
    public IReadOnlyList<OcrLanguage> Languages => OcrLanguage.All;

    private DetectOcrPipeline Pipeline => _pipeline ??= new DetectOcrPipeline();

    public OcrLanguage SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            if (Set(ref _selectedLanguage, value))
            {
                // Pilihan terakhir diingat lintas jalan (v0.7.17).
                _appSettings.DetectLanguage = value.Code;
                AppConfig.SaveSettings(_appSettings);
            }
        }
    }

    /// <summary>
    /// Kait dialog konfirmasi dari View (jendela pemilik): menerima
    /// pesan, mengembalikan true = pengguna memilih Lanjutkan.
    /// Diisi MainWindow; null di luar UI (CLI/tes) berarti tidak
    /// ada yang bisa ditanya → deteksi jalan (pewarisan tetap
    /// menjaga terjemahan/typeset, jadi tidak ada yang hilang).
    /// </summary>
    public Func<string, Task<bool>>? ConfirmRedetectAsync { get; set; }

    /// <summary>Pesan konfirmasi deteksi ulang (makna tetap):
    /// <paramref name="scope"/> = "Halaman ini" / "Bab ini".</summary>
    internal static string RedetectConfirmMessage(string scope) =>
        $"{scope} sudah punya terjemahan/typeset. Deteksi ulang " +
        "membaca ulang teks asli dari gambar; terjemahan & typeset " +
        "dipertahankan lewat pencocokan kotak, tetapi teks OCR " +
        "akan diganti hasil baru. Lanjutkan?";

    /// <summary>Tanya konfirmasi bila sasaran deteksi ulang sudah
    /// punya terjemahan/typeset; true = boleh jalan.</summary>
    private async Task<bool> ConfirmRedetectIfNeededAsync(
        bool needed, string scope)
    {
        if (!needed || ConfirmRedetectAsync is null)
            return true;
        return await ConfirmRedetectAsync(RedetectConfirmMessage(scope));
    }

    public bool ShowOverlay
    {
        get => _showOverlay;
        set => Set(ref _showOverlay, value);
    }

    public bool IsDetecting
    {
        get => _isDetecting;
        private set => Set(ref _isDetecting, value);
    }

    public double DetectProgress
    {
        get => _detectProgress;
        private set => Set(ref _detectProgress, value);
    }

    public TextBlock? SelectedBlock
    {
        get => _selectedBlock;
        set
        {
            if (Set(ref _selectedBlock, value))
            {
                Raise(nameof(SelectedBlockText));
                Raise(nameof(SelectedBlockMeta));
                Raise(nameof(HasSelectedBlock));
            }
        }
    }

    public bool HasSelectedBlock => SelectedBlock is not null;

    /// <summary>Teks blok terpilih — suntingan tersimpan ke proyek.</summary>
    public string SelectedBlockText
    {
        get => SelectedBlock?.Text ?? "";
        set
        {
            if (SelectedBlock is not null && SelectedBlock.Text != value)
            {
                SelectedBlock.Text = value;
                SaveProject();
            }
        }
    }

    public string SelectedBlockMeta
    {
        get
        {
            if (SelectedBlock is null)
                return "Pilih blok untuk melihat & menyunting teksnya.";
            var b = SelectedBlock;
            var dir = b.Direction == TextDirection.Vertical ? "vertikal" : "horizontal";
            return $"({b.X}, {b.Y}) {b.Width}×{b.Height} • {dir} • {b.Language} • " +
                   $"deteksi {b.DetectionScore:P0} • OCR {b.Confidence:P0}";
        }
    }

    public string BlocksHeader => $"BLOK TEKS — {Blocks.Count}";

    /// <summary>Seret kotak di kanvas Deteksi selesai (v0.7.15 —
    /// pindah badan kotak / tarik pegangan ubah ukuran): simpan
    /// geometri baru blok ke proyek. Tanpa perubahan = tanpa
    /// simpan.</summary>
    public void CommitBlockGeometry(TextBlock block, int x, int y, int w, int h)
    {
        if (block.X == x && block.Y == y && block.Width == w && block.Height == h)
            return;
        block.X = x;
        block.Y = y;
        block.Width = w;
        block.Height = h;
        SaveProject();
        Raise(nameof(SelectedBlockMeta));
        Raise(nameof(ContextInfo));
    }

    public void RefreshModelStatuses()
    {
        if (ModelStatuses.Count == 0)
        {
            foreach (var entry in ModelStore.Catalog)
                ModelStatuses.Add(new ModelStatusViewModel(entry, this));
        }
        else
        {
            foreach (var m in ModelStatuses)
                m.Refresh();
        }
    }

    public async Task DownloadModelAsync(ModelStatusViewModel model)
    {
        if (model.IsDownloading)
            return;
        model.IsDownloading = true;
        model.DownloadProgress = 0;
        try
        {
            var progress = new Progress<double>(p =>
                Dispatcher.UIThread.Post(() => model.DownloadProgress = p));
            await ModelStore.DownloadAsync(model.Entry, progress, CancellationToken.None);
            model.Refresh();
            StatusText = $"Model siap: {model.Name}.";
        }
        catch (Exception ex)
        {
            model.SetError($"Gagal unduh: {ex.Message}");
            StatusText = $"Unduh model gagal ({model.Name}): {ex.Message}";
        }
        finally
        {
            model.IsDownloading = false;
        }
    }

    /// <summary>Muat blok tersimpan untuk halaman aktif ke daftar.</summary>
    public void LoadBlocksForCurrentPage()
    {
        Blocks.Clear();
        TextBlock? first = null;
        if (_projectChapter is not null && SelectedPageIndex >= 0)
        {
            var page = _projectChapter.Pages
                .FirstOrDefault(p => p.Index == SelectedPageIndex);
            if (page is not null)
            {
                foreach (var b in page.Blocks)
                {
                    Blocks.Add(b);
                    first ??= b;
                }
            }
        }
        SelectedBlock = first;
        Raise(nameof(BlocksHeader));
        Raise(nameof(ContextInfo));
    }

    public void SaveProject()
    {
        if (_project is not null && CurrentChapter is not null)
        {
            try
            {
                ProjectStore.Save(_project, CurrentChapter.SourcePath);
            }
            catch (Exception ex)
            {
                StatusText = $"Gagal menyimpan proyek: {ex.Message}";
            }
        }
    }

    public async Task DetectCurrentPageAsync()
    {
        if (CurrentChapter is null || _projectChapter is null || IsDetecting
            || SelectedPageIndex < 0)
            return;
        if (!EnsureModelsReady())
            return;
        // Deteksi ulang halaman yang sudah diterjemahkan/di-typeset
        // mengganti teks OCR-nya — minta persetujuan dulu (v0.7.17).
        var targetPage = _projectChapter.Pages
            .FirstOrDefault(p => p.Index == SelectedPageIndex);
        if (!await ConfirmRedetectIfNeededAsync(
                targetPage is not null
                    && BlockCarryOver.RequiresConfirmation(targetPage.Blocks),
                "Halaman ini"))
        {
            StatusText = "Deteksi ulang dibatalkan — tidak ada yang berubah.";
            return;
        }
        IsDetecting = true;
        DetectProgress = 0;
        _detectCts = new CancellationTokenSource();
        var ct = _detectCts.Token;
        int index = SelectedPageIndex;
        var pageInfo = CurrentChapter.Pages[index];
        StatusText = $"Mendeteksi halaman {index + 1}…";
        try
        {
            var blocks = await Task.Run(() =>
            {
                using var bmp = SKBitmap.Decode(pageInfo.WorkPath)
                    ?? throw new InvalidDataException(
                        $"Halaman tidak terbaca: {pageInfo.SourceName}");
                return Pipeline.AnalyzePage(bmp, SelectedLanguage, ct);
            }, ct);
            var stored = _projectChapter.PageAt(index);
            // Deteksi ulang (v0.7.17): wariskan terjemahan/typeset
            // blok lama ke blok baru yang wilayahnya cocok + petakan
            // ulang operasi bersih ke Id pewarisnya.
            var carry = BlockCarryOver.Apply(
                stored.Blocks, blocks, stored.CleanOps);
            stored.Blocks.Clear();
            stored.Blocks.AddRange(carry.Blocks);
            stored.CleanOps.Clear();
            stored.CleanOps.AddRange(carry.CleanOps);
            SaveProject();
            LoadBlocksForCurrentPage();
            DetectProgress = 1;
            var langNote = SelectedLanguage.IsAuto
                && Pipeline.DetectedLanguages.TryGetValue(0, out var guessedPage)
                    ? $"Bahasa terdeteksi: {guessedPage.DisplayName}"
                    : SelectedLanguage.DisplayName;
            var carryNote = carry.InheritedCount > 0
                ? $" {carry.InheritedCount} blok mempertahankan " +
                  "terjemahan/typeset sebelumnya."
                : "";
            StatusText = blocks.Count > 0
                ? $"Halaman {index + 1}: {blocks.Count} blok teks terbaca " +
                  $"({langNote}).{carryNote}"
                : $"Halaman {index + 1}: tidak ada blok teks terdeteksi.";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Deteksi dibatalkan.";
        }
        catch (Exception ex)
        {
            StatusText = $"Deteksi gagal: {ex.Message}";
        }
        finally
        {
            IsDetecting = false;
            _detectCts?.Dispose();
            _detectCts = null;
        }
    }

    public async Task DetectChapterAsync()
    {
        if (CurrentChapter is null || _projectChapter is null || IsDetecting)
            return;
        if (!EnsureModelsReady())
            return;
        // Sama seperti mode halaman: bab yang sudah punya
        // terjemahan/typeset dikonfirmasi dulu (v0.7.17).
        if (!await ConfirmRedetectIfNeededAsync(
                BlockCarryOver.RequiresConfirmation(_projectChapter.Pages),
                "Bab ini"))
        {
            StatusText = "Deteksi ulang dibatalkan — tidak ada yang berubah.";
            return;
        }
        IsDetecting = true;
        DetectProgress = 0;
        _detectCts = new CancellationTokenSource();
        var ct = _detectCts.Token;
        var progress = new Progress<(int Done, int Total)>(p =>
        {
            DetectProgress = p.Total > 0 ? (double)p.Done / p.Total : 0;
            StatusText = $"Mendeteksi bab… halaman {p.Done}/{p.Total}";
        });
        try
        {
            await Pipeline.AnalyzeChapterAsync(
                CurrentChapter, _projectChapter, SelectedLanguage, progress, ct);
            SaveProject();
            LoadBlocksForCurrentPage();
            int totalBlocks = _projectChapter.Pages.Sum(p => p.Blocks.Count);
            var langNote = SelectedLanguage.DisplayName;
            if (SelectedLanguage.IsAuto && Pipeline.DetectedLanguages.Count > 0)
            {
                var perPage = Enumerable.Range(0, CurrentChapter.Pages.Count)
                    .Where(i => Pipeline.DetectedLanguages.ContainsKey(i))
                    .Select(i => $"hal. {i + 1}: {Pipeline.DetectedLanguages[i].DisplayName}");
                langNote = "Bahasa terdeteksi — " + string.Join(", ", perPage);
            }
            StatusText = $"Deteksi bab selesai: {totalBlocks} blok dari " +
                         $"{CurrentChapter.Pages.Count} halaman ({langNote}).";
        }
        catch (OperationCanceledException)
        {
            SaveProject();
            StatusText = "Deteksi bab dibatalkan — hasil sebagian tersimpan.";
        }
        catch (Exception ex)
        {
            StatusText = $"Deteksi bab gagal: {ex.Message}";
        }
        finally
        {
            IsDetecting = false;
            _detectCts?.Dispose();
            _detectCts = null;
        }
    }

    public void CancelDetect() => _detectCts?.Cancel();

    private bool EnsureModelsReady()
    {
        var missing = ModelStore.MissingFor(SelectedLanguage);
        if (missing.Count > 0)
        {
            StatusText = "Model belum lengkap: " +
                string.Join(", ", missing.Select(m => m.DisplayName)) +
                ". Unduh dulu lewat kartu Model di halaman ini.";
            RefreshModelStatuses();
            return false;
        }
        if (SelectedLanguage.IsAuto && ModelStore.InstalledLanguages().Count == 0)
        {
            StatusText = "Mode Otomatis butuh minimal satu model OCR " +
                "terunduh (Jepang/Korea/Cina/Inggris). Unduh dulu lewat " +
                "kartu Model di halaman ini.";
            RefreshModelStatuses();
            return false;
        }
        return true;
    }
}
