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

## How to re-run the checks that can run anywhere

```bash
dotnet test tests/Coreloom.Core.Tests            # needs nuget.org access for the real xunit packages
dotnet test tests/Coreloom.ViewModels.Tests
python3 build/check-xaml.py
```

To see the pages' layout on Windows without a Windows PC, push to a branch named `shots-…`: the *Page shots*
workflow draws them with sample data and publishes the PNGs on the `pageshots-output` branch.

On Windows, `./build/build.ps1` builds everything and runs every test, including the Windows integration
tests against the real machine.
