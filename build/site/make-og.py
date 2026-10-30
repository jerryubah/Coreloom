#!/usr/bin/env python3
"""Makes the website's link preview (site/assets/og.png, 1200 × 630): the app icon, the name and what it is on the left,
and the Mac window from the website's pictures (site/assets/mac-overview.webp, made by make-site-shots.py) on the right,
running off the edge. Run make-site-shots.py first so the window is the current design.

    python3 build/site/make-og.py
"""
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = Path(__file__).resolve().parents[2]
ASSETS = ROOT / "site" / "assets"
FONTS = ROOT / "src" / "Coreloom.App" / "Fonts"
W, H = 1200, 630


def font(weight, size):
    return ImageFont.truetype(str(FONTS / f"Inter-{weight}.ttf"), size)


def background():
    """The site's dark ground with the brand's two soft lights (blue from the top left, violet from the bottom right)."""
    base = Image.new("RGB", (W, H), (7, 9, 15))
    glow = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(glow)
    d.ellipse((-260, -320, 640, 420), fill=(60, 105, 235, 120))
    d.ellipse((620, 260, 1500, 980), fill=(120, 80, 235, 90))
    glow = glow.filter(ImageFilter.GaussianBlur(140))
    base.paste(glow, (0, 0), glow)
    return base


def rounded(image, radius):
    mask = Image.new("L", image.size, 0)
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, image.width - 1, image.height - 1), radius, fill=255)
    out = image.convert("RGBA")
    out.putalpha(mask)
    return out


def main():
    canvas = background().convert("RGBA")

    # The window, 760 px wide, from x = 500, running off the right edge, with a soft shadow.
    window = Image.open(ASSETS / "mac-overview.webp").convert("RGB")
    width = 760
    window = window.resize((width, round(window.height * width / window.width)), Image.LANCZOS)
    window = rounded(window, 16)
    left, top = 500, 92
    shadow = Image.new("RGBA", canvas.size, (0, 0, 0, 0))
    ImageDraw.Draw(shadow).rounded_rectangle((left + 10, top + 24, left + width + 10, top + window.height + 24), 18, fill=(0, 0, 0, 150))
    canvas.alpha_composite(shadow.filter(ImageFilter.GaussianBlur(28)))
    canvas.alpha_composite(window, (left, top))
    rim = Image.new("RGBA", canvas.size, (0, 0, 0, 0))
    ImageDraw.Draw(rim).rounded_rectangle((left, top, left + width - 1, top + window.height - 1), 16, outline=(255, 255, 255, 40), width=1)
    canvas.alpha_composite(rim)

    # The text side, over a fade so it reads where the window comes close.
    fade = Image.new("RGBA", canvas.size, (0, 0, 0, 0))
    fd = ImageDraw.Draw(fade)
    for x in range(0, 560):
        a = 0 if x < 430 else int(150 * (x - 430) / 130)
        fd.line((x, 0, x, H), fill=(7, 9, 15, 150 - a if x >= 430 else 150))
    canvas.alpha_composite(fade)

    icon = Image.open(ASSETS / "icon-256.png").convert("RGBA").resize((120, 120), Image.LANCZOS)
    canvas.alpha_composite(icon, (64, 92))
    d = ImageDraw.Draw(canvas)
    d.text((64, 252), "Coreloom", font=font("Bold", 64), fill=(244, 245, 247))
    d.text((66, 340), "Hardware monitor", font=font("Medium", 30), fill=(184, 192, 208))
    d.text((66, 380), "for Mac and Windows", font=font("Medium", 30), fill=(184, 192, 208))
    d.text((66, 470), "Free · Open source · No telemetry", font=font("Regular", 22), fill=(140, 150, 168))

    canvas.convert("RGB").save(ASSETS / "og.png", optimize=True)
    print(ASSETS.relative_to(ROOT) / "og.png", f"{(ASSETS / 'og.png').stat().st_size // 1024} KB")


if __name__ == "__main__":
    main()
