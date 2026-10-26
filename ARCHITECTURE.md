# Tinta Studio — Arsitektur

Aplikasi desktop original untuk menerjemahkan & menyunting komik (manga/manhwa):
impor bab komik dari beragam format, deteksi teks JP/CN/KR (OCR), bersihkan
bubble, terjemahkan (BYOK / lokal), typeset di kanvas, ekspor bab.

## Prinsip

- **ATM** (Amati, Tiru, Modifikasi): fitur & gaya referensi (Chisa Translator)
  diamati, lalu ditulis ulang dari nol sebagai karya sendiri. Tidak ada kode,
  aset, font, maskot, atau identitas referensi yang disalin. Nama, logo, dan
  format proyek adalah milik sendiri.
- **Framework sama seperti referensinya** (keputusan Ando, 2026-10-26 —
  mengoreksi asumsi awal "sama seperti Gema Studio"): hasil bedah statis
  tarball Linux resmi ChisaTL 3.0.2 membuktikan referensinya dibangun dengan
  **.NET (C#) + Avalonia UI 12.0.5 + SkiaSharp 3.119.4**, diterbitkan sebagai
  berkas tunggal self-contained. Tinta Studio memakai stack yang sama:
  C# + Avalonia + SkiaSharp, publish single-file untuk win-x64 & linux-x64
  langsung dari SDK .NET (tanpa Wine). Disiplin kerja (tahap, tes,
  screenshot, status jujur) tetap mengikuti pola Gema Studio.
- Berkas asli pengguna tidak pernah ditimpa; semua hasil kerja di salinan.

## Stack

- Bahasa & runtime: C# / .NET (SDK dipasang di VM), terbit single-file
  self-contained win-x64 & linux-x64.
- UI: Avalonia UI 12 (XAML, tema gelap + aksen teal #0d9488, pola sidebar
  seperti proyek-proyek kami sebelumnya), logo original dibuat programatis.
- Gambar & kanvas: SkiaSharp (buka/normalisasi/render teks & halaman).
- Arsip: SharpCompress (CBZ/ZIP, CBR/RAR, CB7/7z, CBT/TAR dalam kode
  terkelola — tanpa backend eksternal) + System.IO.Compression.
- PDF: render halaman via pustaka .NET (PDFtoImage/Pdfium atau setara —
  dipilih di Tahap 1, dicatat di project-state).
- AI (tahap lanjut): Microsoft.ML.OnnxRuntime — deteksi teks/bubble & OCR
  model publik (PaddleOCR / manga-ocr), inpaint LaMA; terjemahan BYOK
  (Gemini → Groq → OpenRouter, key milik pengguna) + lokal Ollama /
  LM Studio lewat HTTP.

## Struktur kode (C# / Avalonia)

- `TintaStudio.csproj` + `Program.cs` — titik masuk aplikasi Avalonia;
  mode CLI (`--info <berkas komik>`) lewat argumen sebelum UI dibuka.
- `Services/ComicImport.cs` — impor bab: deteksi format dari magic bytes
  (bukan ekstensi), buka CBZ/ZIP, CBR/RAR, CB7/7z, CBT/TAR, PDF, EPUB
  gambar, folder & gambar lepas; urutan halaman natural; saring berkas
  sampah; ratakan folder bersarang; baca ComicInfo.xml; normalisasi
  halaman (rotasi EXIF, mode warna seragam) ke salinan kerja PNG.
- `Models/Project.cs` — format proyek sendiri `.tinta.json` (daftar bab,
  halaman, blok teks, hasil typeset) — simpan/muat.
- `Services/Library.cs` — pustaka bab lintas berkas (daftar bab yang
  pernah dibuka).
- `Views/` + `ViewModels/` — antarmuka Avalonia (sidebar: Pustaka,
  Penampil, Deteksi & OCR, Bersihkan, Terjemahan, Typesetting, Ekspor —
  halaman aktif menyusul per tahap, sisanya bertanda "segera").
- Tahap lanjut: `Services/Detect.cs` (deteksi+OCR), `Services/Clean.cs`
  (blok solid+inpaint), `Services/Translate.cs` (BYOK+lokal),
  `Views/TypesetView` (editor kanvas), `Services/Export.cs`.

## Rencana tahap

1. **Fondasi + impor multi-format otomatis** + pustaka bab + penampil
   halaman + proyek `.tinta.json`.
2. **Deteksi teks & OCR** JP/CN/KR (model ONNX publik) → daftar blok teks
   per halaman.
3. **Pembersihan bubble**: blok warna solid + inpaint (LaMA) + alat manual.
4. **Terjemahan BYOK** (Gemini/Groq/OpenRouter) + lokal (Ollama/LM Studio),
   konteks satu halaman penuh.
5. **Editor typesetting kanvas**: kotak teks, preset font, teks vertikal,
   outline stroke, spasi huruf, eyedropper.
6. **Ekspor** (CBZ/PDF/folder, pilihan format gambar) + **packaging .exe**.

## Aturan verifikasi (tetap, dari Gema)

Setiap tahap: tes otomatis (headless + xvfb) lolos, UI diperlihatkan lewat
screenshot, error yang ditemukan saat pengecekan diperbaiki di tahap yang
sama lalu diverifikasi ulang; yang dilaporkan adalah hasil final.
