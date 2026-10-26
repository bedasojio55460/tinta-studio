#!/usr/bin/env python3
"""Buat logo original Tinta Studio (motif tetes tinta + mata pena, teal).

Aset digambar programatis dengan Pillow — bukan turunan logo/branding
produk lain. Keluaran: Assets/logo.png (256x256).
"""
from pathlib import Path

from PIL import Image, ImageDraw

SIZE = 256
BG = (11, 18, 32, 255)        # slate sangat gelap
TEAL = (13, 148, 136, 255)    # aksen teal #0d9488
TEAL_LIGHT = (45, 212, 191, 255)
DARK = (8, 12, 20, 255)

img = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
d = ImageDraw.Draw(img)

# Lencana persegi membulat
d.rounded_rectangle([10, 10, SIZE - 10, SIZE - 10], radius=52, fill=BG, outline=TEAL, width=6)

# Tetes tinta: segitiga ekor + lingkaran badan
cx = SIZE // 2
d.polygon([(cx, 44), (cx - 46, 138), (cx + 46, 138)], fill=TEAL)
d.ellipse([cx - 58, 96, cx + 58, 212], fill=TEAL)
# Sorotan pada tetes
d.ellipse([cx - 34, 128, cx - 8, 172], fill=TEAL_LIGHT)

# Lubang mata pena di tengah bawah tetes
d.ellipse([cx - 13, 156, cx + 13, 182], fill=DARK)
d.line([cx, 182, cx, 204], fill=DARK, width=5)

out = Path(__file__).resolve().parent.parent / "Assets" / "logo.png"
out.parent.mkdir(parents=True, exist_ok=True)
img.save(out)
print("logo:", out)
