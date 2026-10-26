# Tinta Studio

Aplikasi desktop **original** untuk menerjemahkan & menyunting komik
(manga/manhwa): impor bab dari beragam format, deteksi teks
(JP/CN/KR), bersihkan bubble, terjemahkan (BYOK / lokal), typeset
di kanvas, ekspor bab.

Dibangun dengan prinsip **ATM** (Amati, Tiru, Modifikasi) dari
fitur referensi — semua kode, nama, logo, dan format proyek adalah
karya sendiri; tidak ada aset/identitas produk referensi yang
disalin. Peta fitur lengkap: [docs/FEATURE-MAP.md](docs/FEATURE-MAP.md),
arsitektur: [ARCHITECTURE.md](ARCHITECTURE.md).

## Status: v0.1.0 — Tahap 1 (Fondasi & Impor)

- **Impor multi-format otomatis**: format dikenali dari *magic
  bytes* (bukan ekstensi) — CBZ/ZIP, CBR/RAR, CB7/7z, CBT/TAR,
  PDF (render 150 DPI), EPUB bergambar (urutan spine), folder
  gambar bersarang, dan berkas gambar lepas
  (JPG/PNG/WebP/GIF/BMP/TIFF). Halaman AVIF saat ini terlewati
  dengan laporan jujur (codec AVIF belum tersedia di build
  SkiaSharp yang terbundel — lihat project-state.md).
- Urutan halaman **natural** (1, 2, 10), berkas sampah arsip
  tersaring, `ComicInfo.xml` terbaca, halaman rusak **terlewati
  dan dilaporkan** (tidak menggagalkan impor).
- Normalisasi halaman: rotasi EXIF diluruskan, mode warna
  diseragamkan → salinan kerja PNG (berkas asli tidak diubah).
- **Pustaka bab** lintas sesi + **Penampil** halaman (miniatur +
  navigasi) + format proyek sendiri **`.tinta.json`**.

## Stack

C# / .NET 10 + **Avalonia UI 12.0.5** + **SkiaSharp 3.119.4**
(stack yang sama seperti aplikasi referensinya), SharpCompress
(arsip), PDFtoImage/PDFium (render PDF), LibTiff (dekode TIFF).

## Menjalankan (mode sumber)

Butuh SDK .NET (di VM ini terpasang di `~/.dotnet`):

```bash
export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH
dotnet run --project TintaStudio.csproj            # antarmuka grafis
dotnet run --project TintaStudio.csproj -- --info bab.cbz   # info bab via CLI
```

Tes (xUnit): `dotnet test` — di VM sandbox ini kanal soket
vstest↔testhost terhalang jaringan proksi transparan, jadi tes
dijalankan lewat runner konsol xUnit dalam-proses (lihat
project-state.md); di mesin normal `dotnet test` berjalan biasa.

Publish mandiri (single-file):

```bash
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

## Rencana tahap

1. ✅ Fondasi + impor multi-format + pustaka + penampil + proyek
2. Deteksi teks & OCR JP/CN/KR (model ONNX publik)
3. Pembersihan bubble (blok solid + inpaint LaMA + manual)
4. Terjemahan BYOK (Gemini/Groq/OpenRouter) + lokal (Ollama/LM Studio)
5. Editor typesetting kanvas
6. Ekspor (CBZ/PDF/folder) + packaging .exe
