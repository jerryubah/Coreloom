#!/usr/bin/env python3
"""
Builds the PC picture in Coreloom's Overview header from a render (assets/pc-render/render.png).

The render is split into layers the app can recolor and animate:

  base.png             the render with its glow taken out
  glow.png             how much glow each pixel has (white + alpha); the app fills it with the chosen glow color
  blades-<fan>.png     each fan's blades, flattened to a circle (black/white + alpha); the app turns them
  blades-<fan>-blur.png  the same, motion-blurred; faded in as the fan speeds up

and src/Coreloom.App/Views/PcIllustration.xaml is written to place them. The render's own text (the temperature on
the pump screen and the graphics card's model) is painted out; the app draws the live values in the same spots.

Usage:
  python3 build/pc-render.py                     # regenerate the layers and the XAML
  python3 build/pc-render.py --preview out/      # also write recolored previews (blue, purple, red, green)

Needs Python 3.10+, numpy, Pillow and opencv-python-headless. If the render changes, re-check FANS and TEXT below
(source-pixel coordinates) and the CROP.
"""
from __future__ import annotations

import argparse
import json
from pathlib import Path

import cv2
import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parent.parent
SOURCE = ROOT / "assets" / "pc-render" / "render.png"
ASSETS = ROOT / "src" / "Coreloom.App" / "Assets" / "PcRender"
XAML = ROOT / "src" / "Coreloom.App" / "Views" / "PcIllustration.xaml"
PACK = "pack://application:,,,/Assets/PcRender/"

# ---- The render (coordinates in its pixels) -----------------------------------------------------------------

CROP = (84, 27, 1112, 1254)           # x0, y0, x1, y1: the case plus a little room, in the header's shape (196 x 234)
OUTPUT_SCALE = 0.54                   # 1028 x 1227 -> 555 x 663 (sharp up to ~280 % at the header's 196 x 234 DIP)

# The render's glow color (the fan rings' blue), as full-brightness sRGB. Each pixel is split into a neutral base
# and an amount of this color, which the app then replaces with the chosen one.
GLOW = np.array([0.03, 0.40, 1.0], dtype=np.float32)

# Fan rings: center x, center y, radius x, radius y, tilt (degrees), fitted to each ring's outer edge (since 1.9.3's
# render: the top fans seen from a little below, the bottom ones from above). The blades end at BLADE_EDGE of the ring.
FANS = {
    "Top1": (379.0, 257.1, 90.5, 24.5, 2.4),
    "Top2": (571.7, 247.0, 88.5, 25.5, 0.2),
    "Top3": (768.1, 236.4, 90.2, 26.5, -0.8),
    "Bottom1": (357.9, 1048.1, 93.8, 16.9, -1.7),
    "Bottom2": (550.3, 1053.7, 92.2, 17.5, -1.4),
    "Bottom3": (753.3, 1059.1, 96.1, 18.0, -0.4),
}
BLADE_EDGE = 0.875
# Each fan starts at its own angle, so the three in a row don't look like copies.
PHASES = {"Top1": 0, "Top2": 23, "Top3": 9, "Bottom1": 31, "Bottom2": 5, "Bottom3": 17}

# Text in the render: (x0, y0, x1, y1) boxes to paint out (the pump screen's "57°" and "CPU", the card's model).
TEXT_BOXES = [(520, 444, 600, 491), (533, 488, 583, 514), (326, 664, 514, 709)]

# Where the app draws its text, measured from the render's lettering: center x (or left x), cap-height center y,
# cap height, all in source pixels.
SCREEN_VALUE = (557.0, 468.0, 36.0)
SCREEN_LABEL = (557.5, 501.0, 16.0)
GPU_LABEL = (332.0, 687.0, 30.0)
GPU_LABEL_MAX_RIGHT = 660.0           # the vents on the card start here

# Inter's metrics (units per em 2048): ascender 1984, cap height 1490. WPF puts the baseline at the ascender.
INTER_ASCENT = 1984 / 2048
INTER_CAP = 1490 / 2048


# ---- Image work ---------------------------------------------------------------------------------------------

def load(path: Path) -> np.ndarray:
    return np.array(Image.open(path).convert("RGBA")).astype(np.float32) / 255.0


def paint_out_text(img: np.ndarray) -> np.ndarray:
    rgb8 = (np.clip(img[:, :, :3], 0, 1) * 255).astype(np.uint8)
    luma = rgb8.mean(2)
    mask = np.zeros(rgb8.shape[:2], np.uint8)
    for x0, y0, x1, y1 in TEXT_BOXES:
        mask[y0:y1, x0:x1] = (luma[y0:y1, x0:x1] > 0.42 * 255).astype(np.uint8) * 255
    mask = cv2.dilate(mask, np.ones((5, 5), np.uint8), iterations=2)
    out = cv2.inpaint(rgb8, mask, 6, cv2.INPAINT_TELEA).astype(np.float32)
    soft = cv2.GaussianBlur(mask.astype(np.float32) / 255, (0, 0), 3)[:, :, None]
    out = out * (1 - soft) + cv2.GaussianBlur(out, (0, 0), 3) * soft  # hides the inpainting's streaks
    res = img.copy()
    res[:, :, :3] = out / 255.0
    return res


def _rotation(tilt: float):
    t = np.deg2rad(tilt)
    return np.cos(t), np.sin(t)


def _from_disc(u, v, fan):
    cx, cy, rx, ry, tilt = fan
    c, s = _rotation(tilt)
    v = v * (ry / rx)
    return cx + u * c - v * s, cy + u * s + v * c


def _to_disc(x, y, fan):
    cx, cy, rx, ry, tilt = fan
    c, s = _rotation(tilt)
    dx, dy = x - cx, y - cy
    return dx * c + dy * s, (-dx * s + dy * c) * (rx / ry)


def split_fan(img: np.ndarray, fan, n_r=200, n_t=1440):
    """Polar samples of the fan's disc: the lighting (doesn't turn) and the blades (do)."""
    radius = fan[2] * BLADE_EDGE
    r = np.linspace(0, radius, n_r, dtype=np.float32)
    th = np.linspace(0, 2 * np.pi, n_t, endpoint=False, dtype=np.float32)
    rr, tt = np.meshgrid(r, th, indexing="ij")
    x, y = _from_disc(rr * np.cos(tt), rr * np.sin(tt), fan)
    polar = np.stack([cv2.remap(img[:, :, k], x.astype(np.float32), y.astype(np.float32), cv2.INTER_CUBIC) for k in range(3)], -1)
    spectrum = np.fft.rfft(polar, axis=1)
    spectrum[:, 3:, :] = 0  # the lowest angular harmonics are the lighting
    lighting = np.fft.irfft(spectrum, n=n_t, axis=1).astype(np.float32)
    blades = (polar - lighting).mean(2)  # neutral: color stays with the lighting
    return lighting, blades, r, th


def put_lighting(img: np.ndarray, fan, lighting, r, th) -> None:
    """Replaces the disc with its lighting only (no blades), feathered into the ring."""
    cx, cy, rx, ry, _ = fan
    radius = r[-1]
    x0, x1, y0, y1 = int(cx - rx - 4), int(cx + rx + 5), int(cy - ry - 4), int(cy + ry + 5)
    ys, xs = np.mgrid[y0:y1, x0:x1].astype(np.float32)
    u, v = _to_disc(xs, ys, fan)
    rad = np.sqrt(u * u + v * v)
    ri = (rad / radius * (len(r) - 1)).astype(np.float32)
    ti = (np.mod(np.arctan2(v, u), 2 * np.pi) / (2 * np.pi) * len(th)).astype(np.float32)
    flat = np.stack([cv2.remap(lighting[:, :, k], ti, ri, cv2.INTER_LINEAR, borderMode=cv2.BORDER_WRAP) for k in range(3)], -1)
    weight = np.clip((radius - rad) / (0.08 * radius), 0, 1)[:, :, None]
    img[y0:y1, x0:x1, :3] = img[y0:y1, x0:x1, :3] * (1 - weight) + flat * weight


def blade_image(lighting, blades, r, th, size: int, blur_degrees: float = 0.0) -> np.ndarray:
    """
    The blades as black (darker) or white (lighter) with alpha, on a size x size circle, so that drawn over the
    lighting they give back the original pixels.
    """
    if blur_degrees > 0:
        n = max(1, int(round(blur_degrees / 360 * len(th))))
        kernel = np.ones(n, np.float32) / n
        blades = np.stack([np.convolve(np.concatenate([row[-n:], row, row[:n]]), kernel, mode="same")[n:-n] for row in blades])
    light = np.clip(lighting.mean(2), 1e-3, 1 - 1e-3)
    radius = r[-1]
    half = size / 2
    ys, xs = np.mgrid[0:size, 0:size].astype(np.float32)
    u = (xs + 0.5 - half) / half * radius
    v = (ys + 0.5 - half) / half * radius
    rad = np.sqrt(u * u + v * v)
    ri = (rad / radius * (len(r) - 1)).astype(np.float32)
    ti = (np.mod(np.arctan2(v, u), 2 * np.pi) / (2 * np.pi) * len(th)).astype(np.float32)
    d = cv2.remap(blades.astype(np.float32), ti, ri, cv2.INTER_LINEAR, borderMode=cv2.BORDER_WRAP)
    lit = cv2.remap(light.astype(np.float32), ti, ri, cv2.INTER_LINEAR, borderMode=cv2.BORDER_WRAP)
    darker = d < 0
    alpha = np.clip(np.where(darker, -d / lit, d / (1 - lit)), 0, 1) * np.clip((radius - rad) / (0.08 * radius), 0, 1)
    out = np.zeros((size, size, 4), np.float32)
    out[:, :, :3] = np.where(darker, 0.0, 1.0)[:, :, None]
    out[:, :, 3] = alpha
    return out


def split_glow(img: np.ndarray):
    """Least-squares split of each pixel into base * (1 - a) + GLOW * a; the base is neutral where there's glow."""
    P = img[:, :, :3]
    W = np.ones(3, np.float32)
    ww, wc, cc = W @ W, W @ GLOW, GLOW @ GLOW
    a = (ww * (P @ GLOW) - wc * (P @ W)) / (ww * cc - wc * wc)
    a = np.clip(a, 0, 0.98)
    base = (P - a[:, :, None] * GLOW) / (1 - a[:, :, None])
    gray = base.mean(2, keepdims=True)
    k = np.clip(a * 4, 0, 1)[:, :, None]
    return np.clip(base * (1 - k) + gray * k, 0, 1), a


def save(arr: np.ndarray, path: Path, size: tuple[int, int] | None = None) -> None:
    pic = Image.fromarray((np.clip(arr, 0, 1) * 255 + 0.5).astype(np.uint8), "RGBA")
    if size:
        pic = pic.resize(size, Image.LANCZOS)
    pic.save(path, optimize=True)


# ---- XAML ---------------------------------------------------------------------------------------------------

def f(v: float) -> str:
    return f"{v:.2f}".rstrip("0").rstrip(".")


def fan_xaml(name: str, fan, image_px: int) -> str:
    cx, cy, rx, ry, tilt = fan
    x0, y0 = CROP[0], CROP[1]
    s = OUTPUT_SCALE
    r = rx * BLADE_EDGE * s
    left, top = (cx - x0) * s - r, (cy - y0) * s - r
    return f"""            <Grid Canvas.Left="{f(left)}" Canvas.Top="{f(top)}" Width="{f(2 * r)}" Height="{f(2 * r)}" RenderTransformOrigin="0.5,0.5">
              <Grid.RenderTransform>
                <TransformGroup>
                  <RotateTransform x:Name="Fan{name}Turn" Angle="{PHASES[name]}" />
                  <ScaleTransform ScaleY="{ry / rx:.4f}" />
                  <RotateTransform Angle="{f(tilt)}" />
                </TransformGroup>
              </Grid.RenderTransform>
              <Image x:Name="Fan{name}Sharp" Source="{PACK}blades-{name.lower()}.png" />
              <Image x:Name="Fan{name}Blur" Source="{PACK}blades-{name.lower()}-blur.png" Opacity="0" />
            </Grid>"""


def text_xaml() -> str:
    x0, y0 = CROP[0], CROP[1]
    s = OUTPUT_SCALE

    def place(spec, width, left_aligned=False):
        x, y, cap = spec
        size = cap * s / INTER_CAP
        top = (y - y0) * s - (INTER_ASCENT - INTER_CAP / 2) * size
        left = (x - x0) * s if left_aligned else (x - x0) * s - width / 2
        return f(left), f(top), f(size)

    vl, vt, vs = place(SCREEN_VALUE, 90)
    ll, lt, ls = place(SCREEN_LABEL, 60)
    gl, gt, gs = place(GPU_LABEL, 0, left_aligned=True)
    gpu_width = (GPU_LABEL_MAX_RIGHT - GPU_LABEL[0]) * s
    return f"""            <TextBlock Canvas.Left="{vl}" Canvas.Top="{vt}" Width="90" TextAlignment="Center" Text="{{Binding ScreenValue}}"
                       FontSize="{vs}" FontWeight="SemiBold" Foreground="#F5FFFFFF" />
            <TextBlock Canvas.Left="{ll}" Canvas.Top="{lt}" Width="60" TextAlignment="Center" Text="{{Binding ScreenLabel}}"
                       FontSize="{ls}" FontWeight="SemiBold" Foreground="#E6FFFFFF" />
            <TextBlock Canvas.Left="{gl}" Canvas.Top="{gt}" Width="{f(gpu_width)}" TextTrimming="CharacterEllipsis" Text="{{Binding GpuLabel}}"
                       FontSize="{gs}" FontWeight="SemiBold" Foreground="#F2FFFFFF"
                       Visibility="{{Binding ShowGpuLabel, Converter={{StaticResource BoolToVisibility}}}}" />"""


PAGE = r'''<UserControl x:Class="Coreloom.App.Views.PcIllustration"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             AutomationProperties.Name="Picture of this PC"
             Focusable="False">

  <!--
    The PC in the Overview header (bound to SystemSummaryViewModel): a render split into layers by
    build/pc-render.py, which also writes this file (edit the script and regenerate rather than editing it). The base
    picture has its glow taken out; the glow layer is filled with the color chosen under Customize (or cycles for
    Rainbow); each fan's blades turn at a speed that follows the PC's fans; the pump screen and the card show live
    text.

    A render or photo of the real build can replace it: pc.png in %AppData%\Coreloom (or Data next to a portable copy)
    or in assets/pc/ at build time. See assets/pc/README.md.
  -->
  <Grid>
    <Grid x:Name="Picture" Background="Transparent" ToolTip="{Binding FanToolTip}" ToolTipService.InitialShowDelay="900">
      <Viewbox x:Name="Art" Stretch="Uniform" RenderOptions.BitmapScalingMode="HighQuality">
        <Grid Width="{width}" Height="{height}">
          <Image Source="{pack}base.png" Width="{width}" Height="{height}" />
          <Rectangle x:Name="GlowLayer" Width="{width}" Height="{height}" Fill="#0D6BFF" IsHitTestVisible="False">
            <Rectangle.OpacityMask>
              <ImageBrush ImageSource="{pack}glow.png" />
            </Rectangle.OpacityMask>
          </Rectangle>

          <!-- Fan blades: turned by PcIllustration.xaml.cs -->
          <Canvas IsHitTestVisible="False">
{fans}
          </Canvas>

          <!-- Live text: the pump screen and the graphics card -->
          <Canvas IsHitTestVisible="False" TextOptions.TextFormattingMode="Ideal" TextOptions.TextRenderingMode="Grayscale">
{text}
          </Canvas>
        </Grid>
      </Viewbox>
      <Image x:Name="CustomImage" Stretch="Uniform" Visibility="Collapsed" RenderOptions.BitmapScalingMode="HighQuality" />
    </Grid>
  </Grid>
</UserControl>
'''


def build(preview: Path | None) -> None:
    img = paint_out_text(load(SOURCE))
    split = {}
    for name, fan in FANS.items():
        lighting, blades, r, th = split_fan(img, fan)
        put_lighting(img, fan, lighting, r, th)
        split[name] = (lighting, blades, r, th)

    x0, y0, x1, y1 = CROP
    img = img[y0:y1, x0:x1]
    size = (int(round((x1 - x0) * OUTPUT_SCALE)), int(round((y1 - y0) * OUTPUT_SCALE)))
    base, amount = split_glow(img)
    alpha = img[:, :, 3]
    ASSETS.mkdir(parents=True, exist_ok=True)
    save(np.dstack([base, alpha]), ASSETS / "base.png", size)
    save(np.dstack([np.ones_like(base), amount * alpha]), ASSETS / "glow.png", size)

    fans_xaml = []
    for name, fan in FANS.items():
        lighting, blades, r, th = split[name]
        px = int(round(2 * r[-1] * OUTPUT_SCALE * 1.5))  # extra resolution: the blades are turned and squashed
        px += px % 2
        save(blade_image(lighting, blades, r, th, px), ASSETS / f"blades-{name.lower()}.png")
        save(blade_image(lighting, blades, r, th, px, blur_degrees=48), ASSETS / f"blades-{name.lower()}-blur.png")
        fans_xaml.append(fan_xaml(name, fan, px))

    page = (PAGE.replace("{width}", str(size[0])).replace("{height}", str(size[1])).replace("{pack}", PACK)
            .replace("{fans}", "\n".join(fans_xaml)).replace("{text}", text_xaml()))
    XAML.write_text(page, encoding="utf-8", newline="\n")
    print(f"Wrote {len(list(ASSETS.glob('*.png')))} layers to {ASSETS.relative_to(ROOT)} and {XAML.relative_to(ROOT)}")

    if preview is not None:
        preview.mkdir(parents=True, exist_ok=True)
        colors = {"blue": GLOW, "purple": [0.62, 0.30, 1.0], "red": [1.0, 0.16, 0.20], "green": [0.10, 0.85, 0.42]}
        for cname, color in colors.items():
            rgb = base * (1 - amount[:, :, None]) + np.asarray(color, np.float32) * amount[:, :, None]
            save(np.dstack([rgb, alpha]), preview / f"glow-{cname}.png", size)
        (preview / "layout.json").write_text(json.dumps({"size": size}, indent=2))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--preview", type=Path, help="also write recolored previews to this folder")
    build(parser.parse_args().preview)
