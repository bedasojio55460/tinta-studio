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

## Status: v0.2.0 — Tahap 2 (Deteksi & OCR)

- **Deteksi teks komik** dengan model ONNX publik (RT-DETR-v2 int8,
  kelas bubble/text_bubble/text_free) + **OCR PaddleOCR** untuk
  **Jepang, Cina, Korea** (dipilih pengguna). Blok teks per halaman:
  kotak batas piksel asli, arah (horizontal/**vertikal** — teks
  vertikal diputar 90° dan di-OCR sebagai garis horizontal),
  teks, keyakinan. Model diunduh terpisah ke
  `~/.cache/tinta-studio/models/` — `tools/fetch_models.sh` atau
  tombol unduh pada kartu Model; katalog + SHA-256:
  [docs/models.md](docs/models.md).
- Blok tersimpan di proyek **`.tinta.json` v2** (berkas v1 tetap
  termuat) per (bab, halaman); halaman **Deteksi & OCR**: overlay
  kotak di atas halaman + daftar blok + teks blok **bisa disunting**
  (tersimpan otomatis). CLI: `--ocr <bab> [--lang ja|ko|ch]`.
- **Bahasa desain Tahap 2** (mengikuti bahasa aplikasi referensi,
  dimodifikasi dengan aksen teal kami): tombol kompak seragam abu
  gelap radius 7 terpusat di `App.axaml`, toolbar atas di Penampil
  & Deteksi (aksi kiri; navigasi chevron – "X/Y" – chevron kanan),
  panel halaman dengan bilah aksen teal pada item terpilih, bar
  status ramping (status kiri, konteks kanan).

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
(arsip), PDFtoImage/PDFium (render PDF), LibTiff (dekode TIFF),
Microsoft.ML.OnnxRuntime (deteksi teks + OCR PaddleOCR — model
publik, unduhan terpisah, lihat docs/models.md).

## Menjalankan (mode sumber)

Butuh SDK .NET (di VM ini terpasang di `~/.dotnet`):

```bash
export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH
dotnet run --project TintaStudio.csproj            # antarmuka grafis
dotnet run --project TintaStudio.csproj -- --info bab.cbz   # info bab via CLI
tools/fetch_models.sh                               # unduh model deteksi/OCR
dotnet run --project TintaStudio.csproj -- --ocr bab.cbz --lang ja  # deteksi+OCR via CLI
```

Tes (xUnit): `dotnet test` — di VM sandbox ini kanal soket
vstest↔testhost terhalang jaringan proksi transparan, jadi tes
dijalankan lewat runner konsol xUnit dalam-proses (lihat
project-state.md); di mesin normal `dotnet test` berjalan biasa.

Publish mandiri (single-file):

```bash
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

(`IncludeNativeLibrariesForSelfExtract` memastikan pustaka native
SkiaSharp/PDFium/OnnxRuntime ikut di dalam .exe tunggal.)

## Rencana tahap

1. ✅ Fondasi + impor multi-format + pustaka + penampil + proyek
2. ✅ Deteksi teks & OCR JP/CN/KR (model ONNX publik) + blok teks di proyek
3. Pembersihan bubble (blok solid + inpaint LaMA + manual)
4. Terjemahan BYOK (Gemini/Groq/OpenRouter) + lokal (Ollama/LM Studio)
5. Editor typesetting kanvas
6. Ekspor (CBZ/PDF/folder) + packaging .exe
