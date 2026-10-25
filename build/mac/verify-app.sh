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
  codesign --verify --deep --strict "$mount/Coreloom.app" || fail "the app in the disk image"
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

# ---- What it wrote ---------------------------------------------------------------------------------------------
mkdir -p "$OUT/logs"
cp -R "$HOME/Library/Logs/Coreloom/." "$OUT/logs/" 2> /dev/null || true
find "$HOME/Library/Logs/DiagnosticReports" -name 'Coreloom*' -newermt "@$started" -exec cp {} "$OUT/logs/" \; 2> /dev/null || true
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
