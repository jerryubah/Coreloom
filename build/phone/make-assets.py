"""Makes the phone app's icons and web fonts (src/Coreloom.Phone/icons, src/Coreloom.Phone/fonts).

Icons: from the brand's dark tile (assets/branding/dark/coreloom-dark-1024.png). A phone draws its own rounded
corners, so the tile is cropped inside its own rounded edge and its corners filled with the tile's colour:
  apple-touch-icon.png (180 px, iOS Home Screen), icon-192.png and icon-512.png (Android, the manifest),
  icon-maskable-512.png (the artwork inside Android's safe zone), favicon-32.png.
Fonts: Inter (SIL OFL, src/Coreloom.App/Fonts) as WOFF2, Latin and the symbols Coreloom shows (° · – — … × ✓).

Run again after changing the tile:  python3 build/phone/make-assets.py   (needs Pillow, fonttools and brotli)
"""
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
TILE = ROOT / "assets/branding/dark/coreloom-dark-1024.png"
OUT = ROOT / "src/Coreloom.Phone"


def square_tile() -> Image.Image:
    tile = Image.open(TILE).convert("RGBA")
    alpha = tile.getchannel("A").point(lambda a: 255 if a > 250 else 0)
    left, top, right, bottom = alpha.getbbox()
    inset = 40  # inside the light rim
    left, top, right, bottom = left + inset, top + inset, right - inset, bottom - inset
    side = min(right - left, bottom - top)
    cx, cy = (left + right) // 2, (top + bottom) // 2
    box = (cx - side // 2, cy - side // 2, cx - side // 2 + side, cy - side // 2 + side)
    crop = tile.crop(box)
    # The colour just inside the edge, for the corners the tile rounds off.
    fill = crop.getpixel((side // 2, 6))[:3]
    background = Image.new("RGBA", crop.size, fill + (255,))
    background.alpha_composite(crop)
    return background.convert("RGB"), fill


def main() -> None:
    icon, fill = square_tile()
    (OUT / "icons").mkdir(parents=True, exist_ok=True)
    for name, size in [("apple-touch-icon.png", 180), ("icon-192.png", 192), ("icon-512.png", 512), ("favicon-32.png", 32)]:
        icon.resize((size, size), Image.LANCZOS).save(OUT / "icons" / name, optimize=True)

    # Maskable: the artwork at 78 %, inside Android's 80 % safe circle, its edges carried out to the canvas's.
    import numpy as np

    inner = np.asarray(icon.resize((400, 400), Image.LANCZOS))
    padded = np.pad(inner, ((56, 56), (56, 56), (0, 0)), mode="edge")
    Image.fromarray(padded).save(OUT / "icons" / "icon-maskable-512.png", optimize=True)

    from fontTools import subset

    (OUT / "fonts").mkdir(parents=True, exist_ok=True)
    unicodes = "U+0020-007E,U+00A0-00FF,U+0131,U+0152-0153,U+02C6,U+02DA,U+02DC,U+2009,U+2013-2014,U+2018-201E,U+2022,U+2026,U+2032-2033,U+20AC,U+2122,U+2190-2193,U+2212,U+2215,U+00D7,U+2713,U+2715,U+00B7,U+00B0"
    for weight in ["Regular", "Medium", "SemiBold", "Bold"]:
        source = ROOT / "src/Coreloom.App/Fonts" / f"Inter-{weight}.ttf"
        target = OUT / "fonts" / f"Inter-{weight}.woff2"
        options = subset.Options()
        options.flavor = "woff2"
        options.layout_features = ["kern", "liga", "calt", "tnum", "case", "ss01", "cv11"]
        options.name_IDs = ["*"]
        options.notdef_outline = True
        font = subset.load_font(str(source), options)
        subsetter = subset.Subsetter(options)
        subsetter.populate(unicodes=subset.parse_unicodes(unicodes))
        subsetter.subset(font)
        subset.save_font(font, str(target), options)
        print(target.name, target.stat().st_size)


if __name__ == "__main__":
    main()
