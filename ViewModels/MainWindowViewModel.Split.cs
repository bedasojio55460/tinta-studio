using TintaStudio.Models;
using TintaStudio.Services;

namespace TintaStudio.ViewModels;

/// <summary>
/// Bagian ViewModel untuk pemotongan halaman panjang (v0.7.14):
/// bab asli hasil impor disimpan terpisah dari bab EFEKTIF yang
/// tampil (potongan mengembang dari provenans proyek v7).
/// Aksi Potong/Sambung adalah aksi eksplisit pengguna — tidak
/// pernah otomatis. Blok pada potongan yang disambung lagi
/// dipetakan balik ke koordinat induk lewat offset provenans
/// (PageSplitMath.ApplyRejoin), jadi suntingan tidak hilang.
/// </summary>
public sealed partial class MainWindowViewModel
{
    private Chapter? _originalChapter;

    /// <summary>Tata letak halaman efektif saat ini (dari bab asli
    /// + provenans); null bila belum ada bab/proyek.</summary>
    private IReadOnlyList<PageSplitMath.LayoutEntry>? CurrentLayout =>
        _originalChapter is null || _projectChapter is null
            ? null
            : PageSplitMath.ExpandLayout(
                _originalChapter.Pages.Count, _projectChapter.PageSplits);

    private PageSplitMath.LayoutEntry? CurrentLayoutEntry =>
        CurrentLayout is { } layout
        && SelectedPageIndex >= 0 && SelectedPageIndex < layout.Count
            ? layout[SelectedPageIndex]
            : null;

    /// <summary>Halaman aktif bisa dipotong: halaman utuh (bukan
    /// potongan) yang rasio tingginya melewati ambang yang sama
    /// dengan deteksi berpetak.</summary>
    public bool CanSplitCurrentPage =>
        CurrentLayoutEntry is { SliceIndex: < 0 } entry
        && CurrentChapter is not null
        && TileMath.NeedsTiling(CurrentPageWidth, CurrentPageHeight)
        && entry.ParentIndex >= 0;

    /// <summary>Halaman aktif adalah potongan dari halaman induk
    /// yang dipotong pengguna (bisa disambung lagi).</summary>
    public bool IsSplitSlicePage => CurrentLayoutEntry is { SliceIndex: >= 0 };

    /// <summary>Keterangan singkat keadaan potong halaman aktif
    /// untuk toolbar Penampil.</summary>
    public string SplitPageInfo
    {
        get
        {
            var entry = CurrentLayoutEntry;
            if (entry is null || _projectChapter is null)
                return "";
            if (entry.SliceIndex >= 0)
            {
                var group = _projectChapter.PageSplits
                    .FirstOrDefault(g => g.ParentIndex == entry.ParentIndex);
                return group is null
                    ? ""
                    : $"Potongan {entry.SliceIndex + 1} dari {group.Slices.Count} " +
                      $"(halaman induk asli #{entry.ParentIndex + 1})";
            }
            return CanSplitCurrentPage
                ? "Halaman panjang — bisa dipotong jadi halaman pendek."
                : "";
        }
    }

    /// <summary>Ada potongan di bab ini (untuk pilihan ekspor
    /// tersambung).</summary>
    public bool HasSplitPages => _projectChapter is { PageSplits.Count: > 0 };

    /// <summary>Potong halaman aktif jadi halaman-halaman pendek.
    /// Berkas potongan ditulis, provenans disimpan ke proyek, dan
    /// tampilan berpindah ke potongan pertama.</summary>
    public async Task SplitCurrentPageAsync()
    {
        var original = _originalChapter;
        var projectChapter = _projectChapter;
        var entry = CurrentLayoutEntry;
        if (original is null || projectChapter is null || entry is null
            || !CanSplitCurrentPage || IsBusy)
            return;
        IsBusy = true;
        StatusText = "Memotong halaman panjang…";
        try
        {
            int parent = entry.ParentIndex;
            var (first, slices) = await Task.Run(() =>
                ChapterSplitService.SplitPage(original, projectChapter, parent));
            SaveProject();
            Raise(nameof(HasSplitPages));
            StatusText =
                $"Halaman {parent + 1} dipotong menjadi {slices.Count} halaman pendek " +
                $"({string.Join(", ", slices.Select(s => s.Height))} px). " +
                "Sambung lagi kapan pun dari halaman potongannya.";
            RebuildEffectiveChapter(first);
        }
        catch (Exception ex)
        {
            StatusText = $"Gagal memotong halaman: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Sambung lagi potongan halaman aktif ke halaman
    /// induknya: blok/suntingan potongan dipetakan balik ke
    /// koordinat induk, potongan hilang, induk pulih.</summary>
    public async Task RejoinCurrentPageAsync()
    {
        var original = _originalChapter;
        var projectChapter = _projectChapter;
        var entry = CurrentLayoutEntry;
        if (original is null || projectChapter is null || entry is null
            || !IsSplitSlicePage || IsBusy)
            return;
        IsBusy = true;
        StatusText = "Menyambung halaman…";
        try
        {
            int parent = entry.ParentIndex;
            int index = await Task.Run(() =>
                ChapterSplitService.RejoinPage(original, projectChapter, parent));
            SaveProject();
            Raise(nameof(HasSplitPages));
            StatusText =
                $"Potongan disambung lagi — halaman induk {parent + 1} pulih utuh " +
                "(blok & suntingan potongan ikut dipetakan ke induk).";
            RebuildEffectiveChapter(index);
        }
        catch (Exception ex)
        {
            StatusText = $"Gagal menyambung halaman: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
