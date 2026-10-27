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

## Status: v0.4.0 — Tahap 4 (Terjemahan)

- **Dua mode terjemahan**: **Otomatis** (penyedia AI, per blok /
  halaman / bab) dan **Manual penuh tanpa key** — ketik langsung di
  kolom terjemahan tiap blok, tersimpan otomatis dengan cap
  "Manual". Suntingan manual sesudah hasil otomatis tidak ditimpa
  jalan otomatis berikutnya (bloknya sudah berterjemahan).
- **Penyedia**: BYOK **Gemini** (`gemini-2.5-flash`), **Groq**
  (`llama-3.3-70b-versatile`), **OpenRouter**
  (`meta-llama/llama-3.3-70b-instruct:free`) + lokal **Ollama**
  (`http://localhost:11434/v1`, `llama3.1`) dan **LM Studio**
  (`http://localhost:1234/v1`). Base URL & model bisa diatur per
  penyedia. Bahasa tujuan: **Indonesia** (bawaan) / Inggris.
  Konteks **satu halaman penuh**: semua blok sehalaman dikirim dalam
  satu prompt bernomor (suhu 0,3), jawaban wajib JSON ketat
  `[{"id":n,"text":...}]`; parsing toleran pagar kode; blok yang
  hilang dari jawaban dicoba-ulang sekali khusus blok itu; blok
  yang tetap gagal ditandai dengan alasan — blok lain tak ikut
  hilang.
- **Multi-key + rotasi otomatis** (Gemini/Groq/OpenRouter): key
  bisa diisi banyak per penyedia; 429/kuota → key masuk masa
  pendinginan (hormati header `Retry-After`) dan permintaan dicoba
  key berikutnya; 401/403 → key ditandai buruk & dilewati; galat
  lain (mis. 400) dilaporkan apa adanya. Key **hanya** di
  `~/.config/tinta-studio/keys.json` (izin 0600, daftar per
  penyedia) — tidak pernah masuk proyek/git/log/CLI. Status UI
  hanya menampilkan jumlah & nomor key aktif ("3 key • aktif #2").
  Cooldown disimpan sebagai cap waktu di keys.json agar bertahan
  antar-jalan. Pengaturan lain di
  `~/.config/tinta-studio/settings.json`.
- **Tahan limit**: "Terjemahkan Satu Bab" **menyimpan proyek
  sesudah SETIAP halaman** (berhenti di tengah aman), **melewati
  blok yang sudah berterjemahan** (tidak membakar kuota; blok gagal
  dicoba lagi) dan melaporkan jumlah yang dilewati; **jeda antar
  halaman bisa diatur** (bawaan 2 detik, 0 = tanpa jeda, berlaku
  juga sebelum coba-ulang); bila semua key sedang cooldown, bab
  **menjeda & melanjutkan sendiri** saat key paling awal pulih,
  dengan status hitung mundur "Menunggu limit pulih… ±N dtk —
  key #k". Tombol Batal menghentikan bersih kapan pun.
- **Poles AI**: per blok / halaman / bab — mengirim teks sumber +
  terjemahan saat ini, hasil poles menggantikan terjemahan tetapi
  versi lama disimpan satu tingkat (`TranslationBackup`) dengan
  tombol **Urungkan Poles** per blok. Cap hasil poles menyebut
  "• poles". Blok "Manual" tidak dipoles oleh jalan bab (hanya
  poles eksplisit per blok/halaman). Blok tanpa terjemahan
  dilewati.
- Proyek naik ke skema **v4** (blok: `Translation`, `TranslatedBy`,
  `TranslatedAt`, cadangan poles); berkas **v1/v2/v3 tetap
  termuat**. Halaman **Terjemahan** aktif (toolbar: penyedia,
  bahasa, Pengaturan Key & Model, Terjemahkan Halaman/Bab, Poles
  Halaman/Bab; daftar blok dua kolom sumber → terjemahan).
  CLI: `--translate <bab> [--provider gemini|groq|openrouter|ollama|lmstudio] [--target id|en]`
  (OCR ja dijalankan dulu bila proyek belum punya blok; tanpa key
  BYOK → pesan jelas, kode keluar 2).
- Kualitas terjemahan penyedia asli **menunggu uji di PC pengguna
  dengan key-nya**; di VM pengembangan semua jalur terverifikasi
  lewat **stub lokal** (tiruan kompatibel-OpenAI & Gemini yang
  memvalidasi bentuk permintaan persis). Catatan sandbox VM:
  kebijakan jaringan VM menolak koneksi TCP dari proses .NET
  (bahkan loopback), jadi stub diwujudkan sebagai handler HTTP
  dalam-proses dan tes CLI soket-sungguhan di-skip jujur di sini —
  di mesin normal jalur HttpClient standarnya sama.

## Status: v0.3.0 — Tahap 3 (Pembersihan Bubble)

- **Tiga cara membersihkan teks bubble**, semuanya **non-destruktif**
  (berkas asli tak pernah ditimpa; gambar bersih dibangun ulang dari
  daftar operasi yang tersimpan di proyek `.tinta.json` **v3**, berkas
  v1/v2 tetap termuat):
  - **Blok solid** — isi kotak blok dengan warna: sampel otomatis
    dari interior bubble (bawaan) atau warna manual lewat
    **eyedropper** (klik piksel di kanvas) / isian hex;
  - **Inpaint LaMA** — model LaMA manga (ONNX publik, unduhan
    terpisah ±207 MB — lihat docs/models.md) mengisi area teks
    mengikuti latar; dijalankan per potongan blok+margin, mask dari
    tinta di dalam kotak blok, tempelan hanya di dalam mask
    bertepi lembut;
  - **Manual** — kuas warna + penghapus (mengembalikan piksel asli)
    di kanvas; sapuan tercatat sebagai bagian dari operasi halaman.
- Halaman **Bersihkan**: toolbar mode + Bersihkan Halaman Ini /
  Satu Bab (latar + kemajuan + batal), toggle **Asli** untuk
  membandingkan sebelum/sesudah, daftar blok dengan mode per blok
  + Terapkan, tombol **Urungkan**, kartu model Inpaint LaMA.
  CLI: `--clean <bab> [--mode solid|inpaint] [--out <folder>]`
  (deteksi dijalankan dulu bila proyek belum punya blok).
- Gambar dasar hasil operasi di-cache per (bab, halaman) dengan
  tanda tangan operasi; sapuan manual dikomposit di atasnya, jadi
  menyunting sapuan tidak memicu inpaint ulang.

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
dotnet run --project TintaStudio.csproj -- --clean bab.cbz --mode inpaint --out bersih/  # bersihkan via CLI
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
3. ✅ Pembersihan bubble (blok solid + inpaint LaMA + manual, non-destruktif)
4. Terjemahan BYOK (Gemini/Groq/OpenRouter) + lokal (Ollama/LM Studio)
5. Editor typesetting kanvas
6. Ekspor (CBZ/PDF/folder) + packaging .exe
