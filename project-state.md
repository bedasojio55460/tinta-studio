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
- 2026-10-26 ~12:08–13:25 PDT: **Tahap 2 (v0.2.0) SELESAI & terverifikasi —
  Deteksi & OCR.**
  - Model (publik, ONNX, CPU; katalog + SHA-256 di docs/models.md,
    unduh via tools/fetch_models.sh / tombol aplikasi ke
    ~/.cache/tinta-studio/models/; runtime Microsoft.ML.OnnxRuntime):
    detektor = **RT-DETR-v2 int8** ogkalu/comic-text-and-bubble-detector
    (`detector-v4-s_int8.onnx`, 11.120.765 byte) — keluarga
    comic-text-detector; varian YOLO murni yang hidup di HF tidak ada
    yang ringan+terawat, ekspor RT-DETR inilah padanan setaranya
    (kelas bubble/text_bubble/text_free, tanpa NMS; NMS pengaman
    IoU 0,6 tetap dipasang). OCR = PaddleOCR ONNX: ja PP-OCRv4 rec
    (cycloneboy, +kamus 4.399), ko PP-OCRv5 mobile rec, ch PP-OCRv5
    server rec (monkt/paddleocr-onnx), + cls arah garis PP-LCNet
    (bukuroo) untuk koreksi 180°.
  - Kontrak model dikalibrasi empiris (bukan asumsi): detektor
    mengharapkan `orig_target_sizes` = **[lebar, tinggi]** (terbalik
    dari konvensi HF); keluaran rec PaddleOCR **sudah softmax**
    (keyakinan = rata-rata prob. maksimum CTC). Teks vertikal:
    potongan lebih tinggi dari lebar diputar 90° dua arah, dipilih
    keyakinan tertinggi (arah yang benar: tepi atas → tepi kiri).
    Blok multi-baris dibagi per garis via proyeksi tinta BERDENSITAS
    (ambang piksel/baris — garis tepi bubble elips menyumbang tinta
    di semua baris, uji ada/tidak tinta tidak cukup). Blok yang
    OCR-nya kosong tidak disimpan (artefak lengkung bubble).
    Urutan baca: manga ja vertikal = kolom kanan→kiri, selain itu
    baris atas→bawah lalu kiri→kanan (heuristik tumpang-tindih).
  - Skema proyek **v2**: ProjectChapter.Pages[] → TextBlock (bbox
    piksel asli, arah, bahasa, teks, keyakinan OCR, skor deteksi);
    berkas v1 tetap termuat (uji khusus). Proyek kerja per bab
    otomatis di ~/.config/tinta-studio/projects/<hash>.tinta.json.
  - UI: halaman Deteksi & OCR aktif (pemilih bahasa, Deteksi
    Halaman Ini / Satu Bab di latar + kemajuan + Batal, overlay
    kotak bernomor di atas halaman + toggle, daftar blok, editor
    teks blok tersimpan otomatis, kartu Model per model + unduh
    latar). CLI `--ocr <bab> [--lang ja|ko|ch]` + `--download-models`.
  - **Polesan tombol & tata letak (umpan balik Ando dari screenshot
    v0.1.0; referensi resmi dipelajari dari tmp/ref-chisa/)**: gaya
    tombol terpusat di App.axaml — kompak seragam, radius 7, isian
    abu gelap #2A2D33 + border #3D434E, hover/pressed/disabled,
    varian primary TEAL #0D9488; toolbar ATAS di Penampil & Deteksi
    (aksi kiri; judul tengah; navigasi chevron–"X/Y"–chevron kanan —
    tombol Sebelumnya/Berikutnya lama di bawah kanvas dipindah ke
    pola ini); item sidebar terpilih & miniatur terpilih ditandai
    bilah aksen teal tipis di tepi kiri; bar status ramping (versi +
    status kiri, konteks bab/format/blok kanan); tema digelapkan ke
    nyaris hitam netral. Screenshot bukti: tmp/tinta-v02-ui.png
    (Deteksi & OCR + overlay + daftar blok) dan
    tmp/tinta-v02-buttons.png (Penampil + toolbar atas).
    **Konvergensi tata letak editor penuh (kanvas ganda
    Original/Translated + panel properti kanan bertab) dijadwalkan
    di Tahap 5 Typesetting** — tahap ini yang diterapkan adalah
    bahasa tombol + penempatan toolbar.
  - Hasil fixture sintetis (teks diketahui, model asli): JA
    horizontal 「こんにちは」 terbaca persis (keyakinan 1,00), JA
    vertikal 「ありがとう」 persis (0,86; arah terdeteksi vertikal),
    KO 「안녕하세요」 persis (1,00), ZH 「谢谢你」 persis (1,00);
    bubble dua baris terbaca sebagai dua garis tergabung.
  - Tes: **23 lolos, 0 gagal, 0 skip** via runner konsol xUnit
    dalam-proses (13 lama tetap hijau + 10 baru: pipeline ja/ko/ch,
    dua baris, pemisah garis, urutan baca ×2, proyek v2 round-trip,
    muat v1, CLI --ocr pada CBZ fixture). Build Release bersih;
    publish win-x64 single-file ulang: **TintaStudio.exe
    124.375.633 byte** (PE32+ GUI; kini termasuk pustaka native
    OnnxRuntime — IncludeNativeLibrariesForSelfExtract=true agar
    .exe benar-benar mandiri).
  - Batas jujur: (1) OCR satu bahasa per bab per jalan (pilihan
    pengguna) — halaman campuran bahasa perlu jalan terpisah;
    (2) model rec adalah model SATU BARIS — blok multi-baris
    ditangani pemisah garis proyeksi tinta, bukan model deteksi
    garis; teks miring/berputar bebas di luar 0/90/180° belum
    ditangani; (3) detektor kadang menandai lengkung bubble sebagai
    teks — disaring lewat hasil OCR kosong; (4) kualitas pada
    halaman manga asli (screentone, teks kecil, efek suara) belum
    diukur — fixture masih sintetis bersih; (5) CBR & AVIF: batas
    Tahap 1 yang sama. Berikutnya: Tahap 3 — Pembersihan bubble.
- 2026-10-26 ~17:21–18:0x PDT: **Tahap 3 (v0.3.0) SELESAI &
  terverifikasi — Pembersihan Bubble.**
  - **Model inpaint**: LaMA yang di-fine-tune untuk manga dari
    [mayocream/lama-manga-onnx](https://huggingface.co/mayocream/lama-manga-onnx)
    (`lama-manga.onnx`, asal dreMaz/AnimeMangaInpainting, ekspor
    Carve-Photos/lama; Apache-2.0) — 207.482.644 byte, SHA-256
    `4512adab…f02a4`, cache `inpaint-lama.onnx`. Kontrak
    **dikalibrasi empiris**: `image` [B,3,512,512] 0..1 + `mask`
    [B,1,512,512] (1 = di-inpaint) → `output` 0..1, ukuran tetap;
    uji kalibrasi: teks hitam ter-mask terisi 0,9998 putih, luar
    mask utuh. Alternatif Carve big-LaMA fp32 dicatat di
    docs/models.md.
  - **Cara kerja** (`Services/Inpaint.cs` + `Services/Clean.cs`):
    inpaint per potongan blok+margin (24–160 px); mask = tinta
    gelap di dalam kotak blok (menyusut 2 px dari tepi kotak,
    dilasi 3 px); hasil ditempel hanya di dalam mask bertepi
    lembut (expand 2 + blur 2) — piksel di luar mask persis asli
    (teruji piksel-per-piksel). Solid = isi kotak blok, warna
    sampel otomatis interior bubble (fixture: #FEFEFE) atau manual
    (eyedropper/hex). Manual = kuas warna + penghapus (salin
    kembali cakram piksel asli). **Non-destruktif**: gambar bersih
    selalu dibangun ulang dari asli + daftar operasi; Urungkan
    membuang operasi/sapuan ber-Seq terbesar. Cache: gambar dasar
    operasi per (bab, halaman) bertanda tangan operasi di
    ~/.config/tinta-studio/cleaned/; sapuan dikomposit di memori
    di atasnya (sunting sapuan tidak memicu inpaint ulang).
  - **Skema proyek v3**: ProjectPage.CleanOps (BlockId, Mode,
    FillColor, Seq, AppliedUtc) + ManualStrokes (Erased, Color,
    Radius, Points, Seq) + CleanSeq. Berkas v1/v2 tetap termuat
    (uji khusus v2). Assertion versi tes Tahap 2 disesuaikan ke
    CurrentVersion.
  - **UI**: halaman Bersihkan aktif — toolbar atas pola Tahap 2
    (ComboBox mode, Bersihkan Halaman Ini primer, Satu Bab, Batal
    + kemajuan; judul tengah; kanan: toggle Asli, toggle Kotak,
    chevron–"X/Y"–chevron), bar alat (Penetes/Kuas/Penghapus,
    Slider ukuran, swatch + hex, Sampel otomatis, Urungkan),
    info operasi; panel kanan: kartu MODEL INPAINT (Siap/Unduh)
    + daftar blok (mode per blok + Terapkan, lencana ✓ di overlay
    untuk blok bersih). Sapuan dicatat saat pointer dilepas
    (bukan pratinjau langsung). Footer "v0.3.0 — Pembersihan
    Bubble". CLI `--clean <bab> [--mode solid|inpaint]
    [--out <folder>] [--lang]` — deteksi dulu bila proyek belum
    punya blok; operasi dicatat ke proyek; `--info`/`--ocr` utuh.
  - **Hasil fixture** (interior bubble JA horizontal, densitas
    tinta luminansi<160): Solid 0,126 → **0,000**; Inpaint
    0,126 → **0,000**; CLI --clean solid 0,126 → 0,000; diagnosis
    jalur CleanStore pada bab demo: tinta blok 993/4466 → 0.
    Keluaran CLI bab demo: bubble kosong bersih, teks caption
    terhapus (sisa bayangan amat samar pada teks rata besar —
    artefak LaMA yang jujur, lihat tmp/clean-demo-out/).
  - **Tes: 31 lolos, 0 gagal, 0 skip** (runner konsol xUnit
    dalam-proses; 13 Tahap 1 + 10 Tahap 2 tetap hijau + 8 baru:
    solid, sampel warna, inpaint+luar-mask-utuh, urungkan,
    kuas→penghapus pulih persis, proyek v3 round-trip bangun
    ulang identik (hash PNG sama), muat v2, CLI --clean).
    Build Release bersih; publish win-x64 single-file ulang:
    **TintaStudio.exe 124.441.169 byte** (PE32+ GUI; model tetap
    unduhan terpisah). Screenshot bukti: tmp/tinta-v03-ui.png
    (halaman demo SESUDAH inpaint via UI + daftar blok & kartu
    model). Catatan: bangun gambar inpaint dingin di VM ini
    lambat (±47 dtk/2 blok saat CPU berbagi; cache membuatnya
    instan sesudahnya); klik sintetis Xvfb tidak menembus toggle
    "Asli", jadi screenshot sebelum/sesudah terpisah tidak
    diambil — perbandingan ditopang angka densitas + keluaran CLI.
  - Batas jujur: (1) kualitas inpaint pada manga asli berscreentone
    BELUM diukur — fixture masih sintetis bersih; pada teks rata
    besar di demo terlihat sisa bayangan samar; (2) biaya CPU
    inpaint nyata (±10–25 dtk/blok di VM ini) — pembersihan satu
    bab berjalan di latar + bisa dibatalkan; (3) sapuan manual
    diterapkan saat pointer dilepas (tanpa pratinjau garis
    langsung); (4) mask inpaint hanya dari tinta di dalam kotak
    blok — teks yang meluber keluar kotak deteksi tidak ikut
    terhapus; (5) CBR & AVIF: batas Tahap 1 yang sama.
    Berikutnya: Tahap 4 — Terjemahan BYOK + lokal.
- 2026-10-26 ~21:5x–2026-10-27 ~05:2x PDT: **Tahap 4 (v0.4.0)
  SELESAI — Terjemahan dua mode (otomatis + manual), BYOK
  multi-key + rotasi, lokal, Poles AI.**
  - **Layanan** (`Services/Translate.cs`): lima penyedia —
    Gemini (REST generateContent, header x-goog-api-key,
    `generativelanguage.googleapis.com/v1beta`,
    `gemini-2.5-flash`), Groq (`api.groq.com/openai/v1`,
    `llama-3.3-70b-versatile`), OpenRouter (`openrouter.ai/api/v1`,
    `meta-llama/llama-3.3-70b-instruct:free`), Ollama
    (`localhost:11434/v1`, `llama3.1`), LM Studio
    (`localhost:1234/v1`). Base URL/model per penyedia bisa
    diatur. Prompt konteks **satu halaman penuh** (blok bernomor
    urut baca Tahap 2; suhu 0,3; wajib JSON ketat
    `[{"id":n,"text":...}]`); parsing toleran pagar kode + coba
    ulang sekali khusus nomor yang hilang; sisa gagal ditandai per
    blok beralasan. Bahasa tujuan id (bawaan) / en.
  - **Multi-key + rotasi** (`Services/AppSettings.cs`): keys.json
    = DAFTAR key per penyedia (0600; cooldown ikut disimpan
    sebagai cap waktu di keys.json agar bertahan antar-jalan;
    status buruk hanya di memori sesi). 429/RESOURCE_EXHAUSTED →
    cooldown (Retry-After didahulukan, selain itu 60 dtk) + coba
    key berikutnya; 401/403 → key buruk & dilewati; galat lain
    dilaporkan apa adanya tanpa rotasi. Semua key habis →
    AllKeysExhaustedException ("Semua key … kena limit").
    **Tunggu & lanjut**: mode bab menjeda sampai key paling awal
    pulih (callback hitung mundur "Menunggu limit pulih… ±N dtk —
    key #k" di UI/CLI), Batal tetap berhenti bersih. **Jeda antar
    halaman** dapat diatur (pengaturan, bawaan 2 dtk, 0 = mati),
    berlaku antar halaman & sebelum coba-ulang; durasi
    dapat disuntik untuk tes.
  - **Bab**: proyek disimpan sesudah SETIAP halaman; blok yang
    sudah berterjemahan dilewati (ringkasan menyebut jumlah
    dilewati); blok gagal dicoba lagi. **Per blok**: tombol
    "Terjemahkan blok ini" memakai konteks halamannya, mengisi
    hanya blok itu.
  - **Dua mode**: Manual penuh tanpa key (ketik di kolom
    terjemahan → tersimpan otomatis, cap "Manual", dihitung sudah
    diterjemahkan, tak pernah memicu galat key). **Poles AI**
    (per blok/halaman/bab): prompt kirim SUMBER + terjemahan saat
    ini, hasil menggantikan terjemahan, versi lama disimpan satu
    tingkat di `TranslationBackup` (+ cap cadangan), tombol
    "Urungkan Poles"; cap "… • poles". Jalan poles bab melewati
    blok kosong + blok "Manual".
  - **Skema proyek v4**: blok + `Translation`, `TranslatedBy`
    (mis. "Groq • llama-3.3-70b-versatile • key #2" / "Manual"),
    `TranslatedAt`, `TranslationError`, `TranslationBackup`,
    `TranslationBackupBy`. v1/v2/v3 tetap termuat (teruji v3).
    Pengaturan di `~/.config/tinta-studio/` (settings.json +
    keys.json terpisah 0600; dialog menampilkan key bermasker
    "Key #n — ••••{4 terakhir}", status "N key • aktif #k").
    Key tidak pernah masuk proyek/git/log/CLI (teruji: berkas
    proyek tak mengandung nilai dummy key).
  - **UI + CLI**: halaman Terjemahan aktif (bahasa desain Tahap
    2/3; toolbar penyedia/bahasa/Pengaturan/Terjemahkan
    Halaman/Bab/Poles Halaman/Bab/Batal + kemajuan; daftar blok
    dua kolom yang bisa disunting + tombol per blok; teks
    petunjuk dua mode). CLI `--translate <bab> [--provider …]
    [--target id|en]` — OCR ja dulu bila belum ada blok, simpan
    per halaman, ringkasan per halaman + total (termasuk
    dilewati); tanpa key BYOK → pesan jelas, kode keluar 2.
    Footer/VERSION → 0.4.0.
  - **Tes: 53 total — 52 lolos, 0 gagal, 1 skip jujur** (31 lama
    tetap hijau + 22 baru: bentuk permintaan OpenAI-kompatibel &
    Gemini + pemetaan/cap/round-trip v4/key-tak-masuk-proyek,
    pagar kode, coba-ulang nomor hilang, respons rusak, rotasi
    429 dua penyedia, semua-429, 401-buruk, 400-tanpa-rotasi,
    key-kosong, bab lewati+simpan-per-halaman, tunggu-lanjut
    Retry-After (cooldown suntik 30 dtk, selesai <10 dtk), jeda
    antar halaman & coba-ulang terukur ≥0,85 dtk, blok tunggal
    konteks penuh, poles+backup+undo persis, poles lewati
    kosong, poles bab lewati Manual, muat v3, CLI key-kosong).
    Build Release bersih; publish win-x64 single-file ulang:
    **TintaStudio.exe 124.547.665 byte** (PE32+ GUI). Screenshot
    bukti: tmp/tinta-v04-ui.png — halaman Terjemahan bab demo
    dengan kolom sumber → terjemahan TERISI dari jalan stub
    lokal nyata (OCR asli + TranslateService; こんにちは→"Halo!",
    ありがとう→"Terima kasih!").
  - **Catatan lingkungan penting**: kebijakan jaringan sandbox
    VM ini (Direct network protocols: other_tcp = Deny) menolak
    koneksi TCP keluar dari PROSES .NET — bahkan ke 127.0.0.1 —
    terbukti dengan probe (TcpClient mentah dibalas banner
    penolakan; python/curl tidak terpengaruh). Karena itu stub
    tes diwujudkan sebagai **HttpMessageHandler dalam-proses**
    yang menerima HttpRequestMessage persis (URL/header/badan
    divalidasi sama) menggantikan HttpListener, dan tes CLI
    loopback di-skip bersyarat probe (di VM ini skip; di mesin
    normal jalan). Aplikasi di PC pengguna tidak terpengaruh —
    HttpClient standar.
  - Batas jujur: (1) kualitas terjemahan penyedia ASLI belum
    diuji — menunggu Ando dengan key-nya di PC-nya; di VM semua
    via stub; (2) CLI --translate ujung-ke-ujung via soket belum
    terverifikasi DI VM ini karena batas di atas (jalur layanan
    yang sama terverifikasi penuh via stub handler; CLI teruji
    jalur key-kosong); (3) cooldown antar-jalan disimpan, status
    key buruk tidak (sesi saja — disengaja, key bisa pulih/diganti
    di antara jalan); (4) poles menyimpan cadangan hanya satu
    tingkat; (5) CBR & AVIF: batas Tahap 1 yang sama.
    Berikutnya: Tahap 5 — Editor typesetting.
