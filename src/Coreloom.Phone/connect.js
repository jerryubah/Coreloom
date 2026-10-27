// Coreloom Connect, the phone side: pairing, sessions and live readings, matching ConnectCrypto.cs and
// ConnectServer.cs in Coreloom.Core. The page is served by the computer over the local network (plain HTTP, so
// no WebCrypto): the cryptography comes from the noble libraries in vendor/crypto.js.
import { p256, hkdf, hmac, sha256, gcm, randomBytes } from './vendor/crypto.js';

const encoder = new TextEncoder();
const decoder = new TextDecoder();
const STORE_KEY = 'coreloom.connect.v1';

export function toBase64Url(bytes) {
  let binary = '';
  for (const b of bytes) binary += String.fromCharCode(b);
  return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}

export function fromBase64Url(text) {
  if (typeof text !== 'string' || !/^[A-Za-z0-9_-]+$/.test(text)) return null;
  const padded = text.replace(/-/g, '+').replace(/_/g, '/') + '==='.slice((text.length + 3) % 4);
  try {
    const binary = atob(padded);
    const bytes = new Uint8Array(binary.length);
    for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
    return bytes;
  } catch {
    return null;
  }
}

function concat(...parts) {
  const out = new Uint8Array(parts.reduce((n, p) => n + p.length, 0));
  let at = 0;
  for (const p of parts) { out.set(p, at); at += p.length; }
  return out;
}

function seal(key, plaintext, associatedData) {
  const nonce = randomBytes(12);
  return concat(nonce, gcm(key, nonce, encoder.encode(associatedData)).encrypt(plaintext));
}

function open(key, sealed, associatedData) {
  if (!sealed || sealed.length < 28) return null;
  try {
    return gcm(key, sealed.subarray(0, 12), encoder.encode(associatedData)).decrypt(sealed.subarray(12));
  } catch {
    return null;
  }
}

/** A failure the screens can explain: `code` is the server's ("code", "closed", "unpaired"…) or "network". */
export class ConnectError extends Error {
  constructor(code, status = 0) {
    super(code);
    this.code = code;
    this.status = status;
  }
}

async function post(path, body, headers = {}, timeoutMs = 15000) {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), timeoutMs);
  try {
    return await fetch(path, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', ...headers },
      body: typeof body === 'string' ? body : JSON.stringify(body),
      cache: 'no-store',
      credentials: 'omit',
      signal: controller.signal,
    });
  } catch {
    throw new ConnectError('network');
  } finally {
    clearTimeout(timer);
  }
}

async function failure(response) {
  let code = 'server';
  try { code = (await response.json()).error || code; } catch { /* not JSON */ }
  return new ConnectError(code, response.status);
}

// ---- What this phone remembers -----------------------------------------------------------------------------------

export function loadPairing() {
  try {
    const value = JSON.parse(localStorage.getItem(STORE_KEY) || 'null');
    return value && value.device && value.key ? value : null;
  } catch {
    return null;
  }
}

export function savePairing(pairing) {
  try { localStorage.setItem(STORE_KEY, JSON.stringify(pairing)); } catch { /* private mode: kept for this visit */ }
}

export function forgetPairing() {
  try { localStorage.removeItem(STORE_KEY); } catch { /* nothing kept */ }
}

/** The computer's name, version and whether pairing is open (no pairing needed). */
export async function getInfo() {
  try {
    const response = await fetch('/api/info', { cache: 'no-store', credentials: 'omit' });
    if (!response.ok) throw await failure(response);
    return await response.json();
  } catch (e) {
    throw e instanceof ConnectError ? e : new ConnectError('network');
  }
}

/**
 * Pairs this phone. `kind` is "link" (the QR code's secret, 32 bytes) or "code" (the six digits). Returns what to
 * keep: { pc, device, key, name, platform, version }.
 */
export async function pair(kind, secret, deviceName) {
  const secretKey = p256.utils.randomSecretKey();
  const phonePublic = p256.getPublicKey(secretKey, false);

  const start = await post('/api/pair/start', { kind, pub: toBase64Url(phonePublic) });
  if (!start.ok) throw await failure(start);
  const { attempt, pub } = await start.json();
  const pcPublic = fromBase64Url(pub);
  if (!pcPublic || pcPublic.length !== 65) throw new ConnectError('bad-key');

  const shared = p256.getSharedSecret(secretKey, pcPublic, true).subarray(1);
  const secretBytes = typeof secret === 'string' ? encoder.encode(secret) : secret;
  const salt = sha256(concat(encoder.encode('coreloom/pair/v1'), secretBytes));
  const info = concat(encoder.encode('coreloom/pair/keys'), phonePublic, pcPublic);
  const keys = hkdf(sha256, shared, salt, info, 64);
  const encryptKey = keys.subarray(0, 32);
  const macKey = keys.subarray(32);

  const proof = hmac(sha256, macKey, encoder.encode('coreloom/pair/phone'));
  const device = seal(encryptKey, encoder.encode(JSON.stringify({ name: deviceName })), 'coreloom/pair/device');
  const finish = await post('/api/pair/finish', { attempt, proof: toBase64Url(proof), device: toBase64Url(device) });
  if (!finish.ok) throw await failure(finish);
  const { result } = await finish.json();
  const plain = open(encryptKey, fromBase64Url(result), 'coreloom/pair/result');
  if (!plain) throw new ConnectError('tampered');
  return JSON.parse(decoder.decode(plain));
}

/** One connection to the computer: opens with the device key, then every request is signed and every answer sealed. */
export class Session {
  constructor(pairing) {
    this.pairing = pairing;
    this.id = null;
    this.counter = 0;
  }

  async open() {
    const phoneNonce = randomBytes(16);
    const response = await post('/api/session', { device: this.pairing.device, nonce: toBase64Url(phoneNonce) });
    if (!response.ok) throw await failure(response);
    const { session, nonce, hello } = await response.json();
    const pcNonce = fromBase64Url(nonce);
    const deviceKey = fromBase64Url(this.pairing.key);
    if (!pcNonce || !deviceKey) throw new ConnectError('bad-key');
    const keys = hkdf(sha256, deviceKey, concat(phoneNonce, pcNonce), encoder.encode('coreloom/session/v1'), 64);
    this.encryptKey = keys.slice(0, 32);
    this.macKey = keys.slice(32);
    const greeting = open(this.encryptKey, fromBase64Url(hello), 'coreloom/session/hello');
    if (!greeting) throw new ConnectError('tampered');
    this.id = session;
    this.counter = 0;
    this.hello = JSON.parse(decoder.decode(greeting));
    return this.hello;
  }

  async request(path, extra = {}, timeoutMs = 15000) {
    const n = ++this.counter;
    const body = JSON.stringify({ session: this.id, n, ...extra });
    const mac = hmac(sha256, this.macKey, concat(encoder.encode(path + '\n'), encoder.encode(body)));
    const response = await post(path, body, { 'X-Coreloom-Auth': toBase64Url(mac) }, timeoutMs);
    if (response.status === 204) return { n, bytes: null };
    if (!response.ok) throw await failure(response);
    return { n, bytes: new Uint8Array(await response.arrayBuffer()) };
  }

  /** The next readings after `after` (waits up to 20 s on the computer), or null when nothing new came. */
  async live(after) {
    const { n, bytes } = await this.request('/api/live', { after }, 35000);
    if (!bytes) return null;
    const plain = open(this.encryptKey, bytes, `coreloom/live\n${n}`);
    if (!plain) throw new ConnectError('tampered');
    return JSON.parse(decoder.decode(plain));
  }

  /** A one-time pairing secret for a Home Screen copy of this app. */
  async setupLink() {
    const { n, bytes } = await this.request('/api/setup-link');
    const plain = bytes && open(this.encryptKey, bytes, `coreloom/setup\n${n}`);
    if (!plain) throw new ConnectError('tampered');
    return JSON.parse(decoder.decode(plain));
  }

  /** Unpairs this phone on the computer too. */
  async forget() {
    if (!this.id) await this.open();
    await this.request('/api/forget', {}, 8000);
    this.id = null;
  }

  async bye() {
    if (!this.id) return;
    try { await this.request('/api/bye', {}, 3000); } catch { /* leaving anyway */ }
    this.id = null;
  }
}

/**
 * Keeps readings coming: opens a session, asks for each new reading, and reconnects after network trouble with a
 * short back-off. Pauses while the page is hidden (no polling in the background).
 */
export class LiveFeed {
  constructor(pairing, { onSnapshot, onStatus, onUnpaired }) {
    this.pairing = pairing;
    this.onSnapshot = onSnapshot;
    this.onStatus = onStatus;
    this.onUnpaired = onUnpaired;
    this.session = null;
    this.after = 0;
    this.running = false;
    this.failures = 0;
    this.wake = null;
    this.status = 'connecting';
  }

  start() {
    if (this.running) return;
    this.running = true;
    this.loop();
  }

  stop() {
    this.running = false;
    this.session?.bye();
    this.session = null;
    this.wake?.();
  }

  /** Try again now (the page came back, or the person tapped Retry). */
  poke() {
    this.failures = 0;
    this.wake?.();
  }

  setStatus(status) {
    if (this.status !== status) {
      this.status = status;
      this.onStatus?.(status);
    }
  }

  async sleep(ms) {
    await new Promise((resolve) => {
      const timer = setTimeout(resolve, ms);
      this.wake = () => { clearTimeout(timer); resolve(); };
    });
    this.wake = null;
  }

  async loop() {
    while (this.running) {
      try {
        if (!this.session || !this.session.id) {
          this.session = new Session(this.pairing);
          this.hello = await this.session.open();
          this.after = 0;
        }

        const data = await this.session.live(this.after);
        this.failures = 0;
        if (data) {
          this.after = data.seq;
          this.setStatus('live');
          this.onSnapshot(data, this.hello);
        }
      } catch (e) {
        if (!this.running) return;
        const code = e instanceof ConnectError ? e.code : 'network';
        if (code === 'unpaired') {
          this.running = false;
          this.onUnpaired?.();
          return;
        }

        this.session = null; // a new session next time (expired, replayed, or the computer restarted)
        this.failures++;
        if (code === 'network' || this.failures > 1) this.setStatus(this.failures > 2 ? 'offline' : 'reconnecting');
        await this.sleep(Math.min(1000 * this.failures, 5000));
      }
    }
  }
}

/** A name for this phone from its browser: "iPhone", "iPad", "Pixel 9"… (the person can change it). */
export function suggestDeviceName() {
  const ua = navigator.userAgent || '';
  if (/iPad/.test(ua) || (/Macintosh/.test(ua) && navigator.maxTouchPoints > 1)) return 'iPad';
  if (/iPhone/.test(ua)) return 'iPhone';
  const android = /Android[^;]*;\s*([^;)]+?)(?:\s+Build|\))/.exec(ua);
  if (android && android[1] && !/^(K|wv)$/.test(android[1].trim())) return android[1].trim().slice(0, 40);
  if (/Android/.test(ua)) return 'Android phone';
  if (/Windows/.test(ua)) return 'Windows browser';
  if (/Macintosh/.test(ua)) return 'Mac browser';
  return 'Phone';
}
