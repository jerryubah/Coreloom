# Coreloom 1.6.0 for macOS (arm64): packages

macOS 15.7.9 (24G830), arm64

```
total 220984
drwxr-xr-x  5 runner  staff       160 Oct 25 12:56 .
drwxr-xr-x  3 runner  staff        96 Oct 25 12:54 ..
-rw-r--r--@ 1 runner  staff  56977545 Oct 25 12:56 Coreloom-1.6.0-macos-arm64.dmg
-rw-r--r--  1 runner  staff  56079584 Oct 25 12:56 Coreloom-1.6.0-macos-arm64.zip
drwxr-xr-x  3 runner  staff        96 Oct 25 12:55 Coreloom-arm64.app
```

## Code signature (as the updater checks it: codesign --verify --deep --strict)
```
--prepared:/Users/runner/work/Coreloom/Coreloom/artifacts/mac/Coreloom-arm64.app/Contents/MacOS/libHarfBuzzSharp.dylib
--prepared:/Users/runner/work/Coreloom/Coreloom/artifacts/mac/Coreloom-arm64.app/Contents/MacOS/libAvaloniaNative.dylib
--validated:/Users/runner/work/Coreloom/Coreloom/artifacts/mac/Coreloom-arm64.app/Contents/MacOS/libAvaloniaNative.dylib
--validated:/Users/runner/work/Coreloom/Coreloom/artifacts/mac/Coreloom-arm64.app/Contents/MacOS/libHarfBuzzSharp.dylib
--prepared:/Users/runner/work/Coreloom/Coreloom/artifacts/mac/Coreloom-arm64.app/Contents/MacOS/libSkiaSharp.dylib
--validated:/Users/runner/work/Coreloom/Coreloom/artifacts/mac/Coreloom-arm64.app/Contents/MacOS/libSkiaSharp.dylib
/Users/runner/work/Coreloom/Coreloom/artifacts/mac/Coreloom-arm64.app: valid on disk
/Users/runner/work/Coreloom/Coreloom/artifacts/mac/Coreloom-arm64.app: satisfies its Designated Requirement
Executable=/Users/runner/work/Coreloom/Coreloom/artifacts/mac/Coreloom-arm64.app/Contents/MacOS/Coreloom
Identifier=com.jerryubah.coreloom
Format=app bundle with Mach-O thin (arm64)
CodeDirectory v=20400 size=1047343 flags=0x2(adhoc) hashes=32723+3 location=embedded
Hash type=sha256 size=32
CandidateCDHash sha256=10643e96d1a63e95a69baa42d5b36a7b09e0792b
CandidateCDHashFull sha256=10643e96d1a63e95a69baa42d5b36a7b09e0792b736be109391dd24379c66ffd
Hash choices=sha256
CMSDigest=10643e96d1a63e95a69baa42d5b36a7b09e0792b736be109391dd24379c66ffd
CMSDigestType=2
CDHash=10643e96d1a63e95a69baa42d5b36a7b09e0792b
Signature=adhoc
Info.plist entries=22
TeamIdentifier=not set
Sealed Resources version=2 rules=13 files=8
Internal requirements count=0 size=12
```

## Gatekeeper's view (an ad hoc signature is expected to be rejected: people confirm it once)
```
/Users/runner/work/Coreloom/Coreloom/artifacts/mac/Coreloom-arm64.app: rejected
```

## Info.plist
```
{
  "CFBundleDevelopmentRegion" => "en"
  "CFBundleDisplayName" => "Coreloom"
  "CFBundleExecutable" => "Coreloom"
  "CFBundleIconFile" => "AppIcon"
  "CFBundleIconName" => "AppIcon"
  "CFBundleIdentifier" => "com.jerryubah.coreloom"
  "CFBundleInfoDictionaryVersion" => "6.0"
  "CFBundleName" => "Coreloom"
  "CFBundlePackageType" => "APPL"
  "CFBundleShortVersionString" => "1.6.0"
  "CFBundleSignature" => "????"
  "CFBundleVersion" => "1.6.0"
  "LSApplicationCategoryType" => "public.app-category.utilities"
  "LSMinimumSystemVersion" => "14.0"
  "LSMultipleInstancesProhibited" => 1
  "NSAppSleepDisabled" => 1
  "NSBluetoothAlwaysUsageDescription" => "Coreloom lists your Bluetooth accessories and their battery levels on the Devices page, and connects or disconnects them when you ask."
  "NSHighResolutionCapable" => 1
  "NSHumanReadableCopyright" => "Copyright © 2026 Jerry Ubah. MIT License."
  "NSPrincipalClass" => "NSApplication"
  "NSRequiresAquaSystemAppearance" => 0
  "NSSupportsAutomaticGraphicsSwitching" => 1
}
```

## The program
```
Non-fat file: /Users/runner/work/Coreloom/Coreloom/artifacts/mac/Coreloom-arm64.app/Contents/MacOS/Coreloom is architecture: arm64
/Users/runner/work/Coreloom/Coreloom/artifacts/mac/Coreloom-arm64.app/Contents/MacOS/Coreloom:                Mach-O 64-bit executable arm64
/Users/runner/work/Coreloom/Coreloom/artifacts/mac/Coreloom-arm64.app/Contents/MacOS/libAvaloniaNative.dylib: Mach-O universal binary with 2 architectures: [x86_64:Mach-O 64-bit dynamically linked shared library x86_64] [arm64:Mach-O 64-bit dynamically linked shared library arm64]
/Users/runner/work/Coreloom/Coreloom/artifacts/mac/Coreloom-arm64.app/Contents/MacOS/libAvaloniaNative.dylib (for architecture x86_64):	Mach-O 64-bit dynamically linked shared library x86_64
/Users/runner/work/Coreloom/Coreloom/artifacts/mac/Coreloom-arm64.app/Contents/MacOS/libAvaloniaNative.dylib (for architecture arm64):	Mach-O 64-bit dynamically linked shared library arm64
/Users/runner/work/Coreloom/Coreloom/artifacts/mac/Coreloom-arm64.app/Contents/MacOS/libHarfBuzzSharp.dylib:  Mach-O universal binary with 2 architectures: [x86_64:Mach-O 64-bit dynamically linked shared library x86_64] [arm64]
/Users/runner/work/Coreloom/Coreloom/artifacts/mac/Coreloom-arm64.app/Contents/MacOS/libHarfBuzzSharp.dylib (for architecture x86_64):	Mach-O 64-bit dynamically linked shared library x86_64
/Users/runner/work/Coreloom/Coreloom/artifacts/mac/Coreloom-arm64.app/Contents/MacOS/libHarfBuzzSharp.dylib (for architecture arm64):	Mach-O 64-bit dynamically linked shared library arm64
/Users/runner/work/Coreloom/Coreloom/artifacts/mac/Coreloom-arm64.app/Contents/MacOS/libSkiaSharp.dylib:      Mach-O universal binary with 2 architectures: [x86_64:Mach-O 64-bit dynamically linked shared library x86_64] [arm64]
/Users/runner/work/Coreloom/Coreloom/artifacts/mac/Coreloom-arm64.app/Contents/MacOS/libSkiaSharp.dylib (for architecture x86_64):	Mach-O 64-bit dynamically linked shared library x86_64
/Users/runner/work/Coreloom/Coreloom/artifacts/mac/Coreloom-arm64.app/Contents/MacOS/libSkiaSharp.dylib (for architecture arm64):	Mach-O 64-bit dynamically linked shared library arm64

/Users/runner/work/Coreloom/Coreloom/artifacts/mac/Coreloom-arm64.app/Contents/MacOS:
total 301928
drwxr-xr-x  6 runner  staff        192 Oct 25 12:55 .
drwxr-xr-x  7 runner  staff        224 Oct 25 12:55 ..
-rwxr-xr-x  1 runner  staff  135095184 Oct 25 12:55 Coreloom
-rwxr--r--  1 runner  staff    1509808 Oct 25 12:55 libAvaloniaNative.dylib
-rwxr--r--  1 runner  staff    2891424 Oct 25 12:55 libHarfBuzzSharp.dylib
-rwxr--r--  1 runner  staff   15084624 Oct 25 12:55 libSkiaSharp.dylib

/Users/runner/work/Coreloom/Coreloom/artifacts/mac/Coreloom-arm64.app/Contents/Resources:
total 1640
drwxr-xr-x  7 runner  staff     224 Oct 25 12:55 .
drwxr-xr-x  7 runner  staff     224 Oct 25 12:55 ..
-rw-r--r--  1 runner  staff  809909 Oct 25 12:55 AppIcon.icns
-rw-r--r--  1 runner  staff    4380 Oct 25 12:55 LICENSE-Inter.txt
-rw-r--r--  1 runner  staff    4399 Oct 25 12:55 LICENSE-JetBrainsMono.txt
-rw-r--r--  1 runner  staff    1067 Oct 25 12:55 LICENSE.txt
-rw-r--r--  1 runner  staff    5560 Oct 25 12:55 THIRD-PARTY-NOTICES.md
```
- update zip: unpacks to Coreloom.app and its signature checks out

## Disk image contents
```
total 1976
drwxr-xr-x@   7 runner  staff     306 Oct 25 12:56 .
drwx------@ 115 runner  staff    3680 Oct 25 12:56 ..
-rw-r--r--    1 runner  staff  175210 Oct 25 12:56 .background.tiff
-rw-r--r--    1 runner  staff   16388 Oct 25 12:56 .DS_Store
-rw-r--r--    1 runner  staff  809909 Oct 25 12:56 .VolumeIcon.icns
lrwxr-xr-x    1 runner  staff      13 Oct 25 12:56 Applications -> /Applications
drwxr-xr-x    3 runner  staff     102 Oct 25 12:55 Coreloom.app
```
- disk image: Coreloom.app's signature checks out
- disk image: window layout (.DS_Store) present
