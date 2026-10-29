"""Makes assets/textures/grain.png: a small tile of soft grey noise (since 1.8.3).

Both apps lay it a few percent strong over the Gaming and Smart Optimize banners and the What's new card, tiled, so
their dark gradients read as deep and smooth instead of showing steps (banding) on 8-bit screens. Seeded, so running it
again makes the same file.
"""
import pathlib

import numpy as np
from PIL import Image, ImageFilter

ROOT = pathlib.Path(__file__).resolve().parents[2]
OUT = ROOT / "assets" / "textures" / "grain.png"


def main() -> None:
    rng = np.random.default_rng(1830)
    noise = rng.normal(0.5, 0.22, (160, 160))
    image = Image.fromarray((np.clip(noise, 0, 1) * 255).astype("uint8"), "L").filter(ImageFilter.GaussianBlur(0.45))
    OUT.parent.mkdir(parents=True, exist_ok=True)
    image.save(OUT, optimize=True)
    print(OUT.relative_to(ROOT), image.size, OUT.stat().st_size, "bytes")


if __name__ == "__main__":
    main()
