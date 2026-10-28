#!/usr/bin/env python3
"""Makes the MacBook picture for the Mac app's Overview header from the supplied mockup (assets/mac-render).

The mockup is a MacBook cut out on transparency with a sample wallpaper on its screen. This keeps the laptop and
opens its screen (the app draws the Mac's own desktop picture there), takes the sample's purple tint off the aluminium
and the keys (the app lights them with the real screen's colors), and writes:

  src/Coreloom.Mac/Assets/MacBook/body.png  the laptop with a transparent screen, half size
  src/Coreloom.Mac/Assets/MacBook/deck.png  where the screen's light falls on the keyboard deck (an alpha mask)

The screen and deck rectangles are printed for MacIllustration.cs.

    python3 build/mac/make-macbook.py
"""
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / "assets" / "mac-render" / "macbook-mockup.png"
OUT = ROOT / "src" / "Coreloom.Mac" / "Assets" / "MacBook"

# Measured on the 1536 × 1024 mockup (pixel edges, with the anti-aliased fraction).
SCREEN = (337.85, 176.75, 1198.1, 731.9)    # left, top, right, bottom
SCREEN_RADIUS = 6.0                          # its top corners
NOTCH = (722.4, 176.0, 813.45, 193.25)       # the camera notch (stays bezel)
NOTCH_RADIUS = 4.0
DECK = (205.0, 743.0, 1331.0, 846.0)         # the keyboard deck, from the hinge to the front edge
HINGE = 757.0                                # where the dark hinge ends and the deck starts
CROP = (176, 148, 1356, 906)                  # the laptop and its contact shadow
SCALE = 0.5
SUPER = 4


def screen_mask(size):
    w, h = size
    big = Image.new("L", (w * SUPER, h * SUPER), 0)
    d = ImageDraw.Draw(big)
    l, t, r, b = (v * SUPER for v in SCREEN)
    rad = SCREEN_RADIUS * SUPER
    d.rounded_rectangle((l, t, r, b), radius=rad, fill=255)
    d.rectangle((l, t + rad, r, b), fill=255)                      # square bottom corners
    nl, nt, nr, nb = (v * SUPER for v in NOTCH)
    d.rounded_rectangle((nl, nt - 40, nr, nb), radius=NOTCH_RADIUS * SUPER, fill=0)
    return big.resize((w, h), Image.LANCZOS)


def main():
    image = Image.open(SOURCE).convert("RGBA")
    rgba = np.asarray(image).astype(np.float32)
    rgb, alpha = rgba[..., :3], rgba[..., 3]
    alpha = np.clip(alpha * (255 / 253), 0, 255)                 # the mockup's body is 253, not quite opaque

    # Neutral aluminium and keys: the sample wallpaper's purple light, taken out (luminance kept).
    luminance = rgb @ np.array([0.2126, 0.7152, 0.0722], dtype=np.float32)
    neutral = np.repeat(luminance[..., None], 3, axis=2)
    cool = neutral * np.array([0.985, 0.995, 1.02], dtype=np.float32)  # a hint of the cool cast aluminium has
    rgb = np.clip(cool, 0, 255)

    screen = np.asarray(screen_mask(image.size)).astype(np.float32) / 255
    alpha = alpha * (1 - screen)

    body = Image.fromarray(np.dstack([rgb, alpha]).astype(np.uint8), "RGBA").crop(CROP)
    size = (round(body.width * SCALE), round(body.height * SCALE))
    OUT.mkdir(parents=True, exist_ok=True)
    body.resize(size, Image.LANCZOS).save(OUT / "body.png", optimize=True)

    # The deck: the laptop's own outline between the hinge and the front edge, strongest by the hinge.
    deck = np.zeros(alpha.shape, np.float32)
    l, t, r, b = DECK
    ys = np.arange(alpha.shape[0], dtype=np.float32)
    # Faint on the dark hinge, strongest just past it, fading toward the front edge.
    hinge = np.clip((ys - t) / 4, 0, 1) * 0.22
    deck_ramp = np.clip((ys - HINGE) / 4, 0, 1) * np.clip((b - ys) / (b - HINGE - 4), 0, 1) ** 1.4
    ramp = np.where(ys < HINGE, hinge, np.maximum(deck_ramp, hinge * np.clip((HINGE + 4 - ys) / 4, 0, 1)))
    deck[:, int(l):int(r)] = ramp[:, None]
    deck *= (rgba[..., 3] / 255) * (1 - screen)
    mask = Image.fromarray((deck * 255).astype(np.uint8), "L").crop(CROP).resize(size, Image.LANCZOS)
    mask = mask.filter(ImageFilter.GaussianBlur(0.6))
    Image.merge("RGBA", (Image.new("L", size, 255),) * 3 + (mask,)).save(OUT / "deck.png", optimize=True)

    def scaled(box):
        return tuple(round((v - CROP[i % 2]) * SCALE, 2) for i, v in enumerate(box))

    print("body", size)
    print("screen", scaled(SCREEN))
    print("deck", scaled(DECK))


if __name__ == "__main__":
    main()
