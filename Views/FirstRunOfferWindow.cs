using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using TintaStudio.Services;

namespace TintaStudio.Views;

/// <summary>Pilihan pengguna pada dialog penawaran lokasi jalan
/// pertama. Nilai bawaan enum (0) = PakaiBawaan: menutup dialog
/// lewat tombol X jendela diperlakukan sebagai pilihan bawaan
/// (penawaran tidak mengintai dua kali).</summary>
public enum FirstRunChoice
{
    PakaiBawaan = 0,
    PilihFolder = 1,
}

/// <summary>
/// Dialog sambutan sekali-saja (v0.7.13): pengguna BARU ditawari
/// memilih tempat tinggal data &amp; model aplikasi sejak awal —
/// seperti memilih folder saat instal — alih-alih otomatis
/// memakai folder profil di disk sistem. Ditampilkan hanya bila
/// FirstRunOffer.ShouldOfferNow() membolehkan (belum ada penanda,
/// belum pernah ditawari, data bawaan nyaris kosong); sesudah
/// salah satu pilihan diambil, tidak pernah tampil lagi. Kedua
/// pilihan BUKAN keputusan permanen — teks dialog menyatakannya
/// persis, dan seksi Lokasi Data &amp; Model di dialog pengaturan
/// selalu tersedia untuk mengubahnya.
/// </summary>
public sealed class FirstRunOfferWindow : Window
{
    public FirstRunOfferWindow()
    {
        Title = "Selamat Datang di Tinta Studio";
        Width = 540;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.Parse("#101216"));
        CanResize = false;

        var panel = new StackPanel { Spacing = 12, Margin = new Thickness(18) };
        panel.Children.Add(new TextBlock
        {
            Text = "Satu pilihan cepat sebelum mulai: di mana data " +
                   "Tinta Studio tinggal?",
            FontWeight = FontWeight.SemiBold,
            FontSize = 15,
            TextWrapping = TextWrapping.Wrap,
        });
        panel.Children.Add(new TextBlock
        {
            Text = "Pengaturan, key API, proyek, dan model OCR Tinta " +
                   "Studio secara bawaan disimpan di folder profil " +
                   "pengguna di disk sistem. Kalau kamu lebih suka " +
                   "menyimpannya di folder atau disk lain (misalnya " +
                   "disk data yang lebih lega), kamu bisa memilihnya " +
                   "sekarang — atau pakai bawaan saja dulu.",
            FontSize = 12.5,
            TextWrapping = TextWrapping.Wrap,
        });
        panel.Children.Add(new TextBlock
        {
            Text = "Pilihan ini tidak mengunci — lokasi bisa diganti " +
                   "kapan pun lewat Pengaturan Key & Model → Lokasi " +
                   "Data & Model.",
            FontSize = 12.5,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.Parse("#2DD4BF")),
        });
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        var bawaan = new Button { Content = "Pakai Bawaan Saja" };
        bawaan.Click += (_, _) => Close(FirstRunChoice.PakaiBawaan);
        var pilih = new Button { Content = "Pilih Folder…" };
        pilih.Classes.Add("primary");
        pilih.Click += (_, _) => Close(FirstRunChoice.PilihFolder);
        row.Children.Add(bawaan);
        row.Children.Add(pilih);
        panel.Children.Add(row);
        Content = panel;
    }
}
