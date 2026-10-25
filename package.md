# Coreloom 1.6.0 for macOS (arm64): packages

macOS 15.7.9 (24G830), arm64

```
total 221400
drwxr-xr-x  5 runner  staff       160 Oct 25 13:08 .
drwxr-xr-x  3 runner  staff        96 Oct 25 13:06 ..
-rw-r--r--@ 1 runner  staff  56980959 Oct 25 13:08 Coreloom-1.6.0-macos-arm64.dmg
-rw-r--r--  1 runner  staff  56086039 Oct 25 13:08 Coreloom-1.6.0-macos-arm64.zip
drwxr-xr-x  3 runner  staff        96 Oct 25 13:08 Coreloom-arm64.app
```

## Code signature (as the updater checks it: codesign --verify --deep --strict)
```
--prepared:/Users/runner/work/Coreloom/Coreloom/artifacts/mac/Coreloom-arm64.app/Contents/MacOS/libAvaloniaNative.dylib
--validated:/Users/runner/work/Coreloom/Coreloom/artifacts/mac/Coreloom-arm64.app/Contents/MacOS/libAvaloniaNative.dylib
--prepared:/Users/runner/work/Coreloom/Coreloom/artifacts/mac/Coreloom-arm64.app/Contents/MacOS/libHarfBuzzSharp.dylib
--validated:/Users/runner/work/Coreloom/Coreloom/artifacts/mac/Coreloom-arm64.app/Contents/MacOS/libHarfBuzzSharp.dylib
--prepared:/Users/runner/work/Coreloom/Coreloom/artifacts/mac/Coreloom-arm64.app/Contents/MacOS/libSkiaSharp.dylib
--validated:/Users/runner/work/Coreloom/Coreloom/artifacts/mac/Coreloom-arm64.app/Contents/MacOS/libSkiaSharp.dylib
/Users/runner/work/Coreloom/Coreloom/artifacts/mac/Coreloom-arm64.app: valid on disk
/Users/runner/work/Coreloom/Coreloom/artifacts/mac/Coreloom-arm64.app: satisfies its Designated Requirement
Executable=/Users/runner/work/Coreloom/Coreloom/artifacts/mac/Coreloom-arm64.app/Contents/MacOS/Coreloom
Identifier=com.jerryubah.coreloom
Format=app bundle with Mach-O thin (arm64)
CodeDirectory v=20400 size=1047375 flags=0x2(adhoc) hashes=32724+3 location=embedded
Hash type=sha256 size=32
CandidateCDHash sha256=fc4d200a6895a977813e0b10c329781348f3f240
CandidateCDHashFull sha256=fc4d200a6895a977813e0b10c329781348f3f240541c8df9d1cb5345fab2a698
Hash choices=sha256
CMSDigest=fc4d200a6895a977813e0b10c329781348f3f240541c8df9d1cb5345fab2a698
CMSDigestType=2
CDHash=fc4d200a6895a977813e0b10c329781348f3f240
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
total 301936
drwxr-xr-x  6 runner  staff        192 Oct 25 13:08 .
drwxr-xr-x  7 runner  staff        224 Oct 25 13:08 ..
-rwxr-xr-x  1 runner  staff  135102384 Oct 25 13:08 Coreloom
-rwxr--r--  1 runner  staff    1509808 Oct 25 13:08 libAvaloniaNative.dylib
-rwxr--r--  1 runner  staff    2891424 Oct 25 13:08 libHarfBuzzSharp.dylib
-rwxr--r--  1 runner  staff   15084624 Oct 25 13:08 libSkiaSharp.dylib

/Users/runner/work/Coreloom/Coreloom/artifacts/mac/Coreloom-arm64.app/Contents/Resources:
total 1640
drwxr-xr-x  7 runner  staff     224 Oct 25 13:08 .
drwxr-xr-x  7 runner  staff     224 Oct 25 13:08 ..
-rw-r--r--  1 runner  staff  809909 Oct 25 13:08 AppIcon.icns
-rw-r--r--  1 runner  staff    4380 Oct 25 13:08 LICENSE-Inter.txt
-rw-r--r--  1 runner  staff    4399 Oct 25 13:08 LICENSE-JetBrainsMono.txt
-rw-r--r--  1 runner  staff    1067 Oct 25 13:08 LICENSE.txt
-rw-r--r--  1 runner  staff    5560 Oct 25 13:08 THIRD-PARTY-NOTICES.md
```
- update zip: unpacks to Coreloom.app and its signature checks out

## Disk image contents
```
total 1976
drwxr-xr-x@   7 runner  staff     306 Oct 25 13:08 .
drwx------@ 115 runner  staff    3680 Oct 25 13:08 ..
-rw-r--r--    1 runner  staff  175210 Oct 25 13:08 .background.tiff
-rw-r--r--    1 runner  staff   16388 Oct 25 13:08 .DS_Store
-rw-r--r--    1 runner  staff  809909 Oct 25 13:08 .VolumeIcon.icns
lrwxr-xr-x    1 runner  staff      13 Oct 25 13:08 Applications -> /Applications
drwxr-xr-x    3 runner  staff     102 Oct 25 13:08 Coreloom.app
```
- disk image: Coreloom.app's signature checks out
- disk image: window layout (.DS_Store) present
