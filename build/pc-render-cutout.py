#!/usr/bin/env python3
"""
Cuts the PC out of a render that has a background (assets/pc-render/render-source.png) and writes
assets/pc-render/render.png with a transparent background, for build/pc-render.py.

The background goes with a segmentation model (rembg's isnet-general-use); then the case is made solid inside its
outline (the model thinks the glass edge, the memory and the perforated side are partly see-through) and the feet,
which it leaves half see-through, are made solid too. Nothing of the floor or its reflection stays.

Usage:
  python3 build/pc-render-cutout.py

Needs Python 3.10+, numpy, Pillow, opencv-python-headless and rembg (pip install rembg; it downloads the model once).
If the render changes, re-check FEET (boxes around the feet, source pixels) and BOTTOM (the row where the case's
bottom edge starts).
"""
from __future__ import annotations

from pathlib import Path

import cv2
import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parent.parent
SOURCE = ROOT / "assets" / "pc-render" / "render-source.png"
TARGET = ROOT / "assets" / "pc-render" / "render.png"

BOTTOM = 1120                                                          # above this row the case is one solid shape
FEET = [(134, 1140, 220, 1184), (803, 1165, 892, 1208), (1001, 1148, 1077, 1183)]
FLOOR = 1218                                                           # nothing below this row


def smoothstep(x: np.ndarray, lo: float, hi: float) -> np.ndarray:
    t = np.clip((x - lo) / (hi - lo), 0, 1)
    return t * t * (3 - 2 * t)


def main() -> None:
    from rembg import new_session, remove

    source = Image.open(SOURCE).convert("RGB")
    mask = np.asarray(remove(source, session=new_session("isnet-general-use"), only_mask=True, post_process_mask=False),
                      dtype=np.float32) / 255

    # Solid inside the case's outline (its convex hull above the bottom edge), the model's soft edge kept outside it.
    body = (mask > 0.5).astype(np.uint8)
    body[BOTTOM:, :] = 0
    contours, _ = cv2.findContours(body, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_NONE)
    hull = np.zeros(mask.shape, np.uint8)
    cv2.fillPoly(hull, [cv2.convexHull(np.vstack(contours))], 1)
    hull[BOTTOM:, :] = 0
    hull = cv2.GaussianBlur(cv2.erode(hull, np.ones((5, 5), np.uint8)).astype(np.float32), (0, 0), 1.0)
    mask = np.maximum(mask, hull)

    # The feet: partial coverage inside each foot's box becomes full.
    for x0, y0, x1, y1 in FEET:
        part = mask[y0:y1, x0:x1]
        mask[y0:y1, x0:x1] = np.maximum(part, smoothstep(part, 0.18, 0.42))
    mask[FLOOR:, :] = 0

    alpha = (np.clip(mask, 0, 1) * 255 + 0.5).astype(np.uint8)
    Image.fromarray(np.dstack([np.asarray(source), alpha]), "RGBA").save(TARGET, optimize=True)
    print(f"Wrote {TARGET.relative_to(ROOT)} ({source.width} x {source.height})")


if __name__ == "__main__":
    main()
