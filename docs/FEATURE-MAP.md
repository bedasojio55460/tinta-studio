# Peta Fitur — Chisa Translator → Tinta Studio

Referensi diamati dari situs resmi chisa.id (2026-10-26) + installer
ChisaTL_3.0.2.exe yang hanya dibedah statis (tidak dijalankan).
Semua padanan di bawah ditulis ulang dari nol dengan komponen publik.

| Fitur referensi | Versi Tinta Studio | Komponen publik | Tahap |
|---|---|---|---|
| Buka folder gambar, PDF, CBZ, CBR, ZIP, RAR | Impor otomatis: deteksi format dari magic bytes (anti salah label), urutan halaman natural, saring sampah arsip, ratakan folder bersarang, baca ComicInfo.xml | SharpCompress + System.IO.Compression, PDFtoImage/PDFium, SkiaSharp, LibTiff | 1 |
| Simpan proyek .chi (lapisan & teks bisa disunting) | Proyek `.tinta.json` milik sendiri (bab, halaman, blok teks, typeset) | System.Text.Json | 1 |
| Penampil/penyunting halaman di kanvas | Penampil halaman + daftar miniatur; editor kanvas penuh di halaman Typesetting (Tahap 5) | Avalonia + SkiaSharp | 1, 5 |
| Deteksi teks JP/CN/KR + bubble | Deteksi blok teks & bubble → daftar blok per halaman | Model ONNX publik (deteksi teks komik, PaddleOCR / manga-ocr) via Microsoft.ML.OnnxRuntime | 2 |
| Bersihkan bubble: blok solid + inpaint | Blok warna solid (eyedropper) + inpaint LaMA + kuas manual | LaMA ONNX (OnnxRuntime) + SkiaSharp | 3 |
| Terjemahan BYOK: Gemini, GPT-4o, OpenRouter | BYOK Gemini → Groq → OpenRouter (key pengguna; pola Gema) | API publik penyedia | 4 |
| Terjemahan lokal Ollama / LM Studio (offline) | Backend lokal Ollama / LM Studio | API lokal pengguna | 4 |
| Konteks satu halaman (dialog natural) | Prompt konteks halaman penuh (semua blok sehalaman satu prompt bernomor) | — | 4 |
| Typesetting: preset font, teks vertikal CJK, outline stroke, spasi huruf | ✅ Editor kanvas: teks terjemahan digambar ke halaman bersih; preset Dialog/Teriak/Bisik/Narasi, font sistem, pas-otomatis, vertikal CJK, stroke, spasi baris/huruf, rotasi, geser/ubah kotak; klik & seret di kanvas; CLI `--render` | SkiaSharp (render teks) | 5 |
| Outline bubble vektor (anchor, kurva, cusp/smooth) | **DITUNDA** (jujur): detektor kami menghasilkan kotak teks persegi, bukan poligon bubble — penyesuaian bentuk lewat geometri kotak (geser Dx/Dy, ubah Dw/Dh) dulu; editor poligon menyusul bila detektor poligon tersedia | Kanvas sendiri | 5+ |
| Ekspor bab gambar | ✅ Ekspor CBZ / PDF / folder dari halaman final (bersih + typeset); format gambar pilihan (asli/JPG/WebP/PNG) + kualitas; ComicInfo.xml ikut di CBZ; tidak menimpa diam-diam; halaman Ekspor + CLI `--export` | SkiaSharp (termasuk SKDocument PDF), System.IO.Compression | 6 |
| Lisensi langganan + trial | **Tidak ditiru** — aplikasi milik sendiri, tanpa sistem lisensi | — | — |

Status: Tahap 1 ✅ (v0.1.0), Tahap 2 ✅ (v0.2.0), Tahap 3 ✅ (v0.3.0 —
blok solid + eyedropper, inpaint LaMA manga ONNX, kuas/penghapus
manual; semua non-destruktif, operasi tersimpan di proyek v3),
Tahap 4 ✅ (v0.4.0 — terjemahan dua mode otomatis/manual, BYOK
Gemini/Groq/OpenRouter multi-key + rotasi & cooldown, lokal
Ollama/LM Studio, konteks satu halaman, Poles AI + urungkan,
proyek v4). Tahap 5 ✅ (v0.5.0 — typesetting: render teks ke
halaman bersih, editor kanvas + panel properti, preset & gaya
per blok, proyek v5). Tahap 6 ✅ (v0.6.0 — ekspor CBZ/PDF/folder
dari halaman final + halaman Ekspor + CLI `--export` +
AttachConsole untuk CLI di Windows) — **rencana 6 tahap tuntas**.
| Maskot & branding Chisa | **Tidak disalin** — nama & logo original (Tinta Studio) | Logo programatis | 1 |
