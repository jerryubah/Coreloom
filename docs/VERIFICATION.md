# Verification report

This file states plainly what has been verified about Coreloom and what hasn't. Update it as on-device testing
results come in.

## Build environment

Coreloom was developed in a Linux container with these properties:

- .NET SDK 10.0.112, runtime 10.0.12
- No Windows, no GPU
- **No access to nuget.org** (blocked by the environment's network policy)

That last point had consequences:

- The real NuGet packages couldn't be restored: CommunityToolkit.Mvvm, LibreHardwareMonitorLib, xunit.v3
  and the Windows Desktop (WPF) reference pack.
- To compile and test anyway, a local harness replaced them with stand-ins:
  - a minimal xUnit-compatible runner (Fact/Theory/InlineData/Assert subset);
  - a minimal MVVM shim (ObservableObject, RelayCommand, AsyncRelayCommand);
  - an API stub of LibreHardwareMonitorLib.
- The stand-ins are not part of the repository. The repository references the real packages, pinned in
  `Directory.Packages.props`. Their versions were checked to exist on nuget.org.

## Verified here

| What | How | Result |
|---|---|---|
| `Coreloom.Core` compiles | `dotnet build` (net10.0) | ✅ |
| Core unit tests: telemetry engine, history ring buffers, M4 downsampling, axis/curve math, alerts, settings persistence, formatting, semantic versions, release selection, checksum parsing, resumable download, SMBIOS parsing, diagnostic report incl. redaction | Real test code run through the shim runner | ✅ 160 passed, 0 failed (includes the settings-import, vendor-detection and splash-policy tests) |
| `Coreloom.Hardware` compiles | `dotnet build` targeting net10.0-windows from Linux. LibreHardwareMonitor adapter compiled against an API stub whose signatures were checked against the v0.9.6 sources (`Computer`, `IHardware`, `ISensor`, `SensorType`, `HardwareType`). | ✅ |
| Hardware mapping tests: LibreHardwareMonitor sensor name → canonical metric, GPU directory, disk matching, NVML return codes | shim runner | ✅ 47 passed. The 8 Windows integration tests were skipped, as designed, on Linux. |
| `Coreloom.ViewModels` compiles | `dotnet build` (net10.0) with the MVVM shim | ✅ |
| View-model tests: metric formatting and unavailable reasons, °F conversion, dashboard layout, overview cards, CSV export, settings service, shell navigation, section disabling, tray text, polling interval, sign-in task sync, custom accent, alert rule editing, history selection, vendor marks on the GPU and CPU pages | shim runner | ✅ 23 passed |
| `CoreloomDiag` compiles | `dotnet build` | ✅ |
| `Coreloom.App` C# is syntactically valid | Roslyn parse of all source files (97 files under `src`, 14 under `tests`), language version `preview` | ✅ 0 errors |
| `Coreloom.App` C# uses the Core/Hardware/ViewModels APIs correctly | Roslyn semantic compile **without** WPF references, keeping method-body errors. Every remaining error was checked to come only from missing WPF/WinForms types or members, XAML-generated members (`InitializeComponent`, `x:Name` fields) or source-generated P/Invokes. A probe file confirmed that this setup does catch wrong member names on Coreloom's own types. | ✅ No errors in Coreloom's own APIs |
| XAML consistency (15 files, 149 resource keys) | `python3 build/check-xaml.py`, which checks: well-formed XML; x:Class ↔ code-behind; StaticResource defined before use; DynamicResource keys exist (including the brushes ThemeService creates at runtime); event handlers exist; `ElementName` targets exist; 396 binding paths resolve to known properties; icon keys; pack URIs (including linked resources and every embedded font family); settings sections. A mutation test confirmed it catches typos. | ✅ |

## Reported by the owner (not independently verified here)

The first Windows build (as Vigil, before the rename) went through three rounds of fixes on GitHub Actions and
on the owner's PC: the CI build is green, and after the last fix the owner reported that the app starts and
runs without errors. That settles that the XAML compiles and the app launches. It does **not** settle sensor
accuracy, overhead, or the details below, and none of it was observed by the author.

The first CI run with the vendor-mark, tuning and splash changes (run #6) failed in the Windows job's XAML compile:
"Tags of type 'PropertyArrayStart' are not supported in template sections" at `Templates.xaml` line 522. The tuning
chart's `Points` property was array-typed, which WPF can't set inside a template. It is now `IReadOnlyList<T>`, and
`check-xaml.py` flags array-typed dependency properties. With that fix the Windows build went green and the owner
ran the app, including the GPU tuning panel. The owner then asked for the tuning feature (overclock indicator,
*Tuning* button and panel) to be removed; it was, and the vendor marks stayed.

## Changes in this version and what backs them

| Change | What was done here | Not verified here |
|---|---|---|
| Inter + JetBrains Mono, sharper text | Fonts embedded as `Resource`s; Display text formatting, fixed hinting, ClearType hints; `check-xaml.py` confirms every referenced family exists in the embedded files | How the text actually looks and renders at your DPI |
| Animation rate up to the display's refresh rate | Frame-rate override from `EnumDisplaySettings`; page views cached | Real smoothness on a 240 Hz panel (WPF treats the rate as a request, and the DWM decides what is shown) |
| Leak hygiene | Chart/donut/sparkline handlers detach on `Unloaded`; bound immutable items are weak-event safe; per-frame arrays reused; managed heap and handle count are shown in Settings › Diagnostics | That memory stays flat over hours: no leak profiler was run. Use the checklist in TESTING-ON-YOUR-PC.md |
| Always runs as administrator | `requireAdministrator` manifest; sign-in task replaces the Run key; old UI and commands removed; unit tests for the task sync | The UAC prompt, the elevated task actually starting at sign-in, the uninstaller's task removal |
| Renamed Coreloom + supplied icon | Whole-repo rename; the supplied `coreloom.ico` and PNGs are used byte-for-byte (`cmp` identical) for the exe, window, tray (PNG sizes), installer and shortcuts; settings import is unit-tested | That the compiled exe, installer and shortcuts show the icon (needs a Windows build); Windows may show a cached old icon until the icon cache refreshes |

### Vendor marks

| Change | What was done here | Not verified here |
|---|---|---|
| Vendor mark next to the CPU and GPU names | `HardwareVendors` (vendor string first, device name second) is unit-tested with GeForce/RTX/Quadro, Radeon/Ryzen/EPYC, Intel/Arc/Xeon names and ambiguous input; `BrandMark` shows a supplied file or a text badge; `check-xaml.py` confirms the resource pack URI and the wildcard `Resource` item | That a real logo file loads and looks right at your DPI and on both themes. No third-party logos are bundled, so the badge is what you see until you add files |

### Startup splash

| Change | What was done here | Not verified here |
|---|---|---|
| Splash window (native XAML port of the supplied kit) | Layout numbers taken from the kit's `splash.css` measured at 700 × 370 in Chromium with the embedded Inter (the measured layout matches the supplied preview PNG); icon is the approved `png/coreloom-512.png`, byte-identical to the kit's `coreloom-app-icon.png`; `check-xaml.py` confirms the pack URIs, the embedded font, and that the splash uses no App.xaml resources | How it actually renders on Windows (letter spacing, gradients, sharpness at 125–200 %), and that `CenterScreen` puts it on the monitor with the mouse |
| Splash on its own UI thread | Code review; Roslyn checks show only the expected "WPF types missing" errors; `InheritanceBehavior` isolation enforced by `check-xaml.py` | That the line keeps moving while the dashboard is built, and that nothing cross-thread throws at runtime |
| Readiness and handoff (cloak → first frame → minimum 850 ms → raise splash → uncloak → crossfade) | Timing rules unit-tested in `SplashPolicyTests` (start-hidden rule, minimum time, reduced motion, timing ranges from the brief) | That `DWMWA_CLOAK` hides the first frames as intended, the crossfade looks smooth, focus lands on the dashboard, and no extra taskbar/Alt+Tab entry appears |
| Failure and exit paths | Error state plus the existing dialogs (a splash not yet on screen is cancelled so it can't cover the dialog); every exit path closes the splash; background thread; 10 s watchdog for a window that never paints | Behaviour of a real failed start |
| Independent read-through | A separate review pass over the splash code looked for compile errors, cross-thread access and lifecycle bugs. It found no compile or threading problems; its lifecycle findings (late splash over the error dialog, minimum time counted before the splash appeared, a blocking cross-thread call, watchdog priority, double close, live-region announcements) were fixed | Still not a substitute for compiling and running on Windows |

### Automatic updates

How this change was checked in the Linux container (no Windows, no nuget.org):

- **Core tests: 199 passed** (160 before, plus 39 new for the release-notes summary, the pending-update store,
  installer verification and link refusal, clean-up of old installers, the install policy and installer arguments, launch tracking and the
  downloader's link hardening, and reading one release by tag), through the stand-in xUnit runner.
- **View-model tests: 44 passed** (23 before, plus 21 new for the update state machine against a fake GitHub:
  automatic download → verify → ready → install, automatic updates off, portable copies, checksum mismatch,
  restoring a download after a restart, startup install and its attempt cap, `--update-failed`, untrusted or
  changed installers, idle-time installs, the "Updated to" banner, skip, a newer release replacing a download,
  failed checks, the shell badge, and What's new after updating from 1.0.0, which left no notes behind).
- **`Coreloom.App` compiles against the real WPF API surface.** WPF's reference-assembly sources (dotnet/wpf
  `src/*/ref/*.cs`) were compiled into a reference assembly and the whole app (`App.xaml.cs`, services, views'
  code-behind, the new trust store and idle detection, the source-generated P/Invokes) was compiled against it,
  with the XAML compiler's generated members (`InitializeComponent`, `x:Name` fields) produced from the XAML.
  Only the WinForms `NotifyIcon`, a few `System.Drawing` members and `SystemEvents` were stubbed. 0 errors,
  0 warnings, and a probe with wrong member names on WPF and Coreloom types was rejected as expected. This is
  stronger than the earlier Roslyn-without-WPF check, but the XAML itself is still only checked by
  `check-xaml.py` (passed: 15 files, 151 resource keys, 435 bindings; a mutation test caught typos in the new
  bindings).

| Change | What was done here | Not verified here |
|---|---|---|
| Background download + verification | Unit tests against a fake GitHub (listing, `SHA256SUMS.txt`, installer bytes) | Real downloads from GitHub, proxies, a 70 MB installer |
| Install at next start / while idle / *Restart to update* | Policy and state machine unit-tested; idle detection uses `GetLastInputInfo` and the CPU/GPU load already measured | That the installer runs, closes Coreloom and relaunches it (`/UPDATE=1`, `/MINIMIZED=1`) |
| Installer rollback relaunch (`--update-failed`) | Pascal code reviewed; the arguments the app passes are tested against the script's parameter names | Inno Setup compiling the new `[Code]` and the rollback path on a real failure |
| HKLM trust store | Code review; the view model's trust logic is tested with an in-memory store | Writing/reading `HKLM\SOFTWARE\Coreloom\Updates` on Windows |
| Settings › Updates, badge, banner, tray item | XAML checks; bindings resolve; app compiles | How it all looks and behaves on screen |
| Release workflow: existing release, re-run by tag | YAML parses; PowerShell reviewed | A real run. (The likely reason `v1.0.0` was published without its files: its release already existed when the workflow tried to create it, and that step failed.) |

### Overview header and page layout (after 1.1.0)

- **Core tests: 241 passed** (199 before, plus 42 for `SystemDescriptions`: board vendor names and placeholder
  values, GPU badges and card sizes for NVIDIA/AMD/Intel names, memory, cores and Windows lines).
- **View-model tests: 50 passed** (44 before, plus 6: the Overview header from the fixture's summary and GPU list,
  the header following the selected GPU, and the two-column sensor split).
- **`Coreloom.App` compiles** against the WPF reference assembly as above, including `PcIllustration`, `PcImages`,
  `ScrollChain` and the new `AdaptiveGrid.RowSpacing`: 0 errors, 0 warnings.
- **XAML checks passed** (16 files, 153 resource keys, 456 bindings). A mutation test caught a misspelt header
  binding, a misspelt illustration binding and an undefined style. The checker now also resolves `Resource` items
  whose `Link` uses `%(Filename)%(Extension)`.
- `build/pc-illustration.py` regenerates `PcIllustration.xaml` byte-for-byte, and its SVG output was rendered in
  Chromium for two hardware variants and accents to check the drawing.

| Change | What was done here | Not verified here |
|---|---|---|
| Overview header and PC drawing | Unit tests for the text; bindings checked; SVG renders of the same shapes | How the XAML drawing renders in WPF (gradients, the accent mask, sharpness at your scaling) |
| Optional `pc.png` | Code review; loader decodes once, size-capped, errors fall back to the drawing | Loading a real file on Windows |
| GPU specifications beside VRAM, sensors in two columns | Split logic unit-tested; XAML checks | The final look at your window size, and the wheel hand-off from the inner list to the page |

### Install update for every copy (after 1.1.0)

- **Core tests: 269 passed** (241 before, plus 28: `FolderUpdate` staging, applying and cleaning up, keeping `Data`
  and `portable.txt`, zips with a top folder, unsafe paths and non-Coreloom zips refused with nothing left behind,
  a failed copy undone file by file, links planted in the work folder, the helper's location and arguments, and
  picking the installer or the zip per kind of copy).
- **View-model tests: 53 passed** (50 before, plus 3, and the update tests rewritten for *Install update*: one click
  downloads, verifies and installs; portable copies download the zip and install in place; a download for the
  other kind of copy is discarded; an update that can't be unpacked stays ready and explains; a release without
  the needed package explains).
- **`Coreloom.App` compiles** against the WPF reference assembly with the helper mode, the per-copy registry key and
  the new Settings card: 0 errors, 0 warnings. **XAML checks passed** (16 files, 153 keys, 453 bindings).

| Change | What was done here | Not verified here |
|---|---|---|
| *Install update* (download if needed → install → reopen) | View-model tests against a fake GitHub | The whole round trip on Windows |
| In-place update of portable copies | File operations unit-tested on Linux (staging, copy, rollback, cleanup) | Windows specifics: the elevated helper starting without a prompt, locked files and antivirus timing, the hidden `.coreloom-update` folder, the splash while copying, `Compress-Archive` zips from the real release |
| Per-copy trust key `HKLM\SOFTWARE\Coreloom\Updates\Copies\<id>` | Code review | Registry writes and clean-up on Windows |

### 1.5.1: the cards show the latest run, GPU scores on a 10,000-point scale

- **Tests.** Core tests: 681 passed (new: GPU tests score 10,000 at the reference rate, 1.5.0 GPU results multiplied by
  ten once with rates untouched and the CPU and stress results left alone, the scale saved in `benchmarks.json` and a
  file written by 1.5.0 read on the new scale; the one- and two-thread timing test no longer compares the two rates,
  which failed once on a busy build machine). View-model tests: 177 passed (the cards show the latest run and its
  parts, a run below the best is what the card shows while the Best badge stays on the best, a 1.5.0 GPU result of
  37,000 reads 370,000 on the card and in its details).
- **Seen as rendered on Windows.** Page shots of the Benchmarking page: the cards with the latest run and "Latest run ·
  date, time", no badge on them, the Best badges in the list, and GPU scores in the hundreds of thousands fitting the
  cards, the list and the details table at 1600 and 1000 DIP.
- **GitHub Actions.** Branch CI on `release-1.5.1` and `release-1.5.1-b` passed: 941 tests on Windows (Core 681, view
  models 177, Hardware 83; on `-b` the timer check skipped itself on a slow runner timer, as it's designed to), and
  the Linux tests.
- **Release build.** The first release run failed in one view-model test: its fake stress load described an error
  before counting it, so the page sometimes took it for a stopped load. Real loads count first; the page now reads a
  load's failure before its count (so it can't mix the two up whatever the order), the fake counts first, and branch
  CI passed again on both branches before releasing.
- **Not verified:** a GPU run on your RTX 5090 on the new scale (it's the old scale × 10, so your ~37,000 should read
  ~370,000).

### 1.5.0: the Benchmarking page (CPU and GPU benchmarks, saved results with a Best badge, details, stress tests)

- **CPU benchmark, measured.** All eight tests ran in the Linux sandbox (2.8 GHz Xeon, 2 threads) and on GitHub's
  Windows runner (Ice Lake Xeon, 4 threads): every test gives the same answer for the same seed, the references are
  the sandbox's single-thread rates (so it scores 1,000 per test), and the runner scored about 1,700 single-core and
  4,000–4,300 multi-core on two runs. Memory bandwidth and latency were measured on both. **Not measured on your PC:**
  what a Ryzen 9 9950X3D scores, how steady its scores are run to run, and how Windows' scheduler spreads 32 threads.
- **GPU benchmark, on WARP only.** The Direct3D 11 code (adapter listing, device, run-time HLSL compilation, buffers,
  render target, event query, read-backs) and all five shaders ran on WARP, Windows' software renderer, on the build
  machine: at full size from the page-shot tool (Lighting 0.4 fps, Ray marching 2.3 fps, Compute 60 GFLOPS, Memory
  bandwidth 17.5 GB/s, Physics 1.0 billion pairs/s on four CPU threads), and at small size in the Hardware tests, which
  also check that every answer is the same on every run, that a broken shader reports the compiler's message, that
  cancelling works and that the GPU stress load runs without errors. **Not verified:** any real graphics card (the
  runner has none), so the RTX 5090's scores, how long each test takes on it, and the reference rates (estimates from
  each shader's arithmetic for a ~2.5 TFLOPS, ~110 GB/s card) are unchecked.
- **Stress test.** The processor load ran 40 s at full size on every sandbox thread with no calculation errors (the
  answers stay the same as the code is recompiled by .NET's tiered JIT). Loops, stability, the pass rule, the
  temperature guard and stopping on an error or a driver reset are unit- and view-model-tested with fake loads. **Not
  verified:** a real overclock caught by it, real thermals, or whether background activity on a desktop pushes
  stability under 97 %.
- **Readings and tuning notes.** Unit-tested from recorded readings: PBO likely on / off / not reached against the stock
  PPT, a raised, default and lowered GPU power limit, clocks past the rated maximum, the clock-limit reasons. On your PC
  they depend on LibreHardwareMonitor's and NVML's readings, which weren't available here.
- **Seen as rendered on Windows.** The page-shot tool drew the Benchmarking page over sample results (a 9950X3D and an
  RTX 5090) at 1600 and 1000 DIP: idle with saved results and badges, a CPU benchmark running, a stress test running
  with its loops, and the CPU, GPU and stress details (top and scrolled to the end). The pictures were looked at, and
  the spacing of the comma in scores, the stress tiles (only the tested parts), the group labels and the system rows
  were fixed from them.
- **Tests.** Core tests: 678 passed (new: the CPU tests' answers, scoring, the run and cancelling, results history and
  file, readings, tuning notes, stress scoring, loops, guard and the processor load). View-model tests: 175 passed
  (new: the Benchmarking page's results and badges, both benchmarks with readings and tuning, cancelling, no Direct3D,
  details and deleting, filters, and stress tests passing, throttling, erroring, overheating, both parts and stopping
  early). Hardware tests: 83 passed (6 new, the GPU benchmark on WARP).
- **GitHub Actions.** Branch CI on `release-1.5.0` passed: 936 tests on Windows (Core 678, view models 175, Hardware
  83), none skipped, and the Linux tests.

### 1.4.9: the boost count matches the switches, a grey power plan note

- **Tests.** Core tests: 653 passed (the power plan check is now informational). View-model tests: 162 passed, with a
  new one turning switches on and off and checking the ring (2/9, 3/9, 4/9, 3/9, then 9/9 after *Boost now*, which
  now turns on Ultimate Performance too) and that the power plan pill is grey.
- **GitHub Actions.** Branch CI on `release-1.4.9` and `release-1.4.9-b` passed: 892 tests on Windows each time (Core
  653, view models 162, Hardware 77), none skipped, and the Linux tests.

### 1.4.8: a Gaming page in clean rows, core scheduling details in its boost, cards in a row ending on one line

- **Seen as rendered on Windows.** A new tool, `tools/Coreloom.PageShots`, builds the Gaming (at 1600 and 1000 DIP),
  Memory, Processor, Graphics, Smart Optimize and Storage pages over sample data with the app's own resources, theme and
  window text settings, and renders them to PNG on GitHub's Windows runner (*Page shots* workflow). The pictures were
  looked at before release: the boosts in three rows ending on one line (and the ninth spanning the row in two
  columns); the frame limiter and shader cache cards ending together with the pacing row and the cache list at the
  bottom; the core scheduling details card over the page; Memory, Processor, Storage, Smart Optimize and sensor cards
  ending on one line. The sample data isn't his PC: real text lengths, his window size and scaling, and his startup
  apps and modules will differ, so the rows can stretch by a different amount there.
- **Tests.** View-model tests: 161 passed (the core scheduling tests now also check the Details command, the boost's
  test and result note, and the Ultimate Performance advice as a tooltip instead of a line). `Coreloom.App` compiles
  and the XAML checks pass (20 files, 242 shared keys, 1,329 bindings).
- **GitHub Actions.** Branch CI on `release-1.4.8` and `release-1.4.8-b` passed: 891 tests on Windows each time (Core
  653, view models 161, Hardware 77), none skipped, and the Linux tests.

### 1.4.7: intelligent CPU core scheduling, a movable and sizable overlay, cards that fit their content, a compact AMD card

- **Core tests: 653 passed** (52 new: `OverlayPlacementTests`, `CoreLayoutTests` with Windows topology buffers built
  for a Ryzen 9 9950X3D, 9950X, 9800X3D, Core i9-14900K and Ryzen AI 9 HX 370, `CorePlacementTests`,
  `CoreSchedulingChecksTests`, `AffinityTrialTests`, `CoreProfileTests`, `MemoryModuleNamesTests`, and updated AMD
  tuning tests). **View-model tests: 161 passed** (16 new: overlay placement and sizing, and core scheduling against
  a simulated 9950X3D, AMD driver and game process: the checks, *Boost now* leaving Ultimate Performance off, load
  shares, a kept profile applied after AMD's driver had its moment, each case where a profile is left out, the A/B
  test switching cores and offering a profile only for a clear gain, a refused change, and turning off giving the
  cores back). `Coreloom.App` compiles and the XAML checks pass (20 files, 242 shared keys, 1,320 bindings).
- **GitHub Actions.** Branch CI passed on the first run and again on `release-1.4.7` and `release-1.4.7-b` with the
  docs: 891 tests on Windows each time (Core 653, view models 161, Hardware 77), none skipped, and the Linux tests.
- **Not run on the hardware it's for.** There's no Ryzen 9 9950X3D here and no game to play. Not verified: that
  Windows reports the two CCDs' L3 caches as 96 MB and 32 MB (as `coreinfo` shows on X3D processors), the service
  manager's view of `amd3dvcache`/`amd3dvcacheSvc` and the registry preferences on a real chipset driver install,
  `SetProcessAffinityMask` on real games (and anti-cheat refusing it), the load shares against Task Manager, and what
  the A/B test finds in real games. The registry and service names come from the open-source vcache-tray tool's
  source, not from AMD documentation.
- **Overlay dragging and resizing** (the window leaving click-through while moving, `DragMove`, the corner grip in
  per-monitor DPI, per-game places) haven't been tried on Windows.

| Change | What was done here | Not verified here |
|---|---|---|
| Intelligent CPU core scheduling | Unit tests of the topology parser, grouping, load shares, verdicts, checks, A/B rules and profiles; view-model tests with a simulated driver and game | A real 9950X3D and AMD chipset driver; affinity on real games; real gains |
| Movable, sizable overlay | Unit tests of placing, dropping, per-game places, screen scale and sizes; view-model tests | Dragging, the grip and DPI on Windows; how the sizes look in games |
| Cards fit their content | XAML reviewed page by page; `StackedColumns.CanFill` | How each page looks at your window sizes |
| Compact AMD card, leaner frame limiter | View-model tests of the shorter texts; XAML checks | The look on his PC |
| Memory slot names with the channel | Unit tests | His board's SMBIOS strings |

### 1.4.6: process-level memory logs, a log viewer, games added by hand, lighter monitoring, AMD details, adaptive frame pacing

- **Core tests: 601 passed** (99 new: `LogAndMemoryTrimTests`, `CustomGameTests`, the lighter-monitoring cases in
  `TelemetryEngineTests`, `CpuIdTests`, `AmdProcessorTests`, `AmdTuningTests`, `PacingSecondTests`,
  `PacingLimitsTests`, `AdaptivePacingSessionTests`). **View-model tests: 145 passed** (19 new: the log viewer, the
  memory card's process list, adding and editing games, gaming in front making monitoring lighter, the AMD card and
  adaptive pacing against a fake driver). The sensor-mapping tests (64, including the new AMD SMU and SVI2 names,
  taken from LibreHardwareMonitor 0.9.6's own source) also pass here, built against the release's
  LibreHardwareMonitor assembly. `Coreloom.App` and `Coreloom.Hardware` compile (0 errors, 0 warnings) and the XAML
  checks pass (20 files, 242 shared keys, 1,243 bindings).
- **Overhead was not measured on a game PC.** There is no Windows PC with a GPU here, let alone a Ryzen 4000 / GTX
  1650 one, so no "with and without Coreloom" frame rates were taken. What was done: the gaming-in-front path is
  unit-tested (slower polling without resizing history, below-normal thread priority, no page updates while a game
  covers the window), and *Settings › Diagnostics* now shows what each part of Coreloom costs (from each thread's
  processor time), so the comparison in TESTING-ON-YOUR-PC.md can be made on that PC, the overlay on its own included.
- **AMD readings depend on the processor.** Which SMU readings exist comes from LibreHardwareMonitor 0.9.6's tables
  (read from its source): one firmware version each of the Ryzen 2000 APU, Ryzen 3000, Ryzen 5000 and Ryzen
  7000/9000 desktop tables, so TDC/EDC/PPT depend on the processor and its BIOS; none of the SMU table on Ryzen 4000
  (Renoir) processors, which still get package power, clocks and temperatures. The
  CPUID decoding is tested with register values, not on AMD hardware.
- **Adaptive pacing and the driver.** Whether the NVIDIA driver applies a changed per-program limit to a game that is
  already running isn't known here; the engine checks the frames after each change and, if they don't show it,
  waits for the game's next start rather than changing again.
- **GitHub Actions.** The first branch runs failed on Windows only: the WPF markup compiler rejected a TextBlock in
  *Settings › Logs* whose Style was set twice (as an attribute and as a property element), which the XAML checks here
  didn't look for; `check-xaml.py` now does. After the fix, branch CI on `release-1.4.6` and `release-1.4.6-b` passed:
  823 tests on Windows (Core 601, view models 145, Hardware 77), none skipped, and the Linux tests.

| Change | What was done here | Not verified here |
|---|---|---|
| Memory clean-up per process | Unit tests (outcomes, reasons, totals, log lines); view-model test of the list and filter | `K32EmptyWorkingSet` and `K32GetProcessMemoryInfo` on his PC; protected processes failing as expected |
| Log viewer | Parsing and filtering unit-tested; view-model tests | Reading a log that is being written on Windows |
| Games added by hand | Unit tests (ids, paths, names, broad folders, matching the running program); view-model tests | The open-file dialogs, icons, launching and per-program limits on Windows |
| Lighter monitoring while gaming | Unit tests; Diagnostics parts compile | Frame rates with and without Coreloom on the Ryzen 4000 / GTX 1650 PC |
| AMD card | Unit tests (CPUID, models, stock PPT, PBO inference, controls); view-model tests | Readings on real Ryzen processors with PawnIO |
| Adaptive frame pacing | Unit tests of the engine (learning, set, lower, raise, hysteresis, verify, frame generation); view-model tests with a fake driver | Real games: ETW frame times, the driver taking the limit live or at the next start |

### 1.4.5: the frame-time graph on screen, no frame limiter mark

- **Why the graph didn't show (1.4.3–1.4.4).** A new Windows test (`OverlayVisibilityTests`) puts a WPF window set up
  like the overlay on screen (see-through, topmost, click-through, drawn in software, moved with `SetWindowPos`),
  places the graph's layered window over it, presents opaque red pixels from another thread and reads the screen. On
  the first run the graph's window was visible, in place and above the overlay, `UpdateLayeredWindow` returned success,
  yet the screen showed the overlay's blue where the red should be. Two variants were run side by side: giving the
  position and size only when placing (on the window's thread) showed the pixels after a move but not the first ones
  presented from the drawing thread; giving the position and size in every update (the ones the window already has)
  showed them every time. That is the fix. The existing test that the drawing thread never waits on the window's thread
  (the window's thread not pumping messages meanwhile) passes with it.
- **GitHub Actions on two branch runs before release: all 688 tests passed in both, none skipped, the on-screen
  check included (Core 502, view models 126, hardware 60).**
- **Core tests: 502 passed** (the empty-graph test now expects the clearer flat line, and a faint baseline under a
  trace). View-model tests: 126 passed. Here, `Coreloom.App` compiles (0 errors, 0 warnings) and the XAML checks pass
  (20 files, 242 shared keys, 1,121 bindings).

| Change | What was done here | Not verified here |
|---|---|---|
| Graph on screen | Pixels read back on the Windows runner over an overlay-like WPF window, before and after a move | Over a real game on his PC; at his scaling and refresh rate |
| Flat line when idle | Raster unit-tested | How it looks to him |
| No frame limiter mark | Removed from the overlay template; XAML checks | — |

### 1.4.4: automatic game pictures, no G-SYNC warning, quieter search box

- **Core tests: 502 passed** (23 new, `SteamStoreTests`: Steam's real search answer for "Assassin's Creed Shadows"
  (curly apostrophe) picks the game and not its DLC; names that must and mustn't match (editions, GOTY, ™/®, accents,
  "Tetris Ultimate" ≠ "Tetris", a soundtrack, another game of a series); error pages; the store item's hashed picture
  addresses and the order tried; the requests carry only the name or the app number). **View-model tests: 126 passed**
  (the old offer test replaced by two: every game without a picture is looked up after the scan without asking, and a
  slow look-up shows its indicator while a quick one doesn't).
- The answers' shapes were checked against Steam itself: the store search and app details were fetched for Assassin's
  Creed Shadows (app 3159330; its capsule pictures sit under a hashed folder, its header at the plain address), and the
  `IStoreBrowseService/GetItems` asset format (`asset_url_format` with `${FILENAME}`, `library_capsule_2x` with a hash
  folder) from a public bug report of another app that switched to it because the plain library addresses fail for
  newer games. The pictures themselves couldn't be downloaded from here (this environment's web access doesn't fetch
  images), so the first real download is on Jerry's PC.
- The G-SYNC warning came from NVAPI's per-display VRR "requested" flag, which read off on Jerry's PC with G-SYNC on;
  the warning is removed and the card only uses the flag that says whether the display can do variable refresh.
- **GitHub Actions on two branch runs before release: the Windows job compiled the app with the real WPF XAML compiler
  and passed all 687 tests in both (Core 502, view models 126, hardware 59, none skipped), and the Linux job passed.**
  Here, `Coreloom.App` compiles (0 errors, 0 warnings) and the XAML checks pass (20 files, 242 shared keys, 1,126
  bindings).

| Change | What was done here | Not verified here |
|---|---|---|
| Automatic pictures | Matching and parsing unit-tested against Steam's real answers; the flow tested with a fake service | Real downloads on his PC; which of his games Steam has (Ubisoft/EA/Battle.net/Riot names vs Steam's) |
| No G-SYNC warning | Code path removed; compiled | — |
| Search box hint on one line | Compiled; XAML checks | The look |

### 1.4.3: smooth overlay graph, overlay performance, G-SYNC, shader cache size, Auto HDR

- **Core tests: 479 passed** (27 new: the graph's timing (frames placed at the moment they were shown, the delay
  following ETW's batches and easing back, nothing drawn past the left edge), its scale, stutter classes and redraw
  rate; the raster (line, fill, stutter marks, dashed target, opacity, premultiplied blending, nothing drawn outside
  the bitmap); shader cache sizes, G-SYNC on/off states and the recommendation text, the DirectX settings names, and
  repairing a stored size that isn't offered). **View-model tests: 125 passed** (4 new, and the boost tests updated
  for 9 boosts of which 8 count: categories, recommendations, the G-SYNC recommendation from the frame limit and
  refresh rate, the shader cache size flow, a hint shown under a card).
- **New Windows tests** (`OverlaySurfaceTests`, Windows runner): the high-resolution timer averages close to its
  4 ms interval (an ordinary timer would average 15.6 ms), stops and wakes on request, and a real layered window takes
  pixels from a second thread without waiting on the window's thread, and again after being resized.
- **GitHub Actions:** the first push to main failed in the Windows tests while the branch run had passed. The build
  script now turns each test run into a summary and every failed test into an annotation (readable without the
  log), which showed the timer test: on one runner the "high-resolution" timer averaged 20 ms for 4 ms waits. The timer
  now notices late wakes and uses a 1 ms system timer while doing short waits; the test warms up first and skips
  itself only when the machine stays slow even then. 1.4.3 was published from that state (its release build passed
  every test). CI on main then hit one 200 ms pause of the runner in the same test, so the test now judges the middle
  wait length instead of the average and the worst; with that, two runs passed all 663 tests, the timer test and the
  layered-window test included (neither skipped). The Linux job now reports its tests the same way, which caught an
  older flaky test (`StorageBenchmarkViewModelTests.Run_FillsTheGaugesAndTable`, seen failing once before in 1.4.0):
  without a UI context, `Progress<T>` queued the last live report to the thread pool, where it could overwrite the
  finished results. `ContextProgress` handles it in order; the app's behaviour is unchanged. Here, `Coreloom.App` compiles (0 errors, 0 warnings) and the XAML checks pass (20 files, 242 shared
  keys, 1,129 bindings).
- The choppy graph was traced in WPF's own source (`MediaContext`): a window drawn in software isn't paced by the
  display, so WPF schedules its frames with a 10 ms `DispatcherTimer`, which fires on the 15.6 ms system tick: at most
  64 uneven frames a second. That is why the graph now draws itself, on a precise timer, into its own window.
- The NVIDIA setting IDs, values and units (G-SYNC's three global settings, the shader cache size in MB with 0x4000 as
  the default, `NV_GET_VRR_INFO_V1`) were checked against NVIDIA's public NVAPI headers; the HDR query structures
  against the Windows SDK. The graph's pixels were rendered here from the real raster code and looked at.

| Change | What was done here | Not verified here |
|---|---|---|
| Smooth, responsive graph | Model and raster unit-tested; timer and window tested on Windows CI; compiled | How smooth it looks over a running game; that it sits exactly over its place at his scaling |
| No overlay impact on games | Software-only drawing, low-priority threads, below-normal process priority while a game is in front, top re-asserted only when covered | **Not measured**: no game, GPU or frame-time capture here. Needs the before/after check in TESTING |
| No overlay outline | Rim removed from the template; compiled | The look over a game |
| G-SYNC boost | IDs from NVIDIA's headers; state logic unit-tested | That the NVIDIA Control Panel shows the change on his display; the VRR query on his monitor |
| Shader cache size | Sizes and states unit-tested | The control panel showing the chosen size |
| Auto HDR | Setting name unit-tested; HDR query compiled | Detection on his OLED with HDR on and off |
| Frame limiter rename, no Ctrl K badge | Text search over the app and docs; XAML checks | — |

### 1.4.2: design fixes

- **Core tests: 452 passed** (10 new: which shortcut files are Coreloom's, a shortcut starting this exe (case, quotes),
  the dark icon over the usual one, the exe's icon back in the light theme, nothing changed when a shortcut already
  matches, a custom icon left alone). **View-model tests: 121 passed** (6 new: a temperature's glow key following its
  level as it is read and as thresholds change, other metrics keeping their series color, the four temperature keys).
- **New Windows test** (`ShellLinkTests`): creates a real shortcut through the Windows Shell, switches its icon to the
  dark one and back as the app does, and reads it back each time. It runs on the Windows runner (skipped only off
  Windows).
- **GitHub Actions on the branch, before release: the Windows job compiled the app with the real WPF XAML compiler (no
  warnings reported) and passed all tests, including the shortcut test**, and the Linux job passed too. Here,
  `Coreloom.App` compiles (0 errors, 0 warnings) and the XAML checks pass (20 files, 238 shared keys, 1,119 bindings).
- The causes were found from Jerry's screenshots: collapsed nav items are 40 px wide but their icon row needed 42
  (14 + 18 + 10), so WPF clipped the icon about 2 px short of its right edge; the search list sat 18 screen pixels (12
  DIPs) left of the box, which is the popup's −12 offset, because WPF's Bottom placement lines up the panel itself
  (not its shadow margin) and, with right-handed menus, the right edges; and the page scrolled under the open
  dropdown because the list was too short to scroll, so the smooth-scroll handler passed the wheel to the page. The
  popup placement maths was checked against WPF's own `Popup.cs` (custom placement sizes are screen pixels; the
  returned point moves the popup window from the target's top-left).
- The new edge glow and the temperature gradients were drawn with the same maths in an HTML mock-up (cards at 38, 62,
  78 and 86 °C, hovered cards, the collapsed sidebar, the centered search list and the attached dropdown). The WPF
  windows themselves were not rendered here.

| Change | What was done here | Not verified here |
|---|---|---|
| Collapsed sidebar icons | Layout arithmetic; compiled | The look at his scaling |
| Edge glow in the card's color, outside the edge | Mock-up; compiled | Smoothness in WPF; neighbouring cards drawn over a glow |
| Temperature colors | Glow keys unit-tested; gradients in the mock-up | Colors on his panel as temperatures change |
| Dropdowns kept attached, search list centered | Placement checked against WPF's source; compiled | Behaviour with his mouse/touchpad and handedness setting |
| Dark icon on shortcuts | Policy unit-tested; shortcut round trip tested on Windows CI | His Start menu, desktop and pinned taskbar button redrawing (Explorer's icon cache) |

### 1.4.1: design overhaul, per-game frame pacer, overlay graph

- **Core tests: 442 passed** (13 new: picking a game's programs (running program, store program, Unreal shipping
  build, largest non-helper, launchers last); program names from paths and icon references; the limit pacing a program
  (its game's own, else the all-games one); repairing per-game limits; the overlay's frame-time graph offered once to
  existing setups; recent frame times from the tracker; the splash bar's per-stage progress, creep and ceiling, and its
  easing). **View-model tests: 115 passed** (5 new: search suggestions on the Devices and Gaming pages and their
  ranking, the typed text kept as typed; a game's own frame limit set, shown as a chip, changed and removed without
  touching other games; the all-games limit remembered for the overlay; the overlay's pacing mark and graph feed for
  the game in front).
- **GitHub Actions on the branch, before release: the Windows job compiled the app with the real WPF XAML compiler
  (no warnings reported) and passed all tests with the real xUnit and MVVM Toolkit**, and the Linux job passed too.
  Here, `Coreloom.App` compiles (0 errors, 0 warnings) and the XAML checks pass (20 files, 226 shared keys, 1,114
  bindings).
- The squircle path is a port of figma-squircle's math; the same function, ported to JavaScript, drew the HTML mock-ups
  of the Overview (glass sidebar, glow cards with the pointer light, tick gauges, toggles), the frame pacer card, quick
  search, gauges with orbs, the overlay with its graph and pacing mark, and the splash with both icons. The WPF windows
  themselves were not rendered here.
- The dark-mode icon was generated from the approved 1024 px icon by `build/dark-icon.py` and checked at 1024 and at
  16–128 px; the mark itself is unchanged.
- NVAPI per-program functions (`FindApplicationByName`, `CreateProfile`, `CreateApplication`, `DeleteProfileSetting`,
  `GetProfileInfo`, `DeleteProfile`) and the `NVDRS_PROFILE`/`NVDRS_APPLICATION` layouts were taken from NVIDIA's
  public headers; none of these calls has run here.

| Change | What was done here | Not verified here |
|---|---|---|
| Squircles, glow cards, edge light | Geometry ported and rendered in mock-ups; compiled | WPF rendering at his scaling, hover smoothness, CPU cost with many cards |
| Darker palette, glass sidebar, Mica | Reviewed; mock-up | How Mica looks through the sidebar on his wallpaper; text smoothing there |
| Tick gauges, liquid toggle, quick search, smooth scrolling | Compiled; mock-ups; suggestion logic unit-tested | Feel of the toggle and scrolling; the search panel's focus handling in WPF |
| Splash and dark icon | Progress logic unit-tested; mock-up | The layered splash animating smoothly; icons in the taskbar and tray |
| Per-game frame pacer | Program picking and settings unit-tested; NVAPI calls reviewed against the headers | Driver profiles created and applied to his games |
| Overlay redesign and graph | Tracker and view model unit-tested | The graph in games, the layered overlay redrawing at 30 Hz |

### 1.4.0: Gaming tab

- **Core tests: 429 passed** (36 new: KeyValues parsing with escapes, comments and conditions; Steam library folders in
  old and new formats, app manifests, tools and unfinished installs left out, library art in both cache layouts; Epic
  manifests with DLC, engines and unfinished installs left out, catalog covers; GOG play tasks; Xbox config and the
  package family name, checked against Microsoft's known publisher id `8wekyb3d8bbwe`; Ubisoft, Riot, Battle.net and
  EA games from installed-app entries; merging, sorting, search, initials, finding the running game; `powercfg`
  output in English and German; HAGS state with a pending restart; DirectX user settings; the automatic frame limit
  for 60–360 Hz; pacing grades; shader-cache locations and driver-change text; Gaming settings repair).
  **View-model tests: 110 passed** (11 new: the library in order with store, size and last played; sort, search and
  list/grid remembered; Play and Open folder; covers offered and downloaded only when asked; boosts, *Boost now* and
  the restart notice; a switch turned off; a refused change flips back with its reason; the frame pacer's automatic
  and chosen limits; no NVIDIA driver; the running game marked with its pacing; shader caches and a driver change;
  Gaming above Graphics and Updates last in Settings).
- **GitHub Actions on the branch, before release: the Windows job compiled the app with the real WPF XAML compiler
  and passed all tests with the real xUnit and MVVM Toolkit**, and the Linux job passed too. Here, `Coreloom.App`
  compiles (0 errors, 0 warnings) and the XAML checks pass (20 files, 215 shared keys, 1,072 bindings).
- NVAPI function ids, setting ids and the `NVDRS_SETTING` layout were taken from NVIDIA's public NVAPI headers; the
  D3DKMT query type and structures from the Windows headers (cross-checked with Wine's). None of these calls has run
  here.
- HTML mock-ups of the Gaming page (list and grid) were rendered with invented game titles and artwork; the WPF page
  itself was not rendered here.

| Change | What was done here | Not verified here |
|---|---|---|
| Game library | Parsers unit-tested with sample files | His real libraries (missing or doubled games), Xbox packages, launch as non-administrator |
| Boosts | State logic unit-tested; registry and `powercfg` calls reviewed | Each switch on his PC; HAGS after a restart; Ultimate Performance on his edition |
| Frame pacer | Limit math unit-tested; NVAPI calls reviewed against the headers | The driver limit applying to games; NVAPI on driver 58x |
| Shader cache | Locations and notice unit-tested | The unlimited setting in the NVIDIA Control Panel; the sizes |
| Covers | Download only on request; unit-tested | Steam's and Epic's servers answering; picture quality in the grid |

### 1.3.5: Smart Optimize card layout, sidebar order, search hint, Devices headings

- **Core tests: 393 passed** (1 new: in-order stacking keeps cards in their columns, and filling grows only the last
  card of the shorter column). **View-model tests: 99 passed** (Smart Optimize is now second to last, above
  Performance History). **`Coreloom.App` compiles** (0 errors, 0 warnings); **XAML checks passed** (19 files, 212
  shared keys, 914 bindings).
- HTML mock-ups at the owner's width rendered: the Smart Optimize cards with the startup list capped at 300 px as in the
  app (columns end level, no large empty areas), and the Devices headings with the focused search box. The WPF pages
  themselves were not rendered here.

### 1.3.4: aligned device groups, grouping fixes, search, Smart Optimize banner

- **Core tests: 392 passed** (5 new: `CardColumns.Fit` capped and uncapped, every width 600–3,000 px within range,
  `CardColumns.Stack` into the shortest column; wired grouping of an LED controller, a desk light, a monitor's USB
  link, a USB controller card without a driver, an audio interface, a garbled product string and a real gamepad).
  **View-model tests: 99 passed.** **`Coreloom.App` compiles** (0 errors, 0 warnings); **XAML checks passed** (19
  files, 211 shared keys, 913 bindings).
- HTML mock-ups at the owner's page width (≈1,540 DIP) were rendered of the Devices page (wired columns line up with
  the Bluetooth cards) and the Smart Optimize banner (idle, scanning, result), using the same hue-shift math as
  `ThemePalette.Shift`. The WPF pages themselves were not rendered here.

| Change | What was done here | Not verified here |
|---|---|---|
| Wired groups on the Bluetooth grid | Column and stacking math unit-tested | The look in WPF at his size and scaling |
| Grouping fixes | Unit-tested with device trees modelled on his list | His real device tree (names, classes) |
| Leaving text boxes | Compiles; logic reviewed (clicks in pop-ups and in the box ignored) | Behaviour in the app, Esc in Customize |
| Banner gradient and ring | Mock-up rendered | WPF rendering of the sweep (seams), blur glow, light theme |


- `AdaptiveGrid` gained `MaxColumnWidth`: with it set, the grid uses the fewest columns that keep each one between the
  minimum and maximum width (as many as fit at the minimum, otherwise capped and left-aligned), and keeps that column
  count when there are fewer items, so cards don't stretch. The column math was checked for content widths from 520 to
  2,400 px: Bluetooth cards land between 270 and 330 px wide (about 314 px on a 1,943 px page, six to a row, where they
  were 635 px, three to a row).
- The Bluetooth card's status pill moved from the header into the status row, replacing the line that repeated it.
- **`Coreloom.App` compiles** (0 errors, 0 warnings), **XAML checks passed** (19 files, 204 shared keys, 907
  bindings), core tests 387 and view-model tests 99 passed (no logic changed). An HTML mock-up of the new card at the
  owner's window width was rendered to check proportions; the WPF page itself was not rendered here.

### 1.3.2: Devices page

- **Core tests: 387 passed** (23 new: device types from the Bluetooth Class of Device, LE Appearance and names; labels,
  icons and groups; address formatting; battery colors; dual-mode devices merged and connected first; wired devices
  built from a device tree (internal, Bluetooth and network-only containers left out, names from USB product strings
  and disks, a mouse that also exposes a keyboard, monitors with driver problems, drives with eject, hubs left out)).
- **View-model tests: 99 passed** (6 new: listing and ordering, card texts, battery warning, connect/disconnect updating
  the same card, failed connect, search, the Bluetooth switch (also refused), eject, refreshing on Windows' events; nine
  pages with Devices below Storage).
- **`Coreloom.App` compiles** against the WPF reference assembly and stand-ins for the Windows SDK projection types it
  uses (0 errors, 0 warnings); the real projection is first used by GitHub Actions. **Struct sizes** measured on .NET 10:
  BLUETOOTH_DEVICE_INFO 560 (address at 8, last used at 48, name at 64), KSPROPERTY 24, DEVPROPKEY 20. **XAML checks
  passed** (19 files, 204 shared keys, 905 bindings).

| Change | What was done here | Not verified here |
|---|---|---|
| Bluetooth list and status | Merging and typing unit-tested; WinRT calls compiled against stand-ins | Real enumeration, events, names and types on Windows 11 |
| Connect / Disconnect | View model unit-tested; KS and IOCTL codes from the SDK headers | That headphones come back from a phone, other devices' behavior |
| Battery | Colors unit-tested | The device-tree property and the GATT read on real devices |
| Wired devices and Eject | Grouping unit-tested; configuration manager calls reviewed | Names and groups on a real PC, eject with and without files open |

### 1.3.1: gauge colors, memory and storage health, speed tests and Smart Optimize

- **Core tests: 364 passed** (44 new: the cool threshold level and the gauge color scale (pure bands, blends that
  never go backwards, stops from thresholds); memory health (use, commit charge, module speed) and storage health
  (space, wear, temperature, no SMART readings); the drive benchmark on a real temporary file (every test measured,
  progress only forward, the file and its folder gone afterwards, cancelling, too little space); speed ratings; the
  internet speed test against a fake server (latency, download, upload, phases, offline error, ramp-up and live
  windows, `Server-Timing`); the junk cleaner on real files (age limit, links never followed, read-only skipped,
  patterns, missing folders), the Windows junk categories; StartupApproved values and command parsing; Defender status
  JSON (single threat, other antivirus, errors)).
- **View-model tests: 93 passed** (10 new: the drive benchmark (drives, run, ratings, errors), the speed test (ratings,
  offline), Smart Scan and the clean-up run on real files, threats flagged and removed, the Recycle Bin, startup
  switches that Windows refuses, the page without Windows features; health split into four parts; eight pages).
- **`Coreloom.App` compiles** against the WPF reference assembly (0 errors, 0 warnings). **XAML checks passed** (18
  files, 182 shared keys, 826 bindings).

| Change | What was done here | Not verified here |
|---|---|---|
| Gauge color scale | Scale math unit-tested; gauge drawing reviewed | How the colors look and glide on screen, both themes |
| Drive benchmark | Engine run on Linux with buffered I/O | Unbuffered I/O on Windows (alignment), numbers against CrystalDiskMark, the folder on non-system drives |
| Internet speed test | Engine run against a fake server | Cloudflare's real endpoints, numbers against a browser test, Wi-Fi |
| Cleanup | Cleaner unit-tested on real files | Sizes on a real PC, browser caches while browsers run, the Recycle Bin calls |
| Protection | Status parsing unit-tested | The PowerShell script and MpCmdRun on Windows 11, another antivirus, EICAR |
| Free up RAM, startup apps, maintenance | View models unit-tested; P/Invoke signatures reviewed | EmptyWorkingSet and the standby purge, StartupApproved against Task Manager, DNS flush, defrag |

### 1.3.0: in-game overlay and health cards

- **Core tests: 320 passed** (25 new: overlay settings defaults, repairs and JSON round trip, hotkey names and
  modifier-only keys; frame rates for steady frames, stutters pulling the 1% low down, out-of-order frames and a
  process that stops drawing; health of the graphics card (cool, warm, hot, near the driver's slowdown temperature,
  brief spikes, thermal and hardware slowdowns, stopped fans, hot spot gap), processor (missing temperature, fan) and
  system (memory, full drive, worn drive); the worst report; the health card's place and size, and its insertion into
  older layouts).
- **View-model tests: 83 passed** (9 new: health shared by the pages and rated per part; checks updated in place,
  problems first; overlay lines grouped by part leaving out missing readings; frame rates of the program in front;
  appearance and warning colors from settings; the settings card's switch, items, position, layout and look; hotkey
  capture (Esc, modifier on its own, combination, none, End); hex colors; reset; the frame-rate note).
- **ETW struct layouts** compiled and measured on .NET 10: `EVENT_TRACE_PROPERTIES` 120 bytes, `EVENT_TRACE_LOGFILEW`
  448 (callback at 424), `TRACE_LOGFILE_HEADER` 280, `EVENT_HEADER` 80 (provider id at 24), matching the Windows SDK
  for x64; the app checks the same sizes before starting a session.
- **`Coreloom.App` compiles** against the WPF reference assembly (0 errors, 0 warnings). **XAML checks passed** (17
  files, 166 keys, 612 bindings).

| Change | What was done here | Not verified here |
|---|---|---|
| Frame rates from ETW | Tracker math unit-tested; struct sizes checked; provider and event ids from the DXGI and D3D9 manifests | A real session on Windows: that events arrive, the 100 ms flush, FPS against a game's own counter, Vulkan/OpenGL games, the stale-session cleanup |
| Overlay window | Code review | Click-through, no focus, topmost over borderless and fullscreen games, placement on each monitor and DPI, flicker, CPU/GPU cost |
| Hotkey (low-level keyboard hook) | Capture and arming logic unit-tested | The hook in games (raw input, anti-cheat), that keys still reach the game, modifier matching |
| Health cards | Rules and view models unit-tested | The real readings on your PC and how the cards look |

### 1.2.7: moving and resizing dashboard cards, the page scroll bar and the picture switch

- **Core tests: 295 passed** (25 new: column counts for widths, placement of the default dashboard at six and four
  columns, narrowing cards wider than the grid, keeping the order rather than back-filling gaps, tall cards with
  others beside them, where a dragged card lands and the hysteresis that keeps it from flickering, resize snapping,
  row height, card size limits and defaults, and sizes and the picture switch in the settings JSON).
- **View-model tests: 74 passed** (7 new: tiles for every visible card in order and size, moving a card saves the
  order without rebuilding the others, hidden cards keep their place, resizing within limits and storing nothing for
  the default, the right-click menu's one-step commands and when they're greyed out, Customize keeping sizes and
  *Reset layout* clearing them, and the picture switch).
- **`Coreloom.App` compiles** against the WPF reference assembly (0 errors, 0 warnings). **XAML checks passed** (16
  files, 157 keys, 488 bindings); the checker now knows theme brushes looked up from code and `VerticalOffset` in a
  scroll viewer template.

| Change | What was done here | Not verified here |
|---|---|---|
| One grid for cards and panels, moved and resized | Layout, drop target and snapping unit-tested in Core; saving and commands in the view model | The drag itself on Windows: mouse capture, the glide animation, the dashed outline, auto-scroll, Esc, and that clicks in cards (legend toggles, the storage list) still work |
| Row height from the cards' contents | Rule unit-tested | The real heights: that the default looks as before (cards about 200 px, panels about 300 px) |
| Wider page scroll bar clear of the resize border | Styles compile and pass the XAML checks | That the pointer no longer turns into the resize arrow, the hover widening and wheel narrowing, and maximized windows |
| *Show the PC picture* switch | View-model test | The header without the picture |

### 1.2.6: Customize as a card on the Overview

- **View-model tests: 67 passed** (2 new: the card opens and closes on the Overview and shares the lighting colors;
  hiding, reordering, the min/max/avg switch and *Reset layout* apply to the dashboard immediately and are saved).
  Core tests: 270 passed.
- **`Coreloom.App` compiles** against the WPF reference assembly (0 errors, 0 warnings); **XAML checks passed** (16
  files, 154 keys, 468 bindings).
- Not verified here: the card's look and its fade-in on Windows, focus and Esc handling, and the drop shadow's cost.

### 1.2.5: the rendered PC, glow colors and turning fans

- **Core tests: 270 passed** (one new: the glow setting's default, repair of unknown values and its JSON round trip).
- **View-model tests: 65 passed** (12 new: the glow loaded from and saved to settings, Reduce motion stopping the fans,
  fan speeds from board fan readings through the Overview, idling without readings, `PcFans` speed mapping, CPU/case
  classification with pumps dropped, unnamed fans following the average).
- **`Coreloom.App` compiles** against the WPF reference assembly (0 errors, 0 warnings). **XAML checks passed** (16
  files, 153 keys, 461 bindings); a mutation test caught a misspelt binding and a misspelt layer file (the checker now
  verifies files referenced from XAML even when they're embedded with a wildcard).
- The layers were recomposed in Python the way WPF blends them: Blue matches the render (text aside), Purple, Red,
  Green and a Rainbow cycle relight it plausibly, and turning the blade layers looked right at several angles and with
  the blurred copy. A 7-second clip of the turning fans and the rainbow was rendered from the shipped layers.

| Change | What was done here | Not verified here |
|---|---|---|
| Rendered PC with relightable glow | Layer split checked by recomposition; XAML generated from the same numbers | How WPF renders the opacity mask and scales the 552 × 660 layers down to 196 × 234 DIP (moiré on the mesh, sharpness at your scaling) |
| Turning fans | Speed mapping and fan classification unit-tested; animation code compiles | The real motion: smoothness at 60 fps, `SpeedRatio` changes without jumps, pausing when hidden, CPU/GPU cost |
| Fan sensors | Classification by LibreHardwareMonitor's names (CPU/AIO/Radiator; pumps dropped; empty headers ignored) | Which names your board reports, so which fans the picture follows (the tooltip shows it) |

## Not verified yet (needs Windows and the real hardware)

None of the following has been confirmed by the author. Don't treat any of it as working until the checklist in
[TESTING-ON-YOUR-PC.md](TESTING-ON-YOUR-PC.md) has been completed.

- **This version has not been compiled on Windows.** Its changes (fonts, `PageHost`, `DisplayInfo`, tray icon
  from PNG, startup task, linked icon resources) are checked only by the Roslyn and XAML checks above; GitHub
  Actions is the first real compile.
- **Visual details**: the startup splash and its handoff, the vendor badge/logo, layout, theming, Mica, chart rendering and smooth scrolling, animations, tray,
  notifications, single instance, startup registration and the elevated task.
- **Sensors on real hardware**, by any provider:
  - NVML calls, struct layouts and per-metric support on the RTX 5090;
  - PDH counters;
  - storage IOCTLs;
  - SMBIOS on the ASRock X870E;
  - LibreHardwareMonitor sensor names on the 9950X3D;
  - PawnIO detection.
- **Overhead is unmeasured.** The CPU and memory budgets in the docs are design targets, not measurements.
- **The installer script (including the new `shellexec` launch and uninstall task removal), `build.ps1` and the
  release workflow have never been run.**
- **The update flow has not been exercised end to end** against real GitHub releases. Its logic
  (version selection, checksums, resume, automatic download and install, the what's-new summary) is
  unit-tested; section 6 of TESTING-ON-YOUR-PC.md walks through it with two releases.
- **No comparison with established tools** (HWiNFO64, the NVIDIA app, Task Manager) has been made.

## Highest-risk areas

These are the places most likely to need fixes on the first Windows build, in rough order.

1. **XAML compile errors** the static check can't see: property value types, style `TargetType`
   mismatches, template details. These are usually quick to fix from the compiler's message.
2. **Window chrome.** This covers `WindowChrome` combined with DWM attributes:
   - the Mica backdrop (DWMWA 38) and dark mode (DWMWA 20);
   - `GlassFrameThickness` switching;
   - the maximized-window inset;
   - the `WM_NCHITTEST` hook that makes the custom maximize button show the Snap Layouts flyout.
3. **Custom chart controls** (`LiveLineChart`, `Sparkline`, `RadialGauge`, `DonutChart`). They are written
   against WPF's `DrawingVisual` APIs but have never rendered a frame.
4. **LibreHardwareMonitor mapping** on the 9950X3D. It expects "Core (Tctl/Tdie)" and "CCD1 (Tdie)" style
   names, with fallbacks. If the names differ, temperatures will show as unavailable rather than wrong.
   CoreloomDiag's `--lhm-report` shows the real names.
5. **P/Invoke marshalling.** NVML (function-pointer calls and structs), PDH, storage IOCTL structs, and
   `NtQuerySystemInformation` for processes.
6. **Tray context menu.** It is a WPF `ContextMenu` opened from WinForms' `NotifyIcon`, using the
   foreground-window workaround.
7. **Inno Setup script.** Preprocessor expressions, the `/UPDATE=1` relaunch with `--updated`/`--minimized`, and
   the new `[Code]` that restarts the old version after a failed update.

## Coreloom for Mac (1.6.0)

The Mac app was developed in the same Linux container (no Mac, no nuget.org: Avalonia's packages were restored from
a local copy fetched by a GitHub Actions job). Everything that needs macOS was checked on **GitHub's macOS runners**:
`macos-15` (Apple silicon, an Apple M1 virtual machine) and `macos-15-intel` (an Intel Core i7-8700B Mac mini, also
virtualized), both on macOS 15.7.

| What | How | Result |
|---|---|---|
| The macOS back-end (`Coreloom.Hardware.Mac`): CPU, memory, storage, network, GPU memory and load, Metal device, Metal benchmark and stress test, SMC on Intel, process sampling, command-line and decoding helpers | `dotnet test tests/Coreloom.Hardware.Mac.Tests` on both Macs (`mac-verify.yml`, and the CI and release workflows) | ✅ 143 passed on Apple silicon (1 Intel-only test skipped), 144 on Intel |
| The app bundle | `build/mac/bundle.sh`: self-contained single-file publish, Contents/MacOS holds only Mach-O code, Info.plist passes `plutil -lint`, `lipo` shows the right architecture | ✅ both |
| Code signature | `codesign --verify --deep --strict` on the built app, on the app inside the mounted disk image and on the app unpacked from the update zip with `ditto` (what the updater runs) | ✅ all three, both Macs. Gatekeeper (`spctl`) rejects the ad hoc signature, as expected: people confirm the first opening once |
| Disk image | Mounted read-only: Coreloom.app, a link to Applications, the window layout and background | ✅ |
| The app runs | Installed in /Applications and opened: telemetry starts (7 providers), every page opened through a second launch (`--page`, handed to the running copy) and photographed, no warnings or errors in the log, no crash report, quits cleanly on SIGTERM | ✅ both Macs (about 350 MB resident on Apple silicon, 240 MB on Intel, after visiting every page) |
| Layout of every page and dialog | `tools/Coreloom.MacShots` (headless Avalonia, sample Apple silicon readings) with zero binding errors; the screenshots from the runners above | ✅ |
| Shared code | Core 693 and view-model 178 tests (the Windows app's tests too) on Linux | ✅ |

**Not verified on real Mac hardware yet.** The runners are virtual machines: they have no temperature, power,
fan or battery sensors on Apple silicon (the Intel VM passes some SMC readings through), no Bluetooth, no
displays beyond a virtual one, and no games. So these parts are written to Apple's documented and observed
interfaces but haven't been seen working on a physical Mac:

1. IOReport readings on Apple silicon: the performance and efficiency clusters' clocks, CPU, GPU and Neural Engine
   power, and the whole Mac's power.
2. The HID temperature sensors and the SMC on Apple silicon (fans, temperatures), and the battery.
3. Bluetooth: the permission request, listing, connecting and disconnecting accessories, battery levels, the
   switch.
4. The Gaming page's switches: the power mode (with macOS's password dialog), Keep awake with a real game, the Metal
   HUD in a real Metal game; the game library with real Steam and Epic installs.
5. The in-game overlay over a full-screen game, and its global hotkey.
6. Updating on a physical Mac. On the runners it works end to end (below), but a real Mac adds things a fresh
   virtual machine doesn't have: Gatekeeper's quarantine on a copy that was downloaded with a browser, and an app
   that isn't in /Applications.
7. The translucent sidebar's vibrancy, *Open at login*, notifications and *Move to Applications*.

### In-app updates on a Mac, end to end (after 1.6.0)

`build/mac/update-test.sh`, run by `.github/workflows/mac-update-test.yml` on both Macs after a release is
published (push a branch named `macupdatetest-…`): an older Coreloom is installed in /Applications and opened; it
finds the latest release, downloads its `-macos-<arch>.zip`, checks it against `SHA256SUMS.txt` and its code
signature, and installs it when it's opened again; the test then checks the version in /Applications, the update
helper's log (`update.log`: "updated"), the new copy's signature and that it started with `--updated`. Since 1.6.1
it also does this with the previous release's own app downloaded from GitHub (so the updater as shipped is the one
tested), not only with an old version built from the current code.

| Run | Result |
|---|---|
| 0.9.0 (built from 1.6.0's code) → the published 1.6.0, run 40194296972 | ✅ Apple silicon: found and verified in 24 s, replaced in under a second after quitting, 1.6.0 running with "Coreloom was updated to 1.6.0". ✅ Intel: the same, in 27 s |
| The published 1.6.0 app (downloaded from its release) → the published 1.6.1, run 40197580106 | ✅ Apple silicon: found and verified in 23 s, 1.6.1 running after reopening with "Coreloom was updated to 1.6.1" and the Overview's card showing all its specs. ✅ Intel: the same |
| 0.9.0 (built from 1.6.1's code) → the published 1.6.1, same run | ✅ both Macs |

### 1.6.1: the Overview's top card fits its specs on a Mac

The update test's screenshot showed the Overview's top card cut off its last specs on the Apple silicon runner
(a 1024 × 768 screen, so the window is 942 points wide and the specs fit in one column). It was there in 1.6.0's
verification shots too and was missed. A temporary probe on a test branch (never merged) logged the header's
layout 15 s after the page opened: every element reported a valid layout, the card *wanted* 265 points but had
206, the grid holding the specs wanted 160 and had 0. So the specs, which come in after the page is first laid out
(Storage last, once the drives are known), were measured but the card was never placed again at its new size.
The headless MacShots tool couldn't reproduce it (new `--width`, `--from-width`, `--collapsed` and
`--late-specs` options), so the fix was checked on the runners:

| Try | Result on the Apple silicon runner |
|---|---|
| The header as a dock panel (name and Customize on top, the specs under them), rows that size themselves, the header's own elements measured again when the specs change | ❌ still 206 of 265: the parent never placed the card again |
| As above, plus everything the header sits in, up to the page, laid out again once the specs have changed | ✅ the card 249 points tall (265 with its margin), every element's size matching what it asked for; all specs visible at 942 points, and on Intel at 1,600 |

Both Macs passed the rest of the verification (packages, signatures, every page, clean quit). Windows is
unchanged.

## Coreloom on your phone: Connect (1.7.0)

### Verified

| What | How | Result |
|---|---|---|
| The cryptography agrees on both sides | Known-answer tests: the pairing and session keys, the proof and a request MAC computed by the phone app's own bundle (`vendor/crypto.js`, run in Node) are pinned in `ConnectTests.cs` and checked by the C# code | ✅ in the container, on Windows (CI) and on both Macs |
| Pairing, sessions and readings end to end, C# phone | `ConnectServerTests`: a phone written from the protocol pairs by code and by link over loopback, opens a session, reads live readings with history, gets a Home Screen setup link and pairs with it; a used link, a wrong code, a closed window, a bad key, a replayed counter, a forged MAC, an unknown device and a forgotten phone are refused; another Host name gets 421 | ✅ 43 Connect tests on Windows (CI runs 40461634859 and, for the release commit, 40489516887: Core 736, view models 186, Hardware 83 passed), Linux, and the Apple silicon and Intel Macs (runs 40460769693 and 40489520359) |
| Pairing, sessions and readings end to end, the real phone app | `connect.js` itself, run in Node against the demo server (`tools/Coreloom.ConnectDemo`): link and code pairing, wrong code, session hello, first reading with history, the next reading, forged and replayed requests, setup link, unpaired device, foreign Host | ✅ 16 of 16 |
| The phone app's pages | Playwright with Chromium at iPhone size (390 × 844, 3×) against the demo server, dark and light, PC and Mac profiles: pairing screen, wrong code, every tab, the Add to Home Screen sheet, link pairing from the URL, the Home Screen copy pairing itself, reload resuming | ✅ the only console errors are the deliberate wrong code's 403 and requests cut off by reloads |
| The QR code | `QrCodeTests` (finder and timing patterns, format bits, versions 1–40, path data) and, earlier, decoding with ZXing | ✅ |
| Settings › Connect on Windows | Page shots of the WPF page (run 40460766370) at 1,600 and 1,000 px: switch, the Public-network note, QR code, address, code, paired phones; the XAML checker passes | ✅ |
| Settings › Connect on a Mac | MacShots (dark, light, off) with 0 binding errors; and in the real app on both Macs (below) | ✅ |
| The real Mac app serves Connect | Mac verify runs 40460769693 and 40489520359 (the release commit): Connect turned on in the saved settings, Coreloom reopened: `/api/info`, the page (with its Content-Security-Policy), every script, the manifest, an icon and a font answer 200 on port 47101, also from the Mac's own network address (192.168.64.x); `Host: evil.example.com` gets 421; pairing is closed until Settings › Connect shows, then open (and photographed); Coreloom quits cleanly on SIGTERM and stops answering. The first run's picture said "Phones on en0": the network is now named as the Network page names it ("Ethernet" on the runners) | ✅ Apple silicon and Intel |
| Windows: an off-curve key | Windows' CNG reports it with `PlatformNotSupportedException`, which the first CI run caught; `SharedSecret` now refuses it on every platform | ✅ |

### Updating to 1.7.0 on a Mac

| Run | Result |
|---|---|
| The published 1.6.1 app (downloaded from its release) → the published 1.7.0, runs 40492628869 and the re-run on `macupdatetest-170-b` | ✅ Apple silicon and Intel, both times: found and verified in about 20 s, replaced after quitting, 1.7.0 running with "Coreloom was updated to 1.7.0" |
| 0.9.0 (built from 1.7.0's code) → the published 1.7.0, same runs | ✅ Intel, both times. ❌ Apple silicon, both times: GitHub answered the update check with its rate limit ("GitHub rate limit reached", the 60 requests an hour GitHub allows a shared runner address without signing in), so nothing was downloaded within the test's 10 minutes; after reopening, the first run did download and verify 1.7.0. The same Mac updated the 1.6.1 app minutes later. On a home connection the limit is per household, and Coreloom says "try again later" when it's hit |

### Not verified yet

- **A real phone on a real home network.** No iPhone or Android phone has opened Coreloom yet: Safari's and
  Chrome's behaviour on a device (the camera's QR link, *Add to Home Screen* and its separate storage on iOS,
  locking and unlocking, Wi-Fi changes) is untested. Chromium at phone size is the closest check so far.
- **Windows Defender Firewall on a real PC.** The rule is added and removed with `netsh` (Coreloom runs as
  administrator), but GitHub's Windows machine only ran the tests over loopback, which the firewall doesn't
  filter; nothing confirmed a phone gets through on a Private network, or that the Public-network note appears
  on a real Public network. The uninstaller's removal of the rule is untested.
- **The macOS firewall prompt.** GitHub's Macs have the firewall off. With it on, macOS should ask once whether
  Coreloom may accept incoming connections, and because Coreloom is signed ad hoc it may ask again after updates.
- Other firewalls and security suites, VPNs that capture the whole network, and routers that keep Wi-Fi clients
  apart (guest networks, "AP isolation") will block phones; Coreloom can't detect those.
- What the encryption protects against: someone listening on the network. The page itself comes from the
  computer over plain HTTP, so someone able to tamper with the network's traffic could serve a changed page;
  phones can only read, so the readings are what's at stake.

## 1.8.0: the phone's Overlay tab, messages that leave, the Mac sidebar

### Verified

| What | How | Result |
|---|---|---|
| The overlay channel | `ConnectServerTests.APhoneShowingItsOverlay_IsWatchedUntilItStopsAsking`: a live request with `"overlay":true` makes the server watched (told once), another without it ends that; `RemoteOverlayTests`: the readings on offer (more than the in-game overlay shows, none this computer lacks, none for frame rates on a computer that can't measure them), the in-game overlay's items and look, frame times sent once each on the computer's clock and afresh for another game | ✅ in the container and on GitHub's Windows machine (CI run 40582329983: Core 737, view models 199, Hardware 83, all passed) and Linux |
| The Overlay tab, the real phone app | Playwright with Chromium at iPhone size against the demo server, which pretends a game runs at about 141 FPS with stutters: the game, FPS and frame time, the graph moving with stutters marked, readings added and removed (kept on the phone), *Same as my computer*, the switch off, full screen upright and sideways, dark and light; `connect.js` in Node 16 of 16 | ✅ the only console errors are the deliberate wrong code's 403 and requests cut off by reloads |
| The update notice on the phone | The demo server told to offer 1.8.1: the bar says *Coreloom 1.8.1 is available*, × keeps it closed for that version | ✅ |
| Messages leave after five seconds | View-model tests with a stand-in timer: a refused switch (the note comes back, the "Restart to finish" status stays), *Boost now*'s result, a failed update check (back to "You have Coreloom …"), Bluetooth and radio errors, the low-battery banner (once per charge), maintenance and Defender outcomes (not while working), Connect's firewall note and why it couldn't start, the message that replaced the *Export failed* dialog; the timer itself runs the dismissal on the UI thread and never after being cancelled. The phone: the wrong-code message is gone after 5 s | ✅ in the container, on Windows and Linux (run 40582329983); the Mac tests passed on both Macs (187 + 1 skipped on Apple silicon, 188 on Intel) |
| Windows pages | Page shots (run 40582333493: Gaming, Benchmarking, Graphics, Processor, Memory, Storage, Smart Optimize, Settings › Connect) and the XAML checker; the WPF window with the new message over the page compiled in CI | ✅ |
| The Mac sidebar below the traffic lights | MacShots (0 binding errors) and the real app on GitHub's Macs (Mac verify run 40582339670): on the Intel Mac's 1920 × 1080 screen with the sidebar wide, and the Apple silicon Mac's 1024 × 768 screen with it narrow, the close, minimize and zoom buttons sit on the backdrop above the sidebar; every page, Connect on port 47101 and a clean quit passed on both | ✅ Apple silicon and Intel |

### Updating to 1.8.0 on a Mac

| Run | Result |
|---|---|
| The published 1.7.0 app (downloaded from its release) → the published 1.8.0, runs 40586214778 and 40589066122 (`macupdatetest-180`, `-180-b`) | ✅ Apple silicon both times: found and verified in about 20 s, replaced after quitting, 1.8.0 running with "Coreloom was updated to 1.8.0" and the sidebar below the window's buttons. Intel: ❌ the first time (GitHub answered the update check with its rate limit for the shared runner, so nothing was downloaded in 10 minutes), ✅ on the re-run |
| 0.9.0 (built from 1.8.0's code) → the published 1.8.0, same runs | ✅ Apple silicon both times. Intel: the first run couldn't build the 0.9.0 disk image ("hdiutil: create failed - Resource busy", a runner hiccup), ✅ on the re-run |

### Not verified yet

- **A real game's frame rate on a real phone.** The phone's overlay has only been fed by the demo server's pretend
  game. On a real PC it measures the program in front from Windows' graphics events, as the in-game overlay does,
  under its own name ("phone overlay"), so it works with the in-game overlay off; that hasn't run with a real game.
- **Keeping the screen on.** Full screen asks for a wake lock where the browser offers one (HTTPS pages only, so not
  Coreloom's) and otherwise plays a 3 KB silent video in a loop, which is how iPhones and Android phones are usually
  kept awake from a web page. Not tried on a real phone; the phone's own Auto-Lock wins if it doesn't take.
- Full screen on an iPhone: Safari only offers full screen for videos, so on an iPhone the button fills the page
  instead (open from the Home Screen icon for the most room).
- The five-second messages on real Windows and Mac windows: the timer is tested, and each page's view model, but
  nobody has watched them leave in the apps yet.

## 1.8.1: menu bar readings, the MacBook with your wallpaper, the window buttons in the glass, renaming, the update button

### Verified

| What | How | Result |
|---|---|---|
| Tests | Core (`Release181Tests`: Bluetooth names kept, trimmed and capped; menu bar modules in order, none shown at first, repaired when edited; the Mac's compute scores ×10 with older results converted once) and view models (`Release181ViewModelTests`: rename, Enter/Esc/empty/reset, re-sorting and search by either name, the low-battery line with the given name; menu bar readings in Settings' order with bars and names as chosen, the fastest fan, only new readings published; the desktop picture choice; *What's new* chips and "Released … · N changes"; `UpdatesViewModelTests` for *New update available*) | ✅ in the container and on GitHub (CI run 40793590164: Windows Core 741, view models 205, Hardware 82 + 1 skipped timer test; Linux; macOS 188 on Intel, 187 + 1 skipped on Apple silicon) |
| The window buttons inside the sidebar's glass | The real app on GitHub's Macs (Mac verify run 40793593475): the log line two seconds after launch gives the buttons' frames and what a click near the top reaches. Narrow sidebar (Apple silicon, 1024 × 768): glass at 10,10, close/minimize/zoom at 21/41/61, 22 from the top (centred across the glass, 30 below the window's top); wide (Intel, 1920 × 1080): 26/46/66, 22, with the collapse button's centre at (220, 31) on their line. The collapse button, the first page, the range picker and Freeze all reach Coreloom's view (AvnView), and so does the glass's top edge. Screenshots show the lights inside the glass, left-aligned, in light mode | ✅ Apple silicon and Intel |
| Menu bar readings | Mac verify run 40793593475 turns on Processor, Memory and Network in the saved settings and reopens Coreloom: the log says "Readings in the menu bar: cpu, memory, network", and the screenshot shows three items of their own beside Spotlight and the clock (CPU with its bar, MEM with its bar, NET with ↓/↑ on two lines), drawn black on the light menu bar as template images | ✅ Apple silicon and Intel |
| Settings › Menu bar, the Overview's MacBook, rename, *What's new*, Customize | MacShots (0 binding errors): the module list with live values and "Not on this Mac", the preview strip; the MacBook at 204 points centred in its column with a sample desktop picture (`--wallpaper`) and with Coreloom's picture (Blue and Rainbow), dark and light; the Customize toggle and the conic rainbow swatch; a Bluetooth card being renamed and one with a given name; Settings › Updates with 1.8.1's notes as the update | ✅ |
| Windows window | Page shots run 40794081756 now draw the whole WPF window (sidebar and page, 1440 × 960 at 2×): Overview dark and light, Gaming, Smart Optimize, and Settings › Updates with an update waiting: the red dot on Settings, the gradient *New update available · Coreloom 1.8.1* button with its glow, and the new *What's new* card | ✅ |
| Website | Playwright (Chromium) against the page served locally, as a Mac and a Windows visitor (user agent), dark and light, 1440 and 390 px wide: the right hero picture, download, wording and install steps for each; "See it on…" switches; no horizontal scrolling at 390 px; the phones in their mockups; no console errors | ✅ |

### Updating to 1.8.1 on a Mac

| Run | Result |
|---|---|
| The published 1.8.0 app (downloaded from its release) → the published 1.8.1, runs 40797004700 and 40803975371 (`macupdatetest-181`, `-181-b`) | ✅ Apple silicon and Intel both times: found and verified in about 25 s, replaced after quitting, 1.8.1 running with "Coreloom was updated to 1.8.1" |
| 0.9.0 (built from 1.8.1's code) → the published 1.8.1, same runs | Intel ✅ both times. Apple silicon: ❌ the first time (its update check failed twice, ten minutes apart, so nothing was downloaded; the same shared-runner trouble with GitHub's API as 1.8.0's first Intel run), ✅ on the re-run |

### Not verified yet

- **Liquid Glass.** Not used: Apple's `NSGlassEffectView` needs macOS 26, and GitHub's Macs run macOS 15, so it couldn't be tried. The sidebar stays Coreloom's own glass (with the desktop's vibrancy through it when translucent).
- **Your desktop picture on a real Mac.** The runners' default picture isn't photographed (GitHub's Macs show a black desktop), so the MacBook's screen has only shown a sample picture in MacShots. Dynamic and Aerial wallpapers, a picture in Downloads (*Show it* and macOS's question) and changing the picture while Coreloom runs haven't been tried.
- **The window buttons over time.** Placed again every 250 ms when AppKit moves them back; resizing, full screen and switching Spaces on a real Mac haven't been watched.
- **Menu bar readings on a dark menu bar, ⌘-drag ordering and the items' menu** (the menu's actions are wired to *Open Coreloom*, *Menu Bar Settings…* and *Quit*; not clicked on a runner).
- **Bluetooth renaming on real devices** (the names are Coreloom's; the system's Bluetooth settings keep theirs) and clicking out of search boxes in the real apps.
- **The Mac's GPU compute scores** on real Apple silicon: ten times what 1.8.0 showed; how they compare with the graphics scores on a real M-series GPU isn't known yet.

## 1.8.2: your background behind frosted glass, System health that fits, a softer shadow

### Verified

| What | How | Result |
|---|---|---|
| Tests | Core (`Release182Tests`: background settings default to Coreloom's gradient and repair bad values, round trip through the settings file; the presets dark, unique and complete; the gradient dithered and lit where its lights are; a picture covering the frame from its middle; the blur keeping the average; each kind making the background and a smaller frost; System health fitting its content on rows of its own and taking a taller neighbour's height) and view models (`Release182ViewModelTests`: Mica/Translucent only where the system has it; choosing a color, gradient or picture switches and saves; custom colors take valid hex only; the sliders clamped and described; your own picture copied and the previous copy removed, none offered without a folder; only the health tile fits its content; the Storage card's read/write chart) | ✅ in the container (Core 750, view models 213, Mac tests 180 + 8 skipped) and on GitHub (CI run 40810174991: Windows Core 750, view models 213, Hardware 83; Linux; macOS 188 on Intel, 187 + 1 skipped on Apple silicon) |
| One background, frosted glass on both apps | MacShots (0 binding errors) of every page over Coreloom's gradient and over each of the six pictures, dark and light; page shots of the WPF window (runs 40808456208 and 40809589418) over the gradient, Midnight, Aurora and Dunes, and Settings › Appearance: the sidebar and every card show the blurred part of the background right behind them, with no separate color behind the sidebar | ✅ |
| The real app on GitHub's Macs | Mac verify runs 40808456542 and 40809591267: a new step saves the Aurora picture as the background and reopens Coreloom ("Background: Image aurora drawn at 943×634 (frost 158×106) in 85–146 ms" on Apple silicon, 1360×880 in about 400 ms on Intel, off the screen's work), then *Translucent* ("Background: See-through"); screenshots of both, every page, Connect, menu bar readings and a clean quit | ✅ Apple silicon and Intel |
| System health fits its content | MacShots and the runners' Overview: on its own row the card ends under its last line (no empty space); beside a taller card the tests above check it takes that height | ✅ |
| A softer shadow | MacShots before and after: the band beside the sidebar went from about 35 % darker than the background to under 15 %, and nothing is drawn inside the glass | ✅ |
| Storage fills its card | MacShots of the whole Overview: the read/write chart under the drives ends level with the Network card's chart | ✅ on the Mac; Windows compiled and drew the window (page shots), but the shots don't scroll down to that card |
| Website and README | The website's Mac and Windows pictures and the README's two pictures redrawn from these shots; Playwright against the page as a Mac and a Windows visitor, 1440 and 390 px: the new "New in 1.8.2" section switches the window between backgrounds, no horizontal scrolling | ✅ |

### Updating to 1.8.2 on a Mac

| Run | Result |
|---|---|
| The published 1.8.1 app (downloaded from its release) → the published 1.8.2, run 40812435039 (`macupdatetest-182`) | ✅ Apple silicon and Intel: found and verified in about 25 s, replaced after quitting, 1.8.2 running with "Coreloom was updated to 1.8.2" over the new glass |
| 0.9.0 (built from 1.8.2's code) → the published 1.8.2, same run | ✅ Apple silicon and Intel |

### Not verified yet

- **Your own picture on a real computer**: the file dialogs, a HEIC picture converted by macOS's `sips`, very large pictures. The tests choose a picture through a stand-in for the dialog.
- **Mica on Windows for the whole window** (page shots can't show Mica) and *Translucent* on a Mac with a real desktop picture behind it (GitHub's Macs show a black desktop).
- **Cost on a real high-resolution screen**: the background is drawn once per window size off the screen's work and each card copies its part; scrolling and resizing on a 4K or Retina display haven't been timed.
- The light theme's pale version of each background has only been seen in MacShots and on the runners' light menu bar setting.

## 1.8.3: banners lit from above, new dials, dark glass buttons, fills without edges, the overlays

### Verified

| What | How | Result |
|---|---|---|
| Tests | View models (`Release183ViewModelTests`: Smart Optimize's dial is full before the first scan, follows the scan's progress and takes the result's color, and its properties are raised when the stage, progress or color change) plus every earlier test | ✅ in the container (Core 750, view models 214) and on GitHub (CI run 40820701467: Windows Core 750, view models 214, Hardware 82 + 1 skipped timer test; Linux Core 750, view models 214, Mac tests 180 + 8 skipped; macOS 188 on Intel, 187 + 1 skipped on Apple silicon) |
| Banners, dials and glass buttons on the Mac | MacShots (0 binding errors) of Gaming, Smart Optimize and *Settings › Updates* with an update waiting, dark and light, over Coreloom's gradient and the pictures: the light comes from the banner's top edge and is gone before the text; the dial is centred in its tile (measured on Smart Optimize's full dial at 3×: 289 pixels from each wall on the Mac, 284 and 285 on Windows); the buttons are charcoal with white text; the *What's new* card and the sidebar's update button use the same glass | ✅ |
| The same on Windows | Page shots runs 40818078880 and 40819652807 (the WPF window at 1440 × 960, 2×): Gaming, Smart Optimize, Overview dark and light, *Settings › Updates* with an update waiting. They match the Mac's | ✅ |
| The real Mac app | Mac verify run 40820703638: every page, Connect, menu bar readings, the background picture and a clean quit on Apple silicon and Intel; the new banners and dials in the screenshots (the Intel shot of Gaming caught the page while it was still fading in, as 1.8.2's did once) | ✅ Apple silicon and Intel |
| Graph fills | MacShots and page shots of the Overview: each card's fill eases to nothing below the line and fades in and out at its ends, with no hard top or side edge against the card's glow | ✅ |
| Mac readings window keeps its shape | Probe runs 40818384906 and 40819647738 turned readings on and off while it was shown and photographed it each time from the script (not from inside the app, which made macOS ask for Screen Recording). Before the fix the window followed every change in size; after it, the window stayed 192 × 128 points while its readings went from 94 × 43 to 116 × 60 to 112 × 77, and all 20 pictures show crisp text at its normal size. Mac verify now has a permanent *Readings window* step: it turns the window on, opens Coreloom, photographs the top-left corner and checks the log for "Readings window shown" | ✅ Apple silicon and Intel, at the runners' 1× scale |
| No outline around the overlay on Windows | The cause, found in the code: whenever the theme, accent or background changed, `ThemeService.Apply` asked Windows for round corners (and Mica) on every Coreloom window, the overlay and splash included, and Windows 11 draws a 1-pixel grey border with round corners. The overlay and splash are now left out, and both overlays (the WPF window and the layered window) tell Windows: no non-client drawing, no round corners, border color "none". Page shots photograph the overlay on the screen both ways | ⚠️ The code path is fixed, but GitHub's Windows Server machines draw no border either way (`overlay-on-screen.txt`: the same pixels with and without round corners asked for), so the outline itself couldn't be seen there |
| Website and README | The website's Mac and Windows pictures and the README's two pictures redrawn from these shots; Playwright as a Mac, a Windows and a phone visitor: the right pictures, the background swatches switch, no horizontal scrolling at 390 px, no console errors | ✅ |

### Not verified yet

- **The overlay's outline on a real Windows 11 PC.** See above: the fix removes what asked Windows for the border, but only a Windows 11 desktop can show that it's gone.
- **The Mac readings window on a Retina screen and while a game is full screen.** GitHub's Macs run at 1×; at 2× the picture macOS keeps while the window grows should still be pinned to the top-left corner.
- **The banners' light and the grain at other scales** (125 %, 150 %, 175 % on Windows) and on a real 4K or Retina display; the shots are at 2×.

## How to re-run the checks that can run anywhere

```bash
dotnet test tests/Coreloom.Core.Tests            # needs nuget.org access for the real xunit packages
dotnet test tests/Coreloom.ViewModels.Tests
python3 build/check-xaml.py
```

To see the pages' layout on Windows without a Windows PC, push to a branch named `shots-…`: the *Page shots*
workflow draws them with sample data and publishes the PNGs on the `pageshots-output` branch. For the Mac app,
`dotnet run --project tools/Coreloom.MacShots -- out` draws every Mac page on any system, and pushing a branch named
`macverify-…` builds, packages, opens and photographs the real app on GitHub's Macs (results on the
`mac-verify-arm64` and `mac-verify-x64` branches). After a release, pushing a branch named `macupdatetest-…` updates
an older Coreloom to it on both Macs (results on `mac-update-test-arm64` and `mac-update-test-x64`).

On Windows, `./build/build.ps1` builds everything and runs every test, including the Windows integration
tests against the real machine.
