// The tick-ring gauge of Coreloom's desktop app (Controls/RadialGauge.cs, RenderTicks), drawn on a canvas: fine radial
// ticks over a 270° sweep, the lit ones brightening toward the head, whose last ticks reach further and glow. Colours
// follow the desktop's scale (GaugeTone: green, blue, orange, red) or, for temperatures, its thermometer gradients.

const reduceMotion = window.matchMedia('(prefers-reduced-motion: reduce)');
const lightTheme = window.matchMedia('(prefers-color-scheme: light)');

const TEMPERATURE = {
  dark: [['#2E5FF2', '#6FCBFF'], ['#1FAFC4', '#4FE08C'], ['#FFD24A', '#FF8A2E'], ['#FF7F3A', '#FF3B5C']],
  light: [['#2450D6', '#27A2E8'], ['#138EA8', '#1DAA5C'], ['#E0A810', '#E0680F'], ['#E8692A', '#D92A4A']],
};

const clamp = (v, lo, hi) => Math.min(hi, Math.max(lo, v));

export function cssColor(name) {
  return getComputedStyle(document.documentElement).getPropertyValue(name).trim() || '#5B8CFF';
}

export function parseColor(text) {
  const hex = text.trim().replace('#', '');
  if (/^[0-9a-f]{6}$/i.test(hex)) return [parseInt(hex.slice(0, 2), 16), parseInt(hex.slice(2, 4), 16), parseInt(hex.slice(4, 6), 16), 1];
  const m = /rgba?\(([^)]+)\)/.exec(text);
  if (m) {
    const p = m[1].split(',').map((s) => parseFloat(s));
    return [p[0], p[1], p[2], p.length > 3 ? p[3] : 1];
  }
  return [91, 140, 255, 1];
}

export const rgba = (c, a = 1) => `rgba(${Math.round(c[0])}, ${Math.round(c[1])}, ${Math.round(c[2])}, ${clamp(a * (c[3] ?? 1), 0, 1)})`;

export const lerp = (a, b, t) => [a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, a[2] + (b[2] - a[2]) * t, (a[3] ?? 1) + ((b[3] ?? 1) - (a[3] ?? 1)) * t];

/** The desktop's ThemePalette.Shift: rotate the hue and lighten in HSL. */
export function shift(color, degrees, lighten = 0) {
  const r = color[0] / 255, g = color[1] / 255, b = color[2] / 255;
  const max = Math.max(r, g, b), min = Math.min(r, g, b);
  let l = (max + min) / 2, h = 0, s = 0;
  const d = max - min;
  if (d > 1e-9) {
    s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
    h = max === r ? (g - b) / d + (g < b ? 6 : 0) : max === g ? (b - r) / d + 2 : (r - g) / d + 4;
    h *= 60;
  }
  h = (((h + degrees) % 360) + 360) % 360;
  l = clamp(l + lighten, 0, 1);
  const q = l < 0.5 ? l * (1 + s) : l + s - l * s;
  const p = 2 * l - q;
  const channel = (hue) => {
    hue = ((hue % 360) + 360) % 360;
    const v = hue < 60 ? p + (q - p) * hue / 60 : hue < 180 ? q : hue < 240 ? p + (q - p) * (240 - hue) / 60 : p;
    return Math.round(v * 255);
  };
  return [channel(h + 120), channel(h), channel(h - 120), color[3] ?? 1];
}

/** GaugeTone.At: a gauge fraction's position on the scale, 0 green … 3 red, with short blends between bands. */
export function toneAt(fraction, cool = 0.3, warm = 0.8, hot = 0.95) {
  const blend = 0.04;
  if (!Number.isFinite(fraction)) return 1;
  const f = clamp(fraction, 0, 1);
  const step = (v, from, to) => (to > from ? clamp((v - from) / (to - from), 0, 1) : 1);
  if (f >= hot) return 3;
  const b1 = Math.min(Math.min(blend, cool), (warm - cool) / 2);
  const b2 = Math.min(blend, Math.min(warm - cool, hot - warm) / 2);
  const b3 = Math.min(blend * 2, (hot - warm) / 2);
  if (f < cool - b1) return 0;
  if (f < cool + b1) return step(f, cool - b1, cool + b1);
  if (f < warm - b2) return 1;
  if (f < warm + b2) return 1 + step(f, warm - b2, warm + b2);
  if (f < hot - b3) return 2;
  return 2 + step(f, hot - b3, hot);
}

export function scaleColor(position) {
  const stops = ['--success', '--info', '--warn', '--critical'].map((n) => parseColor(cssColor(n)));
  const p = clamp(position, 0, 3);
  const band = clamp(Math.floor(p), 0, 2);
  return lerp(stops[band], stops[band + 1], clamp(p - band, 0, 1));
}

export function temperatureColors(position) {
  const table = (lightTheme.matches ? TEMPERATURE.light : TEMPERATURE.dark).map((pair) => pair.map(parseColor));
  const p = Number.isFinite(position) ? clamp(position, 0, 3) : 1;
  const band = clamp(Math.floor(p), 0, 2);
  const f = clamp(p - band, 0, 1);
  return [lerp(table[band][0], table[band + 1][0], f), lerp(table[band][1], table[band + 1][1], f)];
}

/** The colour that stands for a metric right now (glows, chips): its scale colour, or its own series colour. */
export function metricColor(metric) {
  if (!metric || metric.v === undefined) return parseColor(cssColor('--text-3'));
  if (metric.g) {
    const tone = toneAt(metric.f, ...metric.g);
    return metric.t ? lerp(...temperatureColors(tone), 0.6) : scaleColor(tone);
  }
  return parseColor(cssColor(seriesVar(metric.c)));
}

export function seriesVar(colorKey) {
  return {
    Cpu: '--cpu', Gpu: '--gpu', Memory: '--memory', Storage: '--storage', Temperature: '--temperature', Power: '--power',
    Fan: '--fan', NetworkDown: '--down', NetworkUp: '--up', Read: '--read', Write: '--write', Neutral: '--text-3', Accent: '--accent',
  }[colorKey] || '--accent';
}

/** A canvas that keeps its backing store at the screen's pixel density. */
export function sizeCanvas(canvas) {
  const dpr = Math.min(window.devicePixelRatio || 1, 3);
  const w = canvas.clientWidth, h = canvas.clientHeight;
  if (canvas.width !== Math.round(w * dpr) || canvas.height !== Math.round(h * dpr)) {
    canvas.width = Math.round(w * dpr);
    canvas.height = Math.round(h * dpr);
  }
  const ctx = canvas.getContext('2d');
  ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
  ctx.clearRect(0, 0, w, h);
  return { ctx, w, h };
}

export class Ring {
  constructor(canvas, { orb = false, thickness = 0.1 } = {}) {
    this.canvas = canvas;
    this.orb = orb;
    this.thicknessRatio = thickness;
    this.value = 0;
    this.target = 0;
    this.metric = null;
    this.frame = 0;
    new ResizeObserver(() => this.draw()).observe(canvas);
  }

  set(metric) {
    this.metric = metric;
    const target = metric && metric.v !== undefined ? clamp(metric.f ?? 0, 0, 1) : 0;
    if (reduceMotion.matches || !this.canvas.isConnected) {
      this.value = target;
      this.target = target;
      this.draw();
      return;
    }
    this.from = this.value;
    this.target = target;
    this.start = performance.now();
    cancelAnimationFrame(this.frame);
    const tick = (now) => {
      const t = clamp((now - this.start) / 520, 0, 1);
      const eased = 1 - Math.pow(1 - t, 3);
      this.value = this.from + (this.target - this.from) * eased;
      this.draw();
      if (t < 1) this.frame = requestAnimationFrame(tick);
    };
    this.frame = requestAnimationFrame(tick);
  }

  draw() {
    const { ctx, w, h } = sizeCanvas(this.canvas);
    const size = Math.min(w, h);
    if (size < 20) return;
    const metric = this.metric;
    const available = metric && metric.v !== undefined;
    const thickness = size * this.thicknessRatio;
    const reach = thickness * 0.26;
    const radius = (size - thickness) / 2 - reach - 2;
    const cx = w / 2, cy = h / 2;
    const sweep = 270;
    const start = 90 + (360 - sweep) / 2;
    const value = available ? this.value : 0;

    const pitch = clamp(size * 0.058, 3.3, 6.4);
    const count = Math.max(8, Math.round(radius * sweep * Math.PI / 180 / pitch)) + 1;
    const step = sweep / (count - 1);
    const width = clamp(pitch * 0.44, 1.2, 2.9);
    const inner = radius - thickness / 2 + width / 2;
    const outer = radius + thickness / 2 - width / 2;
    const lit = clamp(value, 0, 1) * (count - 1);
    const full = value <= 0.002 ? -1 : Math.floor(lit + 1e-6);
    const partial = lit - Math.floor(lit + 1e-6);

    // Colours: a temperature's gradient, the scale's colour at the reading, or the metric's own colour.
    let colorAt;
    if (metric?.g) {
      const tone = toneAt(value, ...metric.g);
      if (metric.t) {
        const [tail, head] = temperatureColors(tone);
        colorAt = (_, along) => lerp(tail, head, along);
      } else {
        const c = scaleColor(tone);
        colorAt = () => c;
      }
    } else {
      const c = parseColor(cssColor(seriesVar(metric?.c)));
      colorAt = () => c;
    }

    const head = colorAt(clamp(lit / Math.max(count - 1, 1), 0, 1), 1);
    const tick = (degrees, from, to) => {
      const a = degrees * Math.PI / 180;
      ctx.moveTo(cx + from * Math.cos(a), cy + from * Math.sin(a));
      ctx.lineTo(cx + to * Math.cos(a), cy + to * Math.sin(a));
    };

    if (this.orb && available) this.drawOrb(ctx, cx, cy, inner - thickness * 0.55, head);

    ctx.lineCap = 'round';
    ctx.lineWidth = width;
    ctx.strokeStyle = cssColor('--track');
    ctx.beginPath();
    for (let i = Math.max(full + 1, 0); i < count; i++) tick(start + i * step, inner, outer);
    ctx.stroke();

    if (full < 0 && partial <= 0.01) return;

    const headIndex = partial > 0.01 ? full + 1 : full;
    for (let i = 0; i <= headIndex && i < count; i++) {
      const t = count > 1 ? i / (count - 1) : 1;
      const along = headIndex > 0 ? i / headIndex : 1;
      let color = colorAt(t, along);
      const fromHead = headIndex - i;
      if (fromHead <= 2) color = lerp(color, shift(color, 0, 0.16), 1 - fromHead / 3);
      let alpha = 0.5 + 0.5 * Math.pow(along, 1.5);
      if (i === headIndex && partial > 0.01) alpha *= partial;
      const extend = fromHead <= 1 ? reach : fromHead === 2 ? reach * 0.45 : 0;
      ctx.strokeStyle = rgba(color, alpha);
      ctx.beginPath();
      tick(start + i * step, inner - extend * 0.35, outer + extend);
      ctx.stroke();
    }

    const headAngle = (start + Math.min(lit, count - 1) * step) * Math.PI / 180;
    const hx = cx + radius * Math.cos(headAngle), hy = cy + radius * Math.sin(headAngle);
    const glow = Math.max(thickness * 1.5, 7);
    const halo = ctx.createRadialGradient(hx, hy, 0, hx, hy, glow);
    halo.addColorStop(0, rgba(shift(head, 0, 0.12), 0.44));
    halo.addColorStop(0.5, rgba(head, 0.14));
    halo.addColorStop(1, rgba(head, 0));
    ctx.fillStyle = halo;
    ctx.beginPath();
    ctx.arc(hx, hy, glow, 0, Math.PI * 2);
    ctx.fill();
  }

  /** A sphere of soft light inside the ring, brightest toward the top right (RadialGauge.DrawOrb). */
  drawOrb(ctx, cx, cy, radius, color) {
    if (radius < 6) return;
    const dark = !lightTheme.matches;
    const light = shift(color, 0, 0.2);
    const ox = cx + radius * 0.4, oy = cy - radius * 0.48;
    const body = ctx.createRadialGradient(ox, oy, 0, cx, cy, radius * 1.24);
    body.addColorStop(0, rgba(light, dark ? 0.24 : 0.16));
    body.addColorStop(0.5, rgba(color, dark ? 0.11 : 0.07));
    body.addColorStop(0.82, rgba(color, dark ? 0.04 : 0.025));
    body.addColorStop(1, rgba(color, 0));
    ctx.fillStyle = body;
    ctx.beginPath();
    ctx.arc(cx, cy, radius, 0, Math.PI * 2);
    ctx.fill();
    const rim = ctx.createLinearGradient(cx + radius * 0.7, cy - radius * 0.8, cx - radius * 0.2, cy + radius * 0.4);
    rim.addColorStop(0, rgba(light, dark ? 0.44 : 0.31));
    rim.addColorStop(0.6, rgba(light, 0));
    ctx.strokeStyle = rim;
    ctx.lineWidth = 1;
    ctx.beginPath();
    ctx.arc(cx, cy, radius - 0.5, 0, Math.PI * 2);
    ctx.stroke();
  }
}

/** The memory donut: used and available as two arcs with a gap, like the desktop's DonutChart. */
export function drawDonut(canvas, parts) {
  const { ctx, w, h } = sizeCanvas(canvas);
  const size = Math.min(w, h);
  const thickness = size * 0.13;
  const radius = (size - thickness) / 2 - 1;
  const cx = w / 2, cy = h / 2;
  const total = parts.reduce((n, p) => n + Math.max(0, p.value), 0);
  ctx.lineCap = 'round';
  ctx.lineWidth = thickness;
  if (total <= 0) {
    ctx.strokeStyle = cssColor('--track');
    ctx.beginPath();
    ctx.arc(cx, cy, radius, 0, Math.PI * 2);
    ctx.stroke();
    return;
  }
  const gap = 0.07;
  let angle = -Math.PI / 2;
  for (const part of parts) {
    const span = (part.value / total) * Math.PI * 2;
    if (span > gap * 1.2) {
      ctx.strokeStyle = part.color;
      ctx.beginPath();
      ctx.arc(cx, cy, radius, angle + gap / 2, angle + span - gap / 2);
      ctx.stroke();
    }
    angle += span;
  }
}

export function onThemeChange(callback) {
  lightTheme.addEventListener('change', callback);
}
