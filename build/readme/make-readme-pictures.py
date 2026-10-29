#!/usr/bin/env python3
"""Makes the README's pictures of the app (assets/readme/coreloom-hero.webp and coreloom-backgrounds.webp).

  coreloom-hero.webp         Coreloom on Windows and, in front of it, on a Mac, on Coreloom's gradient.
  coreloom-backgrounds.webp  The Mac's Overview cut into three bands, each over another background (Coreloom's
                             gradient, Aurora, Dunes): the same window, three looks.

From the same 2x window pictures as the website (build/site/make-site-shots.py): the MacShots folder with
overview.png, backdrop-aurora.png and backdrop-dunes.png, and the PageShots folder with windows-overview.png.

    python3 build/readme/make-readme-pictures.py <MacShots folder> <PageShots folder>
"""
import importlib.util
import sys
from pathlib import Path

from PIL import Image, ImageChops, ImageDraw, ImageFilter

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "assets" / "readme"

spec = importlib.util.spec_from_file_location("site_shots", ROOT / "build" / "site" / "make-site-shots.py")
site_shots = importlib.util.module_from_spec(spec)
spec.loader.exec_module(site_shots)


def rounded(image, radius):
    mask = Image.new("L", image.size, 0)
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, image.width - 1, image.height - 1), radius, fill=255)
    out = image.convert("RGBA")
    out.putalpha(ImageChops.multiply(out.getchannel("A"), mask))
    return out


def window(image, width, radius=22):
    """A window picture at a width, with rounded corners and a hairline rim."""
    image = image.convert("RGB").resize((width, round(image.height * width / image.width)), Image.LANCZOS)
    image = rounded(image, radius)
    rim = Image.new("RGBA", image.size, (0, 0, 0, 0))
    ImageDraw.Draw(rim).rounded_rectangle((0, 0, image.width - 1, image.height - 1), radius, outline=(170, 190, 255, 70), width=2)
    return Image.alpha_composite(image, rim)


def shadow(size, radius, blur=46, alpha=150):
    layer = Image.new("RGBA", (size[0] + blur * 4, size[1] + blur * 4), (0, 0, 0, 0))
    ImageDraw.Draw(layer).rounded_rectangle((blur * 2, blur * 2, blur * 2 + size[0], blur * 2 + size[1]), radius, fill=(0, 0, 0, alpha))
    return layer.filter(ImageFilter.GaussianBlur(blur)), blur * 2


def stage(width, height):
    """Coreloom's colors: near-black with a blue light at the top left and a violet one at the bottom right."""
    import numpy as np
    y, x = np.mgrid[0:height, 0:width].astype(np.float32)
    u, v = x / height, y / height
    base = np.array([7, 8, 13], np.float32) / 255
    img = np.ones((height, width, 3), np.float32) * base
    for cx, cy, r, color, k in [(0.15 * width / height, -0.1, 1.0, (0.36, 0.55, 1.0), 0.32), (0.95 * width / height, 1.1, 0.9, (0.54, 0.42, 1.0), 0.26)]:
        g = np.exp(-2.2 * ((u - cx) ** 2 + (v - cy) ** 2) / (r * r))[..., None]
        img += np.array(color, np.float32) * g * k
    img += np.random.default_rng(3).normal(0, 0.006, img.shape[:2])[..., None]
    return Image.fromarray((np.clip(img, 0, 1) * 255 + 0.5).astype("uint8"), "RGB").convert("RGBA")


def hero(mac, windows):
    W, H = 2400, 1420
    canvas = stage(W, H)
    back = window(windows, 1640)
    front = window(site_shots.window_buttons(mac), 1640)
    for picture, (x, y) in [(back, (90, 70)), (front, (W - 1640 - 90, H - front.height - 60))]:
        s, pad = shadow(picture.size, 22)
        canvas.alpha_composite(s, (x - pad, y - pad + 18))
        canvas.alpha_composite(picture, (x, y))
    return canvas


def bands(pictures, names):
    """The same window in three bands, each over another background, a thin light between them."""
    base = [p.convert("RGB") for p in pictures]
    w, h = base[0].size
    out = Image.new("RGB", (w, h))
    edges = [0, w // 3, 2 * w // 3, w]
    for i, picture in enumerate(base):
        out.paste(picture.crop((edges[i], 0, edges[i + 1], h)), (edges[i], 0))
    d = ImageDraw.Draw(out)
    for x in edges[1:-1]:
        d.line((x, 0, x, h), fill=(150, 165, 215), width=2)
    return site_shots.window_buttons(out)


def save(image, name, width):
    image = image.convert("RGB").resize((width, round(image.height * width / image.width)), Image.LANCZOS)
    target = OUT / f"{name}.webp"
    image.save(target, "WEBP", quality=86, method=6)
    print(target.relative_to(ROOT), image.size, f"{target.stat().st_size // 1024} KB")


def main():
    mac, windows = Path(sys.argv[1]), Path(sys.argv[2])
    save(hero(Image.open(mac / "overview.png"), Image.open(windows / "windows-overview.png")), "coreloom-hero", 2000)
    pictures = [Image.open(mac / n) for n in ("overview.png", "backdrop-aurora.png", "backdrop-dunes.png")]
    save(bands(pictures, ["Coreloom", "Aurora", "Dunes"]), "coreloom-backgrounds", 1800)


if __name__ == "__main__":
    main()
