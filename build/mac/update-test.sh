#!/usr/bin/env bash
# An update from end to end on a Mac, against the real latest release on GitHub: builds an older Coreloom (version
# 0.9.0 by default), installs it in /Applications, lets it find and download the newest release, quits it, and opens
# it again, when it installs the update at startup. Passes when the copy in /Applications is the new version, the
# update helper logged "updated" and the new version is running. Screenshots and logs go to <out-dir>.
#
# Usage: build/mac/update-test.sh <arm64|x64> <out-dir> [old-version] [repository]
set -uo pipefail

ARCH="${1:?arch}"
mkdir -p "${2:?out-dir}"
OUT="$(cd "$2" && pwd)"
OLD="${3:-0.9.0}"
REPOSITORY="${4:-${GITHUB_REPOSITORY:-jerryubah/Coreloom}}"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
APP="/Applications/Coreloom.app"
LOGS="$HOME/Library/Logs/Coreloom"
SUPPORT="$HOME/Library/Application Support/Coreloom"
failed=0
fail() { echo "FAILED: $*" | tee -a "$OUT/failures.txt"; failed=1; }
log() { echo "$(date '+%H:%M:%S') $*" | tee -a "$OUT/update-test.md"; }

auth=()
[[ -n "${GH_TOKEN:-}" ]] && auth=(-H "Authorization: Bearer $GH_TOKEN")
latest="$(curl -fsSL ${auth[@]+"${auth[@]}"} "https://api.github.com/repos/$REPOSITORY/releases/latest" | python3 -c 'import json,sys; print(json.load(sys.stdin)["tag_name"].lstrip("v"))')"
echo "# Updating Coreloom $OLD to the latest release ($latest) on macOS $(sw_vers -productVersion) ($ARCH)" > "$OUT/update-test.md"
echo >> "$OUT/update-test.md"

# ---- The old version ------------------------------------------------------------------------------------------
bash "$ROOT/build/mac/bundle.sh" --arch "$ARCH" --version "$OLD" --repository "$REPOSITORY" --out "$ROOT/artifacts/mac-old" > "$OUT/bundle.log" 2>&1 \
  || { tail -40 "$OUT/bundle.log"; fail "building $OLD"; exit 1; }
pkill -x Coreloom 2> /dev/null || true
rm -rf "$APP" "$LOGS" "$SUPPORT"
ditto "$ROOT/artifacts/mac-old/Coreloom-$ARCH.app" "$APP"
log "Installed $OLD in /Applications."

# ---- First run: finds and downloads the update --------------------------------------------------------------------
open "$APP"
ready=false
for _ in $(seq 1 120); do
  if grep -qiE "ready to install|downloaded and verified|verified the download|is ready" "$LOGS"/*.log 2> /dev/null; then
    ready=true
    break
  fi
  sleep 5
done
screencapture -x "$OUT/1-downloaded.png" 2> /dev/null || true
if [[ "$ready" != true ]]; then
  fail "the update wasn't downloaded within 10 minutes"
fi
grep -h "\[Updates\]" "$LOGS"/*.log 2> /dev/null | tee -a "$OUT/update-test.md"

# ---- Quit, then open again: it installs at startup ----------------------------------------------------------------
pid="$(pgrep -x Coreloom | head -1)"
[[ -n "$pid" ]] && kill -TERM "$pid"
for _ in $(seq 1 30); do pgrep -x Coreloom > /dev/null || break; sleep 0.5; done
log "Quit $OLD; opening it again."
open "$APP"

installed=""
for _ in $(seq 1 60); do
  installed="$(plutil -extract CFBundleShortVersionString raw "$APP/Contents/Info.plist" 2> /dev/null)"
  if [[ "$installed" == "${latest%%-*}" ]] && pgrep -x Coreloom > /dev/null && grep -q "updated" "$LOGS/update.log" 2> /dev/null; then
    break
  fi
  sleep 3
done
sleep 15
screencapture -x "$OUT/2-updated.png" 2> /dev/null || true

log "Version in /Applications: $installed"
[[ "$installed" == "${latest%%-*}" ]] || fail "/Applications/Coreloom.app is $installed, not $latest"
grep -q "updated" "$LOGS/update.log" 2> /dev/null || fail "the update helper didn't log 'updated'"
pgrep -x Coreloom > /dev/null || fail "the new version isn't running"
codesign --verify --deep --strict "$APP" || fail "the updated app's signature"
grep -q "starting (args: --updated" "$LOGS"/coreloom*.log 2> /dev/null || fail "the new version didn't start with --updated"

{
  echo; echo "## update.log"; echo '```'; cat "$LOGS/update.log" 2> /dev/null; echo '```'
  echo; echo "## Coreloom's log (updates and starts)"; echo '```'
  grep -hE "\[Updates\]|starting \(args" "$LOGS"/coreloom*.log 2> /dev/null
  echo '```'
} >> "$OUT/update-test.md"
mkdir -p "$OUT/logs" && cp -R "$LOGS/." "$OUT/logs/" 2> /dev/null || true

pkill -x Coreloom 2> /dev/null || true
if [[ "$failed" != 0 ]]; then
  cat "$OUT/failures.txt"
  exit 1
fi
log "Coreloom $OLD updated itself to $latest."
