# Coreloom on your phone

The phone app for **Coreloom Connect**: your PC's or Mac's readings, live in a phone's browser, over your own network.
There is nothing to install from a store. Coreloom serves these files itself (they're embedded in `Coreloom.Core`),
and a phone opens them by scanning the QR code in **Settings › Connect** or by typing the address and the six-digit
pairing code shown there. "Add to Home Screen" keeps an icon that opens it full screen afterwards.

## Files

| File | What it is |
| --- | --- |
| `index.html` | The page: boot screen, metadata for iOS and Android, the fonts and the app. |
| `app.css` | Coreloom's design tokens (the desktop app's dark and light palettes), components and layout. |
| `app.js` | The app: pairing, the five tabs (Overview, Graphics, Processor, Memory, More), the sheet, the Home Screen link. |
| `connect.js` | The protocol: pairing, sessions, signed requests, sealed readings, reconnecting. |
| `gauge.js` | The desktop's tick-ring gauge on a canvas, colour scales and the memory donut. |
| `chart.js` | Two-minute live charts and sparklines. |
| `vendor/crypto.js` | P-256, HKDF, HMAC-SHA-256 and AES-GCM from the audited noble libraries (MIT), bundled by `build/phone/crypto`. |
| `icons/`, `fonts/` | Home Screen icons from the brand tile and Inter (SIL OFL), made by `build/phone/make-assets.py`. |
| `manifest.webmanifest` | Name, colours and icons for Android's "Add to Home screen". |

No framework, no build step for the app itself, no third-party requests: the page's Content-Security-Policy allows
only Coreloom's own address.

## Protocol

- **Pairing.** The phone and the computer each make a one-time P-256 key. The shared secret, the pairing secret
  (the 32-byte secret in the QR link's `#p=` fragment, which a browser never sends, or the six-digit code) and both
  public keys give, through HKDF-SHA-256, an encryption key and a MAC key. The phone proves it knows the secret
  with an HMAC; the computer answers with the device's long-term key, sealed with AES-256-GCM. A wrong code costs
  one of five attempts; ten failures in a minute pause pairing for a minute. Pairing is open only while
  Settings › Connect is showing (a paired phone can also ask for a 15-minute link for its Home Screen copy), and
  every code and link is used once.
- **Sessions.** Each visit starts a session with a nonce from each side (HKDF again). Every request carries an
  HMAC over its path and body and a counter that only goes up, so a copied request can't be replayed. Readings come
  back sealed with the session key; `/api/live` waits up to 20 s for the next one.
- **Limits.** Coreloom only answers addresses on private networks (10/8, 172.16/12, 192.168/16, 169.254/16,
  100.64/10, IPv6 link-local and unique-local, loopback) and only Host names that are an address, `.local`,
  `localhost` or the computer's own name. Phones only read: nothing on the computer can be changed through Connect.
- **What it doesn't cover.** The page itself comes from your computer over plain HTTP (a home network has no
  certificate to offer), so the encryption keeps readings from anyone listening on the network, but not from
  someone who can actively tamper with your network's traffic. Since phones can only read, the most such a person
  could get is the readings.

The C# side is `src/Coreloom.Core/Connect`; `tests/Coreloom.Core.Tests/ConnectTests.cs` pins both sides to the
same key-derivation values.

## Trying it without a phone

`tools/Coreloom.ConnectDemo` runs the server with sample readings (`--mac` for a Mac). Open the address it prints
in a desktop browser with a phone-sized window, and pair with the code it prints.
