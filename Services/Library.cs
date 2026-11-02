using System.Text.Json;
using System.Text.Json.Serialization;

namespace TintaStudio.Services;

/// <summary>Satu entri bab pada indeks pustaka.</summary>
public sealed record LibraryEntry(
    string SourcePath,
    string Title,
    string Format,
    int PageCount,
    DateTimeOffset OpenedAtUtc);

/// <summary>
/// Indeks pustaka bab yang pernah dibuka, disimpan sebagai JSON
/// bernama library.json di folder data aplikasi
/// (AppConfig.ConfigDir — mengikuti penanda lokasi v0.7.13).
/// Berkas asli komik tidak disalin — pustaka hanya mengingat jalurnya.
/// </summary>
public sealed class LibraryService
{
    private readonly string _filePath;
    private List<LibraryEntry> _entries = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public LibraryService(string? filePath = null)
    {
        _filePath = filePath ?? DefaultPath();
        Load();
    }

    public static string DefaultPath()
        => Path.Combine(AppConfig.ConfigDir, "library.json");

    public IReadOnlyList<LibraryEntry> Entries => _entries;

    /// <summary>Tambah bab baru, atau segarkan entri yang jalur sumbernya sama.</summary>
    public void AddOrUpdate(Chapter chapter)
    {
        _entries.RemoveAll(e =>
            string.Equals(e.SourcePath, chapter.SourcePath, StringComparison.OrdinalIgnoreCase));
        _entries.Insert(0, new LibraryEntry(
            chapter.SourcePath,
            chapter.Title,
            chapter.Format,
            chapter.Pages.Count,
            DateTimeOffset.UtcNow));
        Save();
    }

    public bool Remove(string sourcePath)
    {
        var removed = _entries.RemoveAll(e =>
            string.Equals(e.SourcePath, sourcePath, StringComparison.OrdinalIgnoreCase)) > 0;
        if (removed) Save();
        return removed;
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_filePath)) return;
            var json = File.ReadAllText(_filePath);
            _entries = JsonSerializer.Deserialize<List<LibraryEntry>>(json, JsonOptions)
                       ?? new List<LibraryEntry>();
        }
        catch (Exception)
        {
            // Indeks rusak tidak boleh menggagalkan aplikasi; mulai dari kosong.
            _entries = new List<LibraryEntry>();
        }
    }

    private void Save()
    {
        var dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(_filePath, JsonSerializer.Serialize(_entries, JsonOptions));
    }
}
