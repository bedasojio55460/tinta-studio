using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
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
    private readonly AppSettings _settings;
    private readonly Dictionary<string, List<string>> _keys = new();
    private readonly Dictionary<string, StackPanel> _keyRows = new();
    private readonly Dictionary<string, TextBlock> _keyStatus = new();
    private readonly Dictionary<string, (TextBox Base, TextBox Model)> _fields = new();
    private readonly TextBox _delayBox;

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
        return card;
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
