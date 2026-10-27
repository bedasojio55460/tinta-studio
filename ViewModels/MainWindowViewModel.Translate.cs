using System.Collections.ObjectModel;
using Avalonia.Threading;
using TintaStudio.Models;
using TintaStudio.Services;

namespace TintaStudio.ViewModels;

/// <summary>
/// Baris daftar blok di halaman Terjemahan: teks sumber (OCR, bisa
/// disunting) → teks terjemahan (bisa disunting). Keduanya tersimpan
/// otomatis ke proyek. Blok gagal tampil dengan alasannya.
/// </summary>
public sealed class TranslateBlockItemViewModel : ViewModelBase
{
    private readonly MainWindowViewModel _owner;

    public TranslateBlockItemViewModel(TextBlock block, int number,
        MainWindowViewModel owner)
    {
        Block = block;
        Number = number;
        _owner = owner;
        TranslateCommand = new RelayCommand(
            () => _ = _owner.TranslateBlockAsync(this));
        PolishCommand = new RelayCommand(
            () => _ = _owner.PolishBlockAsync(this));
        UndoPolishCommand = new RelayCommand(() => _owner.UndoPolishBlock(this));
    }

    public TextBlock Block { get; }
    public int Number { get; }

    /// <summary>Mode otomatis per blok: terjemahkan hanya blok ini
    /// (dengan konteks halamannya) memakai penyedia aktif.</summary>
    public RelayCommand TranslateCommand { get; }

    /// <summary>Poles AI per blok (perlu terjemahan yang ada).</summary>
    public RelayCommand PolishCommand { get; }

    /// <summary>Kembalikan teks sebelum poles terakhir blok ini.</summary>
    public RelayCommand UndoPolishCommand { get; }

    /// <summary>Ada cadangan pra-poles yang bisa diurungkan.</summary>
    public bool HasBackup => Block.TranslationBackup is not null;

    public bool HasTranslation => !string.IsNullOrWhiteSpace(Block.Translation);

    public string Meta => $"#{Number} • {Block.Language} • " +
                          $"OCR {Block.Confidence:P0} • " +
                          $"{Block.Width}×{Block.Height}";

    public string SourceText
    {
        get => Block.Text;
        set
        {
            if (Block.Text != value)
            {
                Block.Text = value;
                _owner.SaveProject();
                Raise();
            }
        }
    }

    public string TranslationText
    {
        get => Block.Translation ?? "";
        set
        {
            var v = string.IsNullOrWhiteSpace(value) ? null : value;
            if (Block.Translation != v)
            {
                Block.Translation = v;
                if (v is not null)
                {
                    Block.TranslationError = null;
                    Block.TranslatedBy = "Manual";
                    Block.TranslatedAt = DateTime.UtcNow;
                }
                _owner.SaveProject();
                Raise();
                Raise(nameof(HasError));
                Raise(nameof(StatusLine));
            }
        }
    }

    public bool HasError => Block.TranslationError is not null;

    public string StatusLine
    {
        get
        {
            if (Block.TranslationError is not null)
                return $"Gagal: {Block.TranslationError}";
            if (!string.IsNullOrWhiteSpace(Block.Translation))
            {
                var at = Block.TranslatedAt?.ToLocalTime().ToString("HH:mm")
                         ?? "";
                return $"Diterjemahkan: {Block.TranslatedBy} {at}".Trim();
            }
            return "Belum diterjemahkan";
        }
    }

    public void Refresh()
    {
        Raise(nameof(SourceText));
        Raise(nameof(TranslationText));
        Raise(nameof(HasError));
        Raise(nameof(StatusLine));
        Raise(nameof(HasBackup));
        Raise(nameof(HasTranslation));
    }
}

/// <summary>
/// Bagian ViewModel untuk Tahap 4: pilihan penyedia/bahasa, status
/// key (jumlah + nomor aktif — tanpa nilai), jalan terjemahan
/// halaman/bab di latar + batal. Jalan bab: proyek disimpan sesudah
/// SETIAP halaman, blok yang sudah berterjemahan dilewati, mode
/// tunggu &amp; lanjut otomatis saat semua key kena limit.
/// </summary>
public sealed partial class MainWindowViewModel
{
    private AppSettings _appSettings = AppConfig.LoadSettings();
    private bool _isTranslating;
    private double _translateProgress;
    private CancellationTokenSource? _translateCts;

    public ObservableCollection<TranslateBlockItemViewModel> TranslateBlocks { get; } = new();

    public IReadOnlyList<ProviderInfo> Providers => TranslateService.Providers;
    public IReadOnlyList<TargetLanguage> TargetLanguages => TranslateService.TargetLanguages;

    public ProviderInfo SelectedProvider
    {
        get => TranslateService.ProviderById(_appSettings.Provider);
        set
        {
            if (value is not null && _appSettings.Provider != value.Id)
            {
                _appSettings.Provider = value.Id;
                AppConfig.SaveSettings(_appSettings);
                Raise();
                Raise(nameof(ProviderStatusText));
            }
        }
    }

    public TargetLanguage SelectedTargetLanguage
    {
        get => TranslateService.TargetByCode(_appSettings.TargetLanguage);
        set
        {
            if (value is not null && _appSettings.TargetLanguage != value.Code)
            {
                _appSettings.TargetLanguage = value.Code;
                AppConfig.SaveSettings(_appSettings);
                Raise();
            }
        }
    }

    /// <summary>Status key penyedia terpilih — hanya hitungan &amp;
    /// nomor urut, TIDAK PERNAH nilai key.</summary>
    public string ProviderStatusText
    {
        get
        {
            var p = SelectedProvider;
            if (!p.RequiresKey)
                return $"{p.DisplayName}: tanpa key (server lokal)";
            var (count, active, cooling, bad) = AppSecrets.GetStatus(p.Id);
            if (count == 0)
                return $"{p.DisplayName}: belum ada key — isi lewat Pengaturan Key & Model";
            var text = $"{p.DisplayName}: {count} key • aktif #{active}";
            if (cooling > 0)
                text += $" • {cooling} cooldown";
            if (bad > 0)
                text += $" • {bad} tidak sah";
            return text;
        }
    }

    public bool IsTranslating
    {
        get => _isTranslating;
        private set => Set(ref _isTranslating, value);
    }

    public double TranslateProgress
    {
        get => _translateProgress;
        private set => Set(ref _translateProgress, value);
    }

    public string TranslateBlocksHeader => $"BLOK — {TranslateBlocks.Count}";

    /// <summary>Muat ulang daftar blok terjemahan untuk halaman aktif.</summary>
    public void LoadTranslateForCurrentPage()
    {
        TranslateBlocks.Clear();
        var page = CurrentProjectPage();
        if (page is not null)
        {
            int n = 0;
            foreach (var b in page.Blocks)
                TranslateBlocks.Add(new TranslateBlockItemViewModel(b, ++n, this));
        }
        Raise(nameof(TranslateBlocksHeader));
        Raise(nameof(ContextInfo));
    }

    /// <summary>Pengaturan dimuat ulang sesudah dialog Pengaturan
    /// ditutup (penyedia/bahasa/key/base URL/model/jeda bisa berubah).</summary>
    public void ReloadSettings()
    {
        _appSettings = AppConfig.LoadSettings();
        Raise(nameof(SelectedProvider));
        Raise(nameof(SelectedTargetLanguage));
        Raise(nameof(ProviderStatusText));
    }

    private TranslateOptions CurrentTranslateOptions() => new(
        SelectedProvider,
        _appSettings.For(SelectedProvider.Id).BaseUrl,
        _appSettings.For(SelectedProvider.Id).Model,
        SelectedTargetLanguage.Code,
        TimeSpan.FromSeconds(_appSettings.RequestDelaySeconds));

    private TranslateService NewTranslateService(bool waitForRecovery)
    {
        var service = new TranslateService { WaitForKeyRecovery = waitForRecovery };
        service.WaitCallback = (remaining, keyNumber) =>
            Dispatcher.UIThread.Post(() => StatusText =
                $"Menunggu limit pulih… ±{(int)Math.Ceiling(remaining.TotalSeconds)} " +
                $"dtk — key #{keyNumber}");
        return service;
    }

    /// <summary>Terjemahkan ulang halaman aktif secara eksplisit —
    /// semua blok di halaman ini dikirim (termasuk yang sudah ada
    /// terjemahannya, karena ini permintaan pengguna langsung).</summary>
    public async Task TranslateCurrentPageAsync()
    {
        var page = CurrentProjectPage();
        if (page is null || IsTranslating || CurrentChapter is null)
            return;
        if (page.Blocks.Count == 0)
        {
            StatusText = "Halaman ini belum punya blok teks — jalankan " +
                         "Deteksi & OCR dulu.";
            return;
        }
        IsTranslating = true;
        TranslateProgress = 0;
        _translateCts = new CancellationTokenSource();
        var ct = _translateCts.Token;
        StatusText = $"Menerjemahkan halaman {SelectedPageIndex + 1}…";
        try
        {
            var service = NewTranslateService(waitForRecovery: false);
            var result = await service.TranslatePageAsync(
                page.Blocks.ToList(), CurrentTranslateOptions(), ct);
            TranslateService.ApplyResult(page, result);
            SaveProject();
            LoadTranslateForCurrentPage();
            Raise(nameof(ProviderStatusText));
            TranslateProgress = 1;
            StatusText = $"Halaman {SelectedPageIndex + 1}: " +
                         $"{result.Translations.Count} blok diterjemahkan, " +
                         $"{result.Failures.Count} gagal " +
                         $"({result.TranslatedBy}).";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Terjemahan dibatalkan.";
        }
        catch (TranslateException ex)
        {
            StatusText = $"Terjemahan gagal: {ex.Message}";
            Raise(nameof(ProviderStatusText));
        }
        catch (Exception ex)
        {
            StatusText = $"Terjemahan gagal: {ex.Message}";
        }
        finally
        {
            IsTranslating = false;
            _translateCts?.Dispose();
            _translateCts = null;
        }
    }

    /// <summary>
    /// Terjemahkan satu bab: halaman demi halaman, proyek disimpan
    /// sesudah SETIAP halaman; blok yang sudah berterjemahan
    /// dilewati; saat semua key limit, pekerjaan MENUNGGU pulih
    /// lalu lanjut sendiri (status hitung mundur di bar status).
    /// </summary>
    public async Task TranslateChapterAsync()
    {
        if (CurrentChapter is null || _projectChapter is null || IsTranslating)
            return;
        if (_projectChapter.Pages.Sum(p => p.Blocks.Count) == 0)
        {
            StatusText = "Bab ini belum punya blok teks — jalankan " +
                         "Deteksi & OCR dulu.";
            return;
        }
        IsTranslating = true;
        TranslateProgress = 0;
        _translateCts = new CancellationTokenSource();
        var ct = _translateCts.Token;
        var projectChapter = _projectChapter;
        try
        {
            var service = NewTranslateService(waitForRecovery: true);
            var progress = new Progress<(int Done, int Total)>(p =>
            {
                TranslateProgress = p.Total > 0 ? (double)p.Done / p.Total : 0;
                StatusText = $"Menerjemahkan bab… halaman {p.Done}/{p.Total}";
            });
            var summary = await service.TranslateChapterAsync(
                projectChapter, CurrentTranslateOptions(), progress,
                _ => SaveProject(), ct);
            SaveProject();
            LoadTranslateForCurrentPage();
            Raise(nameof(ProviderStatusText));
            StatusText = $"Terjemahan bab selesai: {summary.Translated} blok " +
                         $"diterjemahkan, {summary.Failed} gagal, " +
                         $"{summary.Skipped} dilewati (sudah ada terjemahan).";
        }
        catch (OperationCanceledException)
        {
            SaveProject();
            LoadTranslateForCurrentPage();
            StatusText = "Terjemahan bab dibatalkan — hasil sebagian tersimpan.";
        }
        catch (AllKeysExhaustedException ex)
        {
            SaveProject();
            LoadTranslateForCurrentPage();
            Raise(nameof(ProviderStatusText));
            StatusText = $"Terjemahan berhenti: {ex.Message} " +
                         "Halaman yang sudah jadi tetap tersimpan.";
        }
        catch (Exception ex)
        {
            SaveProject();
            StatusText = $"Terjemahan bab gagal: {ex.Message}";
        }
        finally
        {
            IsTranslating = false;
            _translateCts?.Dispose();
            _translateCts = null;
        }
    }

    /// <summary>
    /// Mode otomatis PER BLOK: terjemahkan hanya blok ini memakai
    /// penyedia aktif + prompt konteks halamannya (blok lain tidak
    /// diubah). Menghormati rotasi key &amp; jeda yang sama.
    /// </summary>
    public async Task TranslateBlockAsync(TranslateBlockItemViewModel item)
    {
        var page = CurrentProjectPage();
        if (page is null || IsTranslating || CurrentChapter is null)
            return;
        IsTranslating = true;
        _translateCts = new CancellationTokenSource();
        var ct = _translateCts.Token;
        StatusText = $"Menerjemahkan blok #{item.Number}…";
        try
        {
            var service = NewTranslateService(waitForRecovery: false);
            var result = await service.TranslateSingleBlockAsync(
                page.Blocks.ToList(), item.Block, CurrentTranslateOptions(), ct);
            TranslateService.ApplyResult(page, result);
            SaveProject();
            item.Refresh();
            Raise(nameof(ProviderStatusText));
            StatusText = result.Translations.Count > 0
                ? $"Blok #{item.Number} diterjemahkan ({result.TranslatedBy})."
                : $"Blok #{item.Number} gagal: " +
                  $"{result.Failures.Values.FirstOrDefault() ?? "alasan tidak diketahui"}";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Terjemahan blok dibatalkan.";
        }
        catch (TranslateException ex)
        {
            StatusText = $"Terjemahan blok gagal: {ex.Message}";
            Raise(nameof(ProviderStatusText));
        }
        catch (Exception ex)
        {
            StatusText = $"Terjemahan blok gagal: {ex.Message}";
        }
        finally
        {
            IsTranslating = false;
            _translateCts?.Dispose();
            _translateCts = null;
        }
    }

    // ── Poles AI ────────────────────────────────────────────────

    /// <summary>Poles AI satu blok (eksplisit — termasuk blok hasil
    /// manual). Teks lama tersimpan sebagai cadangan satu tingkat.</summary>
    public async Task PolishBlockAsync(TranslateBlockItemViewModel item)
    {
        var page = CurrentProjectPage();
        if (page is null || IsTranslating || CurrentChapter is null)
            return;
        if (string.IsNullOrWhiteSpace(item.Block.Translation))
        {
            StatusText = $"Blok #{item.Number} belum punya terjemahan untuk dipoles.";
            return;
        }
        IsTranslating = true;
        _translateCts = new CancellationTokenSource();
        var ct = _translateCts.Token;
        StatusText = $"Memoles blok #{item.Number}…";
        try
        {
            var service = NewTranslateService(waitForRecovery: false);
            var result = await service.PolishPageAsync(
                new[] { item.Block }, CurrentTranslateOptions(), ct);
            TranslateService.ApplyPolish(page, result);
            SaveProject();
            item.Refresh();
            Raise(nameof(ProviderStatusText));
            StatusText = result.Translations.Count > 0
                ? $"Blok #{item.Number} terpoles ({result.TranslatedBy}). " +
                  "Tombol Urungkan Poles mengembalikan teks sebelumnya."
                : $"Poles blok #{item.Number} gagal: " +
                  $"{result.Failures.Values.FirstOrDefault() ?? "alasan tidak diketahui"}";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Poles dibatalkan.";
        }
        catch (TranslateException ex)
        {
            StatusText = $"Poles gagal: {ex.Message}";
            Raise(nameof(ProviderStatusText));
        }
        catch (Exception ex)
        {
            StatusText = $"Poles gagal: {ex.Message}";
        }
        finally
        {
            IsTranslating = false;
            _translateCts?.Dispose();
            _translateCts = null;
        }
    }

    /// <summary>Urungkan poles terakhir satu blok (offline, tanpa penyedia).</summary>
    public void UndoPolishBlock(TranslateBlockItemViewModel item)
    {
        if (TranslateService.UndoPolish(item.Block))
        {
            SaveProject();
            item.Refresh();
            StatusText = $"Poles blok #{item.Number} diurungkan — teks sebelumnya kembali.";
        }
    }

    /// <summary>Poles semua blok berterjemahan di halaman aktif
    /// (eksplisit — termasuk hasil manual di halaman ini).</summary>
    public async Task PolishCurrentPageAsync()
    {
        var page = CurrentProjectPage();
        if (page is null || IsTranslating || CurrentChapter is null)
            return;
        if (page.Blocks.All(b => string.IsNullOrWhiteSpace(b.Translation)))
        {
            StatusText = "Halaman ini belum punya terjemahan untuk dipoles.";
            return;
        }
        IsTranslating = true;
        TranslateProgress = 0;
        _translateCts = new CancellationTokenSource();
        var ct = _translateCts.Token;
        StatusText = $"Memoles halaman {SelectedPageIndex + 1}…";
        try
        {
            var service = NewTranslateService(waitForRecovery: false);
            var result = await service.PolishPageAsync(
                page.Blocks.ToList(), CurrentTranslateOptions(), ct);
            TranslateService.ApplyPolish(page, result);
            SaveProject();
            LoadTranslateForCurrentPage();
            Raise(nameof(ProviderStatusText));
            TranslateProgress = 1;
            StatusText = $"Poles halaman {SelectedPageIndex + 1}: " +
                         $"{result.Translations.Count} blok terpoles, " +
                         $"{result.Failures.Count} gagal ({result.TranslatedBy}).";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Poles dibatalkan.";
        }
        catch (TranslateException ex)
        {
            StatusText = $"Poles gagal: {ex.Message}";
            Raise(nameof(ProviderStatusText));
        }
        catch (Exception ex)
        {
            StatusText = $"Poles gagal: {ex.Message}";
        }
        finally
        {
            IsTranslating = false;
            _translateCts?.Dispose();
            _translateCts = null;
        }
    }

    /// <summary>
    /// Poles satu bab: simpan per halaman; blok tanpa terjemahan dan
    /// blok hasil MANUAL dilewati (manual hanya dipoles lewat aksi
    /// eksplisit per blok/halaman); mode tunggu key aktif.
    /// </summary>
    public async Task PolishChapterAsync()
    {
        if (CurrentChapter is null || _projectChapter is null || IsTranslating)
            return;
        IsTranslating = true;
        TranslateProgress = 0;
        _translateCts = new CancellationTokenSource();
        var ct = _translateCts.Token;
        var projectChapter = _projectChapter;
        try
        {
            var service = NewTranslateService(waitForRecovery: true);
            var progress = new Progress<(int Done, int Total)>(p =>
            {
                TranslateProgress = p.Total > 0 ? (double)p.Done / p.Total : 0;
                StatusText = $"Memoles bab… halaman {p.Done}/{p.Total}";
            });
            var summary = await service.PolishChapterAsync(
                projectChapter, CurrentTranslateOptions(), progress,
                _ => SaveProject(), ct);
            SaveProject();
            LoadTranslateForCurrentPage();
            Raise(nameof(ProviderStatusText));
            StatusText = $"Poles bab selesai: {summary.Polished} blok terpoles, " +
                         $"{summary.Failed} gagal, {summary.Skipped} dilewati " +
                         "(tanpa terjemahan / hasil manual).";
        }
        catch (OperationCanceledException)
        {
            SaveProject();
            LoadTranslateForCurrentPage();
            StatusText = "Poles bab dibatalkan — hasil sebagian tersimpan.";
        }
        catch (AllKeysExhaustedException ex)
        {
            SaveProject();
            LoadTranslateForCurrentPage();
            Raise(nameof(ProviderStatusText));
            StatusText = $"Poles berhenti: {ex.Message} Halaman yang sudah jadi tetap tersimpan.";
        }
        catch (Exception ex)
        {
            SaveProject();
            StatusText = $"Poles bab gagal: {ex.Message}";
        }
        finally
        {
            IsTranslating = false;
            _translateCts?.Dispose();
            _translateCts = null;
        }
    }

    public void CancelTranslate() => _translateCts?.Cancel();

    public void ShowTranslate()
    {
        IsLibraryActive = false;
        IsDetectActive = false;
        IsCleanActive = false;
        IsTypesetActive = false;
        IsExportActive = false;
        IsTranslateActive = true;
        Raise(nameof(ProviderStatusText));
        LoadTranslateForCurrentPage();
        Raise(nameof(ContextInfo));
    }
}
