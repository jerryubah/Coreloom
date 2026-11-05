# Coreloom 1.9.3 for macOS (x64): packages

macOS 15.7.9 (24G830), x86_64

```
total 252896
drwxr-xr-x  5 runner  staff       160 Nov  5 13:41 .
drwxr-xr-x  3 runner  staff        96 Nov  5 13:36 ..
-rw-r--r--@ 1 runner  staff  64714793 Nov  5 13:41 Coreloom-1.9.3-macos-x64.dmg
-rw-r--r--  1 runner  staff  63772996 Nov  5 13:41 Coreloom-1.9.3-macos-x64.zip
drwxr-xr-x  3 runner  staff        96 Nov  5 13:41 Coreloom-x64.app
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
CodeDirectory v=20400 size=972111 flags=0x2(adhoc) hashes=30372+3 location=embedded
Hash type=sha256 size=32
CandidateCDHash sha256=32601a6149fce336d070b56d7fa5b9495fb36185
CandidateCDHashFull sha256=32601a6149fce336d070b56d7fa5b9495fb36185d31a2d72c3e80660a68aac1f
Hash choices=sha256
CMSDigest=32601a6149fce336d070b56d7fa5b9495fb36185d31a2d72c3e80660a68aac1f
CMSDigestType=2
CDHash=32601a6149fce336d070b56d7fa5b9495fb36185
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
  "CFBundleShortVersionString" => "1.9.3"
  "CFBundleSignature" => "????"
  "CFBundleVersion" => "1.9.3"
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
total 282976
drwxr-xr-x  6 runner  staff        192 Nov  5 13:41 .
drwxr-xr-x  7 runner  staff        224 Nov  5 13:41 ..
-rwxr-xr-x  1 runner  staff  125391392 Nov  5 13:41 Coreloom
-rwxr--r--  1 runner  staff    1509808 Nov  5 13:41 libAvaloniaNative.dylib
-rwxr--r--  1 runner  staff    2891424 Nov  5 13:41 libHarfBuzzSharp.dylib
-rwxr--r--  1 runner  staff   15084624 Nov  5 13:41 libSkiaSharp.dylib

/Users/runner/work/Coreloom/Coreloom/artifacts/mac/Coreloom-x64.app/Contents/Resources:
total 1640
drwxr-xr-x  7 runner  staff     224 Nov  5 13:41 .
drwxr-xr-x  7 runner  staff     224 Nov  5 13:41 ..
-rw-r--r--  1 runner  staff  809909 Nov  5 13:41 AppIcon.icns
-rw-r--r--  1 runner  staff    4380 Nov  5 13:41 LICENSE-Inter.txt
-rw-r--r--  1 runner  staff    4399 Nov  5 13:41 LICENSE-JetBrainsMono.txt
-rw-r--r--  1 runner  staff    1067 Nov  5 13:41 LICENSE.txt
-rw-r--r--  1 runner  staff    7743 Nov  5 13:41 THIRD-PARTY-NOTICES.md
```
- update zip: unpacks to Coreloom.app and its signature checks out

## Disk image contents
```
total 1976
drwxr-xr-x@   7 runner  staff     306 Nov  5 13:41 .
drwx------@ 113 runner  staff    3616 Nov  5 13:41 ..
-rw-r--r--    1 runner  staff  175210 Nov  5 13:41 .background.tiff
-rw-r--r--    1 runner  staff   16388 Nov  5 13:41 .DS_Store
-rw-r--r--    1 runner  staff  809909 Nov  5 13:41 .VolumeIcon.icns
lrwxr-xr-x    1 runner  staff      13 Nov  5 13:41 Applications -> /Applications
drwxr-xr-x    3 runner  staff     102 Nov  5 13:41 Coreloom.app
```
- disk image: Coreloom.app's signature checks out
- disk image: window layout (.DS_Store) present
