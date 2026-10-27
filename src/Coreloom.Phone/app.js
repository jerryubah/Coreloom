// Coreloom on the phone: pairing, then the computer's readings in five tabs. Everything here is drawn from the
// snapshots Coreloom sends (formatted as the desktop app shows them); nothing on the computer can be changed.
import {
  ConnectError, LiveFeed, Session, forgetPairing, fromBase64Url, getInfo, loadPairing, pair, savePairing, suggestDeviceName,
} from './connect.js';
import { Ring, cssColor, drawDonut, metricColor, onThemeChange, parseColor, rgba, seriesVar } from './gauge.js';
import { History, drawChart } from './chart.js';

const app = document.getElementById('app');

// The desktop app's icons (Themes/Icons.axaml), 24-unit stroke paths.
const PATHS = {
  overview: 'M4,4 L10,4 10,11 4,11 Z M14,4 L20,4 20,8 14,8 Z M14,12 L20,12 20,20 14,20 Z M4,15 L10,15 10,20 4,20 Z',
  gpu: 'M2.5,7 L19,7 19,17 2.5,17 Z M10.5,12 m-3,0 a3,3 0 1,0 6,0 a3,3 0 1,0 -6,0 M19,9.5 L21.5,9.5 21.5,14.5 19,14.5 M5,17 L5,20 M8.5,17 L8.5,20 M15,10 L16.5,10 M15,12 L16.5,12 M15,14 L16.5,14',
  cpu: 'M7,7 L17,7 17,17 7,17 Z M10,10 L14,10 14,14 10,14 Z M9.5,3 L9.5,7 M14.5,3 L14.5,7 M9.5,17 L9.5,21 M14.5,17 L14.5,21 M3,9.5 L7,9.5 M3,14.5 L7,14.5 M17,9.5 L21,9.5 M17,14.5 L21,14.5',
  memory: 'M3,7 L21,7 21,15 3,15 Z M6.5,15 L6.5,18 M10,15 L10,18 M14,15 L14,18 M17.5,15 L17.5,18 M6.5,10 L6.5,12 M10,10 L10,12 M14,10 L14,12 M17.5,10 L17.5,12',
  storage: 'M3.5,13 L20.5,13 20.5,19 3.5,19 Z M3.5,13 L6.5,5 17.5,5 20.5,13 M16.5,16 L16.6,16 M13.5,16 L13.6,16',
  network: 'M8,20 L8,4.5 M4,8.5 L8,4.5 12,8.5 M16,4 L16,19.5 M12,15.5 L16,19.5 20,15.5',
  temperature: 'M10,14.5 L10,5 A2,2 0 0,1 14,5 L14,14.5 A4,4 0 1,1 10,14.5 Z M12,9 L12,16.5',
  power: 'M13,2.5 L5,13.5 11.5,13.5 10.5,21.5 18.5,10.5 12,10.5 Z',
  fan: 'M12,12 m-1.6,0 a1.6,1.6 0 1,0 3.2,0 a1.6,1.6 0 1,0 -3.2,0 M12,10.4 C11,7 12,4 14.5,4 C17,4 17,7.5 13.4,11 M13.6,12 C17,11 20,12 20,14.5 C20,17 16.5,17 13,13.4 M12,13.6 C13,17 12,20 9.5,20 C7,20 7,16.5 10.6,13 M10.4,12 C7,13 4,12 4,9.5 C4,7 7.5,7 11,10.6',
  download: 'M12,4 L12,15 M7.5,10.5 L12,15 16.5,10.5 M5,19.5 L19,19.5',
  upload: 'M12,15 L12,4 M7.5,8.5 L12,4 16.5,8.5 M5,19.5 L19,19.5',
  shield: 'M12,3 L19.5,6 L19.5,11.5 C19.5,16 16.5,19.5 12,21 C7.5,19.5 4.5,16 4.5,11.5 L4.5,6 Z M9,12 L11,14 15,10',
  check: 'M12,12 m-8.5,0 a8.5,8.5 0 1,0 17,0 a8.5,8.5 0 1,0 -17,0 M8.3,12.3 L10.9,14.8 15.9,9.7',
  info: 'M12,12 m-8.5,0 a8.5,8.5 0 1,0 17,0 a8.5,8.5 0 1,0 -17,0 M12,11 L12,16.5 M12,7.6 L12,7.7',
  warning: 'M12,3.5 L21,19.5 3,19.5 Z M12,9.5 L12,14 M12,16.8 L12,16.9',
  close: 'M6,6 L18,18 M18,6 L6,18',
  refresh: 'M19.5,12 A7.5,7.5 0 1,1 17.3,6.7 M19.5,4.5 L19.5,9 15,9',
  chevron: 'M9.5,6 L15.5,12 9.5,18',
  more: 'M5,12 L5.01,12 M12,12 L12.01,12 M19,12 L19.01,12',
  phone: 'M8.5,3 L15.5,3 Q17,3 17,4.5 L17,19.5 Q17,21 15.5,21 L8.5,21 Q7,21 7,19.5 L7,4.5 Q7,3 8.5,3 Z M11,18 L13,18',
  link: 'M10,14 L14,10 M8.5,11.5 L6.5,13.5 A2.83,2.83 0 0,0 10.5,17.5 L12.5,15.5 M15.5,12.5 L17.5,10.5 A2.83,2.83 0 0,0 13.5,6.5 L11.5,8.5',
  unlink: 'M8.5,11.5 L6.5,13.5 A2.83,2.83 0 0,0 10.5,17.5 L12.5,15.5 M15.5,12.5 L17.5,10.5 A2.83,2.83 0 0,0 13.5,6.5 L11.5,8.5 M5,5 L7,7 M17,17 L19,19',
  battery: 'M3.5,8 L18.5,8 18.5,16 3.5,16 Z M21,10.5 L21,13.5',
  share: 'M12,3.5 L12,14.5 M8,7.5 L12,3.5 16,7.5 M7.5,10.5 L6,10.5 6,20.5 18,20.5 18,10.5 16.5,10.5',
  plus: 'M12,5 L12,19 M5,12 L19,12',
  pc: 'M7,3 L17,3 17,21 7,21 Z M10,6.5 L14,6.5 M10,9.5 L14,9.5 M12,17 L12.01,17',
  laptop: 'M5,6 L19,6 19,15 5,15 Z M2.5,18 L21.5,18',
  imac: 'M3,4 L21,4 21,16 3,16 Z M9.5,20 L14.5,20 M12,16 L12,20',
  macmini: 'M3,10 Q3,8 5,8 L19,8 Q21,8 21,10 L21,14 Q21,16 19,16 L5,16 Q3,16 3,14 Z M6.5,12 L6.51,12',
};

const SVG = 'http://www.w3.org/2000/svg';

function icon(name, className = 'icon') {
  const svg = document.createElementNS(SVG, 'svg');
  svg.setAttribute('viewBox', '0 0 24 24');
  svg.setAttribute('class', className);
  svg.setAttribute('aria-hidden', 'true');
  const path = document.createElementNS(SVG, 'path');
  path.setAttribute('d', PATHS[name] || PATHS.info);
  svg.append(path);
  return svg;
}

/** Builds an element: h('div', { class, text, onclick, '--tint': … }, children…). No HTML strings anywhere. */
function h(tag, props, ...children) {
  const el = document.createElement(tag);
  if (props) {
    for (const [key, value] of Object.entries(props)) {
      if (value == null || value === false) continue;
      if (key === 'class') el.className = value;
      else if (key === 'text') el.textContent = value;
      else if (key.startsWith('on') && typeof value === 'function') el.addEventListener(key.slice(2), value);
      else if (key.startsWith('--')) el.style.setProperty(key, value);
      else el.setAttribute(key, value === true ? '' : value);
    }
  }
  for (const child of children.flat()) {
    if (child == null || child === false) continue;
    el.append(child instanceof Node ? child : document.createTextNode(String(child)));
  }
  return el;
}

const setText = (el, text) => { if (el.textContent !== text) el.textContent = text; };

function clearHash() {
  if (location.hash) history.replaceState(null, '', location.pathname + location.search);
}

const isStandalone = () => window.matchMedia('(display-mode: standalone)').matches || navigator.standalone === true;
const isIos = () => /iPhone|iPad|iPod/.test(navigator.userAgent) || (/Macintosh/.test(navigator.userAgent) && navigator.maxTouchPoints > 1);
const computerWord = (platform) => (platform === 'macos' ? 'Mac' : 'PC');
const phoneWord = () => (/iPad/.test(navigator.userAgent) ? 'iPad' : 'phone');

// ---- Start ------------------------------------------------------------------------------------------------------

async function boot() {
  const params = new URLSearchParams(location.hash.slice(1));
  const linkSecret = params.get('p');
  let info = null;
  try { info = await getInfo(); } catch { /* shown on the screens below */ }

  let pairing = loadPairing();
  // Another computer answers at this address now (addresses get reused): this phone isn't paired with it.
  if (pairing && info?.pc && pairing.pc && info.pc !== pairing.pc) {
    forgetPairing();
    pairing = null;
  }

  if (pairing) {
    if (linkSecret) clearHash();
    showDashboard(pairing);
    return;
  }

  if (linkSecret) {
    clearHash();
    await pairWithLink(linkSecret, info);
    return;
  }

  showPair(info);
}

function pairMessage(error, name) {
  switch (error?.code) {
    case 'code': return `That code doesn't match. Check the six digits in Settings › Connect on ${name} and try again.`;
    case 'link': return 'This pairing link has expired. Scan the QR code in Settings › Connect again, or type the code.';
    case 'closed': return `Pairing isn't open. On ${name}, open Coreloom › Settings › Connect, then try again.`;
    case 'paused': return 'Too many wrong tries. Wait a minute, then try again.';
    case 'expired': return 'Pairing took too long. Try again.';
    case 'network': return `Can't reach ${name}. Check that this ${phoneWord()} is on the same Wi‑Fi and Coreloom is open.`;
    default: return 'Pairing didn\'t work. Try again.';
  }
}

function brand(title, lead) {
  return h('div', { class: 'pair__brand' },
    h('img', { class: 'pair__icon', src: 'icons/icon-192.png', alt: '', width: 76, height: 76 }),
    h('h1', { class: 'pair__title', text: title }),
    lead ? h('p', { class: 'pair__lead', text: lead }) : null);
}

async function pairWithLink(secretText, info) {
  const name = info?.name || 'your computer';
  app.replaceChildren(h('section', { class: 'screen pair', 'aria-label': 'Pairing' },
    brand(`Pairing with ${name}…`, null),
    h('div', { class: 'pair__brand' }, h('div', { class: 'spinner', role: 'progressbar', 'aria-label': 'Pairing' }))));
  const secret = fromBase64Url(secretText);
  try {
    if (!secret) throw new ConnectError('link');
    const result = await pair('link', secret, suggestDeviceName() + (isStandalone() ? ' (Home Screen)' : ''));
    savePairing(result);
    showDashboard(result);
  } catch (e) {
    showPair(info, e);
  }
}

function showPair(info, error = null) {
  const name = info?.name || 'your computer';
  const computer = info ? computerWord(info.platform) : 'computer';
  const code = h('input', {
    class: 'code-input num', type: 'text', inputmode: 'numeric', autocomplete: 'one-time-code', pattern: '[0-9]*',
    maxlength: 6, placeholder: '000000', 'aria-label': 'Pairing code', enterkeyhint: 'go',
  });
  const deviceName = h('input', { class: 'field__input', type: 'text', value: suggestDeviceName(), maxlength: 40, autocomplete: 'off', 'aria-label': `Name for this ${phoneWord()}` });
  const button = h('button', { class: 'button', type: 'submit', disabled: true }, 'Pair');
  const notice = h('p', { class: 'notice', role: 'alert', hidden: !error }, icon('warning'), h('span', { text: error ? pairMessage(error, name) : '' }));
  const availability = h('p', { class: 'availability', text: info ? `Checking ${name}…` : `Can't reach the computer right now` });

  const digits = () => code.value.replace(/\D/g, '').slice(0, 6);
  code.addEventListener('input', () => {
    const d = digits();
    if (code.value !== d) code.value = d;
    button.disabled = d.length !== 6;
    if (d.length === 6 && document.activeElement === code) code.blur();
  });

  const form = h('form', { class: 'pair', novalidate: true },
    h('div', { class: 'field' }, h('label', { class: 'field__label', text: 'Pairing code' }), code),
    h('div', { class: 'field' }, h('label', { class: 'field__label', text: `Name for this ${phoneWord()}` }), deviceName),
    notice,
    button);
  form.addEventListener('submit', async (event) => {
    event.preventDefault();
    if (digits().length !== 6) return;
    button.disabled = true;
    button.replaceChildren(h('span', { class: 'spinner spinner--small' }), 'Pairing…');
    notice.hidden = true;
    try {
      const result = await pair('code', digits(), deviceName.value.trim() || suggestDeviceName());
      savePairing(result);
      stopWatching();
      showDashboard(result);
    } catch (e) {
      notice.querySelector('span').textContent = pairMessage(e, name);
      notice.hidden = false;
      button.replaceChildren('Pair');
      code.value = '';
      button.disabled = true;
      code.focus();
    }
  });

  app.replaceChildren(h('section', { class: 'screen pair', 'aria-label': 'Pair this phone' },
    brand(`Pair with ${name}`, `See this ${computer}'s temperatures, load and power live on your ${phoneWord()}.`),
    h('ol', { class: 'steps' },
      h('li', null, h('span', { class: 'step-badge', text: '1' }), h('span', null, 'On ', h('strong', { text: name }), ', open ', h('strong', { text: 'Coreloom › Settings › Connect' }), '.')),
      h('li', null, h('span', { class: 'step-badge', text: '2' }), h('span', null, 'Scan its QR code with your camera, or type the six-digit code shown next to it.'))),
    form,
    availability,
    h('p', { class: 'fineprint', text: `Readings stay on your network and are encrypted between this ${phoneWord()} and your ${computer}. This ${phoneWord()} can only look: it can't change anything.` })));

  // Shows whether pairing is open on the computer, as the person opens Settings › Connect there.
  let watching = true;
  const stopWatching = () => { watching = false; };
  const watch = async () => {
    while (watching && availability.isConnected) {
      try {
        const latest = await getInfo();
        const open = latest.pairing?.code;
        availability.classList.toggle('is-open', !!open);
        setText(availability, open ? `Pairing is open on ${latest.name}` : `Waiting for Settings › Connect on ${latest.name}`);
      } catch {
        availability.classList.remove('is-open');
        setText(availability, `Can't reach ${name}. Is this ${phoneWord()} on the same Wi‑Fi?`);
      }
      await new Promise((r) => setTimeout(r, 3000));
    }
  };
  watch();
  if (!error) code.focus({ preventScroll: true });
}

function showUnpaired(name) {
  forgetPairing();
  app.replaceChildren(h('section', { class: 'screen pair', 'aria-label': 'Removed' },
    brand('This phone was removed', `In Coreloom on ${name}, this ${phoneWord()} was removed from the paired phones. Pair again to see its readings.`),
    h('button', { class: 'button', type: 'button', onclick: async () => showPair(await getInfo().catch(() => null)) }, 'Pair again')));
}

// ---- Dashboard --------------------------------------------------------------------------------------------------

const TABS = [
  { id: 'overview', label: 'Overview', icon: 'overview' },
  { id: 'graphics', label: 'Graphics', icon: 'gpu' },
  { id: 'processor', label: 'Processor', icon: 'cpu' },
  { id: 'memory', label: 'Memory', icon: 'memory' },
  { id: 'more', label: 'More', icon: 'more' },
];

const HEALTH_TONE = { Excellent: '--success', Good: '--info', Attention: '--warn', Checking: '--text-3' };
const CHECK_TONE = { Ok: '--success', Notice: '--warn', Problem: '--critical', Unknown: '--text-3' };

function allMetrics(s) {
  return [
    ...(s.overview?.cards || []).map((c) => c.metric),
    ...(s.gpu?.metrics || []), ...(s.cpu?.metrics || []), ...(s.memory?.metrics || []),
    ...(s.storage?.metrics || []), ...(s.network?.metrics || []), ...(s.battery || []),
  ];
}

class Dashboard {
  constructor(pairing) {
    this.pairing = pairing;
    this.history = new History();
    this.snapshot = null;
    this.tab = TABS.some((t) => t.id === sessionStorage.getItem('coreloom.tab')) ? sessionStorage.getItem('coreloom.tab') : 'overview';

    this.name = h('p', { class: 'header__name', text: pairing.name || 'Coreloom' });
    this.model = h('p', { class: 'header__model', text: '' });
    this.statusPill = h('span', { class: 'status', 'data-status': 'connecting', role: 'status', text: 'Connecting' });
    this.header = h('header', { class: 'header' }, h('div', { class: 'header__text' }, this.name, this.model), this.statusPill);

    this.bannerText = h('span', { class: 'banner__text' });
    this.banner = h('div', { class: 'banner', role: 'status', hidden: true }, icon('warning'), this.bannerText,
      h('button', { class: 'banner__button', type: 'button', onclick: () => this.feed?.poke() }, 'Retry'));

    this.pages = {
      overview: new OverviewPage(this),
      graphics: new GpuPage(this),
      processor: new CpuPage(this),
      memory: new MemoryPage(this),
      more: new MorePage(this),
    };
    this.pagesEl = h('div', { class: 'pages' },
      h('div', { class: 'page' }, this.banner),
      Object.entries(this.pages).map(([id, page]) => { page.el.hidden = id !== this.tab; return page.el; }));

    this.tabButtons = TABS.map((t) => h('button', {
      class: 'tab', type: 'button', role: 'tab', 'aria-selected': String(t.id === this.tab), 'aria-label': t.label,
      onclick: () => this.select(t.id),
    }, icon(t.icon), h('span', { text: t.label })));
    this.tabbar = h('nav', { class: 'tabbar', role: 'tablist', 'aria-label': 'Sections' }, this.tabButtons);

    this.sheet = new Sheet();
    this.el = h('div', { class: 'dashboard' }, this.header, this.pagesEl, this.tabbar, this.sheet.el);

    window.addEventListener('scroll', () => this.header.classList.toggle('is-scrolled', window.scrollY > 4), { passive: true });
    document.addEventListener('visibilitychange', () => this.onVisibility());
    onThemeChange(() => this.redraw());
  }

  start() {
    this.feed = new LiveFeed(this.pairing, {
      onSnapshot: (data, hello) => this.onSnapshot(data, hello),
      onStatus: (status) => this.setStatus(status),
      onUnpaired: () => showUnpaired(this.pairing.name || 'your computer'),
    });
    this.feed.start();
  }

  onVisibility() {
    if (document.hidden) {
      this.feed?.stop();
    } else {
      this.setStatus('connecting');
      this.start();
    }
  }

  select(id) {
    if (id === this.tab) {
      window.scrollTo({ top: 0, behavior: 'smooth' });
      return;
    }
    this.tab = id;
    try { sessionStorage.setItem('coreloom.tab', id); } catch { /* not kept */ }
    for (const [i, t] of TABS.entries()) this.tabButtons[i].setAttribute('aria-selected', String(t.id === id));
    for (const [pid, page] of Object.entries(this.pages)) page.el.hidden = pid !== id;
    window.scrollTo(0, 0);
    if (this.snapshot) this.pages[id].update(this.snapshot);
  }

  setStatus(status) {
    this.status = status;
    this.statusPill.dataset.status = status;
    setText(this.statusPill, { live: 'Live', connecting: 'Connecting', reconnecting: 'Reconnecting', offline: 'Offline' }[status] || status);
    const offline = status === 'offline';
    this.banner.hidden = !offline;
    setText(this.bannerText, `Can't reach ${this.pairing.name || 'your computer'}. Make sure it's on with Coreloom open, and that this ${phoneWord()} is on the same Wi‑Fi.`);
    this.pagesEl.classList.toggle('is-stale', offline);
  }

  onSnapshot(data) {
    if (data.history) this.history.seed(data.history);
    const s = data.snapshot;
    this.snapshot = s;
    for (const m of allMetrics(s)) if (m?.id && m.v !== undefined) this.history.add(m.id, s.t, m.v);
    if (/^#[0-9a-f]{6}$/i.test(s.accent || '')) document.documentElement.style.setProperty('--accent', s.accent);
    setText(this.name, s.pc?.name || this.pairing.name || 'Coreloom');
    setText(this.model, [s.pc?.title, s.pc?.subtitle].filter(Boolean).join(' · '));
    document.title = `${s.pc?.name || 'Coreloom'} · Coreloom`;
    this.pages[this.tab].update(s);
  }

  redraw() {
    if (this.snapshot) this.pages[this.tab].update(this.snapshot);
  }

  async setupHomeScreen() {
    const session = new Session(this.pairing);
    await session.open();
    const { secret } = await session.setupLink();
    await session.bye();
    // The Home Screen copy opens with this one-time link and pairs itself (its storage is separate from Safari's).
    history.replaceState(null, '', `${location.pathname}#p=${secret}`);
    // The link stays in the address until the page reloads, so it's there whenever the person adds the page.
    this.sheet.open(isIos()
      ? { title: 'Add to Home Screen', text: 'Tap the Share button in Safari, then Add to Home Screen. Open Coreloom from your Home Screen within 15 minutes and it pairs itself.' }
      : { title: 'Add to Home screen', text: 'Open the browser menu (⋮), then Add to Home screen. Open Coreloom from your Home screen within 15 minutes and it pairs itself.' });
  }

  async forget() {
    if (!window.confirm(`Forget ${this.pairing.name || 'this computer'}? This ${phoneWord()} stops seeing its readings until you pair again.`)) return;
    this.feed?.stop();
    try {
      const session = new Session(this.pairing);
      await session.forget();
    } catch { /* the computer may be off: forget here anyway */ }
    forgetPairing();
    showPair(await getInfo().catch(() => null));
  }
}

class Sheet {
  constructor() {
    this.title = h('h2', { class: 'sheet__title' });
    this.text = h('p', { class: 'sheet__text' });
    this.done = h('button', { class: 'button', type: 'button', onclick: () => this.close() }, 'Done');
    this.el = h('div', { class: 'sheet', hidden: true, role: 'dialog', 'aria-modal': 'true', onclick: (e) => { if (e.target === this.el) this.close(); } },
      h('div', { class: 'sheet__panel' }, this.title, this.text, this.done));
  }

  open({ title, text }, onClose) {
    setText(this.title, title);
    setText(this.text, text);
    this.onClose = onClose;
    this.el.hidden = false;
    this.done.focus();
  }

  close() {
    this.el.hidden = true;
    this.onClose?.();
  }
}

// ---- Building blocks --------------------------------------------------------------------------------------------

/** A tick-ring gauge with its reading in the middle and, optionally, a temperature chip under it. */
class RingView {
  constructor(label, { orb = true, small = false } = {}) {
    this.canvas = h('canvas', { 'aria-hidden': 'true' });
    this.number = h('span', { class: 'ring__number num', text: '—' });
    this.unit = h('span', { class: 'ring__unit' });
    this.label = h('span', { class: 'ring__label', text: label });
    this.temp = h('span', { class: 'ring__temp num', hidden: true });
    this.el = h('div', { class: `ring${small ? ' ring--small' : ''}`, role: 'img' },
      this.canvas,
      h('div', { class: 'ring__center' }, h('div', { class: 'ring__value' }, this.number, this.unit), this.label),
      this.temp);
    this.ring = new Ring(this.canvas, { orb });
  }

  update(metric, temperature = null, label = null) {
    this.ring.set(metric);
    setText(this.number, metric?.n ?? '—');
    setText(this.unit, metric?.u ?? '');
    if (label) setText(this.label, label);
    this.el.setAttribute('aria-label', `${this.label.textContent} ${metric?.n ?? '—'}${metric?.u ?? ''}`);
    if (temperature && temperature.v !== undefined) {
      this.temp.hidden = false;
      setText(this.temp, `${temperature.n}${temperature.u}`);
      this.temp.style.setProperty('--temp', rgba(metricColor(temperature)));
    } else {
      this.temp.hidden = true;
    }
    return metricColor(metric);
  }
}

/** A summary card: icon, title, the reading, a secondary line and a sparkline of its last two minutes. */
class Tile {
  constructor(dash, title, iconName) {
    this.dash = dash;
    this.iconTile = h('span', { class: 'icon-tile' }, icon(iconName || 'info'));
    this.title = h('span', { text: title });
    this.number = h('span', { class: 'tile__number num', text: '—' });
    this.unit = h('span', { class: 'tile__unit' });
    this.detail = h('div', { class: 'tile__detail' });
    this.spark = h('canvas', { 'aria-hidden': 'true' });
    this.sparkWrap = h('div', { class: 'tile__spark' }, this.spark);
    this.el = h('div', { class: 'tile' },
      h('div', { class: 'tile__head' }, this.iconTile, this.title),
      h('div', { class: 'tile__value' }, this.number, this.unit),
      this.detail,
      this.sparkWrap);
  }

  update(metric, now) {
    setText(this.number, metric?.n ?? '—');
    setText(this.unit, metric?.u ?? '');
    setText(this.detail, metric?.d ?? '');
    this.detail.hidden = !metric?.d;
    const color = metricColor(metric);
    const tint = metric?.v !== undefined ? rgba(color) : cssColor('--text-3');
    this.el.style.setProperty('--glow', tint);
    this.iconTile.style.setProperty('--tint', metric?.g ? tint : cssColor(seriesVar(metric?.c)));
    // A sparkline once there's half a minute to draw (charted readings come with two minutes of history).
    const points = metric ? this.dash.history.get(metric.id) : [];
    const enough = points.length >= 3 && points[points.length - 1][0] - points[0][0] >= 30000;
    this.sparkWrap.hidden = !enough;
    if (enough) {
      drawChart(this.spark, [{ points, color: metric.g ? rgba(color) : cssColor(seriesVar(metric.c)) }], { now, scale: metric.u === '%' ? 'percent' : metric.t ? 'temperature' : 'auto', sparkline: true });
    }
  }
}

const ICON_FOR = (metric) => {
  const id = metric.id || '';
  if (metric.t) return 'temperature';
  if (/\/power(\/|$)|^sys\/power/.test(id)) return 'power';
  if (/\/fan\//.test(id)) return 'fan';
  if (/\/clock(\/|$)/.test(id)) return 'refresh';
  if (/^net\/.*\/(down|rx-session)$|^disk\/.*\/read$/.test(id)) return 'download';
  if (/^net\/.*\/(up|tx-session)$|^disk\/.*\/write$/.test(id)) return 'upload';
  if (/\/vram\/|^mem\//.test(id)) return 'memory';
  if (/^battery\//.test(id)) return 'battery';
  if (/^gpu\//.test(id)) return 'gpu';
  if (/^cpu\//.test(id)) return 'cpu';
  return 'info';
};

/** A grid of tiles for a list of metrics, rebuilt only when the list of readings changes. */
class TileGrid {
  constructor(dash) {
    this.dash = dash;
    this.el = h('div', { class: 'grid' });
    this.signature = '';
    this.tiles = [];
  }

  update(metrics, now) {
    const signature = metrics.map((m) => m.id).join('|');
    if (signature !== this.signature) {
      this.signature = signature;
      this.tiles = metrics.map((m) => new Tile(this.dash, m.label, ICON_FOR(m)));
      this.el.replaceChildren(...this.tiles.map((t) => t.el));
    }
    metrics.forEach((m, i) => this.tiles[i].update(m, now));
  }
}

/** A chart card: title, the chart, and a legend with the latest values. */
class ChartCard {
  constructor(dash, title, specs, scale) {
    this.dash = dash;
    this.specs = specs; // [{ id: fn(s) | string, label, color }]
    this.scale = scale;
    this.canvas = h('canvas', { 'aria-hidden': 'true' });
    this.legend = h('div', { class: 'legend' });
    this.el = h('section', { class: 'card' },
      h('div', { class: 'card__head' }, h('h3', { class: 'card__title', text: title }), h('span', { class: 'card__aside', text: 'Last 2 minutes' })),
      h('div', { class: 'chart' }, this.canvas),
      this.legend);
  }

  update(s, metricsById) {
    const series = [];
    const legend = [];
    for (const spec of this.specs) {
      const metric = metricsById.get(typeof spec.id === 'function' ? spec.id(s) : spec.id);
      if (!metric) continue;
      const color = cssColor(spec.color);
      series.push({ points: this.dash.history.get(metric.id), color, fill: spec.fill !== false });
      legend.push(h('span', { class: 'legend__item' }, h('span', { class: 'legend__swatch', '--swatch': color }), spec.label, ' ',
        h('span', { class: 'legend__value num', text: `${metric.n}${metric.u === '%' || metric.u.startsWith('°') ? '' : ' '}${metric.u}` })));
    }
    this.el.hidden = series.length === 0;
    this.legend.replaceChildren(...legend);
    // Only percentages carry axis labels: other readings come raw (bytes, °C) and are shown formatted in the legend.
    drawChart(this.canvas, series, { now: s.t, scale: this.scale, label: this.scale === 'percent' ? (v) => `${Math.round(v)}%` : null });
  }
}

const byId = (s) => new Map(allMetrics(s).filter(Boolean).map((m) => [m.id, m]));

// ---- Overview ---------------------------------------------------------------------------------------------------

class OverviewPage {
  constructor(dash) {
    this.dash = dash;
    this.machine = h('span', { class: 'hero__machine' });
    this.title = h('h1', { class: 'hero__title' });
    this.subtitle = h('p', { class: 'hero__subtitle' });
    this.health = h('button', { class: 'health-pill', type: 'button', onclick: () => { dash.select('more'); setTimeout(() => document.getElementById('health')?.scrollIntoView({ behavior: 'smooth' }), 50); } });
    this.gpuRing = new RingView('GPU');
    this.cpuRing = new RingView('CPU');
    this.hero = h('section', { class: 'hero card' },
      h('div', { class: 'hero__top' }, this.machine, h('div', { class: 'hero__text' }, this.title, this.subtitle, this.health)),
      h('div', { class: 'rings' }, this.gpuRing.el, this.cpuRing.el));
    this.cards = new TileGrid(dash);
    this.cards.el.classList.add('section');
    this.load = new ChartCard(dash, 'Load', [
      { id: 'cpu/load', label: 'CPU', color: '--cpu' },
      { id: (s) => s.overview?.cards?.find((c) => c.card === 'gpu-load')?.metric?.id || 'gpu/0/load', label: 'GPU', color: '--gpu' },
    ], 'percent');
    this.temps = new ChartCard(dash, 'Temperatures', [
      { id: 'cpu/temp', label: 'CPU', color: '--cpu', fill: false },
      { id: (s) => s.overview?.cards?.find((c) => c.card === 'gpu-temp')?.metric?.id || 'gpu/0/temp', label: 'GPU', color: '--gpu', fill: false },
    ], 'temperature');
    this.el = h('div', { class: 'page', role: 'tabpanel', 'aria-label': 'Overview' }, this.hero, this.cards.el,
      h('div', { class: 'section split' }, this.load.el, this.temps.el));
  }

  update(s) {
    const pc = s.pc || {};
    const kind = { pc: 'pc', macbook: 'laptop', imac: 'imac', macmini: 'macmini', macstudio: 'macmini', macpro: 'pc' }[pc.kind] || 'pc';
    if (this.machine.dataset.kind !== kind) {
      this.machine.dataset.kind = kind;
      this.machine.replaceChildren(icon(kind, 'icon hero__machine'));
    }
    setText(this.title, pc.title || pc.name || '');
    setText(this.subtitle, pc.subtitle || '');

    const health = s.health || {};
    this.health.style.setProperty('--pill', cssColor(HEALTH_TONE[health.key] || '--text-3'));
    this.health.replaceChildren(icon('shield'), `${health.text || 'Checking…'}`);
    this.health.setAttribute('aria-label', `Health: ${health.text}. ${health.summary || ''}`);

    const card = (id) => s.overview?.cards?.find((c) => c.card === id)?.metric;
    const gpuColor = this.gpuRing.update(card('gpu-load'), card('gpu-temp'));
    const cpuColor = this.cpuRing.update(card('cpu-load'), card('cpu-temp'));
    this.hero.style.setProperty('--ring-a', rgba(gpuColor));
    this.hero.style.setProperty('--ring-b', rgba(cpuColor));

    const cards = (s.overview?.cards || []).map((c) => ({ ...c.metric, label: c.title }));
    this.cards.update(cards, s.t);
    const metrics = byId(s);
    this.load.update(s, metrics);
    this.temps.update(s, metrics);
  }
}

// ---- Graphics and Processor -------------------------------------------------------------------------------------

class PartPage {
  constructor(dash, { label, section, loadLabel = 'Utilization', chartColor }) {
    this.dash = dash;
    this.section = section;
    this.title = h('h1', { class: 'page__title' });
    this.subtitle = h('p', { class: 'page__subtitle', text: label });
    this.loadRing = new RingView(loadLabel);
    this.tempRing = new RingView('Temperature');
    this.rings = h('section', { class: 'ring-card card' }, h('div', { class: 'rings' }, this.loadRing.el, this.tempRing.el));
    this.extra = h('div');
    this.grid = new TileGrid(dash);
    this.grid.el.classList.add('section');
    this.chartLoad = new ChartCard(dash, loadLabel, [{ id: (s) => s[section]?.metrics?.[0]?.id, label: loadLabel, color: chartColor }], 'percent');
    this.chartTemp = new ChartCard(dash, 'Temperature', [{ id: (s) => s[section]?.metrics?.[1]?.id, label: 'Temperature', color: '--temperature', fill: false }], 'temperature');
    this.el = h('div', { class: 'page', role: 'tabpanel', 'aria-label': label },
      this.title, this.subtitle, this.rings, this.extra, this.grid.el, h('div', { class: 'section split' }, this.chartLoad.el, this.chartTemp.el));
  }

  update(s) {
    const part = s[this.section] || {};
    setText(this.title, part.name || '');
    const [load, temp, ...rest] = part.metrics || [];
    this.loadRing.update(load);
    this.tempRing.update(temp);
    this.tempRing.el.hidden = !temp || temp.v === undefined;
    this.grid.update(rest, s.t);
    const metrics = byId(s);
    this.chartLoad.update(s, metrics);
    this.chartTemp.update(s, metrics);
  }
}

class GpuPage extends PartPage {
  constructor(dash) {
    super(dash, { label: 'Graphics', section: 'gpu', chartColor: '--gpu' });
  }
}

class CpuPage extends PartPage {
  constructor(dash) {
    super(dash, { label: 'Processor', section: 'cpu', chartColor: '--cpu' });
    this.coresCaption = h('span', { class: 'card__aside' });
    this.cores = h('div', { class: 'cores', role: 'img', 'aria-label': 'Load of each core' });
    this.coresCard = h('section', { class: 'card section' },
      h('div', { class: 'card__head' }, h('h3', { class: 'card__title', text: 'Each core' }), this.coresCaption), this.cores);
    this.extra.append(this.coresCard);
  }

  update(s) {
    super.update(s);
    const threads = s.cpu?.threads || [];
    this.coresCard.hidden = threads.length === 0;
    if (this.cores.childElementCount !== threads.length) {
      this.cores.replaceChildren(...threads.map(() => h('div', { class: 'core' }, h('div', { class: 'core__fill' }))));
      this.cores.style.setProperty('--cores', String(Math.max(1, threads.length)));
    }
    const mac = s.pc?.platform === 'macos';
    setText(this.coresCaption, `${threads.length} ${mac ? 'cores' : 'threads'}`);
    threads.forEach((load, i) => {
      const fill = this.cores.children[i].firstChild;
      fill.style.height = `${Math.max(2, load)}%`;
      fill.style.setProperty('--fill', load >= 97 ? cssColor('--critical') : load >= 85 ? cssColor('--warn') : cssColor('--cpu'));
    });
  }
}

// ---- Memory -----------------------------------------------------------------------------------------------------

class MemoryPage {
  constructor(dash) {
    this.dash = dash;
    this.subtitle = h('p', { class: 'page__subtitle' });
    this.donut = h('canvas', { 'aria-hidden': 'true' });
    this.center = h('span', { class: 'ring__number num', text: '—' });
    this.usedText = h('strong', { class: 'num' });
    this.availableText = h('strong', { class: 'num' });
    this.donutCard = h('section', { class: 'card' },
      h('div', { class: 'donut-row' },
        h('div', { class: 'ring' }, this.donut, h('div', { class: 'ring__center' }, h('div', { class: 'ring__value' }, this.center), h('span', { class: 'ring__label', text: 'in use' }))),
        h('div', { class: 'donut-legend' },
          h('div', { class: 'donut-legend__row' }, (this.usedSwatch = h('span', { class: 'legend__swatch', '--swatch': 'var(--memory)' })), 'In use', this.usedText),
          h('div', { class: 'donut-legend__row' }, h('span', { class: 'legend__swatch', '--swatch': 'var(--track)' }), 'Available', this.availableText))));
    this.grid = new TileGrid(dash);
    this.grid.el.classList.add('section');
    this.chart = new ChartCard(dash, 'Memory in use', [{ id: 'mem/load', label: 'In use', color: '--memory' }], 'percent');
    this.el = h('div', { class: 'page', role: 'tabpanel', 'aria-label': 'Memory' },
      h('h1', { class: 'page__title', text: 'Memory' }), this.subtitle, this.donutCard, this.grid.el, h('div', { class: 'section' }, this.chart.el));
  }

  update(s) {
    const memory = s.memory || {};
    setText(this.subtitle, memory.caption || '');
    const [load, ...rest] = memory.metrics || [];
    setText(this.center, load?.n ? `${load.n}%` : '—');
    setText(this.usedText, memory.usedText || '—');
    setText(this.availableText, memory.availableText || '—');
    const used = memory.used || 0, available = memory.available || 0;
    // The ring takes the gauges' colour scale (as on the desktop); its legend swatch follows it.
    const usedColor = rgba(load?.g ? metricColor(load) : parseColor(cssColor('--memory')));
    drawDonut(this.donut, [{ value: used, color: usedColor }, { value: available, color: cssColor('--track') }]);
    this.usedSwatch.style.setProperty('--swatch', usedColor);
    this.grid.update(rest, s.t);
    this.chart.update(s, byId(s));
  }
}

// ---- More: storage, network, battery, health, this computer, this phone ------------------------------------------

class MorePage {
  constructor(dash) {
    this.dash = dash;
    this.volumes = h('div');
    this.diskTiles = new TileGrid(dash);
    this.diskChart = new ChartCard(dash, 'Disk activity', [{ id: 'disk/total/read', label: 'Read', color: '--read' }, { id: 'disk/total/write', label: 'Write', color: '--write' }], 'auto');
    this.adapter = h('p', { class: 'section__note' });
    this.netTiles = new TileGrid(dash);
    this.netChart = new ChartCard(dash, 'Traffic', [{ id: 'net/active/down', label: 'Download', color: '--down' }, { id: 'net/active/up', label: 'Upload', color: '--up' }], 'auto');
    this.batterySection = h('section', { class: 'section', hidden: true });
    this.batteryTiles = new TileGrid(dash);
    this.batterySection.append(h('h2', { class: 'section__title', text: 'Battery' }), this.batteryTiles.el);
    this.healthList = h('div');
    this.specs = h('ul', { class: 'list' });
    this.about = h('p', { class: 'section__note' });

    const homeRow = isStandalone() ? null : h('button', { class: 'list__row list__row--action', type: 'button', onclick: () => dash.setupHomeScreen().catch(() => window.alert('Couldn\'t prepare the Home Screen link. Try again.')) },
      h('span', { class: 'icon-tile' }, icon('plus')),
      h('span', { class: 'list__main' }, h('span', { class: 'list__title', text: 'Add to Home Screen' }), h('span', { class: 'list__detail', text: 'Open Coreloom like an app, full screen.' })),
      icon('chevron', 'icon chevron'));
    const forgetRow = h('button', { class: 'list__row list__row--action list__row--danger', type: 'button', onclick: () => dash.forget() },
      h('span', { class: 'icon-tile', '--tint': 'var(--critical)' }, icon('unlink')),
      h('span', { class: 'list__main' }, h('span', { class: 'list__title', text: 'Forget this computer' }), h('span', { class: 'list__detail', text: `Stop seeing its readings on this ${phoneWord()}.` })));

    this.el = h('div', { class: 'page', role: 'tabpanel', 'aria-label': 'More' },
      h('h1', { class: 'page__title', text: 'More' }),
      h('p', { class: 'page__subtitle', text: 'Storage, network, health and this connection' }),
      h('section', null, h('h2', { class: 'section__title', text: 'Storage' }), h('div', { class: 'card' }, this.volumes)),
      h('div', { class: 'section' }, this.diskTiles.el),
      h('div', { class: 'section' }, this.diskChart.el),
      h('section', { class: 'section' }, h('h2', { class: 'section__title', text: 'Network' }), this.adapter, this.netTiles.el),
      h('div', { class: 'section' }, this.netChart.el),
      this.batterySection,
      h('section', { class: 'section', id: 'health' }, h('h2', { class: 'section__title', text: 'Health' }), this.healthList),
      h('section', { class: 'section' }, h('h2', { class: 'section__title', text: 'This computer' }), h('div', { class: 'card' }, this.specs)),
      h('section', { class: 'section' }, h('h2', { class: 'section__title', text: `This ${phoneWord()}` }), this.about,
        h('div', { class: 'card' }, h('div', { class: 'list' }, homeRow, forgetRow))));
  }

  update(s) {
    const volumes = s.storage?.volumes || [];
    if (this.volumes.childElementCount !== volumes.length || this.volumes.dataset.ids !== volumes.map((v) => v.id).join('|')) {
      this.volumes.dataset.ids = volumes.map((v) => v.id).join('|');
      this.volumes.replaceChildren(...volumes.map(() => {
        const fill = h('div', { class: 'bar__fill' });
        return h('div', { class: 'volume' },
          h('div', { class: 'volume__head' }, h('span', { class: 'volume__name' }), h('span', { class: 'volume__meta num' })),
          h('div', { class: 'bar' }, fill),
          h('div', { class: 'tile__detail num' }));
      }));
      if (!volumes.length) this.volumes.append(h('p', { class: 'list__detail', text: 'No drives reported.' }));
    }
    volumes.forEach((v, i) => {
      const row = this.volumes.children[i];
      setText(row.querySelector('.volume__name'), v.name);
      setText(row.querySelector('.volume__meta'), `${v.percent} used`);
      setText(row.querySelector('.tile__detail'), `${v.used} of ${v.total} · ${v.free} free`);
      const fill = row.querySelector('.bar__fill');
      fill.style.width = `${Math.max(1, v.f * 100)}%`;
      fill.style.setProperty('--fill', cssColor(v.l === 'Critical' ? '--critical' : v.l === 'Elevated' ? '--warn' : '--storage'));
    });

    const metrics = byId(s);
    this.diskTiles.update(s.storage?.metrics || [], s.t);
    this.diskChart.update(s, metrics);
    setText(this.adapter, s.network?.adapter ? `Connected through ${s.network.adapter}` : '');
    this.netTiles.update(s.network?.metrics || [], s.t);
    this.netChart.update(s, metrics);
    this.batterySection.hidden = !s.battery?.length;
    if (s.battery?.length) this.batteryTiles.update(s.battery, s.t);

    this.renderHealth(s.health);
    const specs = s.pc?.specs || [];
    if (this.specs.dataset.sig !== JSON.stringify(specs)) {
      this.specs.dataset.sig = JSON.stringify(specs);
      this.specs.replaceChildren(...specs.map(([k, v]) => h('li', { class: 'list__row' }, h('span', { class: 'list__key', text: k }), h('span', { class: 'list__value', text: v }))));
    }
    setText(this.about, `Paired with ${s.pc?.name || this.dash.pairing.name} · Coreloom ${this.dash.pairing.version || ''}`.trim());
  }

  renderHealth(health) {
    const signature = JSON.stringify(health?.parts?.map((p) => [p.key, p.summary, p.checks.map((c) => [c.state, c.title, c.detail])]));
    if (this.healthList.dataset.sig === signature) return;
    this.healthList.dataset.sig = signature;
    this.healthList.replaceChildren(...(health?.parts || []).map((part) => h('section', { class: 'card' },
      h('div', { class: 'card__head' },
        h('span', { class: 'icon-tile', '--tint': `var(${HEALTH_TONE[part.key] || '--text-3'})` }, icon(part.icon === 'health' ? 'shield' : part.icon)),
        h('h3', { class: 'card__title', text: part.name }),
        h('span', { class: 'card__aside', text: part.text })),
      h('ul', { class: 'list' }, part.checks.map((check) => h('li', { class: 'list__row list__row--stack' },
        h('span', { class: 'state-dot', '--dot': `var(${CHECK_TONE[check.state] || '--text-3'})` }),
        h('span', { class: 'list__main' }, h('span', { class: 'list__title', text: check.title }), check.detail ? h('span', { class: 'list__detail', text: check.detail }) : null)))))));
  }
}

function showDashboard(pairing) {
  const dashboard = new Dashboard(pairing);
  app.replaceChildren(dashboard.el);
  dashboard.start();
}

boot();
