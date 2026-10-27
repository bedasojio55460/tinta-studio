using System.Collections.ObjectModel;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using SkiaSharp;
using TintaStudio.Models;
using TintaStudio.Services;

namespace TintaStudio.ViewModels;

/// <summary>
/// Baris daftar blok di halaman Typesetting: membungkus satu
/// TextBlock + gaya Typeset-nya. Penyuntingan properti menulis ke
/// Block.Typeset (dibuat dari gaya bawaan bila masih null), lalu
/// proyek disimpan dan kanvas digambar ulang oleh pemilik.
/// Bidang angka diekspos sebagai decimal? untuk NumericUpDown.
/// </summary>
public sealed class TypesetBlockItemViewModel : ViewModelBase
{
    private readonly MainWindowViewModel _owner;

    public TypesetBlockItemViewModel(TextBlock block, int number, MainWindowViewModel owner)
    {
        Block = block;
        Number = number;
        _owner = owner;
        PresetDialogCommand = new RelayCommand(() => ApplyPreset(TypesetPresets.Dialog));
        PresetTeriakCommand = new RelayCommand(() => ApplyPreset(TypesetPresets.Teriak));
        PresetBisikCommand = new RelayCommand(() => ApplyPreset(TypesetPresets.Bisik));
        PresetNarasiCommand = new RelayCommand(() => ApplyPreset(TypesetPresets.Narasi));
    }

    public RelayCommand PresetDialogCommand { get; }
    public RelayCommand PresetTeriakCommand { get; }
    public RelayCommand PresetBisikCommand { get; }
    public RelayCommand PresetNarasiCommand { get; }

    /// <summary>Terapkan preset ke blok ini (mengganti seluruh gaya).</summary>
    public void ApplyPreset(string presetName)
    {
        var geometry = Block.Typeset;
        TypesetPresets.ApplyTo(Block, presetName);
        // Geometri kotak (geser/ubah) tidak diatur preset — pertahankan.
        if (geometry is not null && Block.Typeset is not null)
        {
            Block.Typeset.Dx = geometry.Dx;
            Block.Typeset.Dy = geometry.Dy;
            Block.Typeset.Dw = geometry.Dw;
            Block.Typeset.Dh = geometry.Dh;
        }
        _owner.OnTypesetStyleChanged(this);
        Refresh();
    }

    public TextBlock Block { get; }
    public int Number { get; }

    public string Meta => $"#{Number} • {Block.Width}×{Block.Height} • {Block.Language}";

    public bool HasTranslation => !string.IsNullOrWhiteSpace(Block.Translation);
    public bool HasTypeset => Block.Typeset is not null;

    public string StatusText => Block.Typeset is null
        ? (HasTranslation ? "Belum di-typeset" : "Belum ada terjemahan")
        : $"Sudah di-typeset • {Block.Typeset.PresetName ?? "gaya kustom"}";

    /// <summary>Teks terjemahan yang digambar (bisa disunting ringkas di sini).</summary>
    public string TranslationText
    {
        get => Block.Translation ?? "";
        set
        {
            var v = string.IsNullOrWhiteSpace(value) ? null : value;
            if (Block.Translation != v)
            {
                Block.Translation = v;
                _owner.OnTypesetContentChanged(this);
                Refresh();
            }
        }
    }

    /// <summary>Gaya efektif (bawaan bila belum ada Typeset eksplisit) —
    /// hanya untuk dibaca; penyuntingan lewat Mutate.</summary>
    private TypesetStyle Style => TypesetService.EffectiveStyle(Block);

    private void Mutate(Action<TypesetStyle> edit)
    {
        Block.Typeset ??= TypesetService.DefaultStyle();
        edit(Block.Typeset);
        _owner.OnTypesetStyleChanged(this);
        Refresh();
    }

    public void Refresh()
    {
        Raise(nameof(TranslationText));
        Raise(nameof(HasTranslation));
        Raise(nameof(HasTypeset));
        Raise(nameof(StatusText));
        Raise(nameof(FontFamilyChoice));
        Raise(nameof(IsAutoSize));
        Raise(nameof(FontSizeValue));
        Raise(nameof(Bold));
        Raise(nameof(Italic));
        Raise(nameof(ColorHex));
        Raise(nameof(ColorBrush));
        Raise(nameof(StrokeHex));
        Raise(nameof(StrokeBrush));
        Raise(nameof(StrokeWidthValue));
        Raise(nameof(AlignLeft));
        Raise(nameof(AlignCenter));
        Raise(nameof(AlignRight));
        Raise(nameof(LineSpacingValue));
        Raise(nameof(LetterSpacingValue));
        Raise(nameof(Vertical));
        Raise(nameof(RotationValue));
        Raise(nameof(DxValue));
        Raise(nameof(DyValue));
        Raise(nameof(DwValue));
        Raise(nameof(DhValue));
        Raise(nameof(RenderBoxText));
    }

    // ─── Font ────────────────────────────────────────────────────

    /// <summary>Pilihan font untuk ComboBox: entri pertama = sans aman.</summary>
    public const string SafeFontLabel = "(Sans aman — bawaan)";

    /// <summary>Daftar pilihan font milik pemilik (untuk ComboBox di panel).</summary>
    public IReadOnlyList<string> FontChoices => _owner.FontChoices;

    /// <summary>Daftar pilihan rotasi milik pemilik.</summary>
    public IReadOnlyList<int> RotationOptions => _owner.RotationOptions;

    public string FontFamilyChoice
    {
        get => string.IsNullOrWhiteSpace(Style.FontFamily)
            ? SafeFontLabel : Style.FontFamily;
        set => Mutate(s => s.FontFamily =
            value is null || value == SafeFontLabel ? "" : value);
    }

    public bool IsAutoSize
    {
        get => Style.FontSize <= 0;
        set => Mutate(s => s.FontSize = value ? 0 : (s.FontSize > 0 ? s.FontSize : 28));
    }

    public decimal? FontSizeValue
    {
        get => Style.FontSize <= 0 ? null : (decimal)Style.FontSize;
        set => Mutate(s => s.FontSize =
            value is null ? 0 : Math.Clamp((double)value, 4, 400));
    }

    public bool Bold
    {
        get => Style.Bold;
        set => Mutate(s => s.Bold = value);
    }

    public bool Italic
    {
        get => Style.Italic;
        set => Mutate(s => s.Italic = value);
    }

    // ─── Warna & stroke ──────────────────────────────────────────

    public string ColorHex
    {
        get => Style.ColorHex;
        set => Mutate(s => s.ColorHex = value);
    }

    public IBrush ColorBrush => BrushFrom(Style.ColorHex);

    public string StrokeHex
    {
        get => Style.StrokeHex;
        set => Mutate(s => s.StrokeHex = value);
    }

    public IBrush StrokeBrush => BrushFrom(Style.StrokeHex);

    public decimal? StrokeWidthValue
    {
        get => (decimal)Style.StrokeWidth;
        set => Mutate(s => s.StrokeWidth =
            value is null ? 0 : Math.Clamp((double)value, 0, 40));
    }

    private static IBrush BrushFrom(string hex)
    {
        var c = CleanService.ParseColor(hex);
        return new SolidColorBrush(Color.FromRgb(c.Red, c.Green, c.Blue));
    }

    // ─── Tata letak teks ─────────────────────────────────────────

    public bool AlignLeft
    {
        get => Style.Align == TypesetAlign.Left;
        set { if (value) Mutate(s => s.Align = TypesetAlign.Left); }
    }

    public bool AlignCenter
    {
        get => Style.Align == TypesetAlign.Center;
        set { if (value) Mutate(s => s.Align = TypesetAlign.Center); }
    }

    public bool AlignRight
    {
        get => Style.Align == TypesetAlign.Right;
        set { if (value) Mutate(s => s.Align = TypesetAlign.Right); }
    }

    public decimal? LineSpacingValue
    {
        get => (decimal)Style.LineSpacing;
        set => Mutate(s => s.LineSpacing =
            value is null ? 1.1 : Math.Clamp((double)value, 0.6, 3.0));
    }

    public decimal? LetterSpacingValue
    {
        get => (decimal)Style.LetterSpacing;
        set => Mutate(s => s.LetterSpacing =
            value is null ? 0 : Math.Clamp((double)value, -2, 40));
    }

    public bool Vertical
    {
        get => Style.Vertical;
        set => Mutate(s => s.Vertical = value);
    }

    public int RotationValue
    {
        get => Style.Rotation;
        set => Mutate(s => s.Rotation = value);
    }

    // ─── Geometri kotak render ──────────────────────────────────

    public decimal? DxValue
    {
        get => Style.Dx;
        set => Mutate(s => s.Dx = value is null ? 0 : (int)value);
    }

    public decimal? DyValue
    {
        get => Style.Dy;
        set => Mutate(s => s.Dy = value is null ? 0 : (int)value);
    }

    public decimal? DwValue
    {
        get => Style.Dw;
        set => Mutate(s => s.Dw = value is null ? 0 : (int)value);
    }

    public decimal? DhValue
    {
        get => Style.Dh;
        set => Mutate(s => s.Dh = value is null ? 0 : (int)value);
    }

    /// <summary>Kotak render efektif saat ini (untuk info panel).</summary>
    public string RenderBoxText
    {
        get
        {
            var r = TypesetService.RenderBox(Block, Style);
            return $"Kotak render: {r.Left},{r.Top} • {r.Width}×{r.Height}";
        }
    }
}

/// <summary>
/// Bagian ViewModel untuk Tahap 5 (Typesetting): daftar blok +
/// blok terpilih, bitmap halaman final (bersih + ter-typeset) dan
/// bitmap bersih-saja untuk toggle pembanding, terapkan otomatis
/// per halaman/bab, hit-test &amp; seret untuk kanvas.
/// </summary>
public sealed partial class MainWindowViewModel
{
    private Bitmap? _typesetFinalBitmap;
    private Bitmap? _typesetBaseBitmap;
    private bool _typesetShowOriginal;
    private bool _typesetShowClean;
    private int _typesetRefreshGen;
    private TypesetBlockItemViewModel? _selectedTypesetBlock;

    public ObservableCollection<TypesetBlockItemViewModel> TypesetBlocks { get; } = new();

    /// <summary>Pilihan font: sans aman + keluarga font sistem.</summary>
    public IReadOnlyList<string> FontChoices { get; } =
        new[] { TypesetBlockItemViewModel.SafeFontLabel }
            .Concat(TypesetService.AvailableFonts()).ToList();

    public IReadOnlyList<int> RotationOptions { get; } = new[] { 0, 90, 180, 270 };
    public IReadOnlyList<string> PresetNames => TypesetPresets.Names;

    public TypesetBlockItemViewModel? SelectedTypesetBlock
    {
        get => _selectedTypesetBlock;
        set
        {
            if (Set(ref _selectedTypesetBlock, value))
                Raise(nameof(HasSelectedTypesetBlock));
        }
    }

    public bool HasSelectedTypesetBlock => SelectedTypesetBlock is not null;

    public string TypesetBlocksHeader => $"BLOK — {TypesetBlocks.Count}";

    public bool TypesetShowOriginal
    {
        get => _typesetShowOriginal;
        set
        {
            if (Set(ref _typesetShowOriginal, value))
                Raise(nameof(TypesetCanvasBitmap));
        }
    }

    /// <summary>Toggle pembanding: tampilkan halaman bersih TANPA teks typeset.</summary>
    public bool TypesetShowClean
    {
        get => _typesetShowClean;
        set
        {
            if (Set(ref _typesetShowClean, value))
                Raise(nameof(TypesetCanvasBitmap));
        }
    }

    public Bitmap? TypesetFinalBitmap
    {
        get => _typesetFinalBitmap;
        private set
        {
            if (Set(ref _typesetFinalBitmap, value))
                Raise(nameof(TypesetCanvasBitmap));
        }
    }

    public Bitmap? TypesetBaseBitmap
    {
        get => _typesetBaseBitmap;
        private set
        {
            if (Set(ref _typesetBaseBitmap, value))
                Raise(nameof(TypesetCanvasBitmap));
        }
    }

    /// <summary>Bitmap kanvas Typesetting menurut toggle:
    /// Asli → halaman asli; Bersih → bersih tanpa teks; selain itu final.</summary>
    public Bitmap? TypesetCanvasBitmap =>
        TypesetShowOriginal ? CurrentPageBitmap
        : TypesetShowClean ? TypesetBaseBitmap ?? CurrentPageBitmap
        : TypesetFinalBitmap ?? TypesetBaseBitmap ?? CurrentPageBitmap;

    public void ShowTypeset()
    {
        IsLibraryActive = false;
        IsDetectActive = false;
        IsCleanActive = false;
        IsTranslateActive = false;
        IsExportActive = false;
        IsTypesetActive = true;
        LoadTypesetForCurrentPage();
        Raise(nameof(ContextInfo));
    }

    /// <summary>Muat ulang daftar blok typeset untuk halaman aktif.</summary>
    public void LoadTypesetForCurrentPage()
    {
        TypesetBlocks.Clear();
        var page = CurrentProjectPage();
        if (page is not null)
        {
            int n = 0;
            foreach (var b in page.Blocks)
                TypesetBlocks.Add(new TypesetBlockItemViewModel(b, ++n, this));
        }
        SelectedTypesetBlock = TypesetBlocks.FirstOrDefault();
        Raise(nameof(TypesetBlocksHeader));
        Raise(nameof(ContextInfo));
        _ = RefreshTypesetBitmapsAsync();
    }

    /// <summary>Dipanggil item blok saat gaya berubah: simpan + gambar ulang.</summary>
    public void OnTypesetStyleChanged(TypesetBlockItemViewModel item)
    {
        SaveProject();
        Raise(nameof(ContextInfo));
        _ = RefreshTypesetBitmapsAsync();
    }

    /// <summary>Dipanggil item blok saat teks terjemahan disunting dari panel.</summary>
    public void OnTypesetContentChanged(TypesetBlockItemViewModel item)
    {
        if (!string.IsNullOrWhiteSpace(item.Block.Translation) &&
            item.Block.TranslatedBy is null)
        {
            item.Block.TranslatedBy = "Manual";
            item.Block.TranslatedAt = DateTime.UtcNow;
        }
        SaveProject();
        _ = RefreshTypesetBitmapsAsync();
    }

    /// <summary>Terapkan preset bernama ke blok terpilih.</summary>
    public void ApplyPresetToSelectedBlock(string presetName)
    {
        var item = SelectedTypesetBlock;
        if (item is null)
            return;
        item.ApplyPreset(presetName);
        StatusText = $"Preset \"{presetName}\" diterapkan ke blok #{item.Number}.";
    }

    /// <summary>Bitmap dasar halaman untuk jalur pintar: halaman
    /// bersih dari CleanStore bila ada pekerjaan bersih, selain itu
    /// bitmap halaman kerja (pola yang sama seperti render).
    /// Pemanggil memiliki bitmap hasil.</summary>
    private SKBitmap? BaseBitmapForPage(int index)
    {
        var chapter = CurrentChapter;
        var projectChapter = _projectChapter;
        if (chapter is null || projectChapter is null
            || index < 0 || index >= chapter.Pages.Count)
            return null;
        var page = projectChapter.PageAt(index);
        return CleanStore.GetCleanedPageBitmap(CleanSvc, chapter, projectChapter, page)
               ?? SKBitmap.Decode(chapter.Pages[index].WorkPath);
    }

    /// <summary>Terapkan typeset otomatis ke halaman aktif
    /// (jalur pintar v0.7.0: diukur dari bitmap dasar halaman).</summary>
    public void ApplyAutoTypesetCurrentPage()
    {
        var page = CurrentProjectPage();
        if (page is null)
            return;
        using var baseBmp = BaseBitmapForPage(page.Index);
        int n = baseBmp is not null
            ? TypesetService.SmartAutoTypesetPage(page, baseBmp)
            : TypesetService.AutoTypesetPage(page);
        SaveProject();
        LoadTypesetForCurrentPage();
        _ = RefreshTypesetBitmapsAsync();
        StatusText = n > 0
            ? $"Typeset otomatis pintar: {n} blok halaman ini diisi (tengah bubble, ukuran seragam)."
            : "Semua blok berterjemahan halaman ini sudah di-typeset.";
    }

    /// <summary>Terapkan typeset otomatis ke seluruh bab
    /// (jalur pintar v0.7.0 per halaman).</summary>
    public void ApplyAutoTypesetChapter()
    {
        if (_projectChapter is null)
            return;
        int n = TypesetService.SmartAutoTypesetChapter(
            _projectChapter, BaseBitmapForPage);
        SaveProject();
        LoadTypesetForCurrentPage();
        _ = RefreshTypesetBitmapsAsync();
        StatusText = n > 0
            ? $"Typeset otomatis pintar satu bab: {n} blok diisi (tengah bubble, ukuran seragam)."
            : "Semua blok berterjemahan di bab ini sudah di-typeset.";
    }

    /// <summary>Hit-test kanvas: blok teratas (tergambar terakhir)
    /// yang kotak rendernya memuat titik halaman. Null bila tak ada.</summary>
    public TypesetBlockItemViewModel? HitTestTypesetBlock(int x, int y)
    {
        for (int i = TypesetBlocks.Count - 1; i >= 0; i--)
        {
            var item = TypesetBlocks[i];
            var box = TypesetService.RenderBox(
                item.Block, TypesetService.EffectiveStyle(item.Block));
            if (x >= box.Left && x < box.Right && y >= box.Top && y < box.Bottom)
                return item;
        }
        return null;
    }

    /// <summary>Seret selesai: geser kotak render blok sebesar delta
    /// dari posisi awal seret (Dx/Dy dasar diberikan view).</summary>
    public void CommitBlockDrag(TypesetBlockItemViewModel item,
        int startDx, int startDy, int deltaX, int deltaY)
    {
        item.Block.Typeset ??= TypesetService.DefaultStyle();
        item.Block.Typeset.Dx = startDx + deltaX;
        item.Block.Typeset.Dy = startDy + deltaY;
        item.Refresh();
        OnTypesetStyleChanged(item);
    }

    /// <summary>Bangun ulang bitmap bersih &amp; final halaman aktif
    /// di latar (dasar bersih dari cache CleanStore).</summary>
    public async Task RefreshTypesetBitmapsAsync()
    {
        int gen = ++_typesetRefreshGen;
        var chapter = CurrentChapter;
        var projectChapter = _projectChapter;
        var page = CurrentProjectPage();
        if (chapter is null || projectChapter is null || page is null)
        {
            TypesetBaseBitmap = null;
            TypesetFinalBitmap = null;
            return;
        }
        try
        {
            var (baseAvalonia, finalAvalonia) = await Task.Run(() =>
            {
                SKBitmap? baseSk = CleanStore.GetCleanedPageBitmap(
                    CleanSvc, chapter, projectChapter, page);
                SKBitmap baseOwned;
                if (baseSk is null)
                {
                    baseOwned = SKBitmap.Decode(
                        chapter.Pages[page.Index].WorkPath)
                        ?? throw new InvalidDataException("Halaman kerja tidak terbaca.");
                }
                else
                {
                    baseOwned = baseSk;
                }
                using var finalSk = TypesetService.RenderFinalBitmap(baseOwned, page);
                var basePng = ToAvaloniaBitmap(baseOwned);
                var finalPng = ToAvaloniaBitmap(finalSk);
                baseOwned.Dispose();
                return (basePng, finalPng);
            });
            if (gen != _typesetRefreshGen)
            {
                baseAvalonia.Dispose();
                finalAvalonia.Dispose();
                return;
            }
            var oldBase = _typesetBaseBitmap;
            var oldFinal = _typesetFinalBitmap;
            TypesetBaseBitmap = baseAvalonia;
            TypesetFinalBitmap = finalAvalonia;
            oldBase?.Dispose();
            oldFinal?.Dispose();
        }
        catch (ModelMissingException)
        {
            if (gen == _typesetRefreshGen && IsTypesetActive)
                StatusText = "Model Inpaint LaMA belum diunduh — halaman " +
                             "dengan operasi inpaint belum bisa dibangun; " +
                             "unduh lewat kartu Model di halaman Bersihkan.";
        }
        catch (Exception ex)
        {
            if (gen == _typesetRefreshGen && IsTypesetActive)
                StatusText = $"Gagal membangun halaman final: {ex.Message}";
        }
    }

    private static Bitmap ToAvaloniaBitmap(SKBitmap sk)
    {
        using var img = SKImage.FromBitmap(sk);
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        using var ms = new MemoryStream();
        data.SaveTo(ms);
        ms.Position = 0;
        return new Bitmap(ms);
    }
}
