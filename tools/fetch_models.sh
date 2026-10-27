#!/usr/bin/env bash
# Unduh model ONNX publik untuk Deteksi & OCR (Tahap 2) + Inpaint
# LaMA (Tahap 3) ke cache lokal.
# Idempotent: berkas yang hash-nya sudah cocok dilewati. Model TIDAK masuk
# git — katalog + hash terdokumentasi di docs/models.md.
set -euo pipefail

DEST="${TINTA_MODEL_DIR:-$HOME/.cache/tinta-studio/models}"
mkdir -p "$DEST"

# nama|url|sha256
MODELS=(
  "comic-text-detector.onnx|https://huggingface.co/ogkalu/comic-text-and-bubble-detector/resolve/main/detector-v4-s_int8.onnx|5fe9e4f576e49d4e7e8b0e029d6d3cdc252abd4694113e1cae120e62c931ea79"
  "rec-ja.onnx|https://huggingface.co/cycloneboy/japan_PP-OCRv4_rec_infer/resolve/main/model.onnx|bb0f23444c059d27a615c1a426414240bbd02d3658900d0455e007dc47d3e4a4"
  "rec-ja.dict.txt|https://huggingface.co/cycloneboy/japan_PP-OCRv4_rec_infer/resolve/main/japan_dict.txt|2a0842e81ef31d6a99d811a36fc12c33e37aa9406667ef1bd9b468f9108a66bf"
  "rec-ko.onnx|https://huggingface.co/monkt/paddleocr-onnx/resolve/main/languages/korean/rec.onnx|322f140154c820fcb83c3d24cfe42c9ec70dd1a1834163306a7338136e4f1eaa"
  "rec-ko.dict.txt|https://huggingface.co/monkt/paddleocr-onnx/resolve/main/languages/korean/dict.txt|a88071c68c01707489baa79ebe0405b7beb5cca229f4fc94cc3ef992328802d7"
  "rec-ch.onnx|https://huggingface.co/monkt/paddleocr-onnx/resolve/main/languages/chinese/rec.onnx|26fa4f47060f58e25962b9af6beaee05c8182b90e026c4ecc6db165d9dfdc38a"
  "rec-ch.dict.txt|https://huggingface.co/monkt/paddleocr-onnx/resolve/main/languages/chinese/dict.txt|d1979e9f794c464c0d2e0b70a7fe14dd978e9dc644c0e71f14158cdf8342af1b"
  "rec-en.onnx|https://huggingface.co/monkt/paddleocr-onnx/resolve/main/languages/english/rec.onnx|4e16deb22c4da6468bdca539b2cd3c8687825538b67109177c47d359ab994cd7"
  "rec-en.dict.txt|https://huggingface.co/monkt/paddleocr-onnx/resolve/main/languages/english/dict.txt|e025a66d31f327ba0c232e03f407ae8d105e1e709e7ccb3f408aa778c24e70d6"
  "cls-textline.onnx|https://huggingface.co/bukuroo/PPOCRv5-ONNX/resolve/main/ppocrv5-cls.onnx|f4bb53707100c5f3d59ba834eb05bb400369f20aed35d4b26807b1bfadd2a70e"
  "inpaint-lama.onnx|https://huggingface.co/mayocream/lama-manga-onnx/resolve/main/lama-manga.onnx|4512adab295ee5a5e02ccd1bdf8d45dccbac88309d9cff1532ffd5de876f02a4"
)

for entry in "${MODELS[@]}"; do
  IFS='|' read -r name url sha <<<"$entry"
  target="$DEST/$name"
  if [[ -f "$target" ]] && echo "$sha  $target" | sha256sum -c --quiet 2>/dev/null; then
    echo "OK (sudah ada): $name"
    continue
  fi
  echo "Mengunduh $name …"
  curl -sSL --fail -o "$target.part" "$url"
  if ! echo "$sha  $target.part" | sha256sum -c --quiet; then
    echo "GAGAL: SHA-256 tidak cocok untuk $name" >&2
    rm -f "$target.part"
    exit 1
  fi
  mv "$target.part" "$target"
  echo "Selesai: $name"
done
echo "Semua model siap di $DEST"
