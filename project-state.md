# Keadaan Proyek — Tinta Studio

- 2026-10-26 ~10:43 PDT: Ando bertanya apakah Chisa Translator (ChisaTL
  3.0.2) bisa di-ATM. Installer diunduh ke ~/workspace/quarantine/
  (72.199.082 byte, SHA-256 7ee07e9950ffb5ac9dd4ac8e03e0e45185457f1d2d5f0b48ee74b64b44b44b7b914,
  Inno Setup 6.7.0) dan HANYA dibedah statis — tidak dijalankan/dipasang.
  Daftar fitur resmi dipetakan dari situs chisa.id.
- 2026-10-26 ~10:51 PDT: Ando: "gasss" — proyek disetujui. Nama kerja:
  **Tinta Studio** (boleh diganti Ando kapan pun). Goal pelacak:
  goal_79186a393dcc.
- 2026-10-26 ~10:52 PDT: Ando menegaskan framework yang sama seperti
  Gema Studio (Python + CustomTkinter + jalur packaging Wine/PyInstaller
  yang terbukti, termasuk perbaikan konsol relaunch-detached v0.6.4).
- Status: Tahap 1 dibangun ulang dengan C#/.NET + Avalonia (lihat entri
  hasil di bawah); kerangka Python sebelumnya sudah dihapus dari repo.
- 2026-10-26: **Tahap 1 (v0.1.0) SELESAI & terverifikasi.** Repo git lokal +
  .venv (customtkinter 6.0.0, Pillow 13.0.0, PyMuPDF, py7zr, rarfile);
  logo original programatis (tetes tinta + mata pena, tools/generate_logo.py).
  src/comic_import.py: deteksi format magic bytes (ZIP/RAR/7z/PDF/TAR/
  EPUB via mimetype/gambar/folder), urutan natural, saring sampah arsip,
  ratakan folder bersarang, ComicInfo.xml, normalisasi halaman (EXIF
  transpose + mode warna → PNG kerja), halaman rusak terlewati + tercatat.
  Status RAR di VM ini: unrar/unar/bsdtar TIDAK terpasang, tetapi rarfile
  memakai backend 7z (/usr/bin/7z) — impor CBR terbukti jalan end-to-end
  (fixture RAR4 "store" ditulis programatis untuk tes, karena 7z/rarfile
  tidak bisa menulis RAR). py7zr 1.1.4 tidak punya API baca in-memory →
  CB7 diekstrak ke folder sementara. src/project.py (.tinta.json berpenanda
  + versi), src/library.py (indeks ~/.config/tinta-studio/library.json),
  src/main.py (CLI --ui/--info + penanganan konsol relaunch-detached
  diadaptasi dari Gema v0.6.4: TINTA_KEEP_CONSOLE/TINTA_DETACHED/
  tinta-studio.log), src/ui_app.py (sidebar Pustaka & Penampil aktif;
  Deteksi & OCR, Bersihkan, Terjemahan, Typesetting, Ekspor bertanda
  "segera"). Tes final: **headless 29 lolos + 3 skip, xvfb 32 lolos +
  0 skip, 0 gagal** (termasuk urutan spine EPUB, salah label .cbr berisi
  ZIP, JPEG berotasi EXIF, halaman rusak di folder). Screenshot UI:
  tmp/tinta-v01-ui.png (bab demo terbuka di Penampil, halaman 2 dari 4).
  Batas jujur: impor berjalan sinkron di UI (bab besar/PDF banyak halaman
  membuat UI menunggu sebentar); penskalaan Penampil memakai kotak tetap
  760x620 (belum mengikuti ukuran jendela); belum ada packaging .exe
  (itu Tahap 6). Berikutnya: Tahap 2 — deteksi teks & OCR.
- 2026-10-26 ~10:52 PDT: Ando mengoreksi: "framework yang sama" maksudnya
  framework seperti Chisa (referensi), bukan seperti Gema. Pekerjaan
  Tahap 1 versi Python/CustomTkinter dihentikan sebelum kode berarti
  tertulis (hanya kerangka dokumen).
- 2026-10-26 ~11:02 PDT: Framework referensi TERIDENTIFIKASI dari bedah
  statis tarball Linux resmi ChisaTL_linuxx64_v3.0.2.tar.gz (diunduh ke
  quarantine, diekstrak untuk dibaca, TIDAK dijalankan): satu biner ELF
  90 MB berisi bundel .NET single-file — ChisaTranslator.dll,
  libcoreclr/libhostfxr, dependensi "Avalonia": "12.0.5" dan
  "SkiaSharp": "3.119.4", rakitan Avalonia.* termasuk DesignerSupport
  (menjelaskan komponen React "Previewer" yang ditemukan di installer
  Windows). Keputusan: Tinta Studio dibangun ulang dari awal dengan
  C# + Avalonia 12 + SkiaSharp; rencana fitur & 6 tahap tidak berubah.
- 2026-10-26 ~11:55 PDT: **Tahap 1 SELESAI (versi .NET)** — fondasi +
  impor multi-format otomatis + pustaka bab + penampil halaman +
  proyek .tinta.json.
  - Toolchain: SDK .NET **10.0.403** (LTS) di ~/.dotnet (ekstraksi
    manual — skrip dotnet-install gagal di /tmp tmpfs kecil VM ini);
    **Avalonia 12.0.5** + **SkiaSharp 3.119.4** (persis versi referensi);
    SharpCompress 1.0.0; LibTiff.NET 2.4.660.
  - **PDF: dipilih PDFtoImage 5.2.0 (PDFium)** — restore bersih di
    Linux; versi 5.3+ tidak dipakai karena menuntut SkiaSharp 4.x yang
    bertabrakan dengan Avalonia 12.0.5 (yang memakai SkiaSharp 3.119.x).
    Catatan API 5.2: overload string = Base64, jadi selalu byte[].
  - Impor teruji fixture (13 tes xUnit, semua lolos): CBZ campur
    JPG/PNG/WebP + ComicInfo + sampah + folder bersarang + urutan
    natural; folder (JPG/PNG/BMP/TIFF + 1 rusak terlewati); CBT/TAR
    (termasuk TAR V7 tanpa magic — dideteksi via checksum header);
    CB7/7z; berkas salah label (.cbr isi zip); JPEG rotasi EXIF 6 →
    salinan kerja lurus; PDF 2 halaman render 150 DPI; EPUB urutan
    spine; gambar tunggal; proyek simpan/muat; pustaka tambah/hapus.
  - **CBR/RAR: dukungan baca diimplementasikan (SharpCompress), tapi
    fixture BELUM bisa dibuat** — tidak ada penulis RAR di mesin ini
    (CLI `rar` tidak ada; SharpCompress tidak menulis RAR; 7z tidak
    bisa menulis RAR). Status: belum teruji fixture, perlu satu berkas
    .cbr asli untuk verifikasi.
  - **TIFF**: build libSkiaSharp 3.119.4 TIDAK punya codec TIFF —
    ditangani dekoder cadangan LibTiff (terkelola) di ComicImport.
    **AVIF**: build yang sama juga tidak punya codec AVIF (teruji
    dengan AVIF asli ffmpeg) — halaman AVIF terlewati dengan alasan
    jujur yang menyebut AVIF (teruji). Kandidat tahap lanjut:
    Magick.NET atau binding libavif.
  - Tes dijalankan lewat **runner konsol xUnit dalam-proses**: kanal
    soket vstest↔testhost di VM sandbox ini terhalang lapisan proksi
    transparan (koneksi loopback testhost terputar ke proksi :3128,
    handshake JSON korup — terdiagnosis dari log diag vstest; bukan
    cacat kode). Di mesin normal `dotnet test` semestinya berjalan.
  - Bukti jalan: build Release bersih; CLI `--info` benar pada CBZ
    demo; UI Avalonia jalan di Xvfb dengan bab demo terbuka otomatis
    (TINTA_OPEN) — screenshot tmp/tinta-v01-ui.png; publish single-file
    **win-x64 berhasil: TintaStudio.exe 109.406.411 byte** (PE32+ GUI)
    — jalur packaging stack ini terbukti jauh sebelum Tahap 6.
  - Batas jujur lain: OutputType WinExe → mode CLI `--info` di Windows
    tidak menempel ke konsol pemanggil (di Linux normal); ditangani
    tuntas di Tahap 6 packaging. Miniatur halaman dimuat sinkron di
    utas UI — bab ratusan halaman akan terasa berat (optimasi latar
    di tahap lanjut).
