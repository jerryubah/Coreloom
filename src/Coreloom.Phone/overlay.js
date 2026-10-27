// The phone's overlay: the in-game overlay's frame-time graph, drawn smoothly from frame times that arrive twice a
// second, and keeping the screen on while it's shown full screen.
import { parseColor, rgba, sizeCanvas } from './gauge.js';

/** How much of the recent past the graph shows, and how far behind the newest frame it draws (so it never runs dry). */
const SPAN = 3000;
const DELAY = 700;

/**
 * Frames of the game in front, placed on the computer's clock. Each update brings the frames shown since the last one
 * ("end": when the newest was shown, "ms": each frame's time, oldest first) and the computer's "now"; the phone's own
 * clock is matched to it by the quickest-arriving update, so the graph scrolls at the real pace.
 */
export class FrameTimeline {
  constructor() {
    this.frames = []; // [computer time ms, frame time ms]
    this.offsets = [];
    this.offset = null;
    this.game = null;
    this.lastFrameAt = -Infinity;
  }

  add(live, arrivedAt = performance.now()) {
    if (!live || typeof live.now !== 'number') return;
    this.offsets.push(arrivedAt - live.now);
    if (this.offsets.length > 12) this.offsets.shift();
    this.offset = Math.min(...this.offsets);

    if ((live.game || null) !== this.game) {
      this.game = live.game || null;
      this.frames = [];
    }

    const frames = live.frames;
    if (frames && Array.isArray(frames.ms) && frames.ms.length) {
      let t = frames.end;
      const added = [];
      for (let i = frames.ms.length - 1; i >= 0; i--) {
        added.push([t, frames.ms[i]]);
        t -= frames.ms[i];
      }
      added.reverse();
      const last = this.frames.length ? this.frames[this.frames.length - 1][0] : -Infinity;
      for (const f of added) if (f[0] > last + 0.1) this.frames.push(f);
      this.lastFrameAt = frames.end;
    }

    const oldest = live.now - SPAN - DELAY - 1000;
    while (this.frames.length && this.frames[0][0] < oldest) this.frames.shift();
  }

  /** The computer-clock time the graph shows as "now". */
  displayTime(perfNow = performance.now()) {
    return this.offset === null ? null : perfNow - this.offset - DELAY;
  }

  reset() {
    this.frames = [];
    this.offsets = [];
    this.offset = null;
    this.game = null;
  }
}

/** A typical frame time and the scale's top, eased so the graph doesn't jump with each spike. */
class Scale {
  constructor() { this.top = 33.4; }

  update(frames) {
    if (!frames.length) return this.top;
    const sorted = frames.map((f) => f[1]).sort((a, b) => a - b);
    const typical = sorted[Math.floor(sorted.length / 2)];
    const high = sorted[Math.floor(sorted.length * 0.98)] || typical;
    const target = Math.max(typical * 2.2, high * 1.15, 8);
    this.top += (target - this.top) * (target > this.top ? 0.25 : 0.04);
    this.typical = typical;
    return this.top;
  }
}

const scales = new WeakMap();

/**
 * Draws the frame times of the last three seconds as the in-game overlay does: a line with a soft fill, stutters
 * marked amber (over 1.6× the typical frame and 2 ms more) or red (over 2.5×), a flat line when no frames come.
 */
export function drawFrameGraph(canvas, timeline, { text = '#FFFFFF', warning = '#FFB547', critical = '#FF5C5C' } = {}, perfNow = performance.now()) {
  const { ctx, w, h } = sizeCanvas(canvas);
  if (w < 10) return;
  let scale = scales.get(canvas);
  if (!scale) scales.set(canvas, (scale = new Scale()));

  const color = parseColor(text);
  const now = timeline.displayTime(perfNow);
  const top = 3, bottom = h - 2;
  const baseline = Math.round(bottom) + 0.5;
  ctx.strokeStyle = rgba(color, 0.18);
  ctx.lineWidth = 1;
  ctx.beginPath();
  ctx.moveTo(0, baseline);
  ctx.lineTo(w, baseline);
  ctx.stroke();

  if (now === null) return;
  const from = now - SPAN;
  const visible = timeline.frames.filter((f) => f[0] >= from - 200 && f[0] <= now);
  if (visible.length < 2 || now - timeline.lastFrameAt > 1500 + DELAY) {
    return; // no game drawing: just the line
  }

  const max = scale.update(visible);
  const typical = scale.typical || max / 2.2;
  const x = (t) => ((t - from) / SPAN) * w;
  const y = (ms) => bottom - (Math.min(ms, max) / max) * (bottom - top);

  const gradient = ctx.createLinearGradient(0, top, 0, bottom);
  gradient.addColorStop(0, rgba(color, 0.28));
  gradient.addColorStop(1, rgba(color, 0.02));
  ctx.beginPath();
  ctx.moveTo(x(visible[0][0]), bottom);
  for (const [t, ms] of visible) ctx.lineTo(x(t), y(ms));
  ctx.lineTo(x(visible[visible.length - 1][0]), bottom);
  ctx.closePath();
  ctx.fillStyle = gradient;
  ctx.fill();

  ctx.beginPath();
  visible.forEach(([t, ms], i) => (i ? ctx.lineTo(x(t), y(ms)) : ctx.moveTo(x(t), y(ms))));
  ctx.strokeStyle = rgba(color, 0.92);
  ctx.lineWidth = 1.6;
  ctx.lineJoin = 'round';
  ctx.stroke();

  for (const [t, ms] of visible) {
    const bad = ms > typical * 2.5 ? critical : ms > typical * 1.6 && ms > typical + 2 ? warning : null;
    if (!bad) continue;
    const px = x(t), py = y(ms);
    ctx.strokeStyle = bad;
    ctx.lineWidth = 3;
    ctx.lineCap = 'round';
    ctx.beginPath();
    ctx.moveTo(px, Math.min(bottom - 1, py + 7));
    ctx.lineTo(px, py);
    ctx.stroke();
  }
  ctx.lineCap = 'butt';
}

/**
 * Keeps the screen on while the overlay is shown full screen. Browsers offer a wake lock only on secure (HTTPS) pages,
 * and Coreloom's page comes over the home network, so a tiny silent looping video does the job where needed (started
 * from the tap that asks for it). Best effort: the phone's own Auto-Lock setting wins if it doesn't take.
 */
export class KeepAwake {
  constructor() {
    this.lock = null;
    this.video = null;
    this.wanted = false;
    document.addEventListener('visibilitychange', () => { if (!document.hidden && this.wanted) this.start(); });
  }

  async start() {
    this.wanted = true;
    try {
      if ('wakeLock' in navigator && window.isSecureContext) {
        this.lock = await navigator.wakeLock.request('screen');
        return;
      }
    } catch { /* fall back to the video */ }
    try {
      if (!this.video) {
        const blob = await (await fetch('media/awake.mp4')).blob();
        this.video = document.createElement('video');
        this.video.setAttribute('playsinline', '');
        this.video.setAttribute('muted', '');
        this.video.setAttribute('aria-hidden', 'true');
        this.video.muted = true;
        this.video.loop = true;
        this.video.className = 'keep-awake';
        this.video.src = URL.createObjectURL(blob);
        document.body.append(this.video);
      }
      await this.video.play();
    } catch { /* not allowed here: the screen locks as usual */ }
  }

  stop() {
    this.wanted = false;
    this.lock?.release?.().catch(() => {});
    this.lock = null;
    this.video?.pause();
  }
}
