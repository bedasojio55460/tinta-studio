using System.Diagnostics;
using System.Net.Http;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using TintaStudio.Services;

namespace TintaStudio.Views;

/// <summary>
/// Dialog Pengaturan Key &amp; Model (Tahap 4), dibangun dari kode
/// agar ringkas. Per penyedia BYOK: DAFTAR key (bisa banyak —
/// rotasi otomatis saat limit) bermasker dengan tambah/hapus per
/// baris, plus base URL &amp; model. Key hanya disimpan ke
/// ~/.config/tinta-studio/keys.json (0600) lewat AppSecrets;
/// tidak pernah ditampilkan utuh, tidak masuk proyek/log.
/// </summary>
public sealed class TranslateSettingsWindow : Window
{
    // Bukan readonly: Hapus Bersih mengganti objek ini dengan
    // pengaturan bawaan segar sesudah folder data dihapus.
    private AppSettings _settings;
    private readonly Dictionary<string, List<string>> _keys = new();
    private readonly Dictionary<string, StackPanel> _keyRows = new();
    private readonly Dictionary<string, TextBlock> _keyStatus = new();
    private readonly Dictionary<string, (TextBox Base, TextBox Model)> _fields = new();
    private readonly TextBox _delayBox;

    // ── Instal Instan Ollama (v0.7.11) ──────────────────────────
    // Klien bertimeout tak terbatas: unduhan model bisa berjalan
    // lama; pembatalan selalu lewat CancellationTokenSource sendiri.
    private readonly OllamaService _ollama = new(
        new HttpClient { Timeout = Timeout.InfiniteTimeSpan });
    // ── Hapus Bersih (v0.7.11, perluasan) ───────────────────────
    private TextBlock? _wipeList;
    private TextBlock? _wipeResult;
    private Button? _wipeButton;
    private WipePlan? _wipePlan;

    private TextBlock? _ollamaStatus;
    private StackPanel? _ollamaActions;
    private StackPanel? _ollamaModels;
    private StackPanel? _ollamaBusyRow;
    private StackPanel? _ollamaProbePanel;
    private OllamaProbe? _lastProbe;
    private CancellationTokenSource? _ollamaBusy;

    public TranslateSettingsWindow()
    {
        _settings = AppConfig.LoadSettings();
        foreach (var p in TranslateService.Providers)
            if (p.RequiresKey)
                _keys[p.Id] = AppSecrets.GetKeys(p.Id).ToList();

        Title = "Pengaturan Key & Model — Tinta Studio";
        Width = 620;
        Height = 720;
        MinWidth = 520;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        Opened += async (_, _) =>
        {
            RefreshWipeSection();
            await RefreshOllamaAsync();
        };
        Closed += (_, _) =>
        {
            try { _ollamaBusy?.Cancel(); } catch { }
        };

        var root = new StackPanel { Spacing = 14, Margin = new Thickness(16) };
        root.Children.Add(new TextBlock
        {
            Text = "Key disimpan lokal di keys.json (izin 0600), tidak pernah " +
                   "masuk proyek, git, atau log. Penyedia BYOK mendukung banyak " +
                   "key — saat satu key kena limit, aplikasi otomatis beralih " +
                   "ke key berikutnya.",
            Foreground = new SolidColorBrush(Color.Parse("#8A939E")),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
        });

        foreach (var provider in TranslateService.Providers)
            root.Children.Add(BuildProviderSection(provider));

        // Jeda antar halaman (global)
        var delayPanel = new StackPanel { Spacing = 4 };
        delayPanel.Children.Add(SectionLabel("JEDA ANTAR HALAMAN (DETIK)"));
        _delayBox = new TextBox
        {
            Text = _settings.RequestDelaySeconds.ToString(),
            Width = 90,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        delayPanel.Children.Add(_delayBox);
        delayPanel.Children.Add(new TextBlock
        {
            Text = "Jeda di antara permintaan halaman selama Terjemahkan Satu Bab " +
                   "(dan sebelum coba-ulang) agar tidak memancing limit. 0 = tanpa jeda.",
            Foreground = new SolidColorBrush(Color.Parse("#8A939E")),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
        });
        root.Children.Add(delayPanel);

        root.Children.Add(BuildWipeSection());

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        var cancel = new Button { Content = "Batal" };
        cancel.Click += (_, _) => Close(false);
        var save = new Button { Content = "Simpan" };
        save.Classes.Add("primary");
        save.Click += (_, _) => SaveAndClose();
        buttons.Children.Add(cancel);
        buttons.Children.Add(save);
        root.Children.Add(buttons);

        Content = new ScrollViewer { Content = root };
    }

    private static TextBlock SectionLabel(string text) => new()
    {
        Text = text,
        FontSize = 11,
        FontWeight = FontWeight.SemiBold,
        Foreground = new SolidColorBrush(Color.Parse("#8A939E")),
        LetterSpacing = 1.2,
    };

    private Control BuildProviderSection(ProviderInfo provider)
    {
        var ps = _settings.For(provider.Id);
        var panel = new StackPanel { Spacing = 6 };
        var card = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#17191F")),
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(12, 10),
            Child = panel,
        };
        panel.Children.Add(new TextBlock
        {
            Text = provider.RequiresKey
                ? provider.DisplayName
                : $"{provider.DisplayName} — tanpa key",
            FontWeight = FontWeight.SemiBold,
            FontSize = 14,
        });

        if (provider.RequiresKey)
        {
            var status = new TextBlock
            {
                Foreground = new SolidColorBrush(Color.Parse("#2DD4BF")),
                FontSize = 12,
            };
            _keyStatus[provider.Id] = status;
            panel.Children.Add(status);

            var rows = new StackPanel { Spacing = 4 };
            _keyRows[provider.Id] = rows;
            panel.Children.Add(rows);
            RebuildKeyRows(provider.Id);

            var addRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
            };
            var newKey = new TextBox
            {
                PasswordChar = '•',
                Width = 380,
                PlaceholderText = "Tempel API key baru di sini…",
            };
            var add = new Button { Content = "Tambah" };
            add.Click += (_, _) =>
            {
                var v = (newKey.Text ?? "").Trim();
                if (v.Length == 0)
                    return;
                if (!_keys[provider.Id].Contains(v))
                    _keys[provider.Id].Add(v);
                newKey.Text = "";
                RebuildKeyRows(provider.Id);
            };
            addRow.Children.Add(newKey);
            addRow.Children.Add(add);
            panel.Children.Add(addRow);

            // Ambil Key (v0.7.11): buka halaman key resmi penyedia
            // di browser bawaan — pengguna tidak perlu lagi menebak
            // alamatnya (alamat API di kolom Base URL BUKAN halaman
            // untuk manusia dan memang 404 bila dibuka).
            if (KeyPageUrl(provider.Id) is { } keyUrl)
            {
                var keyRow = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                };
                var getKey = new Button { Content = "Ambil Key", Classes = { "tool" } };
                getKey.Click += (_, _) =>
                {
                    try { OllamaService.OpenInBrowser(keyUrl); }
                    catch { /* browser gagal dibuka — teks pendamping menunjuk alamatnya */ }
                };
                keyRow.Children.Add(getKey);
                keyRow.Children.Add(new TextBlock
                {
                    Text = "terbuka di browser; tempel key-nya di sini sesudah dapat",
                    Foreground = new SolidColorBrush(Color.Parse("#8A939E")),
                    FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                });
                panel.Children.Add(keyRow);
            }
        }

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("80,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto"),
            RowSpacing = 6,
            ColumnSpacing = 8,
        };
        var baseBox = new TextBox { Text = ps.BaseUrl };
        var modelBox = new TextBox
        {
            Text = ps.Model,
            PlaceholderText = provider.RequiresKey ? "" : "(model bawaan server boleh dikosongkan)",
        };
        grid.Children.Add(new TextBlock
        {
            Text = "Base URL", VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(Color.Parse("#8A939E")), FontSize = 12,
        });
        Grid.SetColumn(baseBox, 1);
        grid.Children.Add(baseBox);
        var modelLabel = new TextBlock
        {
            Text = "Model", VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(Color.Parse("#8A939E")), FontSize = 12,
        };
        Grid.SetRow(modelLabel, 1);
        grid.Children.Add(modelLabel);
        Grid.SetRow(modelBox, 1);
        Grid.SetColumn(modelBox, 1);
        grid.Children.Add(modelBox);
        panel.Children.Add(grid);
        _fields[provider.Id] = (baseBox, modelBox);

        if (provider.Id == TranslateService.OllamaId)
            panel.Children.Add(BuildOllamaInstallerSection());

        return card;
    }

    /// <summary>Halaman resmi pengambilan API key per penyedia
    /// BYOK (null untuk penyedia lokal / tak dikenal).</summary>
    private static string? KeyPageUrl(string providerId) => providerId switch
    {
        TranslateService.GeminiId => "https://aistudio.google.com/apikey",
        TranslateService.GroqId => "https://console.groq.com/keys",
        TranslateService.OpenRouterId => "https://openrouter.ai/keys",
        _ => null,
    };

    // ── Instal Instan Ollama (v0.7.11) ──────────────────────────

    private static TextBlock Muted(string text, double size = 11) => new()
    {
        Text = text,
        Foreground = new SolidColorBrush(Color.Parse("#8A939E")),
        FontSize = size,
        TextWrapping = TextWrapping.Wrap,
    };

    /// <summary>Seksi Instal Instan di dalam kartu Ollama: status
    /// server (dicek otomatis saat dialog dibuka), tombol
    /// Pasang/Jalankan sesuai keadaan, dan daftar model kurasi
    /// dengan Unduh/Pakai/Hapus per baris.</summary>
    private Control BuildOllamaInstallerSection()
    {
        var panel = new StackPanel { Spacing = 6 };
        _ollamaProbePanel = panel;
        panel.Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.Parse("#2DD4BF")),
            Height = 1,
            Opacity = 0.35,
        });
        panel.Children.Add(new TextBlock
        {
            Text = "INSTAL INSTAN — OLLAMA DI PC INI",
            FontSize = 11,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.Parse("#8A939E")),
            LetterSpacing = 1.2,
        });
        _ollamaStatus = new TextBlock
        {
            Text = "Memeriksa Ollama…",
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
        };
        panel.Children.Add(_ollamaStatus);
        _ollamaActions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
        };
        panel.Children.Add(_ollamaActions);
        // Baris sibuk: satu-satunya kontrol yang tetap aktif selama
        // operasi panjang (panel probe dinonaktifkan) — tempat
        // tombol Batal agar unduhan selalu bisa dihentikan.
        _ollamaBusyRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            IsVisible = false,
        };
        var cancelBusy = new Button { Content = "Batalkan unduhan", Classes = { "tool" } };
        cancelBusy.Click += (_, _) =>
        {
            try { _ollamaBusy?.Cancel(); } catch { }
        };
        _ollamaBusyRow.Children.Add(cancelBusy);
        panel.Children.Add(_ollamaBusyRow);
        _ollamaModels = new StackPanel { Spacing = 4 };
        panel.Children.Add(_ollamaModels);
        return panel;
    }

    /// <summary>Probe ulang keadaan Ollama lalu gambar ulang
    /// status + tombol + daftar model. Aman dipanggil kapan pun;
    /// kegagalan jaringan tampil sebagai teks, bukan pengecualian.</summary>
    private async Task RefreshOllamaAsync()
    {
        if (_ollamaStatus is null)
            return;
        try
        {
            var probe = await _ollama.ProbeAsync();
            _lastProbe = probe;
            RenderOllamaSection(probe);
        }
        catch (OperationCanceledException)
        {
            // dialog ditutup di tengah probe — tidak ada yang digambar
        }
        catch (Exception ex)
        {
            if (_ollamaStatus is not null)
                _ollamaStatus.Text = $"Pemeriksaan Ollama gagal: {ex.Message}";
        }
    }

    private void RenderOllamaSection(OllamaProbe probe)
    {
        if (_ollamaStatus is null || _ollamaActions is null || _ollamaModels is null)
            return;
        _ollamaActions.Children.Clear();
        _ollamaModels.Children.Clear();

        var refresh = new Button { Content = "Segarkan", Classes = { "tool" } };
        refresh.Click += async (_, _) => await RefreshOllamaAsync();

        switch (probe.State)
        {
            case OllamaState.Running:
                _ollamaStatus.Text = probe.Models.Count == 0
                    ? "✓ Ollama sedang jalan — belum ada model terpasang. " +
                      "Unduh satu model di bawah untuk mulai."
                    : $"✓ Ollama sedang jalan — {probe.Models.Count} model terpasang.";
                _ollamaActions.Children.Add(refresh);
                RenderOllamaModelRows(probe);
                break;

            case OllamaState.InstalledNotRunning:
                _ollamaStatus.Text =
                    "Ollama sudah terpasang di PC ini tapi belum jalan. " +
                    "Klik Jalankan Ollama, tunggu beberapa detik, lalu Segarkan.";
                var run = new Button { Content = "Jalankan Ollama" };
                run.Click += async (_, _) =>
                {
                    if (probe.ExecutablePath is null)
                        return;
                    try
                    {
                        OllamaService.StartOllama(probe.ExecutablePath);
                        SetOllamaStatus("Memulai Ollama… tunggu beberapa detik lalu " +
                                        "pemeriksaan diulang otomatis.");
                        await Task.Delay(2500);
                        await RefreshOllamaAsync();
                    }
                    catch (Exception ex)
                    {
                        SetOllamaStatus($"Ollama gagal dijalankan: {ex.Message}. " +
                                        "Coba buka Ollama dari Start Menu.");
                    }
                };
                _ollamaActions.Children.Add(run);
                _ollamaActions.Children.Add(refresh);
                break;

            default: // NotInstalled
                _ollamaStatus.Text =
                    "Ollama belum terpasang di PC ini. Klik Pasang Ollama — " +
                    "installer resmi (± ratusan MB) diunduh lalu dijalankan. " +
                    "Selesaikan wizard-nya (Next) — sesudah itu kembali ke sini " +
                    "dan klik Segarkan.";
                var install = new Button { Content = "Pasang Ollama" };
                install.Click += async (_, _) => await InstallOllamaAsync();
                _ollamaActions.Children.Add(install);
                _ollamaActions.Children.Add(refresh);
                break;
        }
    }

    /// <summary>Baris-baris model: kurasi (dengan Unduh/Pakai/Hapus
    /// + progres) lalu model lain yang terpasang di luar kurasi
    /// (dengan Pakai/Hapus).</summary>
    private void RenderOllamaModelRows(OllamaProbe probe)
    {
        if (_ollamaModels is null)
            return;
        _ollamaModels.Children.Add(SectionLabel("MODEL UNTUK TERJEMAHAN"));
        foreach (var curated in OllamaService.CuratedModels)
        {
            var installed = probe.Models.FirstOrDefault(
                m => OllamaService.ModelMatches(m.Name, curated.Name));
            _ollamaModels.Children.Add(BuildModelRow(
                curated.Name, $"{curated.Name}  ({curated.SizeLabel})",
                curated.Note, installed?.Name));
        }
        foreach (var extra in probe.Models)
        {
            if (OllamaService.CuratedModels.Any(
                    c => OllamaService.ModelMatches(extra.Name, c.Name)))
                continue;
            _ollamaModels.Children.Add(BuildModelRow(
                extra.Name, $"{extra.Name}  ({OllamaService.FormatSize(extra.SizeBytes)})",
                "Model lain yang terpasang di PC ini", extra.Name));
        }
    }

    private Control BuildModelRow(string wantedName, string title,
        string note, string? installedName)
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto"),
            ColumnSpacing = 6,
        };
        var texts = new StackPanel { Spacing = 0 };
        texts.Children.Add(new TextBlock { Text = title, FontSize = 12 });
        var noteBlock = Muted(note);
        texts.Children.Add(noteBlock);
        row.Children.Add(texts);

        var use = new Button
        {
            Content = "Pakai",
            Classes = { "tool" },
            IsVisible = installedName is not null,
        };
        use.Click += (_, _) =>
        {
            if (installedName is not null
                && _fields.TryGetValue(TranslateService.OllamaId, out var f))
            {
                f.Model.Text = installedName;
                SetOllamaStatus($"Model terpilih: {installedName}. " +
                                "Jangan lupa klik Simpan agar tersimpan.");
            }
        };
        Grid.SetColumn(use, 1);
        row.Children.Add(use);

        var del = new Button
        {
            Content = "Hapus",
            Classes = { "tool" },
            IsVisible = installedName is not null,
        };
        del.Click += async (_, _) =>
        {
            if (installedName is not null)
                await DeleteModelAsync(installedName);
        };
        Grid.SetColumn(del, 2);
        row.Children.Add(del);

        var pull = new Button
        {
            Content = "Unduh",
            Classes = { "tool" },
            IsVisible = installedName is null,
        };
        pull.Click += async (_, _) => await PullModelAsync(wantedName, noteBlock);
        Grid.SetColumn(pull, 3);
        row.Children.Add(pull);
        return row;
    }

    private void SetOllamaStatus(string text)
    {
        if (_ollamaStatus is not null)
            _ollamaStatus.Text = text;
    }

    /// <summary>Tandai awal/akhir operasi panjang Instal Instan:
    /// selama sibuk seluruh panel probe dinonaktifkan dan kursor
    /// tunggu menyala.</summary>
    private CancellationTokenSource BeginOllamaBusy()
    {
        _ollamaBusy?.Cancel();
        _ollamaBusy = new CancellationTokenSource();
        if (_ollamaProbePanel is not null)
            _ollamaProbePanel.IsEnabled = false;
        if (_ollamaBusyRow is not null)
            _ollamaBusyRow.IsVisible = true;
        Cursor = new Cursor(StandardCursorType.Wait);
        return _ollamaBusy; // (tipe Cursor dari Avalonia.Input)
    }

    private void EndOllamaBusy()
    {
        if (_ollamaProbePanel is not null)
            _ollamaProbePanel.IsEnabled = true;
        if (_ollamaBusyRow is not null)
            _ollamaBusyRow.IsVisible = false;
        Cursor = Cursor.Default;
    }

    private async Task PullModelAsync(string modelName, TextBlock noteBlock)
    {
        var busy = BeginOllamaBusy();
        noteBlock.Text = $"Mengunduh {modelName}… 0%";
        try
        {
            await _ollama.PullModelAsync(modelName, p =>
            {
                var text = p.Percent is { } pct
                    ? $"Mengunduh {modelName}… {pct:0}% ({p.Status})"
                    : $"Mengunduh {modelName}… {p.Status}";
                Dispatcher.UIThread.Post(() => noteBlock.Text = text);
            }, busy.Token);
            SetOllamaStatus($"Model {modelName} selesai diunduh.");
            EndOllamaBusy();
            await RefreshOllamaAsync();
        }
        catch (OperationCanceledException)
        {
            noteBlock.Text = $"Unduhan {modelName} dibatalkan.";
            EndOllamaBusy();
        }
        catch (Exception ex)
        {
            noteBlock.Text = $"Unduhan {modelName} gagal: {ex.Message}";
            EndOllamaBusy();
        }
    }

    private async Task DeleteModelAsync(string modelName)
    {
        var busy = BeginOllamaBusy();
        try
        {
            await _ollama.DeleteModelAsync(modelName, busy.Token);
            SetOllamaStatus($"Model {modelName} dihapus.");
            EndOllamaBusy();
            await RefreshOllamaAsync();
        }
        catch (OperationCanceledException)
        {
            EndOllamaBusy();
        }
        catch (Exception ex)
        {
            SetOllamaStatus($"Hapus {modelName} gagal: {ex.Message}");
            EndOllamaBusy();
        }
    }

    private async Task InstallOllamaAsync()
    {
        var busy = BeginOllamaBusy();
        try
        {
            SetOllamaStatus("Mengunduh installer Ollama resmi dari ollama.com…");
            var path = await _ollama.DownloadInstallerAsync(pct =>
            {
                if (pct is { } v)
                {
                    var text = $"Mengunduh installer Ollama… {v:0}%";
                    Dispatcher.UIThread.Post(() => SetOllamaStatus(text));
                }
            }, busy.Token);
            SetOllamaStatus("Installer terunduh — menjalankannya sekarang. " +
                            "Selesaikan wizard-nya (Next) — sesudah itu kembali " +
                            "ke sini dan klik Segarkan.");
            OllamaService.RunInstaller(path);
            EndOllamaBusy();
            await Task.Delay(4000);
            await RefreshOllamaAsync();
        }
        catch (OperationCanceledException)
        {
            EndOllamaBusy();
        }
        catch (Exception ex)
        {
            SetOllamaStatus(
                $"Pasang Ollama gagal: {ex.Message}. Kamu bisa memasangnya " +
                "manual dari https://ollama.com/download lalu kembali ke sini " +
                "dan klik Segarkan.");
            EndOllamaBusy();
        }
    }

    /// <summary>Gambar ulang daftar key bermasker satu penyedia.</summary>
    private void RebuildKeyRows(string providerId)
    {
        var rows = _keyRows[providerId];
        rows.Children.Clear();
        var list = _keys[providerId];
        for (int i = 0; i < list.Count; i++)
        {
            int index = i;
            var row = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            };
            row.Children.Add(new TextBlock
            {
                Text = Masked(index, list[index]),
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 12,
            });
            var del = new Button { Content = "Hapus", Classes = { "tool" } };
            del.Click += (_, _) =>
            {
                if (index < _keys[providerId].Count)
                {
                    _keys[providerId].RemoveAt(index);
                    RebuildKeyRows(providerId);
                }
            };
            Grid.SetColumn(del, 1);
            row.Children.Add(del);
            rows.Children.Add(row);
        }
        var (count, active, cooling, bad) = (list.Count, 0, 0, 0);
        if (count > 0)
        {
            var s = AppSecrets.GetStatus(providerId);
            // Daftar di dialog adalah salinan kerja: nomor aktif dari
            // keadaan tersimpan hanya ditampilkan bila daftar belum berubah.
            active = s.ActiveNumber;
            cooling = s.Cooling;
            bad = s.Bad;
        }
        _keyStatus[providerId].Text = count == 0
            ? "Belum ada key"
            : $"{count} key terisi • aktif #{Math.Max(1, active)}" +
              (cooling > 0 ? $" • {cooling} cooldown" : "") +
              (bad > 0 ? $" • {bad} tidak sah" : "");
    }

    /// <summary>Tampilan bermasker: nomor + 4 karakter terakhir.</summary>
    private static string Masked(int index, string key)
    {
        var tail = key.Length <= 4 ? "" : key[^4..];
        return $"Key #{index + 1} — ••••••••{tail}";
    }

    // ── Hapus Bersih — Zona Berbahaya (v0.7.11) ─────────────────

    /// <summary>Seksi Hapus Bersih di bagian bawah dialog:
    /// instruksi jujur + daftar folder persis beserta ukuran live
    /// + satu tombol yang membuka konfirmasi kedua sebelum
    /// penghapusan apa pun terjadi.</summary>
    private Control BuildWipeSection()
    {
        var panel = new StackPanel { Spacing = 6 };
        var card = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#17191F")),
            BorderBrush = new SolidColorBrush(Color.Parse("#B3382E")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(12, 10),
            Child = panel,
        };
        panel.Children.Add(new TextBlock
        {
            Text = "HAPUS BERSIH — ZONA BERBAHAYA",
            FontSize = 11,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.Parse("#F08379")),
            LetterSpacing = 1.2,
        });
        panel.Children.Add(new TextBlock
        {
            Text = "Tombol ini menghapus bersih SEMUA data Tinta Studio dari " +
                   "komputer ini: key API, pengaturan, proyek dan cadangan yang " +
                   "tersimpan di folder data, serta model OCR yang sudah " +
                   "terunduh. Daftar persisnya (ukuran dihitung langsung dari disk):",
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
        });
        _wipeList = new TextBlock
        {
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.Parse("#D7DCE2")),
        };
        panel.Children.Add(_wipeList);
        panel.Children.Add(new TextBlock
        {
            Text = "Yang TIDAK ikut terhapus: berkas exe Tinta Studio sendiri " +
                   "(aplikasi tidak bisa menghapus dirinya yang sedang berjalan — " +
                   "sesudah data bersih, tutup aplikasi lalu hapus berkas exe-nya " +
                   "manual); proyek .json/.cbz yang kamu simpan di tempat lain " +
                   "(mis. Documents); dan model Ollama di " +
                   DataWipe.OllamaModelsDir + " (itu milik Ollama, dipakai banyak aplikasi).",
            Foreground = new SolidColorBrush(Color.Parse("#8A939E")),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
        });
        _wipeButton = new Button
        {
            Content = "Hapus Semua Data Tinta…",
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        _wipeButton.Click += async (_, _) => await ConfirmAndWipeAsync();
        panel.Children.Add(_wipeButton);
        _wipeResult = new TextBlock
        {
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.Parse("#2DD4BF")),
            IsVisible = false,
        };
        panel.Children.Add(_wipeResult);
        return card;
    }

    /// <summary>Hitung ulang rencana hapus (ukuran live) dan
    /// gambar ulang daftar folder di seksi Hapus Bersih.</summary>
    private void RefreshWipeSection()
    {
        if (_wipeList is null)
            return;
        _wipePlan = DataWipe.BuildPlan();
        var lines = _wipePlan.Targets.Select(t => t.Exists
            ? $"• {t.Label}\n    {t.Path} — {DataWipe.FormatSize(t.SizeBytes)}"
            : $"• {t.Label}\n    {t.Path} — (belum ada, sudah bersih)");
        _wipeList.Text = string.Join("\n", lines) +
            $"\nTotal: {DataWipe.FormatSize(_wipePlan.TotalBytes)} " +
            $"dari {_wipePlan.ExistingCount} folder";
    }

    /// <summary>Konfirmasi kedua (pola wajib pemilik: daftar +
    /// jumlah persis dulu, persetujuan eksplisit, baru eksekusi),
    /// lalu eksekusi, kosongkan state dialog, dan laporkan hasil.</summary>
    private async Task ConfirmAndWipeAsync()
    {
        RefreshWipeSection();
        var plan = _wipePlan ?? DataWipe.BuildPlan();
        var detail = string.Join("\n", plan.Targets.Select(t => t.Exists
            ? $"• {t.Path} — {DataWipe.FormatSize(t.SizeBytes)}"
            : $"• {t.Path} — (belum ada, sudah bersih)"));
        var confirm = new Window
        {
            Title = "Konfirmasi Hapus Bersih",
            Width = 520,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(Color.Parse("#101216")),
        };
        var panel = new StackPanel { Spacing = 10, Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock
        {
            Text = "Hapus bersih semua data Tinta Studio? Tindakan ini TIDAK " +
                   "bisa dibatalkan. Folder yang akan dihapus:",
            FontWeight = FontWeight.SemiBold,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
        });
        panel.Children.Add(new TextBlock
        {
            Text = detail + $"\n\nTotal: {plan.ExistingCount} folder, " +
                   $"{DataWipe.FormatSize(plan.TotalBytes)}",
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
        });
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        var cancel = new Button { Content = "Batal" };
        cancel.Click += (_, _) => confirm.Close(false);
        var yes = new Button { Content = "Ya, Hapus Semua" };
        yes.Click += (_, _) => confirm.Close(true);
        row.Children.Add(cancel);
        row.Children.Add(yes);
        panel.Children.Add(row);
        confirm.Content = panel;

        var agreed = await confirm.ShowDialog<bool>(this);
        if (!agreed || _wipeResult is null)
            return;

        var result = await Task.Run(() => DataWipe.Execute(plan));

        // Kosongkan state pengaturan/key di memori dialog: berkas
        // sumbernya baru saja dihapus; tanpa ini, tombol Simpan
        // akan menulis ulang data yang baru dibersihkan.
        foreach (var id in _keys.Keys.ToList())
        {
            _keys[id].Clear();
            RebuildKeyRows(id);
        }
        _settings = new AppSettings();
        _settings.EnsureDefaults();
        foreach (var (id, f) in _fields)
        {
            var ps = _settings.For(id);
            f.Base.Text = ps.BaseUrl;
            f.Model.Text = ps.Model;
        }
        _delayBox.Text = _settings.RequestDelaySeconds.ToString();
        AppSecrets.ResetCache();

        var parts = new List<string>();
        if (result.Deleted.Count > 0)
            parts.Add($"{result.Deleted.Count} folder terhapus, " +
                      $"±{DataWipe.FormatSize(result.FreedBytes)} dibebaskan");
        if (result.AlreadyAbsent.Count > 0)
            parts.Add($"{result.AlreadyAbsent.Count} folder memang sudah bersih");
        if (result.Errors.Count > 0)
            parts.Add("GAGAL sebagian: " + string.Join("; ", result.Errors));
        _wipeResult.Text = "Selesai — " + string.Join(". ", parts) +
            ". Tutup aplikasi untuk menyelesaikan.";
        _wipeResult.IsVisible = true;
        if (_wipeButton is not null)
            _wipeButton.IsEnabled = result.Errors.Count > 0;
        RefreshWipeSection();
    }

    private void SaveAndClose()
    {
        foreach (var (id, (baseBox, modelBox)) in _fields)
        {
            var ps = _settings.For(id);
            ps.BaseUrl = (baseBox.Text ?? "").Trim();
            ps.Model = (modelBox.Text ?? "").Trim();
        }
        if (int.TryParse((_delayBox.Text ?? "").Trim(), out var delay))
            _settings.RequestDelaySeconds = delay;
        AppConfig.SaveSettings(_settings);
        foreach (var (id, keys) in _keys)
            AppSecrets.SetKeys(id, keys);
        Close(true);
    }
}
