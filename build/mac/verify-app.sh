#!/usr/bin/env bash
# Builds and packages Coreloom.app on a Mac, checks the packages the way macOS and Coreloom's updater will, then
# installs the app in /Applications, opens it, visits its pages and photographs each one, and quits it. Everything it
# saw goes to <out-dir>: package.md (signature, Info.plist, disk image and zip checks), app.md (the run), screenshots,
# the app's log and any crash report.
#
# Usage: build/mac/verify-app.sh <arm64|x64> <out-dir> [version]
# Exit code: 0 when the packages are sound and the app ran and quit cleanly.
set -uo pipefail

ARCH="${1:?arch (arm64 or x64)}"
mkdir -p "${2:?out-dir}"
OUT="$(cd "$2" && pwd)"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
VERSION="${3:-$(sed -nE 's/^## +\[([0-9]+\.[0-9]+\.[0-9]+[-0-9A-Za-z.]*)\].*/\1/p' "$ROOT/CHANGELOG.md" | head -1)}"
VERSION="${VERSION:-0.0.0}"
PACKAGES="$ROOT/artifacts/mac"
APP="/Applications/Coreloom.app"
failed=0
fail() { echo "FAILED: $*" | tee -a "$OUT/failures.txt"; failed=1; }

# ---- Package --------------------------------------------------------------------------------------------------
if ! command -v dmgbuild > /dev/null; then
  python3 -m venv "${RUNNER_TEMP:-/tmp}/dmgbuild-env" > /dev/null 2>&1 \
    && "${RUNNER_TEMP:-/tmp}/dmgbuild-env/bin/pip" install --quiet dmgbuild > /dev/null 2>&1 \
    && export PATH="${RUNNER_TEMP:-/tmp}/dmgbuild-env/bin:$PATH"
fi
bash "$ROOT/build/mac/bundle.sh" --arch "$ARCH" --version "$VERSION" --out "$PACKAGES" > "$OUT/bundle.log" 2>&1 \
  || { tail -60 "$OUT/bundle.log"; fail "bundle.sh"; exit 1; }
tail -25 "$OUT/bundle.log"
BUILT="$PACKAGES/Coreloom-$ARCH.app"
ZIP="$PACKAGES/Coreloom-$VERSION-macos-$ARCH.zip"
DMG="$PACKAGES/Coreloom-$VERSION-macos-$ARCH.dmg"

# ---- Check the packages ---------------------------------------------------------------------------------------
{
  echo "# Coreloom $VERSION for macOS ($ARCH): packages"
  echo; echo "macOS $(sw_vers -productVersion) ($(sw_vers -buildVersion)), $(uname -m)"; echo
  echo '```'; ls -la "$PACKAGES"; echo '```'
  echo; echo "## Code signature (as the updater checks it: codesign --verify --deep --strict)"; echo '```'
  codesign --verify --deep --strict --verbose=4 "$BUILT" 2>&1 || echo "VERIFY FAILED"
  codesign -dvvv "$BUILT" 2>&1
  echo '```'
  echo; echo "## Gatekeeper's view (an ad hoc signature is expected to be rejected: people confirm it once)"; echo '```'
  spctl --assess --type execute -vv "$BUILT" 2>&1 || true
  echo '```'
  echo; echo "## Info.plist"; echo '```'; plutil -p "$BUILT/Contents/Info.plist"; echo '```'
  echo; echo "## The program"; echo '```'
  lipo -info "$BUILT/Contents/MacOS/Coreloom"; file "$BUILT"/Contents/MacOS/*
  echo; ls -la "$BUILT/Contents/MacOS" "$BUILT/Contents/Resources"
  echo '```'
} > "$OUT/package.md" 2>&1
codesign --verify --deep --strict "$BUILT" || fail "codesign --verify on the built app"
[[ "$(plutil -extract CFBundleIdentifier raw "$BUILT/Contents/Info.plist")" == "com.jerryubah.coreloom" ]] || fail "bundle identifier"

# The update zip, unpacked the way Coreloom's updater does it.
unzipped="$(mktemp -d)"
if ditto -x -k "$ZIP" "$unzipped" && [[ -d "$unzipped/Coreloom.app" ]] && codesign --verify --deep --strict "$unzipped/Coreloom.app"; then
  echo "- update zip: unpacks to Coreloom.app and its signature checks out" >> "$OUT/package.md"
else
  fail "the update zip"
fi
rm -rf "$unzipped"

# The disk image: mounts, holds Coreloom.app and a link to Applications.
mount="$(mktemp -d)"
if hdiutil attach "$DMG" -nobrowse -readonly -mountpoint "$mount" > /dev/null; then
  { echo; echo "## Disk image contents"; echo '```'; ls -la "$mount"; echo '```'; } >> "$OUT/package.md"
  [[ -d "$mount/Coreloom.app" ]] || fail "the disk image has no Coreloom.app"
  [[ -L "$mount/Applications" ]] || fail "the disk image has no Applications link"
  if dmg_check="$(codesign --verify --deep --strict --verbose=2 "$mount/Coreloom.app" 2>&1)"; then
    echo "- disk image: Coreloom.app's signature checks out" >> "$OUT/package.md"
  else
    { echo; echo "## The app in the disk image: signature check failed"; echo '```'; echo "$dmg_check"; xattr -lr "$mount/Coreloom.app" | head -20; echo '```'; } >> "$OUT/package.md"
    fail "the app in the disk image"
  fi
  [[ -f "$mount/.DS_Store" ]] && echo "- disk image: window layout (.DS_Store) present" >> "$OUT/package.md"
  [[ -d "$mount/.background" ]] && echo "- disk image: background present" >> "$OUT/package.md"
  hdiutil detach "$mount" > /dev/null || hdiutil detach -force "$mount" > /dev/null
else
  fail "mounting the disk image"
fi

# ---- Install, open and photograph ------------------------------------------------------------------------------
pkill -x Coreloom 2> /dev/null || true
rm -rf "$APP" && ditto "$BUILT" "$APP" || fail "installing in /Applications"
rm -rf "$HOME/Library/Logs/Coreloom"
started="$(date +%s)"
open "$APP" || fail "open"
pid=""
for _ in $(seq 1 60); do
  pid="$(pgrep -x Coreloom | head -1)"
  [[ -n "$pid" ]] && break
  sleep 1
done
{
  echo "# Coreloom $VERSION on macOS $(sw_vers -productVersion) ($ARCH): a run"
  echo
} > "$OUT/app.md"
if [[ -z "$pid" ]]; then
  fail "Coreloom didn't start"
else
  sleep 20 # the first readings, the charts filling, the first update check
  screencapture -x "$OUT/01-overview.png" 2> /dev/null || echo "(screencapture failed)" >> "$OUT/app.md"
  n=2
  for page in gaming benchmarking gpu cpu memory storage devices network optimize history settings; do
    # A second launch hands the page to the running copy and quits.
    "$APP/Contents/MacOS/Coreloom" --page "$page" > /dev/null 2>&1 &
    sleep 5
    screencapture -x "$OUT/$(printf '%02d' "$n")-$page.png" 2> /dev/null || true
    n=$((n + 1))
  done
  if kill -0 "$pid" 2> /dev/null; then
    echo "- Running after $(( $(date +%s) - started )) s and $((n - 1)) pages:" >> "$OUT/app.md"
    echo '```' >> "$OUT/app.md"; ps -o pid,rss,vsz,%cpu,etime,command -p "$pid" >> "$OUT/app.md"; echo '```' >> "$OUT/app.md"
    others="$(pgrep -x Coreloom | grep -vx "$pid" | wc -l | tr -d ' ')"
    echo "- Other Coreloom processes left (the page launches must hand over and quit): $others" >> "$OUT/app.md"
    [[ "$others" == "0" ]] || fail "a second launch kept running"
  else
    fail "Coreloom quit or crashed while visiting the pages"
  fi
  # Quit the way macOS does at log-out (a SIGTERM): Coreloom saves and exits.
  kill -TERM "$pid" 2> /dev/null
  for _ in $(seq 1 20); do kill -0 "$pid" 2> /dev/null || break; sleep 0.5; done
  if kill -0 "$pid" 2> /dev/null; then
    fail "Coreloom didn't quit within 10 s of SIGTERM"
    kill -KILL "$pid" 2> /dev/null
  else
    echo "- Quit cleanly on SIGTERM" >> "$OUT/app.md"
  fi
fi

# ---- Connect: the phone app and readings, served to this Mac's network ----------------------------------------
# Turned on in the settings Coreloom just saved, then Coreloom is opened again: the server answers, serves the phone
# app's files, refuses other host names, and opens pairing while Settings › Connect is showing.
settings="$HOME/Library/Application Support/Coreloom/settings.json"
connect="http://127.0.0.1:47101"
{ echo; echo "## Connect"; } >> "$OUT/app.md"
if [[ -f "$settings" ]] && python3 - "$settings" <<'PY'
import json, sys
path = sys.argv[1]
with open(path) as f:
    settings = json.load(f)
settings.setdefault("connect", {})["enabled"] = True
# A few readings in the menu bar too (1.8.1): they show as items of their own, photographed below.
menu_bar = settings.setdefault("menuBar", {})
modules = menu_bar.get("modules") or []
for module in modules:
    if module.get("id") in ("cpu", "memory", "network"):
        module["shown"] = True
for wanted in ("cpu", "memory", "network"):
    if all(m.get("id") != wanted for m in modules):
        modules.append({"id": wanted, "shown": True, "showName": True, "showGauge": True})
menu_bar["modules"] = modules
with open(path, "w") as f:
    json.dump(settings, f, indent=2)
PY
then
  open "$APP" || fail "open (Connect)"
  pid=""
  for _ in $(seq 1 60); do
    pid="$(pgrep -x Coreloom | head -1)"
    [[ -n "$pid" ]] && break
    sleep 1
  done
  sleep 10
  if info="$(curl -sS --max-time 10 "$connect/api/info")"; then
    echo "- \`GET /api/info\`: \`$info\`" >> "$OUT/app.md"
    [[ "$info" == *'"app":"Coreloom"'* ]] || fail "Connect's /api/info"
    [[ "$info" == *'"code":false'* ]] || fail "pairing was open without Settings › Connect showing"
  else
    fail "Connect didn't answer on port 47101"
  fi
  for path in / /app.js /app.css /connect.js /gauge.js /chart.js /vendor/crypto.js /manifest.webmanifest /icons/apple-touch-icon.png /fonts/Inter-SemiBold.woff2; do
    code="$(curl -sS -o /dev/null -w '%{http_code}' --compressed --max-time 10 "$connect$path")"
    echo "- \`GET $path\`: $code" >> "$OUT/app.md"
    [[ "$code" == 200 ]] || fail "Connect's $path"
  done
  csp="$(curl -sS -D - -o /dev/null --max-time 10 "$connect/" | grep -i '^content-security-policy' || true)"
  [[ -n "$csp" ]] || fail "the phone app's page has no Content-Security-Policy"
  code="$(curl -sS -o /dev/null -w '%{http_code}' --max-time 10 -H 'Host: evil.example.com' "$connect/api/info")"
  echo "- Another host name (\`Host: evil.example.com\`): $code" >> "$OUT/app.md"
  [[ "$code" == 421 ]] || fail "Connect answered another host name"
  lan="$(ipconfig getifaddr en0 2> /dev/null || true)"
  if [[ -n "$lan" ]]; then
    code="$(curl -sS -o /dev/null -w '%{http_code}' --max-time 10 "http://$lan:47101/api/info" 2> /dev/null || true)"
    echo "- From this Mac's network address $lan: ${code:-no answer} (answered only when the address is a private one)" >> "$OUT/app.md"
  fi

  # The menu bar with Coreloom's readings in it (its top strip, cropped from the whole screen).
  screencapture -x "$OUT/91-menubar.png" 2> /dev/null || true
  if grep -q "Readings in the menu bar: cpu, memory, network" "$HOME/Library/Logs/Coreloom/coreloom.log" 2> /dev/null \
     || grep -rq "Readings in the menu bar: cpu, memory, network" "$HOME/Library/Logs/Coreloom" 2> /dev/null; then
    echo "- Menu bar readings: cpu, memory, network shown (log)" >> "$OUT/app.md"
  else
    echo "- Menu bar readings: not in the log" >> "$OUT/app.md"
  fi

  # Settings › Connect: pairing opens while it shows (the QR code and the code are in the picture).
  "$APP/Contents/MacOS/Coreloom" --page connect > /dev/null 2>&1 &
  sleep 5
  screencapture -x "$OUT/$(printf '%02d' "${n:-90}")-connect.png" 2> /dev/null || true
  pairing="$(curl -sS --max-time 10 "$connect/api/info" || true)"
  echo "- With Settings › Connect showing: \`$pairing\`" >> "$OUT/app.md"
  [[ "$pairing" == *'"code":true'* ]] || fail "pairing didn't open with Settings › Connect showing"

  kill -TERM "$pid" 2> /dev/null
  for _ in $(seq 1 20); do kill -0 "$pid" 2> /dev/null || break; sleep 0.5; done
  if kill -0 "$pid" 2> /dev/null; then
    fail "Coreloom didn't quit within 10 s of SIGTERM (Connect on)"
    kill -KILL "$pid" 2> /dev/null
  else
    echo "- Quit cleanly on SIGTERM with Connect on" >> "$OUT/app.md"
  fi
  code="$(curl -sS -o /dev/null -w '%{http_code}' --max-time 5 "$connect/api/info" 2> /dev/null || true)"
  if [[ "$code" == 200 ]]; then
    fail "Connect still answered after Coreloom quit"
  fi
else
  echo "- (no settings file to turn Connect on in)" >> "$OUT/app.md"
  fail "Connect: no settings file"
fi

# ---- Backgrounds (1.8.2): a picture behind the frosted glass, then the desktop through the window --------------------
{ echo; echo "## Backgrounds"; } >> "$OUT/app.md"
for backdrop in "Image:aurora" "SeeThrough:"; do
  kind="${backdrop%%:*}"; image="${backdrop#*:}"
  [[ -f "$settings" ]] && python3 - "$settings" "$kind" "$image" <<'PY'
import json, sys
path, kind, image = sys.argv[1:4]
with open(path) as f:
    settings = json.load(f)
backdrop = settings.setdefault("appearance", {}).setdefault("backdrop", {})
backdrop["kind"] = kind
if image:
    backdrop["image"] = image
with open(path, "w") as f:
    json.dump(settings, f, indent=2)
PY
  open "$APP" || fail "open (background $kind)"
  pid=""
  for _ in $(seq 1 60); do
    pid="$(pgrep -x Coreloom | head -1)"
    [[ -n "$pid" ]] && break
    sleep 1
  done
  sleep 12
  shot="$( [[ "$kind" == Image ]] && echo 92-background-aurora || echo 93-background-translucent )"
  screencapture -x "$OUT/$shot.png" 2> /dev/null || true
  line="$(grep -h "Background:" "$HOME/Library/Logs/Coreloom/"*.log 2> /dev/null | tail -1 || true)"
  echo "- $kind${image:+ ($image)}: ${line:-no log line}" >> "$OUT/app.md"
  if [[ "$kind" == Image && "$line" != *"Image aurora"* ]]; then
    fail "the Aurora background wasn't drawn"
  fi
  kill -TERM "$pid" 2> /dev/null
  for _ in $(seq 1 20); do kill -0 "$pid" 2> /dev/null || break; sleep 0.5; done
  if kill -0 "$pid" 2> /dev/null; then
    fail "Coreloom didn't quit within 10 s of SIGTERM (background $kind)"
    kill -KILL "$pid" 2> /dev/null
  fi
done

# ---- The readings window (the Mac's in-game overlay, 1.8.3): turned on, shown at its place, photographed ------------
{ echo; echo "## Readings window"; } >> "$OUT/app.md"
[[ -f "$settings" ]] && python3 - "$settings" <<'PY'
import json, sys
path = sys.argv[1]
with open(path) as f:
    settings = json.load(f)
overlay = settings.setdefault("overlay", {})
overlay["enabled"] = True
overlay["hasBeenEnabled"] = True
settings.setdefault("appearance", {}).setdefault("backdrop", {})["kind"] = "Gradient"
with open(path, "w") as f:
    json.dump(settings, f, indent=2)
PY
open "$APP" || fail "open (readings window)"
pid=""
for _ in $(seq 1 60); do
  pid="$(pgrep -x Coreloom | head -1)"
  [[ -n "$pid" ]] && break
  sleep 1
done
sleep 10
screencapture -x -R0,0,420,240 "$OUT/95-readings-window.png" 2> /dev/null || true
if grep -q "Readings window shown" "$HOME/Library/Logs/Coreloom/"*.log 2> /dev/null; then
  echo "- Shown (log); picture: 95-readings-window.png" >> "$OUT/app.md"
else
  fail "the readings window wasn't shown"
fi
kill -TERM "$pid" 2> /dev/null
for _ in $(seq 1 20); do kill -0 "$pid" 2> /dev/null || break; sleep 0.5; done
if kill -0 "$pid" 2> /dev/null; then
  fail "Coreloom didn't quit within 10 s of SIGTERM (readings window)"
  kill -KILL "$pid" 2> /dev/null
fi

# ---- What it wrote ---------------------------------------------------------------------------------------------
mkdir -p "$OUT/logs"
cp -R "$HOME/Library/Logs/Coreloom/." "$OUT/logs/" 2> /dev/null || true
minutes=$(( ($(date +%s) - started) / 60 + 2 ))
find "$HOME/Library/Logs/DiagnosticReports" -name 'Coreloom*' -mmin "-$minutes" -exec cp {} "$OUT/logs/" \; 2> /dev/null || true
if ls "$OUT"/logs/Coreloom*.ips > /dev/null 2>&1 || ls "$OUT"/logs/Coreloom*.crash > /dev/null 2>&1; then
  fail "a crash report was written"
fi
{
  echo; echo "## Log (errors and warnings)"; echo '```'
  grep -hE ' (ERR|WRN|Error|Warning) ' "$OUT"/logs/*.log 2> /dev/null | head -80 || true
  echo '```'
  echo; echo "## Log (first 60 lines)"; echo '```'
  cat "$OUT"/logs/*.log 2> /dev/null | head -60 || true
  echo '```'
} >> "$OUT/app.md"
rm -rf "$APP"

if [[ "$failed" != 0 ]]; then
  echo "Verification failed:"; cat "$OUT/failures.txt"
  exit 1
fi
echo "Coreloom $VERSION ($ARCH): packages and app verified."
