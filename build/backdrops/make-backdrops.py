#!/usr/bin/env python3
"""Draws Coreloom's suggested background pictures (dark, made for the glass cards to sit on).

Every picture is computed here from noise and light, so they are Coreloom's own (no photographs, nothing licensed).
Writes assets/backdrops/<id>.jpg (2560 x 1600) and <id>-thumb.jpg (336 x 210) for the Settings swatches.

    python3 build/backdrops/make-backdrops.py [only-id ...]
"""
import os
import sys

import numpy as np
from PIL import Image
from scipy import ndimage

W, H = 2560, 1600
OUT = os.path.join(os.path.dirname(__file__), "..", "..", "assets", "backdrops")


def grid():
    y, x = np.mgrid[0:H, 0:W].astype(np.float32)
    return x / W, y / H  # 0..1 across, 0..1 down (aspect W/H)


def noise(rng, scale, octaves=1, persistence=0.5):
    """Smooth value noise: random cells (about `scale` px apart) blown up with cubic interpolation, in octaves."""
    total = np.zeros((H, W), np.float32)
    amp, norm = 1.0, 0.0
    for o in range(octaves):
        cell = max(2, int(scale / (2 ** o)))
        gh, gw = H // cell + 6, W // cell + 6
        g = rng.standard_normal((gh, gw)).astype(np.float32)
        # Start two cells in, past the spline's edge (which leans toward zero and showed as a light seam).
        big = ndimage.zoom(g, cell, order=3, mode="mirror")[2 * cell:2 * cell + H, 2 * cell:2 * cell + W]
        total += amp * big
        norm += amp
        amp *= persistence
    total /= norm
    return total / (np.abs(total).max() + 1e-6)


def hex_rgb(h):
    h = h.lstrip("#")
    return np.array([int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)], np.float32)


def ramp(t, stops):
    """Colors along t (0..1) through (offset, '#rrggbb') stops."""
    t = np.clip(t, 0, 1)
    out = np.zeros(t.shape + (3,), np.float32)
    offs = [s[0] for s in stops]
    cols = [hex_rgb(s[1]) for s in stops]
    for i in range(len(stops) - 1):
        a, b = offs[i], offs[i + 1]
        m = (t >= a) & (t <= b)
        u = ((t - a) / max(b - a, 1e-6))[m][:, None]
        out[m] = cols[i] * (1 - u) + cols[i + 1] * u
    out[t < offs[0]] = cols[0]
    out[t > offs[-1]] = cols[-1]
    return out


def stars(rng, count, size=1.0, tint=("#FFFFFF",)):
    layer = np.zeros((H, W, 3), np.float32)
    xs = rng.integers(0, W, count)
    ys = rng.integers(0, H, count)
    mags = rng.random(count) ** 6
    for x, y, m in zip(xs, ys, mags):
        c = hex_rgb(tint[rng.integers(0, len(tint))])
        layer[y, x] += c * (0.25 + 2.5 * m)
    layer = ndimage.gaussian_filter(layer, (size * 0.7, size * 0.7, 0)) * (size * 3.0)
    return layer


def vignette(img, strength=0.55, cx=0.5, cy=0.45):
    x, y = grid()
    d = np.sqrt(((x - cx) * 1.6) ** 2 + ((y - cy) * 1.0) ** 2)
    v = 1 - strength * np.clip(d - 0.25, 0, 1) ** 1.4
    return img * v[..., None]


def finish(img, name, grain=0.012, seed=7):
    rng = np.random.default_rng(seed)
    img = img + rng.normal(0, grain, img.shape[:2])[..., None].astype(np.float32)
    img = np.clip(img, 0, 1) ** (1 / 1.0)
    out = (img * 255 + 0.5).astype(np.uint8)
    os.makedirs(OUT, exist_ok=True)
    pic = Image.fromarray(out, "RGB")
    pic.save(os.path.join(OUT, f"{name}.jpg"), quality=86, subsampling=0, optimize=True, progressive=True)
    pic.resize((336, 210), Image.LANCZOS).save(os.path.join(OUT, f"{name}-thumb.jpg"), quality=88, optimize=True)
    print(name, os.path.getsize(os.path.join(OUT, f"{name}.jpg")) // 1024, "KB")


def shade(height, light=(-0.55, -0.65, 0.52), spec_power=40, scale=1.0):
    gy, gx = np.gradient(height * scale)
    n = np.dstack([-gx, -gy, np.ones_like(height)])
    n /= np.linalg.norm(n, axis=2, keepdims=True)
    l = np.array(light, np.float32)
    l /= np.linalg.norm(l)
    diffuse = np.clip((n * l).sum(2), 0, 1)
    h = l + np.array([0, 0, 1], np.float32)
    h /= np.linalg.norm(h)
    spec = np.clip((n * h).sum(2), 0, 1) ** spec_power
    return diffuse, spec, n


# --------------------------------------------------------------------------------------------- the pictures

def silk():
    """Dark satin sweeping across the frame: broad soft folds and a few crisp creases catching the light (blue steel)."""
    rng = np.random.default_rng(11)
    x, y = grid()
    ax = x * (W / H)
    bend = noise(rng, 1500, 1) * 0.10
    u = ax * 0.55 - y * 0.95 + bend                 # across the folds (they run from lower left to upper right)
    v = ax * 0.80 + y * 0.45                        # along them
    light = np.zeros((H, W), np.float32)
    shadow = np.zeros((H, W), np.float32)
    # (where, width, highlight, shadow): broad soft swells and a few thin bright creases.
    for c, w, a, sh in [(-0.62, 0.11, 0.55, 0.6), (-0.30, 0.012, 1.0, 0.5), (-0.22, 0.06, 0.35, 0.4),
                        (0.05, 0.16, 0.45, 0.7), (0.30, 0.009, 0.9, 0.45), (0.52, 0.09, 0.4, 0.5), (0.80, 0.02, 0.6, 0.4)]:
        line = c + 0.07 * np.sin(v * 2.2 + c * 7) + 0.025 * np.sin(v * 5.3 + c * 3)
        d = u - line
        along = 0.3 + 0.7 * (0.5 + 0.5 * np.sin(v * 1.6 + c * 9)) ** 2   # each fold catches the light in places
        light += a * along * np.exp(-(d / max(w, 0.016)) ** 2)
        shadow += sh * np.exp(-((d - w * 2.6) / (w * 2.8)) ** 2)
    fade = np.clip(1.1 - np.abs(v - 0.9) * 0.55, 0.25, 1)        # brightest across the middle of the frame
    light *= fade
    base = ramp(y * 0.55 + x * 0.45, [(0, "#121C28"), (0.5, "#0B121B"), (1, "#05080D")])
    color = base * np.clip(1.0 + light * 2.2 - shadow * 0.75, 0.15, 4)[..., None]
    color += (light ** 2.2)[..., None] * hex_rgb("#AFC6DD") * 0.55
    color = ndimage.gaussian_filter(color, (1.0, 1.0, 0))
    color = vignette(color, 0.55, 0.55, 0.5)
    finish(color, "silk")


def aurora():
    """Northern lights folding over a dark ridge, with stars."""
    rng = np.random.default_rng(23)
    x, y = grid()
    sky = ramp(y, [(0, "#03040B"), (0.5, "#06101F"), (1, "#0A1A24")])
    img = sky.copy()
    img += stars(rng, 2600, 0.8, ("#FFFFFF", "#CFE0FF", "#FFE8D0")) * (1 - y[..., None]) * 0.7

    for k, (center, amp, height, c_low, c_high, strength) in enumerate([
        (0.50, 0.16, 0.30, "#3DFFB2", "#5A4DFF", 1.0),
        (0.36, 0.10, 0.22, "#22E0C8", "#9A55FF", 0.55),
    ]):
        xs = x[0]
        wave = center + amp * (0.7 * noise(rng, 1600, 2)[k * 40] + 0.3 * np.sin(xs * 5.0 + k * 2.0))
        curve = np.broadcast_to(wave, (H, W))
        d = y - curve                                    # below the edge > 0
        edge = np.exp(-(d / 0.035) ** 2)                 # the bright lower hem
        rise = np.exp(np.clip(d, None, 0) / height) * (d < 0)
        folds = ndimage.gaussian_filter(rng.random(W).astype(np.float32), 9)
        folds = (folds - folds.min()) / (folds.max() - folds.min())
        fine = ndimage.gaussian_filter(rng.random(W).astype(np.float32), 2.2)
        fine = (fine - fine.min()) / (fine.max() - fine.min())
        rays = 0.35 + 0.65 * np.broadcast_to(folds * 0.7 + fine * 0.3, (H, W))
        body = (edge * 0.9 + rise * 0.75 * rays) * (0.55 + 0.45 * np.broadcast_to(folds, (H, W)))
        t = np.clip(-d / height, 0, 1)
        col = ramp(t, [(0, c_low), (0.45, c_low), (1, c_high)])
        img += col * (body * strength)[..., None] * 0.42

    ridge = 0.84 + 0.035 * noise(rng, 700, 3)[0] + 0.025 * np.sin(x[0] * 3.2)
    land = y > np.broadcast_to(ridge, (H, W))
    glow = np.exp(-np.clip(y - 0.84, 0, None) / 0.05)[..., None] * hex_rgb("#0D3A33") * 0.25
    img = np.where(land[..., None], img * 0.05 + hex_rgb("#020405") + glow, img)
    img = ndimage.gaussian_filter(img, (1.6, 1.6, 0))
    img = vignette(img, 0.5, 0.5, 0.4)
    finish(img, "aurora")


def nebula():
    """Gas clouds in violet and blue drifting across the frame, a warm core, and stars."""
    rng = np.random.default_rng(5)
    x, y = grid()
    ax = x * (W / H)
    f = noise(rng, 1100, 6, 0.55)
    ridged = 1 - np.abs(noise(rng, 650, 5, 0.6))           # filaments
    band = np.exp(-((y - 0.25 - 0.45 * x - 0.08 * np.sin(ax * 2.2)) / 0.32) ** 2)   # a diagonal river of gas
    density = np.clip(band * (0.55 + 0.55 * f) + 0.35 * ridged ** 3 * band, 0, 1.3)
    core = np.exp(-(((ax - 1.05) ** 2) + (y - 0.42) ** 2) / 0.035)
    t = np.clip(density * 0.8 + core * 0.15, 0, 1)
    col = ramp(t, [(0, "#030209"), (0.25, "#0C0620"), (0.45, "#2A0F52"), (0.62, "#5C2391"), (0.78, "#3D6CF0"), (0.92, "#9FC4FF"), (1, "#FFD9F0")])
    img = col * (0.35 + 1.35 * (density ** 1.3)[..., None] + core[..., None] * 0.25)
    img = img / (1 + img * 0.9)                                  # roll off the bright core instead of clipping it
    dust = np.clip(noise(rng, 260, 3) * 1.6 - 0.2, 0, 1)
    img *= (1 - 0.6 * dust[..., None] * band[..., None])
    img += stars(rng, 4200, 0.9, ("#FFFFFF", "#D6E2FF", "#FFE6F4")) * 0.85
    img += stars(rng, 70, 2.2, ("#FFFFFF", "#CFE0FF")) * 0.45
    img = vignette(img, 0.5, 0.55, 0.45)
    finish(img, "nebula")


def dunes():
    """Desert dunes at dusk: warm light on the crests, blue in the hollows."""
    rng = np.random.default_rng(31)
    x, y = grid()
    horizon = 0.36
    sky = ramp(y / horizon, [(0, "#06060E"), (0.6, "#1A0F24"), (0.92, "#5A2A2A"), (1, "#B8623A")])
    ax = x * (W / H)
    persp = np.clip((y - horizon) / (1 - horizon), 0, 1)
    depth = 1 / (0.15 + persp)                         # far away near the horizon
    u = ax * 2.2 + noise(rng, 900, 2) * 0.6
    v = depth * 0.55
    ridge = 1 - np.abs(np.sin(u * 1.4 + v * 2.0 + noise(rng, 1300, 1) * 1.5))
    h = ridge ** 2.5 * 0.9 + np.sin(v * 3.0 + u * 0.4) * 0.3
    h = ndimage.gaussian_filter(h, 2)
    diffuse, spec, n = shade(h, light=(0.85, -0.25, 0.25), spec_power=12, scale=60)
    sand = ramp(diffuse, [(0, "#05070E"), (0.35, "#141626"), (0.7, "#6A3A28"), (1, "#E89A5C")])
    sand *= (0.45 + 0.55 * persp[..., None])
    haze = np.exp(-persp / 0.08)[..., None] * hex_rgb("#5A2E2C") * 0.6
    land = sand + haze
    img = np.where((y < horizon)[..., None], sky, land)
    img += stars(rng, 900, 0.8) * ((y < horizon * 0.8)[..., None] * 0.6)
    img = ndimage.gaussian_filter(img, (0.8, 0.8, 0))
    img = vignette(img, 0.45, 0.5, 0.4)
    finish(img, "dunes")


def bloom():
    """Soft orbs of color floating in the dark (blue, violet, teal, a little magenta): what frosted glass loves to sit on."""
    rng = np.random.default_rng(41)
    x, y = grid()
    ax = x * (W / H)
    img = ramp(y, [(0, "#06070D"), (1, "#040509")])
    for px, py, r, color, strength in [(0.35, 0.30, 0.42, "#2F5BFF", 0.95), (1.20, 0.22, 0.36, "#8B3DFF", 0.85),
                                       (0.95, 0.85, 0.48, "#14B8A6", 0.65), (1.45, 0.75, 0.30, "#E23D9A", 0.55),
                                       (0.10, 0.92, 0.30, "#4F46E5", 0.6), (0.72, 0.55, 0.22, "#38BDF8", 0.35)]:
        d2 = ((ax - px) ** 2 + (y - py) ** 2) / (r * r)
        img += hex_rgb(color) * (strength * np.exp(-d2 * 2.2))[..., None] * 0.8
    img = vignette(img, 0.45, 0.5, 0.5)
    finish(img, "bloom", grain=0.016)


def lines():
    """Long-exposure light trails: glowing lines sweeping across a black room."""
    rng = np.random.default_rng(53)
    x, y = grid()
    img = np.zeros((H, W, 3), np.float32) + hex_rgb("#040408")
    palette = [(0, "#2E6BFF"), (0.45, "#7B4DFF"), (0.75, "#FF4FA8"), (1, "#FF9A5A")]
    for i in range(46):
        t = i / 45
        phase = rng.random() * 6.28
        amp = 0.10 + 0.05 * np.sin(t * 3.1)
        freq = 1.6 + 0.6 * t
        center = 0.30 + 0.42 * t + 0.03 * rng.standard_normal()
        curve = center + amp * np.sin(x[0] * freq * 3.14 + phase * 0.2 + t * 2.2) + 0.08 * (x[0] - 0.5) * (t - 0.5)
        d = np.abs(y - np.broadcast_to(curve, (H, W)))
        core = np.exp(-(d / 0.0011) ** 2)
        glow = np.exp(-d / 0.012) * 0.10
        c = ramp(np.array([t]), palette)[0]
        fade = np.clip(np.sin(x * 3.14) * 1.3, 0, 1) ** 0.8
        img += (core * 0.55 + glow)[..., None] * c * fade[..., None] * 0.55
    bloom = ndimage.gaussian_filter(img, (24, 24, 0))
    img = img + bloom * 0.9
    img = vignette(img, 0.5, 0.5, 0.5)
    finish(img, "lines")


ALL = {"silk": silk, "aurora": aurora, "nebula": nebula, "dunes": dunes, "bloom": bloom, "lines": lines}

if __name__ == "__main__":
    for name in sys.argv[1:] or ALL:
        ALL[name]()
