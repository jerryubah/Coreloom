// Live charts: the last two minutes of readings as filled lines, like the desktop's LiveLineChart, and sparklines
// for the summary cards. Points are [time on the computer's clock (ms), value].
import { cssColor, parseColor, rgba, sizeCanvas } from './gauge.js';

export const SPAN = 120000;

/** Chart scales: percentages stay 0–100; others fit their readings with some headroom. */
function bounds(series, scale) {
  if (scale === 'percent') return [0, 100];
  let lo = Infinity, hi = -Infinity;
  for (const s of series) for (const [, v] of s.points) { lo = Math.min(lo, v); hi = Math.max(hi, v); }
  if (!Number.isFinite(lo)) return [0, 1];
  if (scale === 'temperature') {
    const floor = Math.max(0, Math.floor((lo - 8) / 10) * 10);
    const ceiling = Math.max(floor + 20, Math.ceil((hi + 6) / 10) * 10);
    return [floor, ceiling];
  }
  return [0, hi > 0 ? niceCeiling(hi * 1.15) : 1];
}

function niceCeiling(v) {
  const p = Math.pow(10, Math.floor(Math.log10(v)));
  for (const m of [1, 1.5, 2, 2.5, 3, 4, 5, 6, 8, 10]) if (m * p >= v) return m * p;
  return 10 * p;
}

function path(ctx, points, x, y) {
  let started = false;
  for (const [t, v] of points) {
    const px = x(t), py = y(v);
    if (!started) { ctx.moveTo(px, py); started = true; } else ctx.lineTo(px, py);
  }
}

/**
 * Draws series ({ points, color (CSS colour), fill }) over the last two minutes before `now`.
 * `labels` formats the scale's top value for the axis note (or null for none).
 */
export function drawChart(canvas, series, { now, scale = 'auto', label = null, sparkline = false } = {}) {
  const { ctx, w, h } = sizeCanvas(canvas);
  if (!now || w < 10) return;
  const [lo, hi] = bounds(series, scale);
  const top = sparkline ? 3 : 8, bottom = sparkline ? 2 : 4;
  const x = (t) => ((t - (now - SPAN)) / SPAN) * w;
  const y = (v) => top + (1 - (Math.min(Math.max(v, lo), hi) - lo) / (hi - lo || 1)) * (h - top - bottom);

  if (!sparkline) {
    ctx.strokeStyle = cssColor('--divider');
    ctx.lineWidth = 1;
    ctx.setLineDash([2, 4]);
    for (let i = 0; i <= 2; i++) {
      const gy = Math.round(top + (i / 2) * (h - top - bottom)) + 0.5;
      ctx.beginPath();
      ctx.moveTo(0, gy);
      ctx.lineTo(w, gy);
      ctx.stroke();
    }
    ctx.setLineDash([]);
    if (label) {
      ctx.fillStyle = cssColor('--text-3');
      ctx.font = '500 10.5px Inter, system-ui, sans-serif';
      ctx.textBaseline = 'top';
      ctx.fillText(label(hi), 2, 0);
      ctx.textBaseline = 'bottom';
      ctx.fillText(label(lo), 2, h);
    }
  }

  for (const s of series) {
    const points = s.points.filter(([t]) => t >= now - SPAN - 2000);
    if (points.length < 2) continue;
    const color = parseColor(s.color);
    if (s.fill !== false) {
      const gradient = ctx.createLinearGradient(0, top, 0, h);
      gradient.addColorStop(0, rgba(color, sparkline ? 0.32 : 0.26));
      gradient.addColorStop(1, rgba(color, 0));
      ctx.fillStyle = gradient;
      ctx.beginPath();
      path(ctx, points, x, y);
      ctx.lineTo(x(points[points.length - 1][0]), h);
      ctx.lineTo(x(points[0][0]), h);
      ctx.closePath();
      ctx.fill();
    }
    ctx.strokeStyle = rgba(color, 1);
    ctx.lineWidth = sparkline ? 1.6 : 2;
    ctx.lineJoin = 'round';
    ctx.lineCap = 'round';
    ctx.beginPath();
    path(ctx, points, x, y);
    ctx.stroke();

    if (!sparkline) {
      const [lt, lv] = points[points.length - 1];
      ctx.fillStyle = rgba(color, 1);
      ctx.beginPath();
      ctx.arc(Math.min(x(lt), w - 3), y(lv), 3, 0, Math.PI * 2);
      ctx.fill();
    }
  }
}

/** Keeps the last 2½ minutes of each charted reading. */
export class History {
  constructor() {
    this.series = new Map();
  }

  seed(history) {
    if (!history?.series) return;
    for (const [id, points] of Object.entries(history.series)) {
      this.series.set(id, points.map(([t, v]) => [t, v]));
    }
  }

  add(id, t, v) {
    if (!Number.isFinite(v)) return;
    let points = this.series.get(id);
    if (!points) this.series.set(id, (points = []));
    if (points.length && points[points.length - 1][0] >= t) return;
    points.push([t, v]);
    while (points.length && points[0][0] < t - SPAN - 30000) points.shift();
  }

  get(id) {
    return this.series.get(id) || [];
  }
}
