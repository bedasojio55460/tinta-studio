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

    /// <summary>Versi skema format proyek saat ini.</summary>
    public const int CurrentVersion = 1;

    public string Format { get; set; } = FormatMarker;
    public int Version { get; set; } = CurrentVersion;
    public string Name { get; set; } = "Proyek Tanpa Judul";
    public List<ProjectChapter> Chapters { get; set; } = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
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
}
