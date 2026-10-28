# Tinta Studio

Aplikasi desktop **original** untuk menerjemahkan & menyunting komik
(manga/manhwa): impor bab dari beragam format, deteksi teks
(JP/CN/KR/EN), bersihkan bubble, terjemahkan (BYOK / lokal), typeset
di kanvas, ekspor bab.

Dibangun dengan prinsip **ATM** (Amati, Tiru, Modifikasi) dari
fitur referensi — semua kode, nama, logo, dan format proyek adalah
karya sendiri; tidak ada aset/identitas produk referensi yang
disalin. Peta fitur lengkap: [docs/FEATURE-MAP.md](docs/FEATURE-MAP.md),
arsitektur: [ARCHITECTURE.md](ARCHITECTURE.md).

## Status: v0.7.10 — Ikon Aplikasi pada .exe

Berkas `TintaStudio.exe` hasil publish kini menampilkan
**ikon aplikasi sendiri** (logo tetes tinta teal yang sama
dengan logo di sidebar) di Windows Explorer, desktop, dan
taskbar — sebelumnya tampil ikon bawaan generik .NET karena
`.csproj` belum punya `<ApplicationIcon>`.

- **Aset baru**: `Assets/logo.ico` multi-ukuran
  (16/20/24/32/48/64/128/256 px), dikonversi langsung dari
  `Assets/logo.png` (256×256 RGBA) — desain logo tidak
  berubah. Ikon jendela aplikasi (sudah dari `logo.png`)
  tidak diubah.
- **Integrasi**: `<ApplicationIcon>Assets/logo.ico</ApplicationIcon>`
  di `TintaStudio.csproj`; ikon tertanam diverifikasi dengan
  mengekstrak sumber daya ikon dari .exe hasil publish
  (hasil ekstraksi = logo tetes tinta, bukan ikon generik).
- **Tes: 109 total — 108 lolos, 0 gagal, 1 skip** (XML
  `tmp/xunit-results-v0710.xml`; sama hijau dengan baseline
  v0.7.9; skip = loopback sandbox yang sama).

## Status: v0.7.9 — Mode OCR Otomatis

Pemilih bahasa halaman Deteksi & OCR kini punya pilihan
teratas **"Otomatis"** (CLI `--lang auto`): aplikasi menebak
bahasa sendiri dari model yang terpasang — pengguna tidak
perlu tahu bahasa sumber halaman lebih dulu. Bahasa manual
(Jepang/Korea/Cina/Inggris) tidak berubah perilaku.

- **Kandidat = model rec yang terpasang penuh** di folder
  model. Tidak ada satu pun → galat jujur menyebut semua
  model OCR yang perlu diunduh. Tepat satu → langsung dipakai
  tanpa tebakan.
- **Tebakan halaman**: sampai 5 blok terbesar dibaca tiap
  kandidat; skor kandidat = rata-rata keyakinan tertimbang
  panjang teks **+ bonus aksara 0,25** bila teks terdekode
  konsisten (kana → ja, hangul → ko, Latin + spasi → en;
  Han murni netral — keyakinan yang memutuskan ja vs ch).
- **Perbaikan per blok**: blok yang keyakinannya < **0,5**
  pada bahasa halaman, ATAU yang aksaranya sama sekali bukan
  aksara bahasa halaman (kasus nyata: kamus ja memuat Latin,
  teks Inggris terbaca "Helo world." berkeyakinan 0,98 tanpa
  kana/Han), dicoba ulang dengan semua kandidat lain; skor
  terbaik disimpan dan **bahasa aktual tercatat per blok** —
  halaman campur ja+en terbukti benar per blok.
- **Transparansi**: status sesudah jalan menyebut
  "Bahasa terdeteksi: …" per halaman (UI dan ringkasan CLI),
  dan daftar blok menampilkan bahasa aktual tiap blok.
- **Tes: 110 total — 109 lolos, 0 gagal, 1 skip** (XML
  `tmp/xunit-results-v079.xml`; baseline v0.7.8:
  103/102/0/1; skip = loopback sandbox yang sama). 7 tes
  baru: auto memilih ja, auto memilih en, halaman campur
  per blok, aturan bonus aksara, CLI folder-model-tunggal,
  CLI tanpa model (galat jujur, exit 3).
- **Batas jujur**: mode ini lebih lambat dari mode manual —
  tebakan halaman menjalankan rec kandidat × sampel
  (maks 5 blok) per halaman, ditambah coba-ulang per blok
  lemah; halaman Cina murni vs Jepang aksara-Han diputuskan
  keyakinan model semata dan bisa meleset pada teks sangat
  pendek.

## Status: v0.7.8 — Bahasa OCR Keempat: Inggris

Bahasa sumber OCR kini **empat**: Jepang, Korea, Cina, dan —
baru — **Inggris** (permintaan Ando, supaya komik berbahasa
Inggris dan halaman campur bisa dibaca tanpa ganti alat).

- **Model**: `en_PP-OCRv5_mobile_rec` dari repo
  `monkt/paddleocr-onnx` yang sama dengan model ko/ch
  (keluarga PP-OCRv5 mobile) + kamus 436 karakter (Latin,
  angka, tanda baca). Berkas cache `rec-en.onnx`
  (7.830.888 byte) + `rec-en.dict.txt` (1.416 byte);
  URL + SHA-256 tercatat di
  [docs/models.md](docs/models.md), diunduh lewat
  `tools/fetch_models.sh` atau tombol Unduh di kartu model.
- **Integrasi mengikuti pola bahasa yang ada persis**: kode
  `en`, label UI "Inggris"; pemilih bahasa halaman Deteksi &
  OCR bertambah "Inggris"; kartu model baru **"OCR Inggris"**
  (status + ukuran + tombol Unduh); CLI `--ocr --lang en`
  (dan `--clean --lang en`) jalan. Kontrak I/O sama dengan
  rec v5 lain dan dikalibrasi empiris: fixture sintetis
  terbaca persis. Teks Inggris bersifat horizontal; blok yang
  terdeteksi vertikal diperlakukan sama seperti bahasa lain
  (putar 90° dua arah, pilih keyakinan tertinggi) — tidak ada
  perlakuan khusus yang merusak teks horizontal.
- **Tes: 103 total — 102 lolos, 0 gagal, 1 skip** (XML
  `tmp/xunit-results-v078.xml`; baseline v0.7.7:
  101/100/0/1; skip = loopback sandbox yang sama). 2 tes
  baru: pipeline Inggris ujung-ke-ujung (bubble "Hello world"
  terbaca **persis**, kemiripan 1,00, arah Horizontal) +
  register katalog model `rec-en`. Uji asap CLI manual:
  `--ocr fixture-en.cbz --lang en` → 1 blok, "Hello world"
  (det 0,85 / ocr 0,99).
- **Batas jujur**: model dilatih untuk teks cetak Latin;
  akurasi pada teks miring ekstrem, tulisan tangan, dan
  huruf hias (SFX bergaya) tidak dijamin — sama seperti
  batas model rec lain di katalog ini.

## Status: v0.7.7 — Penghapus Latar (Penghapus Inpaint Gaya Photoshop)

Halaman Bersihkan punya alat manual ketiga di samping Kuas
dan Penghapus: **Penghapus Latar**. Lingkaran kuas digerakkan
dengan mouse di atas kanvas (pratinjau cincin mengikuti kursor,
slider "Ukuran" yang sama), dan area huruf yang tersapu
dibersihkan **mengikuti latar** — di-inpaint LaMA — bukan
dicat warna dan bukan dipulihkan ke asli. Pas untuk sisa
huruf/SFX di luar kotak blok.

- **Sapuan jenis baru `BrushStroke.Kind` (enum StrokeKind:
  Paint / Erase / Inpaint; JSON `"kind": "inpaint"`)** +
  `BrushStroke.LineProtect` per sapuan. Skema proyek tetap
  **v6**: berkas lama tanpa `kind` dimuat dari bidang `Erased`
  lama (true = Erase, false = Paint) — kompatibel penuh.
  Sapuan inpaint non-destruktif seperti sapuan lain: tercatat
  di proyek, Urungkan membuangnya, bangun ulang deterministik.
- **Mesin:** sapuan inpaint adalah bagian GAMBAR DASAR
  (`CleanService.BuildBaseBitmap`: operasi blok lalu sapuan
  inpaint, urut Seq gabungan) karena LaMA mahal — hasilnya
  di-cache CleanStore dan tanda tangan cache memuat sapuan
  inpaint (urutan, radius, pilihan garis, hash titik). Sapuan
  cat/pulihkan tetap dikomposit ringan di atas gambar dasar.
  Per sapuan: potongan kotak batas sapuan+margin di-inpaint
  dengan mask = gabungan cakram sapuan bertepi lembut;
  menghormati pilihan "Garis tepi" yang aktif saat sapuan
  dibuat (mekanisme `InkMask.LineProtection` yang sama:
  komponen tinta yang menyentuh tepi potongan atau melintasi
  batas kotak mask dilindungi). Di luar mask piksel persis
  asli. Render & ekspor memakai jalur CleanStore yang sama.
- **Tes: 101 total — 100 lolos, 0 gagal, 1 skip** (baseline
  v0.7.6: 96/95/0/1; skip = loopback sandbox yang sama).
  5 tes baru: (a) sapuan di atas glif sintetis → tinta glif
  hilang ≥ 90%, piksel jauh di luar sapuan persis asli,
  simpan/muat → bangun ulang identik (hash sama, model LaMA
  asli); (b) sapuan melintasi garis tepi dengan glif menyatu:
  LineProtect ON → garis ≥ 95% utuh + sisa glif menyatu
  terdeteksi; OFF → tinta glif menyatu hilang ≥ 90%
  (perbedaan perilaku terasersi pada tinta yang menyatu —
  segmen garis lurus pendek yang terpotong bisa disambung
  kembali oleh model, jadi tidak diasersikan); (c) berkas
  tanpa `kind` dimuat dari `erased` + putaran simpan/muat
  jenis baru; (d) tanda tangan CleanStore berubah hanya
  untuk sapuan inpaint.

## Status: v0.7.6 — Pilihan Mode Perlindungan Garis (Jaga Garis / Hapus Tuntas)

Perlindungan garis v0.7.5 selalu aktif — garis tepi selamat,
tetapi huruf yang menyatu fisik dengan garis ikut
terlindungi dan tidak terhapus. Atas permintaan pengguna,
sekarang ada PILIHAN per operasi bersih, supaya hasil
terbaik bisa dipilih per kasus:

- **Bidang baru `CleanOperation.LineProtect` (bool, bawaan
  true)** dicatat di berkas proyek (skema naik ke **v6**;
  berkas v1–v5 tetap termuat — op lama tanpa bidang ini
  diperlakukan true = perilaku v0.7.5). Bangun-ulang halaman
  bersih menghormati pilihan tiap op, dan tanda tangan cache
  CleanStore memuat pilihan ini.
- **UI halaman Bersihkan:** pemilih "Garis tepi:" di samping
  pemilih mode — "Jaga garis (huruf nempel bisa tertinggal)"
  / "Hapus tuntas (garis bisa terpotong)". Pilihan tersimpan
  (pengaturan aplikasi), dipakai semua tombol bersihkan, dan
  status tiap blok di daftar menampilkan "• garis dijaga"
  atau "• hapus tuntas" untuk op inpaint.
- **CLI:** `--clean` menerima `--line-protect on|off`
  (bawaan on) dan nilainya tercetak di ringkasan hasil.
- Mode "Hapus tuntas" = perilaku pra-v0.7.5 persis: tanpa
  `LineProtection` sama sekali (peta perlindungan serba
  false) — semua tinta di dalam kotak dihapus, garis bisa
  terpotong.
- **Tes: 96 total — 95 lolos, 0 gagal, 1 skip** (baseline
  v0.7.5: 93/92/0/1; skip = loopback sandbox yang sama).
  3 tes regresi baru dengan fixture huruf MENYATU garis
  tepi + model LaMA asli: (a) protect ON → garis utuh
  (≥ 95%) DAN sisa glif nempel masih terdeteksi (≥ 50%
  densitas awal); (b) protect OFF → tinta glif nempel
  hilang ≥ 90%; (c) berkas gaya v5 tanpa `lineProtect`
  dimuat sebagai ON + putaran simpan/muat OFF bertahan.
  Satu penyesuaian tes lama: literal versi hardcoded `5`
  di tes round-trip typeset diganti `CurrentVersion`
  (konsekuensi naik skema ke v6; asersi maknanya sama).

## Status: v0.7.5 — Perlindungan Garis Tepi pada Inpaint

Cacat yang ditemukan verifikasi independen pada sampel asli
"page-c": garis tepi bubble kanan HILANG sebagian sesudah
inpaint. Di gambar asli garisnya utuh melingkar; di hasil
bersih, segmen tepi kanan-tengah (sejajar kolom teks) lenyap
dan goresan hitam di atas bubble ikut terhapus. Penyebabnya:
mask inpaint dibangun dari tinta gelap di dalam kotak blok
lalu didilasi — kolom teks Jepang duduk ±6 px dari garis
tepi, jadi piksel garis ikut masuk mask (langsung atau kena
dilasi) dan ter-inpaint.

- **Komponen tinta yang menyentuh tepi potongan atau
  melintasi batas kotak blok kini DILINDUNGI**
  (`InkMask.LineProtection`, ambang tinta yang sama dengan
  pembangun mask): komponen terhubung (8-arah) dari piksel
  gelap pada potongan dihitung sebelum dilasi; komponen yang
  menyentuh tepi potongan, atau yang punya piksel di dalam
  DAN di luar kotak blok, dianggap garis tepi / karya garis
  — seluruh pikselnya dipaksa keluar dari mask, tidak
  menjadi sumber dilasi, dan alfanya dipaksa 0 lagi sesudah
  feathering. Analisis komponen pada page-c membuktikan
  kedua pola itu nyata: komponen garis utama (terhubung ke
  karya garis yang keluar potongan) dan satu busur garis
  yang lepas dari komponen utama tetapi melintasi batas
  kotak blok — keduanya menyumbang piksel yang terhapus.
  Glif teks yang utuh di dalam kotak tidak berubah
  perilakunya dan tetap ter-inpaint.
- **Tanpa regresi:** fixture Tahap 3 (garis bubble utuh +
  interior bersih) dan seluruh suite lama tetap lolos.
  Tes: **93 total — 92 lolos, 0 gagal, 1 skip** (skip lama
  sandbox yang sama); 2 tes regresi baru (peta perlindungan
  di tingkat mask/alfa + inpaint ujung-ke-ujung: densitas
  garis tepi ≥ 95% dari asli, tinta interior turun ≥ 90%).
  Batas jujur: glif yang secara fisik menyatu dengan garis
  tepi (satu komponen tinta yang sama) ikut terlindungi dan
  tidak terhapus — menghapusnya berarti memotong garisnya.

## Status: v0.7.4 — Penghalang Gelap pada Pindai Bubble

Cacat yang ditemukan verifikasi independen pada sampel asli
"page-c" (halaman berwarna terang): latar di luar bubble
krem pucat (±250,240,225) LOLOS uji interior (saluran minimum
225 > 200, rentang warna 25 < 28), dan garis tepi bubble yang
utuh tapi tipis (±2–3 px) bisa dilompati pindai berlangkah
2 px — hanya 1–2 sampel kena piksel gelap, tertelan toleransi
> 3 sampel non-interior — sehingga pindai bocor di latar
sampai tepi halaman (kotak blok kiri terukur 234×614 dari
interior ±155×400, menempel y=0; perbaikan multi-baris
v0.7.3 tidak menolong karena baris sampel ikut bocor).

- **Semua pindai EstimateBubbleBox (4 arah dari pusat +
  baris sampel ⅓/⅔) kini berhenti SEKETIKA pada piksel
  gelap** (saluran minimum < 140). Toleransi celah yang ada
  hanya berlaku lagi untuk piksel non-interior yang TIDAK
  gelap (anti-alias terang / bercak). Ambang 140 dipilih
  jauh di bawah lantai interior (> 200) dan di atas garis
  tepi tergambar, dan terbukti cukup: replika sintetis
  page-c (latar krem, tepi hitam 2 px, geometri blok asli)
  menghasilkan kotak rapat di interior kedua bubble; tes
  yang sama pada kode lama bocor ke (0,0,387×938).
- **Tanpa regresi:** pagar lama tetap utuh — replika page-b
  (reaksi/Sakamoto/toko), awan tepi halaman v0.7.3, elips
  tinggi, persegi — semua lolos dalam pagar yang sama.
  Tes: **91 total — 90 lolos, 0 gagal, 1 skip** (skip lama
  sandbox yang sama); 1 tes regresi baru, gagal pada kode
  lama (dibuktikan dengan stash).

## Status: v0.7.3 — Estimator Bubble Konservatif Multi-Baris

Cacat yang ditemukan verifikasi independen pada sampel asli
baru (halaman berwarna, bubble kiri berbentuk AWAN menempel
tepi halaman): kotak pintar dari chord tengah terlalu lebar
untuk baris atas/bawah — interior bubble pada ketinggian itu
jauh lebih sempit, sehingga huruf pertama baris-baris itu
mepet/luber keluar tepi kiri bubble. Bubble oval biasa tidak
terdampak.

- **EstimateBubbleBox kini mengukur ulang rentang
  horizontal pada ketinggian ⅓ dan ⅔** rentang vertikal hasil
  pindai pusat (aturan pindai interior sama persis, dari x
  pusat blok, pagar 2× kotak OCR tetap berlaku), lalu tepi
  final tiap sisi = **nilai konservatif kedua** dari
  {pusat, ⅓, ⅔} (kiri: terbesar kedua; kanan: terkecil
  kedua). Hasil tidak pernah melebar melewati chord tengah;
  baris sampel yang gagal ukur dibuang. Perlakuan simetris
  untuk rentang vertikal sengaja belum diterapkan (prioritas
  horizontal sesuai temuan).
- **Dampak terukur:** replika sintetis awan di tepi halaman —
  tepi kiri kotak 15 → 52, lebar 331 → 255, tinta di luar
  bubble 239 → 16 px. Geometri page-b yang sudah bagus
  menyusut ≤ 7% (di bawah pagar regresi 10%); persegi panjang
  tidak berubah; elips tinggi 1:2 → lebar ≈ 87% chord
  tengah. Tes: **90 total — 89 lolos, 0 gagal, 1 skip** (skip
  lama sandbox yang sama); 4 tes regresi baru, 3 di antaranya
  gagal pada kode lama.

## Status: v0.7.2 — Petunjuk Prosedur di Aplikasi

Teks petunjuk statis di dalam aplikasi (permintaan Ando):
halaman Terjemahan kini memuat bar petunjuk urutan kerja
"Terjemahkan → Poles AI → Terapkan Otomatis" beserta catatan
Poles AI (perlu penyedia aktif; teks sebelum poles dicadangkan
otomatis, bisa dikembalikan lewat Urungkan Poles), dan halaman
Typesetting memuat pengingat menjalankan Terapkan Otomatis
sesudah teks final. Tanpa perubahan logika.

## Status: v0.7.1 — Perbaikan Typeset Otomatis Pintar

Dua cacat yang ditemukan verifikasi independen v0.7.0 pada
halaman manga asli, diperbaiki:

- **Kata panjang tidak lagi pecah di tengah.** Ukuran hasil
  pencarian biner bersih pada nilai pecahannya (mis. 26,499),
  tetapi pembulatan ukuran tersimpan ke 0,1 bisa melompat ke
  lebar glif ter-hint yang lebih besar (lebar ter-hint tidak
  monoton terhadap ukuran), sehingga pada ukuran final kata
  terpenggal ("Reaksim/u") atau tanda baca yatim ("?" di baris
  sendiri). Kini ukuran final **divalidasi ulang pada nilai
  tersimpan persis** dengan typeface finalnya (keluarga + bold
  yang sama dengan pengukuran auto-fit) dan diturunkan bertahap
  (0,5 px, lantai 14) sampai tata letaknya sendiri bersih —
  muat dan tanpa kata terpecah. Contoh halaman asli: blok
  reaksi 26,5 → 26; "Sakamoto-kun?" 22,5 → 22, utuh satu baris.
- **Partisi bubble gabungan memakai rentang bubble anggota
  sendiri.** Blok yang berbagi satu bubble kini hanya diiris
  pada sumbu partisi; sumbu lainnya memakai rentang interior
  hasil ukur blok itu sendiri, bukan rentang gabungan (union)
  — teks tiap blok kini terpusat di cuping bubble-nya, bukan
  di tengah union (yang membuat teks dua blok bertabrakan
  visual). Tes: **86 total — 85 lolos, 0 gagal, 1 skip**
  (skip lama sandbox yang sama).

## Status: v0.7.0 — Typeset Otomatis Pintar

- **"Terapkan Otomatis" kini serapi typeset manual** (permintaan
  Ando sesudah melihat hasil manual halaman manga asli). Pada
  jalur yang punya bitmap dasar — render `--render`, ekspor,
  dan tombol Terapkan Otomatis Halaman/Bab di UI — gaya diisi
  oleh **jalur pintar** (`SmartAutoTypesetPage`):
  - **Kotak render = interior bubble**, diukur dari halaman
    bersih (pindai interior terang-netral dari pusat kotak OCR
    ke 4 arah) — teks duduk terpusat di bubble, bukan lagi di
    kotak kolom teks OCR. Dua blok dalam satu bubble dideteksi
    (irisan kotak ukur > 35%) dan bubble-nya dipartisi di titik
    tengah antar pusat blok. Pindai dibatasi 2× dimensi kotak
    OCR per arah agar interior putih tanpa batas (halaman
    polos) tidak bocor; pindai gagal → kotak OCR apa adanya.
  - **Ukuran font seragam sehalaman**: target = persentil-75
    ukuran auto-fit semua blok, dijepit 20–28 px; tiap blok
    memakai min(target, fit-nya), minimum 14 px, dan disimpan
    sebagai ukuran eksplisit.
  - **Semua blok horizontal** (termasuk blok OCR vertikal) dan
    **font gaya komik**: keluarga "Comic Neue" + tebal bila
    font itu terpasang di sistem; selain itu sans aman biasa.
    PERUBAHAN ARAH dari v0.5.2: terapkan-otomatis tidak lagi
    mengikuti arah vertikal sumber — arah vertikal tetap bisa
    dipilih manual per blok (centang Vertikal di panel
    properti Typesetting).
  - Aturan lama utuh: blok bergaya eksplisit tidak ditimpa,
    hanya blok berterjemahan yang di-typeset, dan jalur lama
    tanpa bitmap tidak berubah.

## Status: v0.6.1 — Tahap 6 (Ekspor + Packaging) — RENCANA 6 TAHAP TUNTAS

- **v0.6.1 — uji berkas ASLI dari internet + peredam duplikat.**
  Atas usul Ando, pengujian tidak lagi hanya fixture sintetis:
  (1) **CBR asli terverifikasi** — komik public domain *Four
  Color #856 — Buffalo Bill Jr.* (Internet Archive, RAR asli
  24,8 MB) terbaca penuh: **36 halaman**, 1 berkas .SFV di
  dalamnya terlewati beralasan; deteksi di 36 halaman cetak asli
  menemukan **329 blok** (±9/halaman). Kesenjangan lama "CBR
  belum teruji" TUTUP. (2) **OCR di halaman manga Jepang asli**
  (contoh resmi proyek manga-image-translator, 1487×2048):
  13 bubble dialog → 14 blok, sebagian besar terbaca baik
  sampai nyaris persis (mis. 「髪型変えたらすぐに気付いて
  くれてつ」); yang lemah: bubble sangat kecil & teks dekoratif
  (halaman judul bergaya) — batas jujur model OCR cetak.
  (3) Temuan dari uji itu: detektor bisa melaporkan wilayah
  sama dua kali lintas kelas (text_bubble + text_free) —
  kini diredam `DedupeBoxes` (IoU > 0,85, skor tertinggi
  dipertahankan) + tes regresi.

- **Ekspor bab menjadi halaman FINAL** (gambar bersih + teks
  ter-typeset, jalur render yang sama dengan `--render`) ke
  tiga bentuk: **CBZ** (ZIP berisi `page-001…` + ComicInfo.xml
  bab bila ada), **PDF** (satu halaman per gambar, penulis PDF
  bawaan SkiaSharp), atau **Folder** (satu berkas per halaman).
  Format gambar pilihan: **Asli** (format sumber tiap halaman),
  **JPG**, **PNG**, **WebP**; kualitas JPG/WebP bisa diatur
  (bawaan 90). Berkas tujuan yang sudah ada **tidak ditimpa** —
  hasil baru diberi akhiran (2), (3), dst.
- **Halaman Ekspor** di aplikasi: pilihan bentuk keluaran,
  format gambar, kualitas, folder tujuan (dialog pilih folder),
  tombol Ekspor, dan ringkasan hasil (jalur + ukuran). CLI:
  `--export <bab> [--format cbz|pdf|folder] [--img
  asli|jpg|png|webp] [--quality n] [--out <jalur>]`.
- **Packaging Windows**: mode CLI kini menempel ke konsol
  pemanggil (`AttachConsole` khusus Windows; di platform lain
  tidak berpengaruh) sehingga keluaran `--export`/`--render`/
  dll. terlihat saat .exe dipanggil dari terminal.

## Status: v0.5.2 — Tahap 5 (Typesetting) + arah vertikal

- **Perbaikan v0.5.2 — arah typeset mengikuti arah sumber**
  (permintaan Ando). Blok OCR vertikal kini di-typeset
  **vertikal juga** oleh Terapkan Otomatis: terjemahannya lurus
  ke bawah di kotak aslinya ("Terima kasih!" satu kolom dari atas
  ke bawah), bukan dipaksa horizontal. Pas-otomatis vertikal
  mengutamakan **satu kolom** selama ukuran font masih terbaca
  (≥ 12 px); teks yang terlalu panjang jatuh ke banyak kolom
  (kanan → kiri) seperti pakem CJK. Pengguna tetap bebas
  mematikan/menghidupkan **Vertikal** per blok di panel properti.
  Ini menggantikan pendekatan v0.5.1 (melebarkan kotak blok
  vertikal lalu men-typeset horizontal); yang bertahan dari
  v0.5.1: **pas-otomatis horizontal mengutamakan ukuran terbesar
  yang memuat semua kata UTUH** (pemecahan per karakter hanya
  dipakai bila tak ada ukuran yang memuat kata utuh).
- **Teks terjemahan digambar ke halaman bersih** di dalam kotak
  bloknya (`Services/Typeset.cs`, SkiaSharp) → halaman final yang
  bisa dibaca; dasar render = gambar bersih Tahap 3 (atau halaman
  asli bila belum dibersihkan). Proyek naik ke skema **v5** (blok
  + `typeset`); berkas v1–v4 tetap termuat.
- **Pas-otomatis**: ukuran font dicari (biner) terbesar yang muat
  di kotak render sesudah pembungkusan kata — diutamakan tanpa
  memecah kata (lihat perbaikan v0.5.1); ukuran manual dipakai
  apa adanya. Gaya per blok: keluarga font **sistem** (tanpa font
  tertanam berlisensi), bold/italic, warna teks, **outline stroke**
  (lebar + warna), perataan kiri/tengah/kanan, spasi baris & huruf,
  **vertikal gaya CJK** (kolom kanan → kiri), rotasi 0/90/180/270,
  serta geometri kotak (geser Dx/Dy, ubah Dw/Dh).
- **Preset bawaan** (kumpulan properti, bukan berkas font):
  Dialog, Teriak (bold + stroke), Bisik (italic), Narasi (italic).
- **Halaman Typesetting**: kanvas final + panel properti kanan
  bergaya referensi (seksi kapital kecil), daftar blok berstatus,
  klik kanvas untuk memilih, **seret untuk memindahkan** blok,
  toggle **Asli** / **Bersih** untuk membandingkan, tombol
  "Terapkan Otomatis Halaman Ini / Satu Bab". CLI: `--render
  <bab> [--out <folder>]` menulis halaman final PNG per halaman.
- **Di luar cakupan tahap ini (jujur)**: editor poligon bubble
  vektor ala referensi DITUNDA — detektor kami menghasilkan kotak
  teks persegi, bukan poligon bubble; penyesuaian bentuk lewat
  geometri kotak (geser/ubah ukuran) dulu.

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
  (tersimpan otomatis). CLI: `--ocr <bab> [--lang ja|ko|ch|en]`.
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
2. ✅ Deteksi teks & OCR JP/CN/KR/EN (model ONNX publik) + blok teks di proyek
3. ✅ Pembersihan bubble (blok solid + inpaint LaMA + manual, non-destruktif)
4. Terjemahan BYOK (Gemini/Groq/OpenRouter) + lokal (Ollama/LM Studio)
5. Editor typesetting kanvas
6. Ekspor (CBZ/PDF/folder) + packaging .exe
