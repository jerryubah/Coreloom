"""Makes Coreloom's dark-mode icon from the approved light icon.

The light icon is the blue "C" mark on a white tile. The dark-mode icon keeps the mark exactly as it is and the tile's
exact outline, and puts the mark on a dark glass tile: deep navy, lit blue from below, a soft highlight along the top
and a blue rim, with the mark glowing faintly (the look of the dark icon on the splash reference).

    python3 build/dark-icon.py            # writes assets/branding/dark/

Output: coreloom-dark-1024.png, png/coreloom-dark-{16,24,32,48,64,128,256,512}.png and coreloom-dark.ico (16-256).
Needs numpy, scipy and Pillow.
"""
from pathlib import Path

import numpy as np
from PIL import Image
from scipy import ndimage

ROOT = Path(__file__).resolve().parent.parent
SOURCE = ROOT / 'assets' / 'branding' / 'coreloom-app-icon-1024.png'
OUT = ROOT / 'assets' / 'branding' / 'dark'
SIZES = [16, 24, 32, 48, 64, 128, 256, 512]


def smoothstep(edge0, edge1, x):
    t = np.clip((x - edge0) / (edge1 - edge0), 0, 1)
    return t * t * (3 - 2 * t)


def over(dst_rgb, src_rgb, src_alpha):
    """Composites a color (array or triple) with alpha onto dst."""
    a = src_alpha[..., None]
    return dst_rgb * (1 - a) + np.asarray(src_rgb, dtype=float) * a


def main():
    src = np.asarray(Image.open(SOURCE).convert('RGBA')).astype(float)
    h, w = src.shape[:2]
    rgb, tile = src[..., :3], src[..., 3] / 255
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]

    # The mark's shape: pixels blue enough that they can't be the tile (whose blue tint tops out near 55 levels), with the
    # thin pale highlights on its folds closed in (they are part of the mark, not tile showing through).
    blueness = b - r
    disk = np.hypot(*np.mgrid[-6:7, -6:7]) <= 6
    hard = ndimage.binary_opening(blueness > 100, iterations=2) & (tile > 0.99)
    closed = ndimage.binary_closing(hard, structure=disk) & (tile > 0.99)
    core = ndimage.binary_erosion(closed, iterations=2)

    # Inside the shape the mark is kept exactly. Along its outline the alpha comes from how blue each pixel is (the
    # original anti-aliasing), and the colour from the nearest pixel inside, so no white fringe from the old tile shows.
    ring = ndimage.binary_dilation(closed, iterations=2) & ~core
    mark = np.where(core, 1.0, smoothstep(70, 125, blueness) * ring) * tile
    _, (iy, ix) = ndimage.distance_transform_edt(~core, return_indices=True)
    mark_rgb = np.where(core[..., None], rgb, rgb[iy, ix])

    yy, xx = np.mgrid[0:h, 0:w].astype(float)
    ys, xs = np.where(tile > 0.5)
    top, bottom, left, right = ys.min(), ys.max(), xs.min(), xs.max()
    v = (yy - top) / (bottom - top)           # 0 at the tile's top, 1 at its bottom
    u = (xx - left) / (right - left)

    # Dark glass: navy, a little lighter at the top.
    base_top, base_bottom = np.array([27, 35, 58.0]), np.array([9, 13, 26.0])
    out = base_top * (1 - v[..., None]) + base_bottom * v[..., None]

    # Blue light from below.
    d = np.sqrt(((u - 0.5) / 0.75) ** 2 + ((v - 1.02) / 0.62) ** 2)
    out = over(out, (38, 104, 255), 0.42 * np.clip(1 - d, 0, 1) ** 1.6)

    # A soft highlight across the top, like the curve of glass.
    out = over(out, (255, 255, 255), 0.085 * np.clip(1 - v / 0.42, 0, 1) ** 1.5)

    # The rim: a thin band inside the outline, bright blue-white at the top left, blue at the bottom right.
    inner = ndimage.gaussian_filter((ndimage.binary_erosion(tile > 0.5, iterations=9)).astype(float), 1.2)
    band = np.clip(tile - inner, 0, 1)
    diag = np.clip((u + v) / 2, 0, 1)
    rim_rgb = np.stack([
        150 * (1 - diag) + 60 * diag,
        195 * (1 - diag) + 125 * diag,
        255 * np.ones_like(diag),
    ], axis=-1)
    rim_alpha = band * (0.85 - 0.5 * np.sin(diag * np.pi))       # dimmer along the middle of the sides
    out = over(out, rim_rgb, np.clip(rim_alpha, 0, 1))

    # A faint inner glow along the rim.
    halo = np.clip(ndimage.gaussian_filter(band, 16) * 2.2, 0, 1) * tile
    out = over(out, (60, 130, 255), 0.22 * halo)

    # The mark glows a little, then sits on top unchanged.
    glow = np.clip(ndimage.gaussian_filter(core.astype(float), 26) * 1.5, 0, 1)
    out = over(out, (40, 120, 255), 0.55 * glow)
    out = over(out, mark_rgb, mark)

    rgba = np.dstack([np.clip(out, 0, 255), tile * 255]).round().astype(np.uint8)
    image = Image.fromarray(rgba, 'RGBA')

    (OUT / 'png').mkdir(parents=True, exist_ok=True)
    image.save(OUT / 'coreloom-dark-1024.png', optimize=True)
    resized = {}
    for size in SIZES:
        small = image.resize((size, size), Image.LANCZOS)
        small.save(OUT / 'png' / f'coreloom-dark-{size}.png', optimize=True)
        resized[size] = small
    ico_sizes = [16, 24, 32, 48, 64, 128, 256]
    resized[256].save(OUT / 'coreloom-dark.ico', sizes=[(s, s) for s in ico_sizes],
                      append_images=[resized[s] for s in ico_sizes if s != 256])
    print('wrote', OUT)


if __name__ == '__main__':
    main()
