"""Makes the macOS app icon (Coreloom.icns) from the brand's dark tile, on Apple's icon grid.

The brand tile fills its whole 1024-px canvas; a Mac icon is an 824-px rounded square (Apple's continuous-corner
shape) centred on a 1024-px canvas with a soft shadow below, so it sits at the same size as every other app in the Dock,
Finder and Launchpad. This keeps the tile's artwork, gives it that shape and a fine light edge, and writes:

  src/Coreloom.Mac/Assets/AppIcon.icns     every size macOS asks for (16 px to 1024 px, @1x and @2x)
  assets/branding/macos/coreloom-macos-1024.png (and 512, 256, 128): the same icon as pictures (website, README)

Run it again after changing the brand tile:  python3 build/mac/make-icon.py
"""
from pathlib import Path

from PIL import Image, ImageChops, ImageDraw, ImageFilter

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / "assets/branding/dark/coreloom-dark-1024.png"
ICNS = ROOT / "src/Coreloom.Mac/Assets/AppIcon.icns"
PNG_DIR = ROOT / "assets/branding/macos"

CANVAS = 1024
BODY = 824  # Apple's macOS icon grid: the shape is 824 px wide on a 1024-px canvas
SUPER = 4   # draw the mask 4x larger, then shrink: smooth edges


def squircle_mask(size: int) -> Image.Image:
    """Apple's continuous-corner rounded square, approximated by a superellipse |x|^5 + |y|^5 = 1."""
    big = size * SUPER
    mask = Image.new("L", (big, big), 0)
    n = 5.0
    r = big / 2
    pixels = mask.load()
    for y in range(big):
        dy = abs((y + 0.5 - r) / r) ** n
        if dy >= 1:
            continue
        # the row's half-width where |x|^n = 1 - |y|^n
        half = (1 - dy) ** (1 / n) * r
        x0, x1 = int(round(r - half)), int(round(r + half))
        for x in range(max(0, x0), min(big, x1)):
            pixels[x, y] = 255
    return mask.resize((size, size), Image.LANCZOS)


def main() -> None:
    tile = Image.open(SOURCE).convert("RGBA")
    # The tile's artwork without its own edge (which sits about 14 px inside the canvas edge and isn't centred).
    art = tile.crop((40, 38, 1008, 982)).resize((BODY, BODY), Image.LANCZOS)

    mask = squircle_mask(BODY)
    body = Image.new("RGBA", (BODY, BODY), (0, 0, 0, 0))
    body.paste(art, (0, 0), mask)

    # A fine light edge, brighter at the top, like the brand tile's rim (and macOS's own icons).
    edge = Image.new("RGBA", (BODY, BODY), (0, 0, 0, 0))
    inner = squircle_mask(BODY - 6).resize((BODY - 6, BODY - 6))
    ring = mask.copy()
    hole = Image.new("L", (BODY, BODY), 0)
    hole.paste(inner, (3, 3))
    ring = ImageChops.subtract(ring, hole)
    gradient = Image.new("RGBA", (BODY, BODY))
    draw = ImageDraw.Draw(gradient)
    for y in range(BODY):
        t = y / (BODY - 1)
        # top: a pale blue at 70 %, bottom: the tile's blue at 55 %
        r = int(120 + (40 - 120) * t)
        g = int(160 + (80 - 160) * t)
        b = int(230 + (170 - 230) * t)
        a = int(180 + (140 - 180) * t)
        draw.line([(0, y), (BODY, y)], fill=(r, g, b, a))
    edge.paste(gradient, (0, 0), ring)
    body = Image.alpha_composite(body, edge)

    # The canvas: a soft shadow below the shape, then the shape.
    canvas = Image.new("RGBA", (CANVAS, CANVAS), (0, 0, 0, 0))
    offset = (CANVAS - BODY) // 2
    shadow = Image.new("RGBA", (CANVAS, CANVAS), (0, 0, 0, 0))
    shadow_alpha = Image.new("L", (CANVAS, CANVAS), 0)
    shadow_alpha.paste(mask.point(lambda v: int(v * 0.38)), (offset, offset + 12))
    shadow_alpha = shadow_alpha.filter(ImageFilter.GaussianBlur(14))
    shadow.putalpha(shadow_alpha)
    canvas = Image.alpha_composite(canvas, shadow)
    canvas.alpha_composite(body, (offset, offset))

    PNG_DIR.mkdir(parents=True, exist_ok=True)
    for size in (1024, 512, 256, 128):
        out = canvas if size == CANVAS else canvas.resize((size, size), Image.LANCZOS)
        out.save(PNG_DIR / f"coreloom-macos-{size}.png", optimize=True)

    ICNS.parent.mkdir(parents=True, exist_ok=True)
    sizes = [16, 32, 64, 128, 256, 512]
    canvas.save(ICNS, format="ICNS", append_images=[canvas.resize((s, s), Image.LANCZOS) for s in sizes])
    print(f"wrote {ICNS.relative_to(ROOT)} and {PNG_DIR.relative_to(ROOT)}/coreloom-macos-*.png")


if __name__ == "__main__":
    main()
