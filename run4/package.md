# Coreloom 1.8.6 for macOS (x64): packages

macOS 15.7.9 (24G830), x86_64

```
total 250632
drwxr-xr-x  5 runner  staff       160 Oct 31 01:17 .
drwxr-xr-x  3 runner  staff        96 Oct 31 01:05 ..
-rw-r--r--@ 1 runner  staff  64275170 Oct 31 01:16 Coreloom-1.8.6-macos-x64.dmg
-rw-r--r--  1 runner  staff  63331548 Oct 31 01:16 Coreloom-1.8.6-macos-x64.zip
drwxr-xr-x  3 runner  staff        96 Oct 31 01:16 Coreloom-x64.app
```

## Code signature (as the updater checks it: codesign --verify --deep --strict)
```
--prepared:/Users/runner/work/Coreloom/Coreloom/artifacts/mac/Coreloom-x64.app/Contents/MacOS/libHarfBuzzSharp.dylib
--validated:/Users/runner/work/Coreloom/Coreloom/artifacts/mac/Coreloom-x64.app/Contents/MacOS/libHarfBuzzSharp.dylib
--prepared:/Users/runner/work/Coreloom/Coreloom/artifacts/mac/Coreloom-x64.app/Contents/MacOS/libAvaloniaNative.dylib
--validated:/Users/runner/work/Coreloom/Coreloom/artifacts/mac/Coreloom-x64.app/Contents/MacOS/libAvaloniaNative.dylib
--prepared:/Users/runner/work/Coreloom/Coreloom/artifacts/mac/Coreloom-x64.app/Contents/MacOS/libSkiaSharp.dylib
--validated:/Users/runner/work/Coreloom/Coreloom/artifacts/mac/Coreloom-x64.app/Contents/MacOS/libSkiaSharp.dylib
/Users/runner/work/Coreloom/Coreloom/artifacts/mac/Coreloom-x64.app: valid on disk
/Users/runner/work/Coreloom/Coreloom/artifacts/mac/Coreloom-x64.app: satisfies its Designated Requirement
Executable=/Users/runner/work/Coreloom/Coreloom/artifacts/mac/Coreloom-x64.app/Contents/MacOS/Coreloom
Identifier=com.jerryubah.coreloom
Format=app bundle with Mach-O thin (x86_64)
CodeDirectory v=20400 size=963343 flags=0x2(adhoc) hashes=30098+3 location=embedded
Hash type=sha256 size=32
CandidateCDHash sha256=59d1dea90764424c3edcb551746e536509fa15a4
CandidateCDHashFull sha256=59d1dea90764424c3edcb551746e536509fa15a40bcb3d3915cfbb6e3838a4a8
Hash choices=sha256
CMSDigest=59d1dea90764424c3edcb551746e536509fa15a40bcb3d3915cfbb6e3838a4a8
CMSDigestType=2
CDHash=59d1dea90764424c3edcb551746e536509fa15a4
Signature=adhoc
Info.plist entries=23
TeamIdentifier=not set
Sealed Resources version=2 rules=13 files=8
Internal requirements count=0 size=12
```

## Gatekeeper's view (an ad hoc signature is expected to be rejected: people confirm it once)
```
/Users/runner/work/Coreloom/Coreloom/artifacts/mac/Coreloom-x64.app: rejected
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
  "CFBundleShortVersionString" => "1.8.6"
  "CFBundleSignature" => "????"
  "CFBundleVersion" => "1.8.6"
  "LSApplicationCategoryType" => "public.app-category.utilities"
  "LSMinimumSystemVersion" => "14.0"
  "LSMultipleInstancesProhibited" => 1
  "NSAppSleepDisabled" => 1
  "NSBluetoothAlwaysUsageDescription" => "Coreloom lists your Bluetooth accessories and their battery levels on the Devices page, and connects or disconnects them when you ask."
  "NSHighResolutionCapable" => 1
  "NSHumanReadableCopyright" => "Copyright © 2026 Jerry Ubah. MIT License."
  "NSLocalNetworkUsageDescription" => "When you turn on Connect, Coreloom lets phones you've paired on this network see this Mac's readings."
  "NSPrincipalClass" => "NSApplication"
  "NSRequiresAquaSystemAppearance" => 0
  "NSSupportsAutomaticGraphicsSwitching" => 1
}
```

## The program
```
Non-fat file: /Users/runner/work/Coreloom/Coreloom/artifacts/mac/Coreloom-x64.app/Contents/MacOS/Coreloom is architecture: x86_64
/Users/runner/work/Coreloom/Coreloom/artifacts/mac/Coreloom-x64.app/Contents/MacOS/Coreloom:                Mach-O 64-bit executable x86_64
/Users/runner/work/Coreloom/Coreloom/artifacts/mac/Coreloom-x64.app/Contents/MacOS/libAvaloniaNative.dylib: Mach-O universal binary with 2 architectures: [x86_64:Mach-O 64-bit dynamically linked shared library x86_64] [arm64:Mach-O 64-bit dynamically linked shared library arm64]
/Users/runner/work/Coreloom/Coreloom/artifacts/mac/Coreloom-x64.app/Contents/MacOS/libAvaloniaNative.dylib (for architecture x86_64):	Mach-O 64-bit dynamically linked shared library x86_64
/Users/runner/work/Coreloom/Coreloom/artifacts/mac/Coreloom-x64.app/Contents/MacOS/libAvaloniaNative.dylib (for architecture arm64):	Mach-O 64-bit dynamically linked shared library arm64
/Users/runner/work/Coreloom/Coreloom/artifacts/mac/Coreloom-x64.app/Contents/MacOS/libHarfBuzzSharp.dylib:  Mach-O universal binary with 2 architectures: [x86_64:Mach-O 64-bit dynamically linked shared library x86_64] [arm64]
/Users/runner/work/Coreloom/Coreloom/artifacts/mac/Coreloom-x64.app/Contents/MacOS/libHarfBuzzSharp.dylib (for architecture x86_64):	Mach-O 64-bit dynamically linked shared library x86_64
/Users/runner/work/Coreloom/Coreloom/artifacts/mac/Coreloom-x64.app/Contents/MacOS/libHarfBuzzSharp.dylib (for architecture arm64):	Mach-O 64-bit dynamically linked shared library arm64
/Users/runner/work/Coreloom/Coreloom/artifacts/mac/Coreloom-x64.app/Contents/MacOS/libSkiaSharp.dylib:      Mach-O universal binary with 2 architectures: [x86_64:Mach-O 64-bit dynamically linked shared library x86_64] [arm64]
/Users/runner/work/Coreloom/Coreloom/artifacts/mac/Coreloom-x64.app/Contents/MacOS/libSkiaSharp.dylib (for architecture x86_64):	Mach-O 64-bit dynamically linked shared library x86_64
/Users/runner/work/Coreloom/Coreloom/artifacts/mac/Coreloom-x64.app/Contents/MacOS/libSkiaSharp.dylib (for architecture arm64):	Mach-O 64-bit dynamically linked shared library arm64

/Users/runner/work/Coreloom/Coreloom/artifacts/mac/Coreloom-x64.app/Contents/MacOS:
total 280768
drwxr-xr-x  6 runner  staff        192 Oct 31 01:16 .
drwxr-xr-x  7 runner  staff        224 Oct 31 01:16 ..
-rwxr-xr-x  1 runner  staff  124260832 Oct 31 01:16 Coreloom
-rwxr--r--  1 runner  staff    1509808 Oct 31 01:16 libAvaloniaNative.dylib
-rwxr--r--  1 runner  staff    2891424 Oct 31 01:16 libHarfBuzzSharp.dylib
-rwxr--r--  1 runner  staff   15084624 Oct 31 01:16 libSkiaSharp.dylib

/Users/runner/work/Coreloom/Coreloom/artifacts/mac/Coreloom-x64.app/Contents/Resources:
total 1640
drwxr-xr-x  7 runner  staff     224 Oct 31 01:16 .
drwxr-xr-x  7 runner  staff     224 Oct 31 01:16 ..
-rw-r--r--  1 runner  staff  809909 Oct 31 01:16 AppIcon.icns
-rw-r--r--  1 runner  staff    4380 Oct 31 01:16 LICENSE-Inter.txt
-rw-r--r--  1 runner  staff    4399 Oct 31 01:16 LICENSE-JetBrainsMono.txt
-rw-r--r--  1 runner  staff    1067 Oct 31 01:16 LICENSE.txt
-rw-r--r--  1 runner  staff    7743 Oct 31 01:16 THIRD-PARTY-NOTICES.md
```
- update zip: unpacks to Coreloom.app and its signature checks out

## Disk image contents
```
total 1976
drwxr-xr-x@   7 runner  staff     306 Oct 31 01:16 .
drwx------@ 168 runner  staff    5376 Oct 31 01:17 ..
-rw-r--r--    1 runner  staff  175210 Oct 31 01:16 .background.tiff
-rw-r--r--    1 runner  staff   16388 Oct 31 01:16 .DS_Store
-rw-r--r--    1 runner  staff  809909 Oct 31 01:16 .VolumeIcon.icns
lrwxr-xr-x    1 runner  staff      13 Oct 31 01:16 Applications -> /Applications
drwxr-xr-x    3 runner  staff     102 Oct 31 01:16 Coreloom.app
```
- disk image: Coreloom.app's signature checks out
- disk image: window layout (.DS_Store) present
