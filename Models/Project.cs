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
    /// v3 (Tahap 3): halaman memuat operasi pembersihan + sapuan manual.</summary>
    public const int CurrentVersion = 3;

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

/// <summary>
/// Satu sapuan kuas/penghapus manual yang digambar pengguna di atas
/// lapisan bersih. Erased=true berarti "kembalikan piksel asli"
/// (penghapus), false = lukis warna solid.
/// </summary>
public sealed class BrushStroke
{
    public bool Erased { get; set; }

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
}
