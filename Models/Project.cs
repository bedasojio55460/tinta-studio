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
    /// v2 (Tahap 2): bab memuat halaman + blok teks deteksi/OCR.</summary>
    public const int CurrentVersion = 2;

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
            ch.Pages ??= new List<ProjectPage>();
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

/// <summary>Analisis satu halaman: blok teks hasil deteksi + OCR.</summary>
public sealed class ProjectPage
{
    /// <summary>Indeks halaman (0-based, urutan impor).</summary>
    public int Index { get; set; }

    /// <summary>Blok teks pada halaman ini (sudah dalam urutan baca).</summary>
    public List<TextBlock> Blocks { get; set; } = new();
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
