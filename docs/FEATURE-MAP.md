# Peta Fitur — Chisa Translator → Tinta Studio

Referensi diamati dari situs resmi chisa.id (2026-10-26) + installer
ChisaTL_3.0.2.exe yang hanya dibedah statis (tidak dijalankan).
Semua padanan di bawah ditulis ulang dari nol dengan komponen publik.

| Fitur referensi | Versi Tinta Studio | Komponen publik | Tahap |
|---|---|---|---|
| Buka folder gambar, PDF, CBZ, CBR, ZIP, RAR | Impor otomatis: deteksi format dari magic bytes (anti salah label), urutan halaman natural, saring sampah arsip, ratakan folder bersarang, baca ComicInfo.xml | SharpCompress + System.IO.Compression, PDFtoImage/PDFium, SkiaSharp, LibTiff | 1 |
| Simpan proyek .chi (lapisan & teks bisa disunting) | Proyek `.tinta.json` milik sendiri (bab, halaman, blok teks, typeset) | System.Text.Json | 1 |
| Penampil/penyunting halaman di kanvas | Penampil halaman + daftar miniatur; editor penuh menyusul di typesetting | Avalonia + SkiaSharp | 1, 5 |
| Deteksi teks JP/CN/KR + bubble | Deteksi blok teks & bubble → daftar blok per halaman | Model ONNX publik (deteksi teks komik, PaddleOCR / manga-ocr) via Microsoft.ML.OnnxRuntime | 2 |
| Bersihkan bubble: blok solid + inpaint | Blok warna solid (eyedropper) + inpaint LaMA + kuas manual | LaMA ONNX (OnnxRuntime) + SkiaSharp | 3 |
| Terjemahan BYOK: Gemini, GPT-4o, OpenRouter | BYOK Gemini → Groq → OpenRouter (key pengguna; pola Gema) | API publik penyedia | 4 |
| Terjemahan lokal Ollama / LM Studio (offline) | Backend lokal Ollama / LM Studio | API lokal pengguna | 4 |
| Konteks satu halaman (dialog natural) | Prompt konteks halaman penuh + konteks bab | — | 4 |
| Typesetting: preset font, teks vertikal CJK, outline stroke, spasi huruf | Editor kanvas: kotak teks, preset font, vertikal, stroke, spasi | SkiaSharp (render teks) | 5 |
| Outline bubble vektor (anchor, kurva, cusp/smooth) | Poligon bubble dapat disunting (versi sederhana dulu) | Kanvas sendiri | 5 |
| Ekspor bab gambar | Ekspor CBZ / PDF / folder; format gambar pilihan (asli/JPG/WebP/PNG) | SkiaSharp, SharpCompress | 6 |
| Lisensi langganan + trial | **Tidak ditiru** — aplikasi milik sendiri, tanpa sistem lisensi | — | — |
| Maskot & branding Chisa | **Tidak disalin** — nama & logo original (Tinta Studio) | Logo programatis | 1 |
