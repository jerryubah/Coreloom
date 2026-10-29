#!/usr/bin/env python3
"""Puts screens of Coreloom's phone app into the iPhone mockup for the website (site/assets/phone-*.webp).

The mockup (assets/site-render/iphone-mockup.png) is an iPhone on black with a sample screen. This cuts the phone out
(its outline and side buttons; the black and the glow around it go), replaces the screen with a screenshot of the phone
app, and draws iOS's status bar and the Dynamic Island over it, so the page can light the phone with its own glow.

The screenshots are the phone app at 393 × 852 points, 3× (an iPhone 15 Pro), with iOS's safe areas applied (59 pt at
the top for the status bar, 34 pt at the bottom for the home indicator), as Coreloom shows them added to the Home
Screen: {theme}-{tab}.png, e.g. dark-overview.png.

    python3 build/site/make-phone-mockups.py <screenshots folder>
"""
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[2]
MOCKUP = ROOT / "assets" / "site-render" / "iphone-mockup.png"
FONT = ROOT / "src" / "Coreloom.App" / "Fonts" / "Inter-SemiBold.ttf"
OUT = ROOT / "site" / "assets"

# Measured on the 1024 × 1536 mockup (pixel edges, with the anti-aliased fraction).
PHONE = (226.5, 177.5, 787.5, 1340.5)    # the frame's outside
PHONE_RADIUS = 100.0
SCREEN = (251.5, 200.5, 763.5, 1317.5)   # the display
SCREEN_RADIUS = 72.0
BUTTONS = [(218.0, 370.0, 227.0, 660.0), (787.0, 500.0, 797.0, 670.0)]  # where the side buttons stand out
SCALE = 1.25                              # output size: the phone about 700 px wide
SUPER = 4

SCREENS = [("overview", "dark"), ("overlay", "dark"), ("processor", "dark"), ("overview", "light"), ("overlay", "light"), ("processor", "light")]


def rounded(size, box, radius, scale):
    """An anti-aliased rounded-rectangle mask in the output's pixels."""
    w, h = size
    big = Image.new("L", (w * SUPER, h * SUPER), 0)
    l, t, r, b = (v * scale * SUPER for v in box)
    ImageDraw.Draw(big).rounded_rectangle((l, t, r, b), radius=radius * scale * SUPER, fill=255)
    return big.resize(size, Image.LANCZOS)


def status_bar(shot, dark):
    """iOS's status bar (9:41, signal, Wi-Fi, battery) and the Dynamic Island, on a 3× screenshot."""
    ink = (255, 255, 255, 255) if dark else (0, 0, 0, 255)
    d = ImageDraw.Draw(shot)
    k = 3  # points to pixels
    font = ImageFont.truetype(str(FONT), 17 * k)
    middle = 29.5 * k
    d.text((63 * k, middle), "9:41", font=font, fill=ink, anchor="mm")

    # Signal: four bars, rising.
    x = 300 * k
    for i in range(4):
        height = (4 + i * 2.6) * k
        d.rounded_rectangle((x + i * 5.2 * k, middle + 5.5 * k - height, x + i * 5.2 * k + 3.2 * k, middle + 5.5 * k), radius=1 * k, fill=ink)

    # Wi-Fi: three arcs and a dot.
    cx, cy = 333 * k, middle + 5.5 * k
    for i, r in enumerate((11.5, 7.6, 3.6)):
        r *= k
        if i < 2:
            d.arc((cx - r, cy - r, cx + r, cy + r), start=225, end=315, fill=ink, width=int(2.3 * k))
        else:
            d.pieslice((cx - r, cy - r, cx + r, cy + r), start=225, end=315, fill=ink)

    # Battery: outline, charge and the nub.
    bx, by = 352 * k, middle - 6.5 * k
    d.rounded_rectangle((bx, by, bx + 25 * k, by + 13 * k), radius=4 * k, outline=ink[:3] + (115,), width=int(1 * k))
    d.rounded_rectangle((bx + 2 * k, by + 2 * k, bx + 21 * k, by + 11 * k), radius=2.5 * k, fill=ink)
    d.rounded_rectangle((bx + 26 * k, by + 4.5 * k, bx + 27.5 * k, by + 8.5 * k), radius=1 * k, fill=ink[:3] + (115,))

    # The Dynamic Island.
    w, top, h = 126 * k, 11 * k, 37 * k
    left = (shot.width - w) / 2
    d.rounded_rectangle((left, top, left + w, top + h), radius=h / 2, fill=(0, 0, 0, 255))
    return shot


def main():
    shots = Path(sys.argv[1]) if len(sys.argv) > 1 else Path("phonemock")
    mockup = Image.open(MOCKUP).convert("RGBA")
    size = (round(mockup.width * SCALE), round(mockup.height * SCALE))
    phone = mockup.resize(size, Image.LANCZOS)

    # The phone's outline: its frame and the side buttons (pixels brighter than the glow around them).
    alpha = np.asarray(rounded(size, PHONE, PHONE_RADIUS, SCALE)).astype(np.float32) / 255
    lum = np.asarray(phone.convert("L")).astype(np.float32)
    for l, t, r, b in BUTTONS:
        l, t, r, b = (round(v * SCALE) for v in (l, t, r, b))
        region = np.clip((lum[t:b, l:r] - 62) / 40, 0, 1)
        alpha[t:b, l:r] = np.maximum(alpha[t:b, l:r], region)
    screen_mask = rounded(size, SCREEN, SCREEN_RADIUS, SCALE)

    l, t, r, b = (v * SCALE for v in PHONE)
    crop = (int(l) - 2, int(t) - 2, int(np.ceil(r)) + 2, int(np.ceil(b)) + 2)
    for tab, theme in SCREENS:
        source = shots / f"{theme}-{tab}.png"
        if not source.exists():
            print("missing", source)
            continue

        shot = status_bar(Image.open(source).convert("RGBA"), theme == "dark")
        sl, st, sr, sb = (v * SCALE for v in SCREEN)
        width, height = sr - sl, sb - st
        fit = max(width / shot.width, height / shot.height)
        scaled = shot.resize((round(shot.width * fit), round(shot.height * fit)), Image.LANCZOS)
        ox, oy = (scaled.width - round(width)) // 2, (scaled.height - round(height)) // 2
        scaled = scaled.crop((ox, oy, ox + round(width), oy + round(height)))

        layer = Image.new("RGBA", size, (0, 0, 0, 0))
        layer.paste(scaled, (round(sl), round(st)))
        out = Image.composite(layer, phone, screen_mask)
        rgba = np.asarray(out).astype(np.float32)
        rgba[..., 3] = alpha * 255
        result = Image.fromarray(rgba.astype(np.uint8), "RGBA").crop(crop)
        target = OUT / f"phone-{tab}{'' if theme == 'dark' else '-light'}.webp"
        result.save(target, "WEBP", quality=90, method=6)
        print(target.relative_to(ROOT), result.size, f"{target.stat().st_size // 1024} KB")


if __name__ == "__main__":
    main()
