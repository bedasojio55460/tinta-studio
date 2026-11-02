using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using Avalonia.Media.Imaging;
using TintaStudio.Services;

namespace TintaStudio.ViewModels;

/// <summary>
/// View model jendela utama: pustaka bab + penampil halaman.
/// Seluruh pekerjaan impor berjalan di utas latar; hasil diterapkan di
/// utas UI lewat kelanjutan await.
/// </summary>
public sealed partial class MainWindowViewModel : ViewModelBase
{
    private readonly LibraryService _library = new();

    private string _statusText = "Siap. Buka berkas komik atau folder untuk mulai.";
    private bool _isLibraryActive = true;
    private bool _isDetectActive;
    private bool _isCleanActive;
    private bool _isTranslateActive;
    private bool _isTypesetActive;
    private bool _isBusy;
    private Chapter? _currentChapter;
    private int _selectedPageIndex = -1;
    private Bitmap? _currentPageBitmap;
    private LibraryEntry? _selectedLibraryEntry;

    public MainWindowViewModel()
    {
        LibraryEntries = new ObservableCollection<LibraryEntry>(_library.Entries);
        Pages = new ObservableCollection<PageItemViewModel>();
        PrevPageCommand = new RelayCommand(PrevPage, () => SelectedPageIndex > 0);
        NextPageCommand = new RelayCommand(NextPage,
            () => CurrentChapter is not null && SelectedPageIndex < CurrentChapter.Pages.Count - 1);
    }

    public ObservableCollection<LibraryEntry> LibraryEntries { get; }
    public ObservableCollection<PageItemViewModel> Pages { get; }

    public RelayCommand PrevPageCommand { get; }
    public RelayCommand NextPageCommand { get; }

    public string StatusText
    {
        get => _statusText;
        set => Set(ref _statusText, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => Set(ref _isBusy, value);
    }

    public bool IsLibraryActive
    {
        get => _isLibraryActive;
        private set
        {
            if (Set(ref _isLibraryActive, value)) Raise(nameof(IsViewerActive));
        }
    }

    public bool IsDetectActive
    {
        get => _isDetectActive;
        private set
        {
            if (Set(ref _isDetectActive, value)) Raise(nameof(IsViewerActive));
        }
    }

    public bool IsCleanActive
    {
        get => _isCleanActive;
        private set
        {
            if (Set(ref _isCleanActive, value)) Raise(nameof(IsViewerActive));
        }
    }

    public bool IsTranslateActive
    {
        get => _isTranslateActive;
        private set
        {
            if (Set(ref _isTranslateActive, value)) Raise(nameof(IsViewerActive));
        }
    }

    public bool IsTypesetActive
    {
        get => _isTypesetActive;
        internal set
        {
            if (Set(ref _isTypesetActive, value)) Raise(nameof(IsViewerActive));
        }
    }

    public bool IsViewerActive => !IsLibraryActive && !IsDetectActive
        && !IsCleanActive && !IsTranslateActive && !IsTypesetActive
        && !IsExportActive;

    public LibraryEntry? SelectedLibraryEntry
    {
        get => _selectedLibraryEntry;
        set => Set(ref _selectedLibraryEntry, value);
    }

    public Chapter? CurrentChapter
    {
        get => _currentChapter;
        private set
        {
            if (Set(ref _currentChapter, value))
            {
                Raise(nameof(HasChapter));
                Raise(nameof(ChapterTitle));
                Raise(nameof(PageIndicator));
                Raise(nameof(PageIndicatorShort));
                Raise(nameof(ContextInfo));
                Raise(nameof(ExportDestinationHint));
            }
        }
    }

    public bool HasChapter => CurrentChapter is not null;
    public string ChapterTitle => CurrentChapter?.Title ?? "Belum ada bab yang dibuka";

    public int SelectedPageIndex
    {
        get => _selectedPageIndex;
        set
        {
            if (Set(ref _selectedPageIndex, value))
            {
                UpdateCurrentPageBitmap();
                Raise(nameof(PageIndicator));
                Raise(nameof(PageIndicatorShort));
                Raise(nameof(CurrentPageWidth));
                Raise(nameof(CurrentPageHeight));
                LoadBlocksForCurrentPage();
                RefreshCleanStateForCurrentPage();
                LoadTranslateForCurrentPage();
                LoadTypesetForCurrentPage();
                Raise(nameof(CanSplitCurrentPage));
                Raise(nameof(IsSplitSlicePage));
                Raise(nameof(SplitPageInfo));
                PrevPageCommand.RaiseCanExecuteChanged();
                NextPageCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public Bitmap? CurrentPageBitmap
    {
        get => _currentPageBitmap;
        private set => Set(ref _currentPageBitmap, value);
    }

    public string PageIndicator => CurrentChapter is null || SelectedPageIndex < 0
        ? "Halaman 0 dari 0"
        : $"Halaman {SelectedPageIndex + 1} dari {CurrentChapter.Pages.Count}";

    /// <summary>Indikator ringkas model toolbar referensi: "1/6".</summary>
    public string PageIndicatorShort => CurrentChapter is null || SelectedPageIndex < 0
        ? "0/0"
        : $"{SelectedPageIndex + 1}/{CurrentChapter.Pages.Count}";

    /// <summary>Info konteks di sisi kanan bar status bawah.</summary>
    public string ContextInfo
    {
        get
        {
            if (CurrentChapter is null)
                return "";
            var text = $"{CurrentChapter.Format} • {CurrentChapter.Pages.Count} halaman";
            if (IsDetectActive)
                text += $" • {Blocks.Count} blok di halaman ini";
            if (IsCleanActive)
            {
                var page = CurrentProjectPage();
                if (page is not null)
                    text += $" • {page.CleanOps.Count} blok bersih di halaman ini";
            }
            if (IsTranslateActive)
            {
                var page = CurrentProjectPage();
                if (page is not null)
                    text += $" • {page.Blocks.Count(b => !string.IsNullOrWhiteSpace(b.Translation))}" +
                            $"/{page.Blocks.Count} blok terterjemahkan di halaman ini";
            }
            if (IsTypesetActive)
            {
                var page = CurrentProjectPage();
                if (page is not null)
                    text += $" • {page.Blocks.Count(b => b.Typeset is not null)}" +
                            $"/{page.Blocks.Count} blok ter-typeset di halaman ini";
            }
            return text;
        }
    }

    public int CurrentPageWidth => CurrentChapter is not null
        && SelectedPageIndex >= 0 && SelectedPageIndex < CurrentChapter.Pages.Count
            ? CurrentChapter.Pages[SelectedPageIndex].Width : 0;

    public int CurrentPageHeight => CurrentChapter is not null
        && SelectedPageIndex >= 0 && SelectedPageIndex < CurrentChapter.Pages.Count
            ? CurrentChapter.Pages[SelectedPageIndex].Height : 0;

    public void ShowLibrary()
    {
        IsDetectActive = false;
        IsCleanActive = false;
        IsTranslateActive = false;
        IsTypesetActive = false;
        IsExportActive = false;
        IsLibraryActive = true;
    }

    public void ShowViewer()
    {
        IsDetectActive = false;
        IsCleanActive = false;
        IsTranslateActive = false;
        IsTypesetActive = false;
        IsExportActive = false;
        IsLibraryActive = false;
    }

    public void ShowDetect()
    {
        IsLibraryActive = false;
        IsCleanActive = false;
        IsTranslateActive = false;
        IsTypesetActive = false;
        IsExportActive = false;
        IsDetectActive = true;
        RefreshModelStatuses();
        LoadBlocksForCurrentPage();
        Raise(nameof(ContextInfo));
    }

    public void PrevPage()
    {
        if (SelectedPageIndex > 0) SelectedPageIndex--;
    }

    public void NextPage()
    {
        if (CurrentChapter is not null && SelectedPageIndex < CurrentChapter.Pages.Count - 1)
            SelectedPageIndex++;
    }

    /// <summary>Direktori kerja salinan PNG ternormalisasi untuk satu sumber bab.</summary>
    public static string WorkDirFor(string sourcePath)
    {
        var full = Path.GetFullPath(sourcePath);
        var hash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(full)), 0, 8).ToLowerInvariant();
        return Path.Combine(Path.GetTempPath(), "tinta-studio", "chapters", hash);
    }

    /// <summary>Impor satu bab dari jalur berkas/folder, lalu tampilkan di Penampil.</summary>
    public async Task OpenChapterPathAsync(string path)
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusText = $"Mengimpor {Path.GetFileName(path)}…";
        try
        {
            var workDir = WorkDirFor(path);
            var chapter = await Task.Run(() => ComicImport.OpenChapter(path, workDir));
            ApplyChapter(chapter);
            _library.AddOrUpdate(chapter);
            RefreshLibraryEntries();
            StatusText = chapter.Skipped.Count == 0
                ? $"Terbuka: {chapter.Title} — {chapter.Format}, {chapter.Pages.Count} halaman."
                : $"Terbuka: {chapter.Title} — {chapter.Format}, {chapter.Pages.Count} halaman, " +
                  $"{chapter.Skipped.Count} entri dilewati (termasuk: {chapter.Skipped[0].Name} — {chapter.Skipped[0].Reason}).";
            ShowViewer();
        }
        catch (ComicImportException ex)
        {
            StatusText = $"Gagal membuka: {ex.Message}";
        }
        catch (Exception ex)
        {
            StatusText = $"Gagal membuka: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public Task OpenFromLibraryAsync(LibraryEntry? entry)
    {
        if (entry is null) return Task.CompletedTask;
        return OpenChapterPathAsync(entry.SourcePath);
    }

    public void RemoveFromLibrary(LibraryEntry? entry)
    {
        if (entry is null) return;
        if (_library.Remove(entry.SourcePath))
        {
            RefreshLibraryEntries();
            StatusText = $"Dihapus dari pustaka: {entry.Title}";
        }
    }

    private void ApplyChapter(Chapter chapter)
    {
        (_project, _projectChapter) = ProjectStore.GetOrCreate(chapter);
        // Bab yang tampil = bab efektif: potongan halaman panjang
        // (provenans proyek v7) mengembang menggantikan induknya.
        _originalChapter = chapter;
        var effective = ChapterSplitService.BuildEffectiveChapter(chapter, _projectChapter);
        CurrentChapter = effective;
        Pages.Clear();
        foreach (var page in effective.Pages)
            Pages.Add(new PageItemViewModel(page));
        _selectedPageIndex = -1;
        Raise(nameof(SelectedPageIndex));
        SelectedPageIndex = effective.Pages.Count > 0 ? 0 : -1;
    }

    /// <summary>Bangun ulang daftar halaman tampil dari bab asli +
    /// provenans saat ini (sesudah potong/sambung), pertahankan
    /// posisi pilih sedapat mungkin lewat indeks efektif tujuan.</summary>
    private void RebuildEffectiveChapter(int selectEffective)
    {
        if (_originalChapter is null || _projectChapter is null)
            return;
        var effective = ChapterSplitService.BuildEffectiveChapter(
            _originalChapter, _projectChapter);
        CurrentChapter = effective;
        Pages.Clear();
        foreach (var page in effective.Pages)
            Pages.Add(new PageItemViewModel(page));
        _selectedPageIndex = -1;
        Raise(nameof(SelectedPageIndex));
        SelectedPageIndex = effective.Pages.Count == 0
            ? -1
            : Math.Clamp(selectEffective, 0, effective.Pages.Count - 1);
    }

    private void UpdateCurrentPageBitmap()
    {
        var old = _currentPageBitmap;
        _currentPageBitmap = null;
        if (CurrentChapter is not null
            && SelectedPageIndex >= 0
            && SelectedPageIndex < CurrentChapter.Pages.Count)
        {
            try
            {
                _currentPageBitmap = new Bitmap(CurrentChapter.Pages[SelectedPageIndex].WorkPath);
            }
            catch (Exception)
            {
                _currentPageBitmap = null;
            }
        }
        Raise(nameof(CurrentPageBitmap));
        Raise(nameof(CleanCanvasBitmap));
        old?.Dispose();
    }

    private void RefreshLibraryEntries()
    {
        LibraryEntries.Clear();
        foreach (var entry in _library.Entries)
            LibraryEntries.Add(entry);
    }
}
