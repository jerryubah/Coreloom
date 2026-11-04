# Third-party notices

Coreloom is licensed under the MIT License (see `LICENSE`). Coreloom's release packages also contain the
components listed below, which are distributed unmodified under their own licenses. The source code for
each one is available at the linked project.

This list covers the packages Coreloom references directly and their notable dependencies. The exact set of
files in a build is determined by NuGet; run `dotnet list package --include-transitive` to see it.

## LibreHardwareMonitorLib

- License: Mozilla Public License 2.0 (MPL-2.0), https://mozilla.org/MPL/2.0/
- Source: https://github.com/LibreHardwareMonitor/LibreHardwareMonitor
- Used for CPU, motherboard, non-NVIDIA GPU, SSD health and some NVIDIA sensors (through NvAPI). Coreloom uses
  the library as published on NuGet, without modification.
- LibreHardwareMonitorLib includes **PawnIO modules**, licensed under the GNU Lesser General Public
  License v2.1. See LibreHardwareMonitor's `THIRD-PARTY-NOTICES.txt` for the full text. The PawnIO
  **driver** itself (GPL-2.0, https://github.com/namazso/PawnIO, downloads at https://pawnio.eu) is
  **not** included with Coreloom. Users who want low-level sensors install it separately.

### Dependencies of LibreHardwareMonitorLib

| Package | License | Source |
|---|---|---|
| HidSharp | Apache License 2.0 (© James Bellinger) | https://software.seekye.com/hidsharp |
| DiskInfoToolkit | MPL-2.0 | https://github.com/Blacktempel/DiskInfoToolkit |
| RAMSPDToolkit-NDD | MPL-2.0 | https://github.com/Blacktempel/RAMSPDToolkit |
| Mono.Posix.NETStandard | MIT (Mono project) | https://github.com/mono/mono |
| System.Management, System.IO.Ports, System.IO.FileSystem.AccessControl | MIT (.NET Foundation) | https://github.com/dotnet/runtime |

## Fonts: Inter and JetBrains Mono

- License: SIL Open Font License 1.1 (OFL-1.1), https://openfontlicense.org
- Inter: © 2016 The Inter Project Authors, https://github.com/rsms/inter (Regular, Medium, SemiBold, Bold)
- JetBrains Mono: © 2020 The JetBrains Mono Project Authors, https://github.com/JetBrains/JetBrainsMono
  (Regular, Medium, SemiBold)
- The font files are embedded unmodified in `Coreloom.exe`'s assembly resources. Each license text is
  also installed next to the app as `LICENSE-Inter.txt` and `LICENSE-JetBrainsMono.txt`.

## The phone app (Coreloom Connect): noble cryptography and Inter

The phone app in `src/Coreloom.Phone`, which Coreloom serves to paired phones, is embedded in `Coreloom.Core`'s
assembly resources (in both the Windows and the Mac app).

| Component | License | Source |
|---|---|---|
| @noble/curves 2.4.0 (P-256) | MIT (© Paul Miller) | https://github.com/paulmillr/noble-curves |
| @noble/hashes 2.4.0 (SHA-256, HMAC, HKDF) | MIT (© Paul Miller) | https://github.com/paulmillr/noble-hashes |
| @noble/ciphers 2.4.0 (AES-GCM) | MIT (© Paul Miller) | https://github.com/paulmillr/noble-ciphers |
| Inter (Regular, Medium, SemiBold, Bold) as WOFF2, Latin subset | SIL OFL 1.1 (© 2016 The Inter Project Authors) | https://github.com/rsms/inter |

- The noble libraries are bundled unmodified into `vendor/crypto.js` (see `build/phone/crypto`); their license
  notices are kept at the end of that file.
- Inter is subset to the characters Coreloom shows and converted to WOFF2 (`build/phone/make-assets.py`). Inter's
  copyright notice declares no Reserved Font Name. Its license is served with the fonts as `fonts/LICENSE-Inter.txt`.

## Coreloom icon

The application icon, logo and startup splash files in `assets/branding` (including `assets/branding/splash`) are
Coreloom's own artwork and are used as supplied.
They are covered by the project's license unless the owner states otherwise.

## CommunityToolkit.Mvvm

- License: MIT, © .NET Foundation and Contributors
- Source: https://github.com/CommunityToolkit/dotnet

## .NET runtime and Windows Desktop runtime

The self-contained builds include the .NET runtime, WPF and Windows Forms.

- License: MIT, © .NET Foundation and Contributors
- Sources:
  - https://github.com/dotnet/runtime
  - https://github.com/dotnet/wpf
  - https://github.com/dotnet/winforms
- The runtime's own third-party notices are published in those repositories (`THIRD-PARTY-NOTICES.TXT`).

## The macOS app: Avalonia, SkiaSharp and HarfBuzzSharp

Coreloom for macOS draws its window with Avalonia, which renders through SkiaSharp and shapes text with
HarfBuzzSharp. Their native libraries (`libAvaloniaNative.dylib`, `libSkiaSharp.dylib`, `libHarfBuzzSharp.dylib`)
are inside `Coreloom.app/Contents/MacOS`, unmodified.

| Package | License | Source |
|---|---|---|
| Avalonia, Avalonia.Native, Avalonia.Skia, Avalonia.Themes.Fluent, Avalonia.Remote.Protocol | MIT (© The Avalonia Project) | https://github.com/AvaloniaUI/Avalonia |
| MicroCom.Runtime | MIT (© Nikita Tsukanov) | https://github.com/kekekeks/MicroCom |
| SkiaSharp | MIT (© Microsoft Corporation); Skia itself: BSD-3-Clause (© Google LLC) | https://github.com/mono/SkiaSharp |
| HarfBuzzSharp | MIT (© Microsoft Corporation); HarfBuzz itself: "Old MIT" license | https://github.com/mono/SkiaSharp, https://github.com/harfbuzz/harfbuzz |

The Mac app also includes the .NET runtime (MIT, https://github.com/dotnet/runtime). It uses only macOS's own
frameworks to read the Mac's sensors (IOKit, IOReport, Metal, IOBluetooth); none of them is redistributed.

## NVIDIA Management Library (NVML)

Coreloom does **not** redistribute NVML. At runtime it loads `nvml.dll` from the NVIDIA display driver already
installed on the PC (from `System32` or the NVSMI folder only). NVML is © NVIDIA Corporation and is subject
to NVIDIA's driver license.

## Inno Setup

The Windows installer is built with Inno Setup (© Jordan Russell and Martijn Laan,
https://jrsoftware.org/isinfo.php), under the Inno Setup License. Only the generated setup program is
distributed.

## Device pictures (MacBook and iPhone mockups)

The Mac app's MacBook picture (`src/Coreloom.Mac/Assets/MacBook`, made by `build/mac/make-macbook.py` from
`assets/mac-render/macbook-mockup.png`) and the website's iPhone pictures (`site/assets/phone-*.webp`, made by
`build/site/make-phone-mockups.py` from `assets/site-render/iphone-mockup.png`) come from device mockups supplied by
the repository owner, who is responsible for their licenses. No Apple logo appears on them.

## Background pictures

The six suggested background pictures (`assets/backdrops`, since 1.8.2) are Coreloom's own: they are computed by
`build/backdrops/make-backdrops.py` from noise, light and shading, with no photographs or other works in them, and
are covered by Coreloom's license. A picture you choose yourself stays on your computer (a copy in Coreloom's settings
folder) and isn't distributed.

## Inter on the website

The website (`site/`) uses Inter, the app's typeface, from `site/assets/fonts` (SIL Open Font License 1.1, license
text beside the fonts).

## Vendor logos

Coreloom does **not** include NVIDIA, AMD or Intel logos. The GPU and CPU pages show a plain-text vendor badge
unless the person running Coreloom adds logo files they obtained from the company's own brand page
(`assets/vendors/` or `%AppData%\Coreloom\Vendors`). Anyone who adds a logo is responsible for following that
company's usage rules.

---

NVIDIA, GeForce and RTX are trademarks of NVIDIA Corporation. AMD, Ryzen and Radeon are trademarks of Advanced
Micro Devices, Inc. Intel is a trademark of Intel Corporation. Windows is a trademark of Microsoft Corporation. Apple, Mac, macOS,
Metal and Apple silicon are trademarks of Apple Inc., registered in the U.S. and other countries.
Coreloom is an independent project, and it is not affiliated with or endorsed by these companies.
