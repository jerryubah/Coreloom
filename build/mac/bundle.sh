#!/usr/bin/env bash
# Builds Coreloom.app for one Mac architecture and packages it for a release:
#
#   Coreloom-<version>-macos-<arch>.zip   the app, for in-app updates (Coreloom checks it against SHA256SUMS.txt)
#   Coreloom-<version>-macos-<arch>.dmg   the disk image people download: drag Coreloom into Applications
#
# Usage: build/mac/bundle.sh --arch arm64|x64 --version 1.6.0 [--repository owner/name] [--out artifacts/mac]
#
# The app is self-contained (no .NET install needed) and published as a single file, so Contents/MacOS holds only
# code (the program and Avalonia's, Skia's and HarfBuzz's libraries) and everything else sits in Contents/Resources:
# the layout macOS's code signing expects.
#
# Signing: with a Developer ID (MAC_SIGN_IDENTITY, the certificate already in a keychain) the app gets the hardened
# runtime and, with MAC_NOTARY_APPLE_ID, MAC_NOTARY_TEAM_ID and MAC_NOTARY_PASSWORD, Apple's notarization stapled to
# the app and the disk image. Without them it is signed ad hoc: macOS then asks people to confirm the first opening
# (System Settings › Privacy & Security › Open Anyway), once. Updates installed from inside Coreloom don't ask again.
#
# Runs on macOS (codesign, ditto, hdiutil). dmgbuild (pip) lays out the disk image's window; without it the image is
# plainer but works the same.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
ARCH=""
VERSION=""
REPOSITORY="${GITHUB_REPOSITORY:-}"
OUT="$ROOT/artifacts/mac"
MIN_MACOS="14.0"   # what .NET 10 supports: macOS 14 Sonoma, 15 Sequoia and 26 Tahoe

while [[ $# -gt 0 ]]; do
  case "$1" in
    --arch) ARCH="$2"; shift 2 ;;
    --version) VERSION="$2"; shift 2 ;;
    --repository) REPOSITORY="$2"; shift 2 ;;
    --out) OUT="$2"; shift 2 ;;
    *) echo "Unknown option: $1" >&2; exit 2 ;;
  esac
done

[[ "$ARCH" == "arm64" || "$ARCH" == "x64" ]] || { echo "--arch must be arm64 or x64" >&2; exit 2; }
[[ "$VERSION" =~ ^([0-9]+\.[0-9]+\.[0-9]+)(-[0-9A-Za-z.-]+)?$ ]] || { echo "--version must look like 1.6.0" >&2; exit 2; }
SHORT_VERSION="${BASH_REMATCH[1]}"   # Info.plist versions are numbers only ("1.6.0", not "1.6.0-beta.1")
[[ "$(uname)" == "Darwin" ]] || { echo "bundle.sh runs on macOS" >&2; exit 2; }

export DOTNET_NOLOGO=1 DOTNET_CLI_TELEMETRY_OPTOUT=1 AVALONIA_TELEMETRY_OPTOUT=1
NAME="Coreloom-$VERSION-macos-$ARCH"
PUBLISH="$OUT/publish-$ARCH"
STAGE="$OUT/stage-$ARCH"
APP="$STAGE/Coreloom.app"
ENTITLEMENTS="$ROOT/src/Coreloom.Mac/Coreloom.entitlements"

step() { printf '\n==> %s\n' "$*"; }

rm -rf "$PUBLISH" "$STAGE"
mkdir -p "$OUT" "$PUBLISH" "$STAGE"

# ---- Publish --------------------------------------------------------------------------------------------------
step "Publish for osx-$ARCH ($VERSION)"
publish_args=(
  publish "$ROOT/src/Coreloom.Mac/Coreloom.Mac.csproj" -c Release -r "osx-$ARCH" --self-contained true -nologo
  -o "$PUBLISH" -p:Version="$VERSION" -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=false
  -p:PublishReadyToRun=true -p:DebugType=none -p:GenerateDocumentationFile=false -p:UseAppHost=true
)
[[ -n "$REPOSITORY" ]] && publish_args+=(-p:CoreloomUpdateRepository="$REPOSITORY")
dotnet "${publish_args[@]}"
[[ -x "$PUBLISH/Coreloom" ]] || { echo "Publish output is missing the Coreloom program" >&2; exit 1; }

# ---- The bundle -----------------------------------------------------------------------------------------------
step "Coreloom.app"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
for file in "$PUBLISH"/*; do
  [[ -f "$file" ]] || continue
  if file -b "$file" | grep -q 'Mach-O'; then
    cp "$file" "$APP/Contents/MacOS/"
  else
    cp "$file" "$APP/Contents/Resources/"
  fi
done
chmod +x "$APP/Contents/MacOS/Coreloom"
sed -e "s/__VERSION__/$SHORT_VERSION/g" -e "s/__BUILD__/$SHORT_VERSION/g" -e "s/__MIN_MACOS__/$MIN_MACOS/g" \
  "$ROOT/src/Coreloom.Mac/Info.plist" > "$APP/Contents/Info.plist"
plutil -lint "$APP/Contents/Info.plist"
printf 'APPL????' > "$APP/Contents/PkgInfo"
cp "$ROOT/src/Coreloom.Mac/Assets/AppIcon.icns" "$APP/Contents/Resources/AppIcon.icns"
echo "Contents/MacOS:";     ls -la "$APP/Contents/MacOS"
echo "Contents/Resources:"; ls -la "$APP/Contents/Resources"
lipo -archs "$APP/Contents/MacOS/Coreloom"

# ---- Signing --------------------------------------------------------------------------------------------------
IDENTITY="${MAC_SIGN_IDENTITY:-}"
sign() {
  if [[ -n "$IDENTITY" ]]; then
    codesign --force --timestamp --options runtime --entitlements "$ENTITLEMENTS" --sign "$IDENTITY" "$@"
  else
    codesign --force --sign - "$@"
  fi
}

if [[ -n "$IDENTITY" ]]; then step "Sign with $IDENTITY (hardened runtime)"; else step "Sign ad hoc (no Developer ID configured)"; fi
# Inside out: the libraries, then the program, then the bundle (which seals Resources and Info.plist).
for lib in "$APP"/Contents/MacOS/*.dylib; do
  [[ -f "$lib" ]] && sign "$lib"
done
sign "$APP/Contents/MacOS/Coreloom"
sign "$APP"
codesign --verify --deep --strict --verbose=2 "$APP"
codesign -dv "$APP" 2>&1 | sed 's/^/    /'

# ---- Notarization (Developer ID only) -------------------------------------------------------------------------
notarize() {
  xcrun notarytool submit "$1" --apple-id "$MAC_NOTARY_APPLE_ID" --team-id "$MAC_NOTARY_TEAM_ID" \
    --password "$MAC_NOTARY_PASSWORD" --wait --timeout 30m
}
NOTARIZE=false
if [[ -n "$IDENTITY" && -n "${MAC_NOTARY_APPLE_ID:-}" && -n "${MAC_NOTARY_TEAM_ID:-}" && -n "${MAC_NOTARY_PASSWORD:-}" ]]; then
  NOTARIZE=true
  step "Notarize the app"
  ditto -c -k --keepParent "$APP" "$OUT/notarize-$ARCH.zip"
  notarize "$OUT/notarize-$ARCH.zip"
  rm -f "$OUT/notarize-$ARCH.zip"
  xcrun stapler staple "$APP"
fi

# ---- The update package (zip) ---------------------------------------------------------------------------------
step "$NAME.zip"
rm -f "$OUT/$NAME.zip"
# ditto keeps the bundle exactly (permissions, signatures); Coreloom's updater unpacks it with ditto too.
ditto -c -k --sequesterRsrc --keepParent "$APP" "$OUT/$NAME.zip"

# ---- The disk image -------------------------------------------------------------------------------------------
step "$NAME.dmg"
rm -f "$OUT/$NAME.dmg"
background="$ROOT/build/mac/dmg-background.png"
if command -v tiffutil > /dev/null && [[ -f "$ROOT/build/mac/dmg-background@2x.png" ]]; then
  # One picture with both sizes, so the window is sharp on Retina displays too.
  tiffutil -cathidpicheck "$ROOT/build/mac/dmg-background.png" "$ROOT/build/mac/dmg-background@2x.png" -out "$STAGE/background.tiff" \
    && background="$STAGE/background.tiff"
fi
made_dmg=false
if command -v dmgbuild > /dev/null; then
  if dmgbuild -s "$ROOT/build/mac/dmg-settings.py" -D app="$APP" -D background="$background" \
       -D icon="$ROOT/src/Coreloom.Mac/Assets/AppIcon.icns" "Coreloom" "$OUT/$NAME.dmg"; then
    made_dmg=true
  else
    echo "dmgbuild failed; making a plain disk image instead." >&2
    rm -f "$OUT/$NAME.dmg"
  fi
fi
if [[ "$made_dmg" != true ]]; then
  dmgroot="$STAGE/dmg"
  rm -rf "$dmgroot"; mkdir -p "$dmgroot"
  ditto "$APP" "$dmgroot/Coreloom.app"
  ln -s /Applications "$dmgroot/Applications"
  hdiutil create -volname "Coreloom" -srcfolder "$dmgroot" -fs HFS+ -format UDZO -imagekey zlib-level=9 -ov "$OUT/$NAME.dmg"
fi
if [[ -n "$IDENTITY" ]]; then
  codesign --force --timestamp --sign "$IDENTITY" "$OUT/$NAME.dmg"
fi
if [[ "$NOTARIZE" == true ]]; then
  step "Notarize the disk image"
  notarize "$OUT/$NAME.dmg"
  xcrun stapler staple "$OUT/$NAME.dmg"
fi
hdiutil verify "$OUT/$NAME.dmg"

# Keep the signed app next to the packages (verify-app.sh launches it).
rm -rf "$OUT/Coreloom-$ARCH.app"
ditto "$APP" "$OUT/Coreloom-$ARCH.app"
rm -rf "$PUBLISH" "$STAGE"

step "Done"
ls -la "$OUT"
shasum -a 256 "$OUT/$NAME.zip" "$OUT/$NAME.dmg"
