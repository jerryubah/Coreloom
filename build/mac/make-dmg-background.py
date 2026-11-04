"""Draws the background of Coreloom's disk-image window (build/mac/dmg-background.png and @2x).

660 x 420 points: a title, Coreloom on the left and Applications on the right with an arrow between them, and a line
about the first opening. Finder writes each icon's name under it in black (light mode) or white (dark mode), so the
icons sit on mid-tone tiles where both stay readable. Run it again after changing the layout in dmg-settings.py:
    python3 build/mac/make-dmg-background.py
"""
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = Path(__file__).resolve().parents[2]
FONTS = ROOT / "src/Coreloom.App/Fonts"
OUT = ROOT / "build/mac"
W, H = 660, 420
ICONS = [(175, 205), (485, 205)]  # the icon centres in dmg-settings.py


def font(name: str, size: float, scale: int) -> ImageFont.FreeTypeFont:
    return ImageFont.truetype(str(FONTS / name), int(round(size * scale)))


def draw(scale: int) -> Image.Image:
    w, h = W * scale, H * scale
    img = Image.new("RGB", (w, h))
    px = ImageDraw.Draw(img)
    # Coreloom's night blue, a little lighter at the top.
    for y in range(h):
        t = y / (h - 1)
        px.line([(0, y), (w, y)], fill=(int(24 + (12 - 24) * t), int(32 + (17 - 32) * t), int(56 + (33 - 56) * t)))

    # A soft glow of the accent behind the arrow.
    glow = Image.new("L", (w, h), 0)
    ImageDraw.Draw(glow).ellipse([w * 0.25, h * 0.18, w * 0.75, h * 0.82], fill=120)
    glow = glow.filter(ImageFilter.GaussianBlur(60 * scale))
    accent = Image.new("RGB", (w, h), (64, 120, 255))
    img = Image.composite(accent, img, glow.point(lambda v: int(v * 0.35)))
    d = ImageDraw.Draw(img, "RGBA")

    # Title and instruction.
    title = "Install Coreloom"
    f_title = font("Inter-SemiBold.ttf", 22, scale)
    tw = d.textlength(title, font=f_title)
    d.text(((w - tw) / 2, 34 * scale), title, font=f_title, fill=(240, 244, 255))
    sub = "Drag Coreloom into the Applications folder."
    f_sub = font("Inter-Regular.ttf", 13.5, scale)
    sw = d.textlength(sub, font=f_sub)
    d.text(((w - sw) / 2, 68 * scale), sub, font=f_sub, fill=(176, 189, 216))

    # Mid-tone tiles under the two icons and their names.
    for cx, cy in ICONS:
        box = [(cx - 82) * scale, (cy - 78) * scale, (cx + 82) * scale, (cy + 92) * scale]
        d.rounded_rectangle(box, radius=22 * scale, fill=(112, 126, 158, 205), outline=(160, 178, 214, 120), width=max(1, scale))

    # The arrow from Coreloom to Applications.
    y = ICONS[0][1] * scale
    x0, x1 = (ICONS[0][0] + 102) * scale, (ICONS[1][0] - 102) * scale
    d.line([(x0, y), (x1 - 10 * scale, y)], fill=(108, 160, 255), width=4 * scale)
    d.polygon([(x1, y), (x1 - 16 * scale, y - 10 * scale), (x1 - 16 * scale, y + 10 * scale)], fill=(108, 160, 255))

    # The first opening.
    f_note = font("Inter-Regular.ttf", 11.5, scale)
    f_bold = font("Inter-SemiBold.ttf", 11.5, scale)
    lines = [
        [("First opening: ", f_bold), ("if macOS says it could not verify Coreloom, open", f_note)],
        [("System Settings › Privacy & Security", f_bold), (" and click ", f_note), ("Open Anyway", f_bold), (" (only this once).", f_note)],
    ]
    top = 344 * scale
    for i, parts in enumerate(lines):
        total = sum(d.textlength(text, font=f) for text, f in parts)
        x = (w - total) / 2
        for text, f in parts:
            d.text((x, top + i * 19 * scale), text, font=f, fill=(196, 206, 228))
            x += d.textlength(text, font=f)
    return img


if __name__ == "__main__":
    draw(1).save(OUT / "dmg-background.png", optimize=True)
    draw(2).save(OUT / "dmg-background@2x.png", optimize=True)
    print("wrote build/mac/dmg-background.png and dmg-background@2x.png")
