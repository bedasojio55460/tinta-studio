# Katalog Model — Deteksi & OCR (Tahap 2)

Semua model adalah model **publik berlisensi terbuka**, diunduh
terprogram ke `~/.cache/tinta-studio/models/` oleh
`tools/fetch_models.sh` (idempotent, verifikasi SHA-256) atau tombol
unduh di aplikasi (halaman Deteksi & OCR → kartu Model). Model tidak
masuk git. Runtime: **Microsoft.ML.OnnxRuntime** (CPU).

| Berkas cache | Peran | Sumber (Hugging Face) | Ukuran (byte) | SHA-256 |
|---|---|---|---|---|
| `comic-text-detector.onnx` | Deteksi blok teks komik | [ogkalu/comic-text-and-bubble-detector](https://huggingface.co/ogkalu/comic-text-and-bubble-detector) berkas `detector-v4-s_int8.onnx` | 11.120.765 | `5fe9e4f576e49d4e7e8b0e029d6d3cdc252abd4694113e1cae120e62c931ea79` |
| `rec-ja.onnx` + `rec-ja.dict.txt` | OCR Jepang (PP-OCRv4 rec) | [cycloneboy/japan_PP-OCRv4_rec_infer](https://huggingface.co/cycloneboy/japan_PP-OCRv4_rec_infer) berkas `model.onnx` + `japan_dict.txt` (4.399 karakter) | 9.735.983 / 21.731 | `bb0f23444c059d27a615c1a426414240bbd02d3658900d0455e007dc47d3e4a4` / `2a0842e81ef31d6a99d811a36fc12c33e37aa9406667ef1bd9b468f9108a66bf` |
| `rec-ko.onnx` + `rec-ko.dict.txt` | OCR Korea (PP-OCRv5 mobile rec) | [monkt/paddleocr-onnx](https://huggingface.co/monkt/paddleocr-onnx) `languages/korean/` | 13.401.252 / 47.451 | `322f140154c820fcb83c3d24cfe42c9ec70dd1a1834163306a7338136e4f1eaa` / `a88071c68c01707489baa79ebe0405b7beb5cca229f4fc94cc3ef992328802d7` |
| `rec-ch.onnx` + `rec-ch.dict.txt` | OCR Cina (PP-OCRv5 server rec) | [monkt/paddleocr-onnx](https://huggingface.co/monkt/paddleocr-onnx) `languages/chinese/` | 84.468.836 / 74.012 | `26fa4f47060f58e25962b9af6beaee05c8182b90e026c4ecc6db165d9dfdc38a` / `d1979e9f794c464c0d2e0b70a7fe14dd978e9dc644c0e71f14158cdf8342af1b` |
| `cls-textline.onnx` | Klasifikasi arah garis 0°/180° (PP-LCNet, PP-OCRv5) | [bukuroo/PPOCRv5-ONNX](https://huggingface.co/bukuroo/PPOCRv5-ONNX) berkas `ppocrv5-cls.onnx` | 582.663 | `f4bb53707100c5f3d59ba834eb05bb400369f20aed35d4b26807b1bfadd2a70e` |

## Catatan pemilihan

- **Detektor**: keluarga *comic-text-detector* versi YOLO murni yang
  populer ternyata tidak menyediakan varian ONNX v8n/s yang ringan &
  terawat di HF; ekspor yang hidup dan paling banyak dipakai adalah
  **RT-DETR-v2 int8** dari repo ogkalu (kelas `bubble`,
  `text_bubble`, `text_free` — dilatih ±11 ribu halaman manga/webtoon/
  manhua/komik barat, lisensi Apache-2.0). RT-DETR tidak butuh NMS
  (query teratas per skor); tetap dipasang NMS pengaman IoU 0,6 sekelas.
- **OCR Jepang**: `monkt/paddleocr-onnx` tidak menyediakan bahasa
  Jepang, jadi model rec Jepang diambil dari konversi PP-OCRv4
  (cycloneboy) yang menyertakan kamusnya. Model `ch` PP-OCRv5
  sebenarnya juga memuat aksara Jepang, tetapi model khusus ja memberi
  hasil lebih baik untuk kana.

## Kontrak I/O (hasil kalibrasi empiris, bukan asumsi)

- Detektor: masukan `images` float32 `[N,3,640,640]` — halaman
  **diubah ukurannya langsung** ke 640×640 (model dilatih pada gambar
  yang di-resize, bukan letterbox), normalisasi ImageNet
  (mean 0.485/0.456/0.406, std 0.229/0.224/0.225). Masukan kedua
  `orig_target_sizes` int64 `[N,2]` **harus berisi [lebar, tinggi]**
  halaman asal (terbalik dari konvensi HF transformers — terverifikasi
  dengan halaman kalibrasi: kotak keluaran `boxes` adalah xyxy dalam
  piksel halaman asal, `scores` 0–1, `labels` indeks kelas).
- Rec PaddleOCR: masukan `x` float32 `[1,3,H,W dinamis]`; H dibaca dari
  metadata model (48 untuk ja/ko/ch di katalog ini). Pra-proses:
  tinggi diubah ke H menjaga rasio, kanal **BGR**, normalisasi
  `(v/255 − 0,5)/0,5`. Keluaran **sudah berupa probabilitas softmax**
  (baris berjumlah 1) — jangan di-softmax ulang; keyakinan = rata-rata
  probabilitas maksimum pada langkah yang terpilih. Dekode CTC rakus:
  indeks 0 = blank, 1..N = karakter kamus, indeks terakhir
  (bila jumlah kelas = kamus+2) = spasi.
- Teks vertikal: potongan yang lebih tinggi daripada lebar diputar
  90° (tepi atas menjadi tepi kiri) lalu di-OCR sebagai teks
  horizontal; kedua arah putar dicoba dan dipilih yang keyakinannya
  lebih tinggi.
