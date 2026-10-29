#!/usr/bin/env python3
"""Makes the website's pictures of the app (site/assets/{mac,windows}-{overview,overview-light,gaming,optimize}.webp, and
{mac,windows}-backdrop-{aurora,dunes}.webp: the Overview over two of the background pictures, since 1.8.2).

From the 2× window pictures of the sample machines (1440 × 960 points, so 2880 × 1920 pixels):
  Mac:     tools/Coreloom.MacShots (overview.png, overview-light.png, gaming.png, optimize.png); run with
           --wallpaper=<picture> for the MacBook's desktop picture, and once more with --backdrop=image:aurora and
           --backdrop=image:dunes, its overview.png copied into the folder as backdrop-aurora.png and backdrop-dunes.png. Headless pictures have no window buttons, so the
           close, minimize and zoom buttons are drawn where the app puts them: inside the sidebar's glass, 16 points in,
           centred 20 points below its top.
  Windows: tools/Coreloom.PageShots on Windows (windows-overview.png, windows-overview-light.png, windows-gaming.png,
           windows-optimize.png, windows-overview-aurora.png and windows-overview-dunes.png on the pageshots-output
           branch).

    python3 build/site/make-site-shots.py <MacShots folder> <PageShots folder>
"""
import sys
from pathlib import Path

from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "site" / "assets"
WIDTH = 1600
SCALE = 2           # pixels per point in the pictures
GLASS = (10, 10)     # the sidebar's top-left, points
LIGHTS = [("#FF5F57", "#E0443E"), ("#FEBC2E", "#DEA123"), ("#28C840", "#1AAB29")]


def window_buttons(image):
    """Draws the close, minimize and zoom buttons where the Mac app places them."""
    big = image.convert("RGBA").resize((image.width * 2, image.height * 2), Image.LANCZOS)
    d = ImageDraw.Draw(big)
    k = SCALE * 2
    left, centre = GLASS[0] + 16, GLASS[1] + 20
    for i, (fill, edge) in enumerate(LIGHTS):
        cx = (left + 7 + i * 20) * k
        r = 6 * k
        d.ellipse((cx - r, centre * k - r, cx + r, centre * k + r), fill=fill, outline=edge, width=max(1, k // 2))
    return big.resize(image.size, Image.LANCZOS)


def save(image, name):
    height = round(image.height * WIDTH / image.width)
    image = image.convert("RGB").resize((WIDTH, height), Image.LANCZOS)
    target = OUT / f"{name}.webp"
    image.save(target, "WEBP", quality=88, method=6)
    print(target.relative_to(ROOT), image.size, f"{target.stat().st_size // 1024} KB")


def main():
    mac, windows = Path(sys.argv[1]), Path(sys.argv[2])
    for source, name in [("overview", "mac-overview"), ("overview-light", "mac-overview-light"), ("gaming", "mac-gaming"), ("optimize", "mac-optimize"),
                         ("backdrop-aurora", "mac-backdrop-aurora"), ("backdrop-dunes", "mac-backdrop-dunes")]:
        path = mac / f"{source}.png"
        if path.exists():
            save(window_buttons(Image.open(path)), name)
    for source, name in [("windows-overview", "windows-overview"), ("windows-overview-light", "windows-overview-light"),
                         ("windows-gaming", "windows-gaming"), ("windows-optimize", "windows-optimize"),
                         ("windows-overview-aurora", "windows-backdrop-aurora"), ("windows-overview-dunes", "windows-backdrop-dunes")]:
        path = windows / f"{source}.png"
        if path.exists():
            save(Image.open(path), name)


if __name__ == "__main__":
    main()
