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
    latar). CLI `--ocr <bab> [--lang ja|ko|ch|en]` + `--download-models`.
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
- 2026-10-27 ~05:3x–07:0x PDT: **Tahap 5 (v0.5.0) SELESAI &
  terverifikasi independen.** Typesetting: teks terjemahan
  DIGAMBAR ke halaman bersih di dalam kotak bloknya → halaman
  final yang bisa dibaca (dasar render = gambar bersih Tahap 3
  via CleanStore, atau halaman asli bila belum dibersihkan).
  - Model: proyek skema **v5** — tiap blok + `TypesetStyle`
    (null = belum di-typeset): FontFamily (font SISTEM saja,
    tanpa font tertanam berlisensi), FontSize (0 = otomatis),
    Bold, Italic, ColorHex, StrokeHex, StrokeWidth, Align
    (Left/Center/Right), LineSpacing, LetterSpacing, Vertical,
    Rotation (0/90/180/270), geometri Dx/Dy/Dw/Dh, PresetName.
    Berkas v1–v4 tetap termuat (teruji literal v4: Typeset null).
  - Mesin render `Services/Typeset.cs` (SkiaSharp): bungkus kata
    per baris (kata terlalu lebar dipecah per karakter); lebar
    diukur per glif + LetterSpacing; **pas-otomatis** = pencarian
    biner ukuran terbesar yang muat di kotak render (FontSize>0
    dipakai apa adanya, boleh meluber sesuai paksaan pengguna);
    gambar per glif: stroke (Round join) dulu lalu Fill; vertikal
    gaya CJK = karakter ditumpuk per kolom, kolom kanan → kiri;
    rotasi via transform kanvas di pusat kotak; konten terpusat
    vertikal. Blok tanpa terjemahan dilewati; blok tanpa Typeset
    eksplisit memakai bawaan (sans aman, otomatis, tengah, hitam).
    Render non-destruktif (bitmap dasar tak berubah — teruji hash).
  - Preset bawaan (kumpulan properti, bukan berkas font): Dialog,
    Teriak (bold + stroke putih 3), Bisik (italic #444444),
    Narasi (italic). `AutoTypesetPage/Chapter` mengisi preset
    Dialog ke blok berterjemahan yang belum punya Typeset saja.
  - UI halaman **Typesetting** (menu aktif, footer "v0.5.0 —
    Typesetting"): toolbar "Terapkan Otomatis Halaman Ini / Satu
    Bab" + toggle Asli/Bersih + chevron X/Y; kiri daftar halaman;
    tengah kanvas FINAL (bingkai teal per blok, terpilih tebal +
    lencana #n); kanan panel properti seksi kapital kecil (TEKS
    TERJEMAHAN, FONT + ukuran otomatis + B/I, WARNA & GARIS TEPI,
    TATA LETAK + Vertikal + Rotasi, POSISI & UKURAN KOTAK,
    PRESET). Klik kanvas = hit-test kotak render; **seret =
    pindah blok** (pratinjau bingkai; Dx/Dy disimpan saat lepas).
    Semua perubahan langsung tergambar ulang & tersimpan.
    Bug ditemukan saat pengecekan screenshot pertama: tombol
    "Tengah"/"Kanan" di baris perataan kehilangan Grid.Column
    sehingga menumpuk di kolom 0 — DIPERBAIKI di tahap ini juga
    dan screenshot ulang membuktikan Kiri/Tengah(aktif)/Kanan
    tampil benar.
  - CLI `--render <bab> [--out <folder>]`: halaman bersih +
    typeset tersimpan (bawaan diterapkan dulu ke yang belum) →
    PNG per halaman + ringkasan; teruji ujung-ke-ujung pada CBZ
    fixture OCR asli (blok berterjemahan ber-ink di kotaknya,
    typeset otomatis tersimpan ke proyek).
  - Tes: **64 total — 63 lolos, 0 gagal, 1 skip** (skip lama
    bersyarat sandbox: CLI terjemahan loopback; 53 tes lama =
    52 lolos + 1 skip, TANPA regresi) — 11 tes baru TypesetTests
    semua lolos: render "Halo!" densitas tinta di kotak
    0→>0,02 & 0 piksel berubah di luar kotak+margin 3 & hash
    dasar utuh; auto-fit teks panjang tetap di dalam kotak
    (0 piksel di luar); vertikal menghasilkan tinta jauh lebih
    tinggi dari lebar; stroke merah >40 piksel hadir; Dx=80
    menggeser pusat massa tinta 80±8 px; preset Teriak tercatat
    bold+stroke & tergambar; auto-typeset hanya mengisi yang
    berterjemahan & belum punya gaya; round-trip v5 identik
    termasuk hash render; muat v4; CLI --render pada fixture.
    (tmp/xunit-results-v05.xml; satu jalan menengah sempat
    menggagalkan 1 tes lama Tahap 4 yang sensitif waktu
    (tunggu-pulih Retry-After) di bawah beban CPU penuh —
    terkonfirmasi flake: lolos 3/3 sendirian dan suite penuh
    hijau lagi di jalan ulang pada kode final; jalur
    terjemahan tidak diubah tahap ini.) Build Release bersih 0/0.
    Publish win-x64 single-file: **TintaStudio.exe 124.633.681
    byte** (PE32+ GUI; v0.4.0: 124.547.665). Bukti visual:
    tmp/tinta-v05-ui.png (kanvas final "Halo!"/"Terima kasih!"
    ter-render di bubble bersih + panel properti) dan
    tmp/render-demo/page-001.png (keluaran --render).
  - **Di luar cakupan (jujur)**: editor poligon bubble vektor
    ala referensi DITUNDA — detektor menghasilkan kotak teks
    persegi, bukan poligon bubble; penyesuaian bentuk lewat
    geometri kotak (Dx/Dy/Dw/Dh) dulu (FEATURE-MAP diperbarui).
  - Batas jujur: (1) kualitas visual pada manga ASLI belum
    diukur — semua bukti di fixture sintetis bersih; kotak OCR
    yang sempit membuat kata panjang dipecah per karakter oleh
    auto-fit (pengguna bisa melebarkan kotak via Δ Lebar/seret);
    (2) font yang tersedia mengikuti sistem pengguna (di VM:
    DejaVu; di Windows: font terpasang pengguna); (3) CBR & AVIF:
    batas Tahap 1 yang sama. Berikutnya: Tahap 6 — Ekspor
    (CBZ/PDF/folder dari halaman final) + packaging akhir.
- 2026-10-27: **v0.5.1 — perbaikan dalam-tahap Tahap 5 (temuan
  verifikasi agen utama): kotak render blok vertikal.** Pada
  tmp/render-demo/page-001.png versi 0.5.0, blok OCR vertikal
  fixture (745,245; 72×326, ありがとう → "Terima kasih!") yang
  di-typeset horizontal terbungkus per karakter ("Teri/ma/kasi/h!")
  dan glifnya meluber keluar bubble: kotak teks OCR memang
  sesempit kolom teks vertikalnya, dan geometri bubble tidak
  tersimpan (pipeline deteksi membuang kelas bubble), jadi
  perbaikan dilakukan di lapisan typeset:
  - `AutoTypesetPage/ProjectChapter` kini menerima lebar halaman
    (pemanggil UI memakai ComicPage.Width bab terbuka; CLI
    --render memakai lebar halaman bab) dan untuk blok
    `Direction == Vertical` yang gaya barunya horizontal,
    **melebarkan kotak render simetris di sekitar pusat blok**
    sebelum pas-otomatis: lebar sasaran = `min(lebar halaman,
    max(lebar blok, round(tinggi blok × 0,55)))`, X efektif
    dijepit ke dalam halaman (margin 4). Hasilnya dicatat sebagai
    geometri tersimpan **Dw/Dx** pada Typeset blok (fixture:
    Dw=107, Dx=−53 → kotak efektif 179×326 di x=692). Blok
    horizontal tidak berubah; blok vertikal yang di-typeset
    vertikal tidak diubah; blok bergaya eksplisit tidak ditimpa
    (aturan terapkan-otomatis yang lama).
  - **Pas-otomatis mengutamakan kata utuh**: pelebaran saja
    belum cukup — pada kotak 179 px, pencarian biner lama tetap
    memilih ukuran besar yang "muat" berkat penggalan
    ("Terim/a/kasih!"). `FitLayout` kini mencari dulu ukuran
    terbesar yang muat TANPA memecah kata (`Layout.SplitWord`
    menandai tata letak berisi penggalan; aturan pemecah kata di
    BuildLayout TIDAK diubah), dan hanya jatuh ke pencarian lama
    bila tak ada ukuran yang memuat semua kata utuh (mis. satu
    kata memang lebih lebar dari kotak). Jalur vertikal & ukuran
    manual tidak terpengaruh.
  - Tes: **66 total — 65 lolos, 0 gagal, 1 skip** (skip lama
    bersyarat sandbox yang sama; 64 tes lama = 63 lolos + 1 skip,
    TANPA regresi) + 2 baru di TypesetTests: blok vertikal 72×326
    "Terima kasih!" → Dw tercatat > 0 dan lebar efektif tepat 179,
    render 0 piksel di luar kotak efektif + margin 3, kotak tinta
    **174×106** (lebar tinta 174 > 72 — terbungkus per kata);
    pembanding horizontal: Dw/Dx tetap 0.
    (tmp/xunit-results-v051.xml.) Bukti dibangun ulang:
    tmp/render-demo/page-001.png kini menampilkan "Terima" /
    "kasih!" sebagai kata utuh di dalam bubble vertikal, dan
    screenshot UI tmp/tinta-v05-ui.png diambil ulang (footer
    v0.5.1, kanvas menampilkan "Terima/kasih!" utuh di kotak
    render yang dilebarkan). Build Release bersih 0/0; publish
    win-x64 single-file ulang: **TintaStudio.exe 124.633.681
    byte** (PE32+ GUI; jumlah byte kebetulan sama dengan v0.5.0
    — isi terverifikasi berbeda: simbol WidenVerticalSourceBox
    + string footer v0.5.1 ada di dalam exe, string v0.5.0 tidak).
- 2026-10-27 ~01:11–01:21 PDT: **v0.5.2 — arah typeset mengikuti
  arah sumber (permintaan Ando).** Ando menunjuk render demo
  v0.5.1: "Terima kasih!" di bubble vertikal harusnya **lurus ke
  bawah** mengikuti arah teks Jepangnya, dan menanyakan apakah
  arahnya bisa diedit. Jawaban + perbaikan:
  - Manual memang sudah bisa: centang **Vertikal** per blok di
    panel properti Typesetting (huruf tegak menumpuk ke bawah,
    kolom berikutnya ke kiri).
  - **Terapkan Otomatis diganti pakemnya**: blok
    `Direction == Vertical` kini mendapat gaya `Vertical = true`
    (preset Dialog + vertikal), kotak asli tidak diubah
    (Dw/Dx = 0). Fungsi pelebaran v0.5.1
    (`WidenVerticalSourceBox`) DIHAPUS; pas-otomatis kata-utuh
    untuk horizontal dipertahankan.
  - **Pas-otomatis vertikal mengutamakan satu kolom**:
    `SearchFit` menerima batas `maxColumns`; `FitLayout`
    vertikal mencoba dulu ukuran terbesar yang muat dalam SATU
    kolom dan memakainya bila ≥ 12 px — tanpa ini, pencarian
    lama memilih font lebih besar dalam DUA kolom ("Terima" di
    kolom kanan, "kasih!" di kolom kiri) yang untuk teks Latin
    membingungkan. Teks terlalu panjang tetap jatuh ke banyak
    kolom. Tes blok vertikal ditulis ulang: gaya vertikal,
    Dw/Dx = 0, 0 piksel di luar kotak asli + margin 3, tinta
    SATU kolom (lebar < separuh kotak, tinggi > separuh kotak).
  - Tes: **66 total — 65 lolos, 0 gagal, 1 skip** (skip lama
    bersyarat sandbox yang sama), tmp/xunit-results-v052.xml.
    Build Release bersih 0/0; publish win-x64 single-file ulang:
    **TintaStudio.exe 124.633.681 byte** (lagi-lagi jumlah byte
    sama — isi baru: footer "v0.5.2").
  - Bukti end-to-end di luar suite: halaman uji PIL buatan agen
    utama (bubble horizontal こんにちは + bubble vertikal
    ありがとう) → CLI --ocr membaca 2 blok persis (vertikal
    78×346, ocr 0,83) → --clean solid → terjemahan diisi →
    --render: proyek tersimpan menunjukkan blok vertikal
    `vertical: true, dw: 0`; tmp/render-demo-v052/page-001.png
    menampilkan "Terima kasih!" SATU kolom lurus ke bawah di
    dalam bubble vertikal (terverifikasi visual agen utama;
    potongan zoom: tmp/render-demo-v052/crop-vertikal.png).
  - Catatan: blok yang SUDAH terlanjur bergaya horizontal oleh
    v0.5.1 tidak ditimpa Terapkan Otomatis (aturan lama: gaya
    eksplisit dihormati) — pengguna mematikan Vertikal/hapus
    gaya dulu, atau blok baru otomatis benar. Tersisa Tahap 6.
- 2026-10-27: **Tahap 6 (v0.6.0) SELESAI — Ekspor + Packaging
  (TAHAP TERAKHIR rencana 6 tahap).**
  - **Layanan ekspor `Services/Export.cs`** (`ExportService.
    ExportChapter`): mengekspor bab terbuka menjadi halaman
    FINAL lewat jalur yang SAMA dengan `--render`
    (`TypesetService.BuildFinalPageBitmap`: gambar bersih
    Tahap 3 + typeset Tahap 5; typeset bawaan diterapkan dulu
    ke blok berterjemahan yang belum di-typeset; halaman tanpa
    blok/terjemahan diekspor apa adanya) ke tiga bentuk:
    **CBZ** (ZIP `page-001…` + ComicInfo.xml — disalin persis
    dari sumber bila sumbernya arsip ZIP, selain itu dibuat
    dari metadata judul/seri bab), **PDF** (satu halaman per
    gambar, `SKDocument.CreatePdf` SkiaSharp — TANPA dependensi
    baru), **Folder** (satu berkas gambar per halaman). Format
    gambar pilihan **Asli** (format sumber tiap halaman;
    yang tak bisa dikodekan ulang — mis. halaman PDF — jatuh
    ke PNG) / **JPG** / **PNG** / **WebP**, kualitas JPG/WebP
    bisa diatur (bawaan 90). Keluaran TIDAK menimpa diam-diam:
    berkas yang sudah ada diberi akhiran " (2)", " (3)", dst.
    (`ExportService.UniqueFilePath`; teruji).
  - **UI halaman "Ekspor"** (menggantikan tombol nonaktif
    "Ekspor (segera)" di sidebar): toolbar pola halaman lain
    (judul bab + chevron–X/Y–chevron), panel halaman kiri,
    kartu pilihan — toggle CBZ/PDF/Folder, ComboBox format
    gambar (nonaktif untuk PDF), NumericUpDown kualitas,
    folder tujuan (TextBox + dialog "Pilih Folder…"), petunjuk
    nama hasil, tombol primary "Ekspor Bab", teks hasil
    (jalur + ukuran). Bug kecil tertangkap screenshot pertama
    & diperbaiki di tahap ini juga: petunjuk nama berkas diam
    di nilai "bab" karena `ExportDestinationHint` tidak
    di-Raise saat bab berubah — kini di-Raise dari setter
    CurrentChapter + ShowExport; screenshot ulang membuktikan
    "Bab Demo — Tiga Halaman.cbz". Bukti visual:
    tmp/tinta-v06-ui.png.
  - **CLI `--export <bab> [--format cbz|pdf|folder]
    [--img asli|jpg|png|webp] [--quality n] [--out <jalur>]`**
    mengikuti pola RunRender (OCR ja dulu bila proyek tanpa
    blok; proyek disimpan sesudah ekspor; ringkasan
    "Ekspor selesai: … — N halaman (bentuk) → jalur (ukuran)";
    exit 2 argumen buruk, 3 model hilang, 1 gagal lain).
    Semantik --out: Folder = folder tujuan itu sendiri;
    CBZ/PDF = jalur berkas bila berakhiran .cbz/.pdf, selain
    itu folder yang diisi <namafile>.cbz/.pdf; bawaan =
    ./tinta-export/.
  - **Packaging Windows**: `ConsoleAttach.TryAttachParentConsole`
    (ConsoleAttach.cs) — P/Invoke kernel32
    `AttachConsole(ATTACH_PARENT_PROCESS)` HANYA bila ada
    argumen CLI & platform Windows, lalu aliran standar dibuka
    ulang; di platform lain no-op total, mode GUI tak
    terpengaruh. Kode terkompilasi & CLI terverifikasi jalan
    di Linux (uji asap + tes CLI di bawah); perilaku menempel
    konsol di Windows asli baru terbukti saat Ando mencobanya
    di PC (batas jujur yang sama seperti tahap sebelumnya).
  - Tes: **73 total — 72 lolos, 0 gagal, 1 skip** (skip lama
    bersyarat sandbox yang sama; 66 tes lama TANPA regresi &
    tanpa diubah) + 7 baru di `TintaStudio.Tests/ExportTests.cs`:
    CBZ JPG (entri ComicInfo + 3 halaman, magic FF D8, entri
    pertama terdekode 1000×1400); CBZ Asli (entri .png, magic
    PNG); CBZ dua kali (kedua ber-akhiran " (2)", tak menimpa);
    Folder WebP (3 berkas, magic RIFF/WEBP, ukuran benar);
    PDF (awalan %PDF, "/Count 3", 3 MediaBox); halaman
    ber-typeset ikut tergambar di ekspor folder (densitas
    tinta di kotak blok > 0,02); CLI --export pada fixture
    (folder PNG + "Ekspor selesai"). tmp/xunit-results-v06.xml.
    Build Release bersih 0/0; publish win-x64 single-file:
    **TintaStudio.exe 124.670.545 byte** (PE32+ GUI;
    v0.5.2: 124.633.681).
  - Batas jujur: (1) ekspor memakai render final yang sama
    dengan --render, bukan jalur terpisah — cacat render apa
    pun berlaku sama di ekspor; (2) AVIF tetap tidak bisa
    didekode (halaman AVIF terlewati sejak impor Tahap 1);
    (3) CBR baca tetap terimplementasi tapi BELUM teruji
    fixture (tidak ada penulis RAR di VM); (4) PDF hasil =
    gambar per halaman (tanpa lapisan teks/OCR tersembunyi);
    kualitas PDF mengikuti bitmap halaman; (5) kualitas
    visual manga asli belum diukur (fixture sintetis bersih).
  - Dengan ini rencana 6 tahap Tinta Studio TUNTAS
    (v0.1.0 → v0.6.0). Yang masih menggantung di luar rencana:
    uji .exe di Windows asli oleh Ando (termasuk konsol CLI
    menempel + terjemahan penyedia asli), satu berkas .cbr
    asli untuk verifikasi baca RAR, dan editor poligon bubble
    vektor (ditunda sejak Tahap 5).
- 2026-10-27 ~02:14–02:27 PDT: **Uji berkas ASLI dari internet
  (usul Ando) + v0.6.1.** Tiga temuan, semuanya di luar suite
  (berkas uji di tmp/samples/ & /tmp, tidak dikomit):
  - **CBR ASLI TERBUKTI.** *Four Color #856 — Buffalo Bill Jr.*
    dari Internet Archive (identifier BuffaloBillJr.Comic0856,
    lisensi public domain; RAR v2.0 asli 24.791.307 byte):
    `--info` → Format "RAR (CBR)", **36 halaman** terbaca semua
    (folder bersarang di dalam arsip diratakan benar), 1
    terlewati = berkas .SFV (bukan gambar, alasan eksplisit) —
    persis perilaku rancangan. `--ocr --lang ja` atas 36 halaman
    cetak asli (3m28s): **329 blok, ±9,1/halaman** (halaman
    cerita 5–16 blok; sampul 1 blok) — detektor sehat di cetakan
    halftone asli. (OCR ja atas teks Inggris tidak dinilai —
    memang bukan bahasanya.) **Kesenjangan "CBR belum teruji
    fixture" sejak Tahap 1 DINYATAKAN TUTUP** — tidak perlu
    lagi menunggu kiriman .cbr dari Ando.
  - **OCR manga Jepang ASLI terukur.** Dua halaman contoh resmi
    repo manga-image-translator (user-images GitHub): halaman
    dialog penuh warna 1487×2048 (13 bubble vertikal) → **14
    blok** (13 bubble + 1 artefak SFX): ±8 terbaca baik/nyaris
    persis (「髪型変えたら / すぐに気付いて / くれてつ」
    ocr 0,84; 「店の店員さんとかにも / 赦語使3てて偉いな- /
    とか」 0,83 — 敬→赦 satu-satunya salah kanji; 「ねえねえ /
    めいは本のこと / どう思うへ」 0,85), 3 terbaca sebagian
    (bubble 坂本くん hanya 「本」), 2 bubble kecil gagal baca
    (「え？」→"A."), SFX tulisan tangan tidak terbaca (di luar
    janji model cetak). Halaman judul/ilustrasi 2297×3123:
    teks dekoratif & blurb sampul terbaca kasar — batas jujur
    yang sama. Potongan bubble mungil contoh manga-ocr
    (99–335 px, teks di atas gambar + furigana) memang buruk
    (0–2 blok, baca karut) — BUKAN representasi halaman asli
    (skala thumbnail), dicatat agar tidak disalahartikan.
  - **BUG ditemukan & DIPERBAIKI dalam sesi yang sama
    (v0.6.1)**: di halaman judul, dua pasang kotak IDENTIK
    lolos jadi blok ganda — detektor melaporkan wilayah sama
    sebagai text_bubble DAN text_free, sedangkan NMS bawaan
    hanya menekan duplikat sekelas. Perbaikan:
    `DetectOcrPipeline.DedupeBoxes` sesudah saringan kelas
    (IoU > 0,85 → buang, skor tertinggi dipertahankan) + tes
    regresi `DedupeBoxes_CrossClassDuplicates`. Verifikasi
    ulang langsung: halaman judul 21 blok TANPA pasangan
    identik, halaman dialog tetap 14 blok dengan teks sama.
  - Tes: **74 total — 73 lolos, 0 gagal, 1 skip** (skip lama;
    tmp/xunit-results-v061.xml). Publish ulang: exe
    **124.674.641 byte** PE32+ GUI, footer v0.6.1.
- 2026-10-27: **v0.7.0 — Typeset Otomatis Pintar** (permintaan
  Ando: hasil otomatis harus serapi typeset manual halaman asli
  yang disetujuinya — teks terpusat di bubble, ukuran seragam,
  font gaya komik).
  - **Mesin** (`Services/Typeset.cs`): `SmartAutoTypesetPage` +
    varian bab. Hanya blok berterjemahan yang belum bergaya.
    (1) `EstimateBubbleBox`: pindai interior (min>200, rentang
    <28) dari pusat kotak OCR ke 4 arah, langkah 2 px, berhenti
    sesudah >3 sampel non-interior; inset max(6, 4%) kecuali
    sisi tepi halaman; fallback kotak OCR bila hasil <60%
    kotak OCR. Pagar jangkauan 2x dimensi OCR per arah (temuan
    tes: halaman putih polos membuat pindai bocor selebar
    halaman; rasio hasil terverifikasi di halaman asli <=1,65
    jadi pagar tidak memotong bubble asli).
    (2) `PartitionBubbleBoxes`: irisan >35% luas kotak lebih
    kecil = satu bubble; grup di-union lalu diiris di titik
    tengah antar pusat berurutan sepanjang sumbu sebaran
    terbesar. (3) Ukuran seragam: auto-fit per blok di kotak
    baru (`MeasureAutoFitSize`, logika FitLayout lama), target
    halaman = persentil-75 dijepit [20,28], final =
    min(target, fit) min 14, disimpan eksplisit.
    (4) Font: "Comic Neue" + Bold bila keluarga itu ada di
    SKFontManager, selain itu sans aman; semua horizontal,
    preset Dialog, rata tengah.
  - **PERUBAHAN ARAH**: terapkan-otomatis jalur pintar TIDAK
    lagi mengikuti arah vertikal sumber (v0.5.2) — mengikuti
    hasil manual yang disetujui Ando (semua horizontal di
    tengah bubble). Centang Vertikal manual tetap ada. Jalur
    lama `AutoTypesetPage` tanpa bitmap dipertahankan persis.
  - **Pengait**: `BuildFinalPageBitmap` menjalankan jalur
    pintar (bitmap dasar di tangan) -> `--render` dan ekspor
    otomatis ikut; pra-isi lama di Program.RunRender dan
    ExportService dicabut (RunRender menghitung blok terisi
    dari selisih gaya + menyimpan proyek sesudah loop).
    Tombol UI Terapkan Otomatis Halaman/Bab memakai bitmap
    dasar pola render (CleanStore -> halaman kerja) lewat
    `SmartAutoTypesetChapter`, status menyebut "pintar".
  - Tes: **83 total — 82 lolos, 0 gagal, 1 skip** (skip lama
    loopback TranslateTests yang sama; baseline v0.6.1: 74).
    9 tes baru di SmartTypesetTests (estimator elips, fallback
    gelap, pagar halaman putih, partisi dua blok satu bubble,
    ukuran seragam, pilihan font, tinta di dalam kotak, gaya
    eksplisit tak ditimpa, MeasureAutoFitSize). Satu tes lama
    diubah dengan alasan tercatat di tempatnya:
    ExportTests ambang densitas 0,02 -> 0,01 (ukuran seragam
    <=28 px memang menggambar "Halo!" lebih kecil daripada
    auto-fit lama). Tes jalur lama (vertikal dsb.) TIDAK
    berubah — semua lolos apa adanya.
  - Publish win-x64 single-file: **124.678.737 byte** PE32+
    GUI; string footer "v0.7.0 — Typeset Otomatis" (UTF-16)
    ada di dalam exe, simbol SmartAutoTypesetPage ada.
    Screenshot UI: tmp/tinta-v07-ui.png (footer v0.7.0,
    halaman Typesetting, bab demo terbuka).
  - Batas jujur: (1) estimator mengasumsikan interior bubble
    terang-netral sesudah dibersihkan — bubble berwarna/gelap/
    screentone jatuh ke fallback kotak OCR (perilaku lama);
    (2) halaman yang marginnya putih luas ditahan pagar 2x —
    bubble yang jauh lebih besar dari kotak teksnya (>2x per
    arah dari pusat) terpotong pada pagar; (3) "Comic Neue"
    hanya dipakai bila font itu terpasang di mesin pengguna
    (di Windows: pasang font bebas OFL itu sendiri; aplikasi
    tidak membundel font); (4) kualitas di halaman asli
    berwarna diverifikasi agen utama lewat jalur yang sama
    pada demo manualnya — uji .exe tetap menunggu PC Ando.
- 2026-10-27: **v0.7.1 — dua cacat jalur pintar DIPERBAIKI**
  (temuan verifikasi independen agen utama atas render halaman
  manga asli 1487×2048 via tmp/smart-test, proyek uji TIDAK
  disentuh):
  - **#1 Kata panjang pecah di tengah pada ukuran final.**
    Akar masalah TERBUKTI di kode + terukur (bukan sekadar
    dugaan urutan font — probe pengukuran memang sudah memakai
    SmartFontChoice): pencarian biner FitLayout menemukan
    ukuran bersih pada nilai PECAHAN (reaksi 26,49945: lebar
    "Reaksimu" ter-hint 105 px ≤ kotak 107), lalu
    SmartAutoTypesetPage MEMBULATKAN ke 0,1 → 26,5, dan pada
    26,5 lebar ter-hint kata yang sama MELOMPAT ke 109 px
    (hinting memkuantisasi lebar glif; tidak monoton terhadap
    ukuran) → kata dipecah per karakter saat digambar pada
    ukuran manual tersimpan. Hal yang sama: "Sakamoto-kun?"
    bersih di 22,4957 (146 px) → bulat 22,5 → 153 px > kotak
    149 → "?" yatim. Perbaikan: gaya final tiap blok dibuat
    sekali & dipakai untuk semua pengukurannya, dan ukuran
    final **divalidasi ulang pada nilai tersimpan persis**
    (`LayoutCleanAtSize`: BuildLayout pada ukuran itu dengan
    typeface final — harus muat & tanpa SplitWord), diturunkan
    0,5 px per langkah sampai bersih (lantai 14). Hasil pada
    geometri asli: reaksi 26,5 → **26**, Sakamoto 22,5 → **22**
    (acuan manual agen utama 23 & 20–21; otomatis kini sedikit
    lebih besar karena kotaknya memang terukur muat utuh —
    invarian kata-utuh yang dijaga, bukan angka persisnya).
  - **#2 Partisi bubble gabungan salah di sumbu non-iris.**
    `PartitionBubbleBoxes` memberi anggota grup potongan union
    pada sumbu iris DAN rentang union penuh pada sumbu lain →
    dua blok satu bubble sama-sama setinggi union (411 px),
    teks blok kanan terpusat di tengah union (ujung bawah
    cupingnya) & bertabrakan visual dengan teks blok kiri.
    Perbaikan: sumbu non-iris memakai rentang ukur anggota
    SENDIRI. Hasil geometri asli: blok kanan y 1419..1633
    (cupingnya), blok kiri y 1473..1830 — sesuai acuan.
  - **Tes: 86 total — 85 lolos, 0 gagal, 1 skip** (skip lama
    loopback yang sama; tmp/xunit-results-v071.xml; baseline
    v0.7.0: 83/82/1). 3 tes regresi baru di SmartTypesetTests
    (kata panjang utuh pada ukuran tersimpan — geometri elips
    mereplikasi bubble asli, lebar kata diperiksa dengan SKFont
    mentah independen dari BuildLayout; partisi berdampingan;
    partisi bertumpuk) — ketiganya **gagal 3/3 pada kode
    v0.7.0** (dibuktikan dengan stash perbaikan) dan lolos pada
    kode baru. Tes lama partisi dua-blok-satu-bubble tetap
    lolos apa adanya (union hasil partisi baru tetap menutup
    ≥85% elips pada kasus sintetis itu).
  - Publish win-x64 single-file ulang: **124.678.737 byte**
    PE32+ GUI (jumlah byte kebetulan sama dengan v0.7.0 — isi
    terverifikasi beda: string footer UTF-16 "v0.7.1 —
    Typeset Otomatis" ada, string v0.7.0 tidak). VERSION +
    footer → 0.7.1 (label tetap "Typeset Otomatis").
  - Batas jujur: (1) invarian yang dijaga = kata utuh + muat
    pada ukuran tersimpan; ukuran otomatis bisa ±1–3 px dari
    pilihan manual manusia karena lebar ter-hint memang
    berundak; (2) lantai 14 px: kata yang pada 14 px pun lebih
    lebar dari kotaknya tetap dipecah mesin render (perilaku
    lama yang disengaja untuk kata mustahil); (3) verifikasi
    .exe di Windows asli tetap menunggu PC Ando.
- 2026-10-27: **v0.7.2 — petunjuk prosedur di aplikasi**
  (permintaan Ando): bar petunjuk statis di halaman
  Terjemahan (urutan "Terjemahkan → Poles AI → Terapkan
  Otomatis" + catatan cadangan/Urungkan Poles) dan pengingat
  satu baris di halaman Typesetting. Teks saja, tanpa
  perubahan logika.
- 2026-10-27: **v0.7.3 — estimator bubble konservatif
  multi-baris** (temuan verifikasi agen utama pada sampel
  asli baru "page-c": bubble AWAN menempel tepi kiri —
  chord tengah terlalu lebar untuk baris atas/bawah,
  huruf pertama luber tepi kiri bubble; bubble oval kanan
  sempurna). EstimateBubbleBox kini mengukur ulang rentang
  horizontal pada ketinggian ⅓ & ⅔ rentang vertikal mentah
  (aturan pindai sama, pagar 2× OCR tetap), tepi final =
  konservatif kedua dari {pusat, ⅓, ⅔} per sisi, tidak
  pernah melebar melewati chord tengah, baris sampel gagal
  (lebar < 40% chord tengah / tak beririsan) dibuang.
  Simetris vertikal belum diterapkan (prioritas horizontal
  sesuai brief). Terukur pada replika sintetis: awan tepi
  halaman Left 15→52, lebar 331→255, tinta di luar bubble
  239→16 px; replika page-b menyusut 5,6–6,8% (pagar
  regresi ±10%); persegi panjang tetap 553; elips tinggi
  1:2 → 173 (≈87% chord 200); elips lebar tes lama tetap
  lolos (607 ≥ 595). 4 tes regresi baru di
  SmartTypesetTests (awan geometri, awan tinta, pagar
  page-b, elips tinggi + persegi) — 3 gagal pada kode lama
  (dibuktikan dengan stash), pagar page-b lolos di kedua
  versi sesuai desainnya. Tes final: **90 total — 89 lolos,
  0 gagal, 1 skip** (XML tmp/xunit-results-v073.xml;
  baseline v0.7.2: 86/85/0/1). Batas jujur: sisa tinta di
  luar bubble pada tumpukan teks yang mengisi > ⅔ tinggi
  bubble berlekuk kuat bisa tersisa belasan px di baris
  ekstrem di luar pita sampel ⅓–⅔; verifikasi ulang
  halaman asli page-c di tangan agen utama.
- 2026-10-27: **v0.7.4 — penghalang gelap pada pindai
  bubble** (temuan verifikasi agen utama pada page-c asli:
  perbaikan v0.7.3 tidak menolong di halaman itu — blok
  kiri terukur (0,0,234x614), blok kanan (822,0,...) bocor
  ke y=0; teks blok kiri mepet tepi kiri bubble). Akar
  masalah terdiagnosis agen utama & terverifikasi di kode:
  latar krem pucat page-c (±250,240,225 → min 225 > 200,
  rentang 25 < 28) LOLOS uji IsBubbleInterior, dan garis
  tepi bubble tipis (±2–3 px) dilompati pindai langkah
  2 px + toleransi > 3 sampel (hanya 1–2 sampel gelap).
  Perbaikan: IsDarkBarrier (saluran minimum < 140,
  konstanta DarkBarrierThreshold) diperiksa PERTAMA di
  kedua fungsi pindai EstimateBubbleBox (Scan 4 arah +
  ScanRow ⅓/⅔) — sampel gelap menghentikan pindai
  seketika; toleransi celah hanya untuk non-interior
  tidak gelap. Ambang 140 dibiarkan sesuai diagnosis
  (pita kalibrasi 120–160 tidak perlu digeser): tes
  regresi baru EstimateBubbleBox_CreamBackgroundThinBorder_NoLeak
  (kanvas 1020×1254, latar krem, roundrect putih tepi
  hitam 2 px tanpa anti-alias pada geometri blok asli
  page-c (60,109,137x347) & (855,75,155x326)) lolos
  rapat di interior kedua bubble, dan GAGAL pada kode
  v0.7.3 dengan bocor (0,0,387x938) — dibuktikan dengan
  stash. Seluruh pagar lama (page-b ±10%, awan tepi
  halaman, elips tinggi, persegi, halaman putih) tetap
  lolos. Tes final: **91 total — 90 lolos, 0 gagal,
  1 skip** (XML tmp/xunit-results-v074.xml; baseline
  v0.7.3: 90/89/0/1). Batas jujur: garis tepi bubble yang
  digambar dengan warna abu di atas ambang (saluran
  minimum ≥ 140, mis. tepi abu muda tanpa garis hitam)
  tidak memicu penghalang — kasus seperti itu kembali ke
  perilaku toleransi lama; verifikasi ulang page-c asli
  di tangan agen utama.
- 2026-10-27: **v0.7.5 — perlindungan garis tepi pada
  inpaint.** Cacat dari verifikasi visual agen utama pada
  page-c asli (crop orig vs cleaned): garis tepi bubble
  kanan hilang sebagian sesudah inpaint (segmen kanan-tengah
  sejajar kolom teks lenyap; goresan hitam di atas bubble
  ikut terhapus), padahal di asli garisnya utuh. Akar
  masalah: mask inpaint = tinta gelap dalam kotak blok +
  dilasi; kolom teks ±6 px dari garis, jadi piksel garis
  masuk mask langsung/kena dilasi lalu ter-inpaint.
  Analisis komponen terhubung (agen utama, read-only) pada
  crop blok page-c (855,75,155x326): komponen garis utama
  3.544 px menyentuh tepi crop (376 px di wilayah blok,
  219 terhapus di hasil bersih) DAN busur garis kanan yang
  lepas dari komponen utama (372 px, tidak menyentuh tepi
  crop, 163/171 px wilayahnya terhapus) — busur itu hanya
  melintasi batas kotak blok. Perbaikan: InkMask.
  LineProtection (ambang tinta sama, InkLuminance 140)
  menghitung komponen 8-arah dari piksel gelap potongan
  SEBELUM dilasi; komponen yang menyentuh tepi potongan
  ATAU melintasi batas kotak blok (punya piksel di dalam
  dan di luar wilayah inset) dilindungi: dikeluarkan dari
  mask + bukan sumber dilasi (InkMask.Build) dan alfanya
  dipaksa 0 lagi sesudah expand+blur (InkMask.
  FeatherAlpha); CleanService.InpaintBlock menghitung peta
  sekali dan meneruskannya ke keduanya. Mode solid tidak
  berubah; di luar mask tetap piksel-persis. Tes regresi
  baru: InkMask_LineProtection_KeepsCrossingLines_
  RemovesGlyphs (tingkat mask/alfa, tanpa model) +
  Inpaint_ProtectsBubbleOutlineNearTextColumn (sintetis
  meniru page-c: garis 3 px, kolom vertikal ±6 px dari
  garis, blok mencakup garis; densitas garis ≥ 95% asli,
  tinta interior turun ≥ 90%). Tes final: **93 total —
  92 lolos, 0 gagal, 1 skip** (XML
  tmp/xunit-results-v075.xml; baseline v0.7.4: 91/90/0/1).
  Batas jujur: glif yang menyatu fisik dengan garis tepi
  (satu komponen) ikut terlindungi dan tidak terhapus;
  karya garis gelap lain yang melintasi kotak blok (mis.
  rambut/efek yang masuk bubble) juga terlindungi —
  memang itu tujuannya, tetapi teks yang menempel pada
  karya garis seperti itu bisa tertinggal.
- 2026-10-27: **v0.7.6 — pilihan mode perlindungan garis
  pada inpaint (permintaan Ando: pilih hasil terbaik per
  kasus).** `CleanOperation.LineProtect` (bool, bawaan
  true) dicatat per operasi; skema proyek naik ke **v6**
  (berkas v1–v5 tetap termuat; op tanpa bidang ini = true
  = perilaku v0.7.5). `CleanService.SetBlockOperation(...,
  lineProtect)` meneruskan pilihan ke `InpaintBlock` dan
  bangun-ulang menghormatinya; tanda tangan cache
  CleanStore memuat pilihan ("garis"/"tuntas") supaya
  pergantian mode tidak menelan cache lama. Mode OFF =
  tanpa LineProtection sama sekali: InpaintBlock memakai
  peta serba-false seukuran potongan (panjang cocok → dipakai
  InkMask apa adanya, tidak ada piksel terlindungi di mask
  maupun alfa). UI Bersihkan: pemilih "Garis tepi:" di
  samping pemilih mode dengan label persis "Jaga garis
  (huruf nempel bisa tertinggal)" / "Hapus tuntas (garis
  bisa terpotong)"; pilihan tersimpan di settings.json
  (AppSettings.CleanLineProtect nullable — false harus
  benar-benar tertulis walau serializer WhenWritingDefault;
  bawaan efektif true) dan status blok inpaint di daftar
  menampilkan "• garis dijaga" / "• hapus tuntas". CLI:
  `--clean --line-protect on|off` (bawaan on), tercetak di
  ringkasan "garis tepi: dijaga/hapus tuntas". Tes: **96
  total — 95 lolos, 0 gagal, 1 skip** (XML
  tmp/xunit-results-v076.xml; baseline v0.7.5: 93/92/0/1;
  skip loopback yang sama). 3 tes baru di CleanTests:
  fixture huruf MENYATU garis tepi (LaMA asli) — ON: garis
  ≥ 95% + sisa glif ≥ 50%; OFF: tinta glif nempel hilang
  ≥ 90%; muat berkas gaya v5 tanpa lineProtect → ON +
  round-trip OFF. Satu penyesuaian tes lama: literal `5`
  hardcoded di TypesetTests.Project_RoundTripV5 diganti
  CurrentVersion (konsekuensi naik skema; makna sama).
  Publish win-x64 ulang: TintaStudio.exe 124.686.929 byte,
  PE32+ GUI; footer v0.7.6 terverifikasi di exe + di
  screenshot UI Bersihkan tmp/tinta-v076-ui.png (bab demo
  terbuka, kontrol "Garis tepi:" tampil). Commit lokal,
  tidak di-push.
- 2026-10-27: **v0.7.7 — Penghapus Latar: alat inpaint gaya
  Photoshop di halaman Bersihkan (permintaan Ando: lingkaran
  mouse menyapu huruf, dibersihkan mengikuti latar).**
  `BrushStroke.Kind` (enum StrokeKind Paint/Erase/Inpaint,
  JSON "kind") + `BrushStroke.LineProtect` per sapuan;
  skema tetap v6 — berkas lama tanpa kind diturunkan dari
  Erased (properti Kind memakai backing nullable: tak ada
  di berkas → Erase bila erased=true, Paint bila false).
  Sapuan inpaint masuk GAMBAR DASAR
  (CleanService.BuildBaseBitmap: ops blok lalu sapuan
  inpaint urut Seq gabungan; CleanStore cache + tanda
  tangan memuat sapuan inpaint — seq/radius/pilihan
  garis/hash titik; sapuan cat/pulihkan tetap komposit
  ringan di atasnya). Per sapuan: crop bbox+margin dari
  keadaan dasar saat itu, mask gabungan cakram (titik
  dirapatkan + ruas tebal) bertepi lembut via
  FeatherAlpha, perlindungan InkMask.LineProtection
  dengan wilayah = kotak mask (inset 0) bila sapuan
  LineProtect — pilihan "Garis tepi" aktif saat sapuan
  dibuat; composite bersama InpaintBlock
  (CompositeInpaint). UI: toggle "Penghapus Latar" di
  bar alat manual + pratinjau cincin kursor (Ellipse di
  ToolCanvas, diameter = BrushSize × skala, mengikuti
  pointer untuk ketiga alat lingkaran); VM
  AddBackgroundEraserStroke memeriksa model siap dan
  mencatat LineProtect = pilihan aktif. Tes: **101
  total — 100 lolos, 0 gagal, 1 skip** (XML
  tmp/xunit-results-v077.xml; baseline v0.7.6: 96/95/0/1;
  skip loopback yang sama). 5 tes baru di CleanTests
  (LaMA asli): sapuan menghapus glif ≥90% + luar sapuan
  persis asli + simpan/muat bangun ulang identik; glif
  menyatu garis — ON: garis ≥95% + sisa glif ≥50%, OFF:
  glif hilang ≥90% (segmen garis pendek tidak
  diasersikan: model bisa menyambung garis lurus yang
  terpotong pendek — temuan putaran tes pertama);
  muat sapuan tanpa kind dari erased + round-trip jenis
  baru; tanda tangan CleanStore hanya berubah untuk
  sapuan inpaint. Publish win-x64 ulang: TintaStudio.exe
  124.695.121 byte, PE32+ GUI; footer v0.7.7 + label
  "Penghapus Latar" terverifikasi di exe. Screenshot UI
  Bersihkan tmp/tinta-v077-ui.png: bab demo terbuka,
  toggle "Penghapus Latar" tampil di bar alat + teks
  bantuan baru (klik sintetis Xvfb tidak menembus toggle,
  jadi cincin kursor tidak ikut terpotret — logika cincin
  mengikuti pola kanvas yang ada). Commit lokal, tidak
  di-push.
- 2026-10-27: **v0.7.8 — Bahasa OCR keempat: Inggris
  (permintaan Ando).** Model `en_PP-OCRv5_mobile_rec` dari
  monkt/paddleocr-onnx (`languages/english/`, keluarga yang
  sama dengan ko v5 mobile) + kamus 436 karakter; cache
  `rec-en.onnx` 7.830.888 byte (SHA-256 4e16deb2…) +
  `rec-en.dict.txt` 1.416 byte (SHA-256 e025a66d…);
  tercatat di docs/models.md + tools/fetch_models.sh.
  Integrasi pola persis bahasa lama: `OcrLanguage.English`
  kode "en" label "Inggris" masuk `All` + `FromCode`;
  entri katalog `rec-en` "OCR Inggris" (kartu model ikut
  daftar generik dari katalog); usage CLI
  `--lang ja|ko|ch|en`. Kontrak I/O = rec v5 lain,
  kalibrasi empiris lolos: fixture sintetis Inggris
  ("Hello world" dalam bubble, font Latin sistem) terbaca
  PERSIS (kemiripan 1,00, keyakinan 1,00, Horizontal) lewat
  pipeline penuh; uji asap CLI `--ocr --lang en` pada CBZ
  gambar PIL: 1 blok "Hello world" (det 0,85/ocr 0,99).
  SortReadingOrder tidak berubah (kolom manga hanya ja).
  Tes: **103 total — 102 lolos, 0 gagal, 1 skip** (XML
  tmp/xunit-results-v078.xml; baseline v0.7.7:
  101/100/0/1). +2 tes: pipeline Inggris + register
  katalog rec-en. Publish win-x64 ulang: TintaStudio.exe
  124.695.121 byte, PE32+ GUI (ukuran sama dengan v0.7.7
  kebetulan bundel — isi terverifikasi: footer v0.7.8 ada,
  v0.7.7 hilang, string "OCR Inggris" + "rec-en.onnx"
  ada). Screenshot Deteksi & OCR tmp/tinta-v078-ui.png:
  dropdown bahasa TERBUKA menampilkan Jepang/Korea/Cina/
  Inggris + kartu "OCR Inggris — Siap" (bab page-c
  terbuka, salinan config tmp/shot078-config; config asli
  tidak disentuh). Batas jujur: teks miring ekstrem /
  tulisan tangan / huruf hias tidak dijamin. Commit
  lokal, tidak di-push.
- 2026-10-27: **v0.7.9 — Mode OCR Otomatis (permintaan
  Ando).** `OcrLanguage.Auto` kode "auto" label "Otomatis"
  di posisi teratas `All`; `Concrete` = 4 bahasa sungguhan;
  `FromCode("auto")` + CLI `--lang auto`. Mesin di pipa
  yang ada (bukan jalur paralel): kandidat =
  `ModelStore.InstalledLanguages()`; kosong → galat
  ModelMissingException menyebut semua model rec; satu →
  langsung dipakai. Tebakan halaman (`GuessPageLanguage`):
  sampel ≤5 blok terbesar × tiap kandidat, skor =
  keyakinan tertimbang panjang teks + bonus aksara 0,25
  (`LanguageGuess`: kana→ja, hangul→ko, Latin+spasi→en,
  Han netral). Perbaikan per blok: keyakinan < 0,5 ATAU
  aksara tidak cocok bahasa halaman (`ScriptMismatch` —
  temuan tes: model ja membaca "Hello world" sebagai
  "Helo world." conf 0,98 karena kamusnya memuat Latin)
  → coba-ulang semua kandidat, skor terbaik menang,
  bahasa aktual tercatat per blok; halaman campur ja+en
  terbukti benar dua-duanya. Transparansi:
  `Pipeline.DetectedLanguages` per indeks halaman; status
  UI "Bahasa terdeteksi: …" per halaman; ringkasan CLI
  --ocr mencetak baris yang sama. Tes: **110 total —
  109 lolos, 0 gagal, 1 skip** (XML
  tmp/xunit-results-v079.xml; baseline v0.7.8:
  103/102/0/1). +7 tes (3 pipeline auto + aturan bonus +
  2 CLI folder-model panggung). Batas jujur: biaya waktu
  = rec kandidat×sampel per halaman + coba-ulang blok
  lemah; Han murni ja-vs-ch diputuskan keyakinan saja.
  Commit lokal, tidak di-push.
- 2026-10-28: **v0.7.10 — Ikon aplikasi pada .exe
  (permintaan Ando)**: jendela aplikasi sudah berikon
  `logo.png`, tetapi `.csproj` belum punya
  `<ApplicationIcon>` sehingga `TintaStudio.exe` tampil
  berikon generik .NET di Explorer/desktop/taskbar. Aset
  baru `Assets/logo.ico` multi-ukuran
  (16/20/24/32/48/64/128/256 px) dikonversi langsung dari
  `Assets/logo.png` (256×256 RGBA; desain tidak berubah);
  `<ApplicationIcon>Assets/logo.ico</ApplicationIcon>`
  ditambahkan ke `TintaStudio.csproj`. Verifikasi: ikon
  diekstrak balik dari .exe hasil publish — hasilnya logo
  tetes tinta Tinta Studio, bukan ikon generik. Tes: 109
  total — 108 lolos, 0 gagal, 1 skip (XML
  tmp/xunit-results-v0710.xml; sama hijau dengan baseline
  v0.7.9). Commit lokal, tidak di-push.
- 2026-11-01: **v0.7.11 — Instal Instan (Ollama + Ambil Key) &
  Hapus Bersih (permintaan Ando: "instal instan yang mencakup
  semua di bagian key" + perluasan Hapus Bersih)**:
  - Layanan baru `Services/Ollama.cs`: klien API manajemen
    Ollama (akar http://localhost:11434, BUKAN /v1) — probe
    GET /api/tags (timeout 3 dtk) + deteksi executable (PATH,
    %LOCALAPPDATA%\Programs\Ollama, %ProgramFiles%\Ollama) →
    tiga keadaan (Running/InstalledNotRunning/NotInstalled,
    pemetaan murni MapState); POST /api/pull aliran JSONL
    dengan parsing progres completed/total per baris; DELETE
    /api/delete; unduh OllamaSetup.exe resmi ke temp dengan
    progres; Process.Start untuk serve/installer/browser.
    Parsing & pemetaan murni/statis → teruji penuh via stub
    HttpMessageHandler dalam-proses + pencari exe tiruan.
  - Dialog Pengaturan Key & Model: kartu Ollama memuat seksi
    INSTAL INSTAN — status otomatis saat dialog dibuka +
    Segarkan; tombol Pasang Ollama (teks jujur soal wizard/UAC)
    / Jalankan Ollama sesuai keadaan; daftar kurasi
    qwen2.5:7b (±4,7 GB, "Saran utama — JA/KO/ZH→ID"),
    qwen2.5:14b, qwen2.5:3b, llama3.1:8b, gemma3:12b per baris:
    Terpasang/Unduh (progres % live, bisa dibatalkan lewat
    baris Batal yang tetap aktif saat panel sibuk)/Pakai
    (mengisi kolom Model dengan nama persis dari /api/tags)/
    Hapus; model terpasang di luar kurasi ikut tampil. Semua
    async, galat = teks manusiawi. Pencocokan nama sadar alias
    :latest (bawaan lama "llama3.1" tetap sah).
  - Tombol **Ambil Key** persis di kartu Gemini
    (aistudio.google.com/apikey), Groq (console.groq.com/keys),
    OpenRouter (openrouter.ai/keys) + keterangan "terbuka di
    browser; tempel key-nya di sini sesudah dapat".
  - **Hapus Bersih — Zona Berbahaya** (`Services/DataWipe.cs`):
    instruksi jujur + daftar folder persis dengan ukuran LIVE
    dari resolver yang sama dengan aplikasi (AppConfig.ConfigDir;
    ModelStore.CacheRootDir baru = induk CacheDir, tanpa
    hardcode duplikat) + kalimat eksklusi (exe tidak bisa
    menghapus dirinya; proyek .json/.cbz di folder lain aman;
    model Ollama di ~/.ollama/models TIDAK disentuh). Tombol
    "Hapus Semua Data Tinta…" → konfirmasi kedua menyebut ulang
    daftar + total persis → eksekusi rekursif; folder tak ada =
    "sudah bersih"; state key/pengaturan dialog dikosongkan di
    memori (Simpan tidak menulis ulang data yang dibersihkan);
    hasil "Selesai — N folder terhapus, ±X MB dibebaskan. Tutup
    aplikasi untuk menyelesaikan."
  - Alat screenshot: env TINTA_SETTINGS=1 membuka dialog
    otomatis (tertunda 1,5 dtk) — screenshot
    docs/v0711-instal-instan.png (keadaan "belum terpasang" di
    VM ini + Ambil Key di 3 kartu) dan
    docs/v0711-hapus-bersih.png (ukuran live terbaca: data
    0,6 MB + cache 319,3 MB).
  - Tes: **129 total — 128 lolos, 0 gagal, 1 skip** (XML
    tmp/xunit-results-v0711.xml; +14 OllamaTests +6
    DataWipeTests; baseline v0.7.10 109/108/0/1 tetap hijau;
    skip = loopback sandbox yang sama). Catatan harness:
    runner konsol harus berjalan dari dalam
    TintaStudio.Tests/bin (tes CLI menyelesaikan DLL aplikasi
    relatif terhadap lokasi rakitan tes) dan /tmp adalah tmpfs
    512 MB — jangan bangun runner di sana.
  - Build Release bersih 0/0; publish win-x64 single-file:
    **TintaStudio.exe 124.830.289 byte** PE32+ GUI (ikon
    multi-ukuran terverifikasi tertanam via wrestool/icotool).
  - Batas jujur: alur unduh installer, pull model asli, dan
    wizard installer TIDAK dibuktikan ujung-ke-ujung dari VM
    ini (TCP keluar proses .NET diblokir sandbox; tidak ada
    Ollama di VM) — yang terbukti: seluruh logika parsing/
    pemetaan/progres lewat stub dalam-proses, keadaan
    NotInstalled tampil nyata di UI VM, dan kode proses
    mengikuti pola yang sama seperti rilis sebelumnya.
    Pembuktian penuh menunggu PC Windows Ando. Commit lokal,
    tidak di-push.
