using System.Text.Json;
using System.Text.Json.Serialization;

namespace TintaStudio.Models;

/// <summary>
/// Format proyek milik Tinta Studio sendiri, disimpan sebagai berkas
/// <c>.tinta.json</c>. Memuat daftar bab yang dirujuk (berkas asli tidak
/// pernah disalin/diubah — proyek hanya menyimpan jalur sumbernya).
/// </summary>
public sealed class TintaProject
{
    /// <summary>Penanda format berkas proyek.</summary>
    public const string FormatMarker = "tinta-studio-project";

    /// <summary>Versi skema format proyek saat ini.
    /// v2 (Tahap 2): bab memuat halaman + blok teks deteksi/OCR.
    /// v3 (Tahap 3): halaman memuat operasi pembersihan + sapuan manual.
    /// v4 (Tahap 4): blok memuat hasil terjemahan + cap penyedia.
    /// v5 (Tahap 5): blok memuat gaya typeset (font, warna, stroke,
    /// perataan, geometri render) untuk digambar ke halaman bersih.
    /// v6 (v0.7.6): operasi bersih memuat pilihan perlindungan garis
    /// tepi untuk inpaint (LineProtect; berkas lama tanpa bidang ini
    /// dimuat sebagai true = perilaku v0.7.5).
    /// Bidang sapuan tambahan (v0.7.7, tetap skema v6): sapuan manual
    /// memuat jenisnya (Kind: cat solid / pulihkan asli / penghapus
    /// latar inpaint) + pilihan garis tepi per sapuan inpaint;
    /// berkas lama tanpa bidang ini dimuat sebagai sapuan
    /// cat/pulihkan menurut Erased — kompatibel penuh.
    /// CATATAN: API key TIDAK PERNAH disimpan di berkas proyek —
    /// key hanya hidup di ~/.config/tinta-studio/keys.json (0600).</summary>
    public const int CurrentVersion = 6;

    public string Format { get; set; } = FormatMarker;
    public int Version { get; set; } = CurrentVersion;
    public string Name { get; set; } = "Proyek Tanpa Judul";
    public List<ProjectChapter> Chapters { get; set; } = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public void Save(string path)
    {
        var json = JsonSerializer.Serialize(this, JsonOptions);
        File.WriteAllText(path, json);
    }

    public static TintaProject Load(string path)
    {
        var json = File.ReadAllText(path);
        var project = JsonSerializer.Deserialize<TintaProject>(json, JsonOptions)
            ?? throw new InvalidDataException("Berkas proyek kosong atau rusak.");
        if (project.Format != FormatMarker)
            throw new InvalidDataException(
                "Berkas ini bukan proyek Tinta Studio (penanda format tidak cocok).");
        if (project.Version > CurrentVersion)
            throw new InvalidDataException(
                $"Versi proyek ({project.Version}) lebih baru dari yang didukung aplikasi ini ({CurrentVersion}).");
        project.Chapters ??= new List<ProjectChapter>();
        foreach (var ch in project.Chapters)
        {
            ch.Pages ??= new List<ProjectPage>();
            foreach (var page in ch.Pages)
            {
                page.Blocks ??= new List<TextBlock>();
                page.CleanOps ??= new List<CleanOperation>();
                page.ManualStrokes ??= new List<BrushStroke>();
            }
        }
        return project;
    }
}

/// <summary>Satu bab komik di dalam proyek.</summary>
public sealed class ProjectChapter
{
    public string SourcePath { get; set; } = "";
    public string Title { get; set; } = "";
    public string Format { get; set; } = "";
    public int PageCount { get; set; }

    /// <summary>Hasil analisis per halaman (Tahap 2+). Berkas v1 tidak
    /// memuat ini → daftar kosong; aman dimuat.</summary>
    public List<ProjectPage> Pages { get; set; } = new();

    /// <summary>Ambil/buat entri halaman untuk indeks tertentu.</summary>
    public ProjectPage PageAt(int index)
    {
        var page = Pages.FirstOrDefault(p => p.Index == index);
        if (page is null)
        {
            page = new ProjectPage { Index = index };
            Pages.Add(page);
            Pages.Sort((a, b) => a.Index.CompareTo(b.Index));
        }
        return page;
    }
}

/// <summary>Analisis satu halaman: blok teks hasil deteksi + OCR,
/// plus riwayat pembersihan (Tahap 3).</summary>
public sealed class ProjectPage
{
    /// <summary>Indeks halaman (0-based, urutan impor).</summary>
    public int Index { get; set; }

    /// <summary>Blok teks pada halaman ini (sudah dalam urutan baca).</summary>
    public List<TextBlock> Blocks { get; set; } = new();

    /// <summary>Operasi pembersihan per blok (Tahap 3). Berkas v1/v2
    /// tidak memuat ini → daftar kosong; aman dimuat.</summary>
    public List<CleanOperation> CleanOps { get; set; } = new();

    /// <summary>Sapuan kuas/penghapus manual di atas hasil bersih
    /// (Tahap 3), diterapkan sesudah operasi per blok, urut Seq.</summary>
    public List<BrushStroke> ManualStrokes { get; set; } = new();

    /// <summary>Penghitung urutan untuk operasi bersih &amp; sapuan
    /// (dasar fitur Urungkan: yang Seq-nya terbesar dibatalkan dulu).</summary>
    public int CleanSeq { get; set; }
}

/// <summary>Cara membersihkan teks sebuah blok.</summary>
public enum CleanMode
{
    /// <summary>Isi area teks dengan warna solid.</summary>
    Solid,

    /// <summary>Inpaint LaMA: isi mengikuti latar di sekitarnya.</summary>
    Inpaint,
}

/// <summary>
/// Satu operasi pembersihan atas satu blok: mode + warna isi.
/// Warna null = otomatis (sampel warna kertas di dalam bubble,
/// bawaan putih). Operasi dapat dibangun ulang kapan pun dari
/// halaman asli — berkas asli tidak pernah ditimpa.
/// </summary>
public sealed class CleanOperation
{
    /// <summary>Id blok (TextBlock.Id) yang dibersihkan.</summary>
    public string BlockId { get; set; } = "";

    public CleanMode Mode { get; set; } = CleanMode.Solid;

    /// <summary>Warna isi mode Solid dalam format "#RRGGBB";
    /// null = sampel otomatis dari dalam bubble.</summary>
    public string? FillColor { get; set; }

    /// <summary>Pilihan perlindungan garis tepi untuk mode Inpaint
    /// (v0.7.6): true = garis tepi bubble / karya garis yang
    /// melintasi area blok dilindungi dari mask inpaint (perilaku
    /// v0.7.5 — garis utuh, tetapi huruf yang menyatu fisik dengan
    /// garis ikut terlindungi dan bisa tertinggal); false = tanpa
    /// perlindungan sama sekali (semua tinta di dalam kotak
    /// dihapus tuntas, garis tepi bisa terpotong). Diabaikan mode
    /// Solid. Berkas v1–v5 tidak memuat bidang ini → nilai awal
    /// true berlaku (perilaku v0.7.5).</summary>
    public bool LineProtect { get; set; } = true;

    /// <summary>Nomor urut penerapan (dasar Urungkan).</summary>
    public int Seq { get; set; }

    /// <summary>Waktu penerapan (UTC) — informasi saja.</summary>
    public DateTime AppliedUtc { get; set; }
}

/// <summary>Satu titik sapuan manual, dalam piksel halaman asli.</summary>
public sealed class StrokePoint
{
    public int X { get; set; }
    public int Y { get; set; }
}

/// <summary>Jenis sapuan manual di halaman Bersihkan.</summary>
public enum StrokeKind
{
    /// <summary>Lukis warna solid di atas hasil bersih (kuas).</summary>
    Paint,

    /// <summary>Kembalikan piksel asli di area sapuan (penghapus).</summary>
    Erase,

    /// <summary>Penghapus latar: area sapuan di-inpaint LaMA
    /// mengikuti latar di sekitarnya (v0.7.7).</summary>
    Inpaint,
}

/// <summary>
/// Satu sapuan manual yang digambar pengguna di lapisan bersih.
/// Tiga jenis (Kind): cat solid (kuas), pulihkan asli (penghapus),
/// dan penghapus latar (inpaint LaMA di area sapuan). Bidang
/// Erased lama dipertahankan sebagai bayangan Kind untuk
/// Paint/Erase (true = Erase) agar berkas lama terbaca jelas;
/// Kind adalah sumber kebenaran — berkas v1–v6 tanpa Kind dimuat
/// sebagai Paint/Erase menurut Erased (lihat TintaProject.Load).
/// Sapuan inpaint selalu Erased=false.
/// </summary>
public sealed class BrushStroke
{
    private StrokeKind? _kind;

    /// <summary>Jenis sapuan. Bila berkas tidak memuat bidang ini
    /// (proyek lama), nilainya diturunkan dari Erased: true = Erase,
    /// false = Paint — jadi pemetaan lama tetap persis.</summary>
    public StrokeKind Kind
    {
        get => _kind ?? (Erased ? StrokeKind.Erase : StrokeKind.Paint);
        set => _kind = value;
    }

    /// <summary>Bayangan lama: true bila Kind=Erase. Diabaikan
    /// untuk Kind=Inpaint.</summary>
    public bool Erased { get; set; }

    /// <summary>Pilihan perlindungan garis tepi untuk sapuan
    /// inpaint (v0.7.7): nilai yang aktif saat sapuan dibuat —
    /// garis yang melintasi mask sapuan dilindungi bila true.
    /// Diabaikan jenis lain. Berkas lama tanpa bidang ini → true.</summary>
    public bool LineProtect { get; set; } = true;

    /// <summary>Warna kuas "#RRGGBB" (diabaikan bila Erased).</summary>
    public string Color { get; set; } = "#FFFFFF";

    /// <summary>Radius kuas dalam piksel halaman.</summary>
    public int Radius { get; set; } = 12;

    public List<StrokePoint> Points { get; set; } = new();

    /// <summary>Nomor urut penerapan (dasar Urungkan).</summary>
    public int Seq { get; set; }
}

/// <summary>Arah teks sebuah blok.</summary>
public enum TextDirection
{
    Horizontal,
    Vertical,
}

/// <summary>
/// Satu blok teks: kotak batas dalam piksel halaman asli, arah,
/// bahasa OCR, teks (dapat disunting pengguna), keyakinan 0..1.
/// </summary>
public sealed class TextBlock
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }

    public TextDirection Direction { get; set; } = TextDirection.Horizontal;

    /// <summary>Kode bahasa OCR: "ja" | "ko" | "ch".</summary>
    public string Language { get; set; } = "ja";

    /// <summary>Teks hasil OCR (blok yang disunting pengguna menyimpan teks ini).</summary>
    public string Text { get; set; } = "";

    /// <summary>Keyakinan OCR 0..1 (rata-rata probabilitas CTC terpilih).</summary>
    public double Confidence { get; set; }

    /// <summary>Skor deteksi 0..1 dari model detektor.</summary>
    public double DetectionScore { get; set; }

    /// <summary>Hasil terjemahan blok ini (Tahap 4); dapat disunting
    /// pengguna. Null/kosong = belum diterjemahkan. Berkas v1–v3
    /// tidak memuat ini → null; aman dimuat.</summary>
    public string? Translation { get; set; }

    /// <summary>Cap asal terjemahan: "NamaPenyedia • model" yang
    /// menghasilkan terjemahan terakhir (Tahap 4).</summary>
    public string? TranslatedBy { get; set; }

    /// <summary>Waktu terjemahan terakhir dihasilkan (UTC).</summary>
    public DateTime? TranslatedAt { get; set; }

    /// <summary>Alasan kegagalan terjemahan terakhir untuk blok ini;
    /// null bila percobaan terakhir berhasil / belum pernah dicoba.
    /// Hanya untuk ditampilkan di UI — bukan bagian hasil.</summary>
    public string? TranslationError { get; set; }

    /// <summary>Cadangan satu tingkat teks terjemahan sebelum Poles
    /// AI terakhir (Tahap 4) — dasar tombol "Urungkan Poles".
    /// Null = tidak ada poles yang bisa diurungkan.</summary>
    public string? TranslationBackup { get; set; }

    /// <summary>Cap asal milik teks di TranslationBackup.</summary>
    public string? TranslationBackupBy { get; set; }

    /// <summary>Gaya typeset blok ini (Tahap 5): cara teks terjemahan
    /// digambar ke halaman bersih. Null = belum di-typeset (render
    /// memakai gaya bawaan). Berkas v1–v4 tidak memuat ini → null;
    /// aman dimuat.</summary>
    public TypesetStyle? Typeset { get; set; }
}

/// <summary>Perataan teks di dalam kotak render typeset.</summary>
public enum TypesetAlign
{
    Left,
    Center,
    Right,
}

/// <summary>
/// Gaya typeset satu blok (Tahap 5). Nilai bawaan kelas ini adalah
/// gaya "aman": font sans sistem, ukuran otomatis (pas ke kotak),
/// hitam, tanpa stroke, rata tengah. FontFamily kosong berarti
/// "sans aman" yang diselesaikan mesin render ke font sistem yang
/// ada — tidak ada font tertanam/berlisensi yang disimpan proyek.
/// </summary>
public sealed class TypesetStyle
{
    /// <summary>Nama keluarga font sistem; "" = sans aman bawaan.</summary>
    public string FontFamily { get; set; } = "";

    /// <summary>Ukuran font dalam piksel halaman; 0 = otomatis
    /// (dicari ukuran terbesar yang muat di kotak render).</summary>
    public double FontSize { get; set; }

    public bool Bold { get; set; }
    public bool Italic { get; set; }

    /// <summary>Warna isi teks "#RRGGBB".</summary>
    public string ColorHex { get; set; } = "#000000";

    /// <summary>Warna garis tepi (outline) teks "#RRGGBB".</summary>
    public string StrokeHex { get; set; } = "#FFFFFF";

    /// <summary>Lebar garis tepi dalam piksel halaman; 0 = tanpa stroke.</summary>
    public double StrokeWidth { get; set; }

    public TypesetAlign Align { get; set; } = TypesetAlign.Center;

    /// <summary>Pengali tinggi baris terhadap tinggi alami font.</summary>
    public double LineSpacing { get; set; } = 1.1;

    /// <summary>Spasi tambahan antar huruf, piksel halaman.</summary>
    public double LetterSpacing { get; set; }

    /// <summary>Tulis vertikal gaya CJK: karakter ditumpuk per kolom,
    /// kolom berjalan kanan → kiri.</summary>
    public bool Vertical { get; set; }

    /// <summary>Rotasi kotak render: 0/90/180/270 derajat.</summary>
    public int Rotation { get; set; }

    /// <summary>Geser kotak render dari kotak blok (piksel).</summary>
    public int Dx { get; set; }
    public int Dy { get; set; }

    /// <summary>Ubah lebar/tinggi kotak render dari kotak blok (piksel,
    /// boleh negatif; hasil akhir dijepit minimum 8 px).</summary>
    public int Dw { get; set; }
    public int Dh { get; set; }

    /// <summary>Nama preset terakhir yang diterapkan ke gaya ini.</summary>
    public string? PresetName { get; set; }

    /// <summary>Salinan dalam (semua bidang nilai/teks).</summary>
    public TypesetStyle Clone() => new()
    {
        FontFamily = FontFamily,
        FontSize = FontSize,
        Bold = Bold,
        Italic = Italic,
        ColorHex = ColorHex,
        StrokeHex = StrokeHex,
        StrokeWidth = StrokeWidth,
        Align = Align,
        LineSpacing = LineSpacing,
        LetterSpacing = LetterSpacing,
        Vertical = Vertical,
        Rotation = Rotation,
        Dx = Dx, Dy = Dy, Dw = Dw, Dh = Dh,
        PresetName = PresetName,
    };
}
