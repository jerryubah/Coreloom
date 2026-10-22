# Architecture

Coreloom is a layered .NET 10 application. Only the bottom-right box knows about WPF, and only the Hardware
layer calls Windows sensor APIs, so most of the logic is testable on any OS.

```
┌──────────────────── Coreloom.App (WPF, net10.0-windows10.0.19041.0) ───────────────────────────┐
│ Views (XAML) · chart controls · ThemeService · tray · startup · single instance · App.xaml.cs │
└───────────────▲──────────────────────────────────────────────────────────────────────────────┘
                │ data binding (frames delivered on the UI thread, coalesced)
┌───────────────┴────────────── Coreloom.ViewModels (net10.0, no WPF types) ──────────────────────┐
│ ShellViewModel · page view models · TimelineViewModel · Metric/Chart VMs · Settings · Updates │
└───────────────▲──────────────────────────────────────────────────────────────────────────────┘
                │ TelemetryFrame (immutable) · HistoryStore · MetricCatalog · DeviceRegistry
┌───────────────┴────────────── Coreloom.Core (net10.0, platform-neutral) ────────────────────────┐
│ TelemetryEngine · ITelemetryProvider · History + M4 downsampling · AlertEngine · Settings     │
│ UnitFormatter · axis/curve math · GitHub release client · checksums · resumable downloads    │
└───────────────▲──────────────────────────────────────────────────────────────────────────────┘
                │ ITelemetryProvider implementations
┌───────────────┴────────────── Coreloom.Hardware (net10.0-windows) ──────────────────────────────┐
│ NVML (GPU) · PDH + NtQuerySystemInformation (CPU) · memory · storage IOCTLs + PDH (disks)     │
│ network statistics · SMBIOS · process sampler · LibreHardwareMonitor adapter (optional)      │
└──────────────────────────────────────────────────────────────────────────────────────────────┘
```

`Coreloom.Diagnostics` (CoreloomDiag.exe) reuses Core and Hardware without any UI.

## Telemetry pipeline

1. **Providers** implement `ITelemetryProvider`.
   - `Initialize` detects hardware and registers metrics, each with a canonical ID, a kind, a category and
     its provenance.
   - `Poll` writes values.
   - `Shutdown` releases native handles.
2. **Canonical IDs** (`MetricIds`) such as `cpu/load`, `gpu/0/temp` and `gpu/0/temp/hotspot` let several
   sources describe the same quantity. Each registration carries a **priority**: NVML 100, PDH CPU 50,
   LibreHardwareMonitor GPU 50, LibreHardwareMonitor CPU 40, LibreHardwareMonitor CPU load 10.
   - The frame takes the highest-priority source that produced a value in that cycle.
   - Every LibreHardwareMonitor sensor is also available unmerged under `lhm/...` as a "raw sensor".
3. **Provenance.** Values are `Measured` (read from a sensor or counter) or `Derived` (computed from
   measured values, for example the average effective clock or the Task Manager-style clock).
   - The UI labels derived values.
   - A metric with no source has an *unavailable reason*, such as "Requires administrator rights and the
     PawnIO driver", that the UI shows instead of a number.
4. **TelemetryEngine** runs on a dedicated background thread.
   - **Staged initialization.** Stage 0 brings up the fast providers (NVML, PDH, storage, network) first.
     LibreHardwareMonitor (stage 1) follows, so the dashboard shows data within about a second.
   - **One frame per interval.** Each cycle builds one immutable `TelemetryFrame`, appends tracked metrics to
     history and raises `FrameProduced`. The default interval is 1 s; the range is 0.25–10 s.
   - **Slow providers.** A provider with a `MinimumPollInterval` keeps its last values between polls, so
     they aren't treated as missing.
   - **Fault isolation.** A provider that throws three times in a row is marked *Faulted*, shut down and
     re-initialized with back-off (5, 10, 30, 60, then 120 s). This covers GPU driver updates and device
     resets. NVML's fatal codes (`GPU_IS_LOST`, `DRIVER_NOT_LOADED`, `RESET_REQUIRED`, …) raise errors, so
     a lost driver is detected within three polls and NVML is re-initialized. Unsupported queries are
     simply dropped. After sleep and resume, the app asks for a full re-initialization.
   - **Disabled sections** (*Settings → Monitoring*) are skipped entirely.
   - **Lighter while gaming (1.4.6).** `GamingModeService` (app, every 2 s with a few window calls) decides whether a
     game is in front (`AdaptiveMonitoring.IsGameInFront`, Core: a program other than Coreloom filling its screen, not
     the desktop or taskbar, or a library game) and calls `ShellViewModel.SetGameInFront`. With *Lighter monitoring
     while gaming* on (default), `TelemetryEngine.TemporaryInterval` slows polling to the gaming interval (2 s
     default; 1, 2 or 5 s; at most 1 s while the overlay shows readings) without resizing history, `LowPriority`
     drops the telemetry thread to below normal, `ProcessPriority.GiveWay` lowers Coreloom's process priority, and the
     shell stops page updates while the game covers the main window. The telemetry thread runs at normal priority
     (above normal before 1.4.6). `OverheadMeters` registers Coreloom's own threads (sensor reading, window and
     overlay text, the overlay's graph and frame counter) and reads their processor time (`GetThreadTimes`) for
     *Settings › Diagnostics*.
5. **Frame delivery.** `App` subscribes to `FrameProduced`.
   - Alert rules are evaluated right there, on the telemetry thread, so alerts work while the window is
     hidden.
   - The UI gets at most one queued dispatcher operation, which always reads the *latest* frame. A slow UI
     can't build a backlog.

## History and charts

- **HistoryStore** keeps one ring buffer per tracked metric.
  - Each point is 12 bytes: a monotonic timestamp (`Environment.TickCount64`, unaffected by clock changes)
    and a float value.
  - Capacity is one hour at the current interval plus slack. At 1 s that's 3,632 points, about 43 KB per
    metric, so memory is bounded no matter how long Coreloom runs.
- **Downsampling.** Long windows are reduced with M4: the first, minimum, maximum and last point per pixel
  column. This keeps spikes that plain averaging would hide.
- **Curves** use monotone cubic interpolation (Fritsch–Carlson), so smoothed lines never overshoot the data:
  0 % stays 0 %. **Axes** snap to "nice" steps, with hysteresis so they don't jitter.
- **Rendering.** `LiveLineChart` and `Sparkline` draw with WPF's retained, GPU-composited `DrawingVisual`s
  and rebuild geometry only when a frame arrives.
  - Smooth scrolling is a composited translate animation at 30 fps, used for windows of 5 minutes or less.
    It is disabled with *Reduce motion*.
  - Hovering one chart moves a synchronized crosshair across all charts that share the `TimelineViewModel`.
  - Freezing charts stops the timeline while history keeps recording.
- **Minimized or hidden.** The shell deactivates the current page, so no bindings update and nothing
  renders. Polling, history and alerts continue.

## UI layer

- **MVVM.** Page view models (`OverviewViewModel`, `GpuViewModel`, …) expose plain properties and
  `ObservableCollection`s. Views are XAML with data templates and no logic in code-behind. View models
  never reference WPF, so they're unit-tested on Linux in CI.
- **Theming.** `ThemeService` creates every brush once, unfrozen, and animates its color on theme or accent
  changes. All XAML uses `DynamicResource` for brushes. The status colors double as the gauge scale: color keys
  `Tone.Cool`, `Tone.Normal`, `Tone.Warm` and `Tone.Hot` resolve to the success (green), info (blue), elevated
  (orange) and critical (red) brushes, and `Brush.*Soft` are tinted backgrounds of the same four.
- **Gauge colors.** `Thresholds` has a third level, `Cool` (`ThresholdLevel.Cool` below it). `GaugeTone` (Core,
  unit-tested) turns thresholds into stops on a gauge (`Stops`) and a gauge fraction into a position from 0 (green) to
  3 (red) with short blends at each boundary (`At`). `RadialGauge` with `ToneScale` colors its arc from the
  *animated* value, so the color glides with the arc; `MetricViewModel.GaugeCoolAt/WarmAt/HotAt` feed it. For
  ratings where more is better (speeds), the view model sets `Tone` directly and the gauge animates between colors.
  Usage donuts (memory, volumes) recolor their "in use" segment with `ColorKeys.ForLevel`. Mica uses `DWMWA_SYSTEMBACKDROP_TYPE` on Windows 11
  22H2 and later. The custom caption buttons support Snap Layouts by answering `WM_NCHITTEST` with
  `HTMAXBUTTON`.
- **Cards in a row end on one line (1.4.7, 1.4.8).** Cards side by side share one bottom line, and the difference is
  taken by content that can use it rather than by empty space: `FitRows` (the process lists on the Processor and
  Memory pages) asks for its usual rows and shows more when its card is taller; a disk card's chart sits in a star row
  and grows; the Frame limiter's pacing row and the shader cache list sit at the bottom of their cards; the Memory
  tiles share the donut card's height (`StretchRows`); in Smart Optimize's masonry (`StackedColumns` with
  `FillColumns`) the lower card of the shorter column (*Speed up*, whose startup list grows, or *Free up memory*, whose
  gauge stays centred) is marked `CanFill`. The Performance boosts are an `AdaptiveGrid` whose rows are each as tall
  as their longest card, with `FillLastRow` so an unfull last row spans the grid. Copy is kept short so the stretch is
  small. Grids of like tiles (gauges, metric tiles, charts) keep a uniform row height.
- **Page shots.** `tools/Coreloom.PageShots` (not part of the app) loads the app's resource dictionaries and theme
  into a plain WPF application, builds the Gaming, Memory, Processor, Graphics, Smart Optimize and Storage pages over
  sample data (a 9950X3D/RTX 5090 PC, two DIMMs, two NVMe drives), lays each out at 1600 DIP (and the Gaming page at
  1000) with the main window's text settings, and renders it at 125 % to PNG. `.github/workflows/pageshots.yml` runs
  it on Windows for pushes to `shots-*` branches and publishes the pictures as an artifact and on the
  `pageshots-output` branch, so layout can be checked without a Windows PC.
- **Settings** are JSON in `%APPDATA%\Coreloom\settings.json`, or `Data\` in portable mode.
  - Writes are atomic: write a temp file, then replace.
  - Writes are debounced by 600 ms.
  - A corrupt file is kept as `settings.json.bad` and defaults are used.

## Design system (1.4.1, 1.4.2)

- **Squircles.** `Squircle.Create` (Controls/SquircleBorder.cs) builds Figma's corner smoothing (figma-squircle,
  "preserve smoothing"): with smoothing ξ (0.6, Apple's) a corner starts (1 + ξ)·r from the vertex, a circular arc of
  90°·(1 − ξ) sits in the middle and two cubic Béziers lead into it; when the side is too short the straight lead-in is
  shortened, never the arc. `SquircleBorder` derives from `Border` (so the `Card`/`Card.Inset` styles and every
  `Border` template still apply) and replaces `OnRender`: shadow (a few widening translucent outlines, no blur effect),
  background, the glow, border, lit edge and sheen, all on the squircle; `ClipContent` clips children through
  `GetLayoutClip`. Every card and rounded container in the XAML, and the button, field, menu, tooltip, segmented and
  navigation templates, are `c:SquircleBorder`.
- **Glow cards.** `GlowKey` names a color (a series such as `Gpu`, a status such as `Success`, a temperature such as
  `Temp.Cool`, `Accent`); the card is lit from below by a radial gradient clipped to its shape and its lower rim
  brightens. When the key changes the color fades over 450 ms (the last drawn color eased toward the new one on
  `CompositionTarget.Rendering`). `Card.Glow` adds `EdgeGlow`: the pointer's position (eased toward the pointer each
  frame, only while hovered or fading) centres a radial gradient in the glow color itself (since 1.4.2, no second
  shade), stroked along the edge in four widths up to 28 px and clipped to the outside of the card (a combined geometry:
  the area around it minus the squircle), so it glows outward; inside, a faint 6 px rim and a bright 1.3 px line. It
  brightens as the pointer nears the edge. `HitTestCore` only answers inside the card's rectangle, so neither the glow
  nor a shadow takes the pointer.
- **Temperatures (1.4.2).** `MetricViewModel.IsTemperature` (its style is Temperature) and `GlowKey`
  (`ColorKeys.ForTemperature(Level)`: `Temp.Cool` blue, `Temp.Normal` teal-green, `Temp.Warm` amber-orange, `Temp.Hot`
  red; otherwise the series `ColorKey`). The palette has each band's glow color and a `.Tail`/`.Head` pair;
  `RadialGauge.TemperatureScale` colors the lit ticks from tail to head along the lit part, the pair blended between
  bands at the scale position (`GaugeTone.At`), and the head, orb and halo in the head color. The gauge cards and the
  dashboard's summary cards bind their glow, gauge, sparkline and icon to it.
- **Palette and glass.** The dark palette is near-black (`Background` #0A0B0E, cards #15171C). `ThemeService` publishes
  the sidebar's glass (`Brush.GlassFill`, `Brush.GlassRim`, `Brush.GlassLightA/B`) from the accent; the sidebar column is
  the backdrop (Mica shows through it when on), the pages sit on an opaque layer, and the sidebar's text switches to
  grayscale smoothing under Mica (ClearType can't be blended onto see-through pixels).
- **Gauges.** `RadialGauge.Look` (Auto: ticks from 40 px, an arc for spinners and comets) draws a tick ring: unlit
  ticks in one geometry, lit ones per tick with alpha rising toward the head, the last ticks longer and lighter, a
  radial halo at the head, and a partial head tick so values glide. `Orb` adds a sphere of light inside.
- **Controls.** The liquid toggle (`ToggleSwitch`): track cross-fade, knob slide with `ElasticEase`, and on press a
  scale-up with the white cover fading to a glass lens. `QuickSearch` (Controls/QuickSearch.cs + implicit style): a
  borderless `TextBox` in a squircle field and a `Popup` list of `QuickSearchRow`s (hint, *Show all matches*, results
  from the page's `SearchSuggestions`, ranked by `SearchSuggestions.Rank`), Up/Down/Enter, picks on press, Ctrl+K/F via
  the window's `PreviewKeyDown`. `SmoothScroll` (attached to the page scroll styles) takes the wheel in
  `PreviewMouseWheel` at the innermost scroll area that can still move, eases the offset toward a target each frame
  (exponential, ~165 ms) and stops when something else scrolls. Over an open dropdown (the walk up from the pointer
  ends at a popup's root without reaching the page, or passes a `ComboBox` with its list open, which holds the mouse)
  it marks the wheel handled so the page stays put; the list's own scroll area still takes it when it can move.
- **Dropdowns (1.4.2).** `PopupAnchor.Align` (Start or Center) on a template's `Popup` switches it to custom placement:
  WPF passes the panel's and the control's sizes in screen pixels and moves the popup window by the returned point from
  the control's top-left, so the callback places the panel (inside its shadow margin) under the control, aligned or
  centered, with the gap scaled to the DPI, and above it as the fallback. Built-in `Bottom` placement follows Windows'
  right-handed menu setting (right edges aligned) and lines up the panel itself, not its margin. Open anchored popups
  are tracked; when a `SmoothScroll` page scrolls, each one on it is placed again (an offset nudge) or, once its control
  is out of view, closed through its owner (`IsDropDownOpen`).
- **Text.** Large text uses `TextFormattingMode.Ideal` (Display mode distorts big glyphs); headline numbers are Inter
  Bold with tabular digits.
- **Icons.** `build/dark-icon.py` makes the dark-mode icon from the approved one (the mark's shape found by colour with
  thin highlights closed in, its outline anti-aliased from the original, put on a navy glass tile with a blue rim) into
  `assets/branding/dark`. `ThemeService` publishes `Image.AppIcon`/`Image.AppIconLarge` and raises `AppIconChanged`, on
  which the app swaps the window and tray icons (`AppIcons`) and, since 1.4.2, its shortcuts' icons
  (`ShortcutIconService`): Windows draws the Start menu entry, desktop shortcut and a pinned taskbar button from the
  shortcut, not the window. `coreloom-dark.ico` ships next to Coreloom.exe; in the background the service opens the
  `.lnk` files named for Coreloom in the user's Start menu (and a Coreloom folder there), desktop and *User Pinned*
  (TaskBar, StartMenu) folders through the Shell's `ShellLink` (Hardware/Native/ShellLink.cs), and for those that start
  this exe sets the icon by `ShortcutIcons.Desired` (Core: dark icon over the usual one in the dark theme, the exe's icon
  again in the light theme, a custom icon left alone), then `SHChangeNotify` (each file, then the icon association) so
  Explorer redraws them.

## Overview header

`SystemSummaryViewModel` (owned by `OverviewViewModel`) holds the board name (title), the graphics card (subtitle), two
columns of specs and what the PC picture needs: the card's model badge, the pump-screen text, the lighting color
(`PcGlow`, saved in `Dashboard.PcGlow` and picked in the Customize card) and the fans' speeds. `SystemDescriptions` (Core, unit-tested) turns raw SMBIOS
and driver strings into readable ones: board vendor names ("ASUSTeK COMPUTER INC." → "ASUS"), the memory line, the
model badge and whether the card is discrete (integrated graphics get no badge).

**The picture** is a render (`assets/pc-render/render.png`) split into layers by `build/pc-render.py`, which also
writes `Views/PcIllustration.xaml`; edit the script and regenerate rather than editing the XAML. The layers
(`src/Coreloom.App/Assets/PcRender`, ~670 KB) are:

- `base.png`: the render with its glow taken out. Each pixel is split by least squares into a neutral base and an
  amount of the render's blue; `glow.png` holds that amount as alpha. The app draws a rectangle in the chosen color
  masked by it, so Blue reproduces the render and the other colors relight it (fans, RAM, pump ring, the light
  spilling onto the glass and mesh). Rainbow animates the color around a wheel (14 s, 30 fps); Accent uses
  `Brush.Accent`.
- `blades-<fan>.png` (and `-blur`): each fan's disc unrolled to polar coordinates; its lowest angular harmonics are
  the lighting, which stays in `base.png`, and the rest is the blades, stored as black/white plus alpha on a circle.
  The app turns each circle and squashes it back into the fan's ellipse; the blurred copy fades in with speed.
- The pump screen's and card's text is painted out; the app draws the live values there (Inter, sized from the
  render's lettering).

`PcFans` maps the motherboard's fan readings (`board/fan/…`) onto the picture: the top fans follow the fans named for
the CPU, the bottom ones the other case fans; pumps are left out and headers reading 0 RPM are treated as empty.
Without names, or without readings (no PawnIO), all six follow the average or idle at 0.6 turns a second. The picture
turns at a thousandth of the real speed (1,200 RPM → 1.2 turns a second, capped at 3) so it never strobes. Each fan
has its own additive `DoubleAnimation` clock (60 fps); speed changes go through `ClockController.SpeedRatio`, so there
are no jumps. Clocks exist only while the picture is visible (not on another page, minimized, in the tray, or with
Reduce motion on); otherwise they are removed and the blades keep their angle, so a hidden Overview renders nothing.

`pc.png`/`pc.jpg` in the settings folder, or `assets/pc/pc.png` embedded at build time, replaces the picture
(`PcImages`, decoded once at most 800 px wide); the lighting color doesn't apply to it.

**Customize** is a card over the Overview (`DashboardEditorViewModel`, owned by `OverviewViewModel`): the lighting
color, the min/max/avg switch, and which cards and panels show in what order. Every change goes through
`SettingsService.Update("Dashboard", …)`, so the shell's settings fan-out re-lays out the dashboard behind the card
immediately. It closes with its X, *Done*, Esc or a click on the scrim (`Brush.Scrim`). Settings no longer has a
Dashboard section. It also holds `Dashboard.ShowPcPicture` (on by default), which hides the header's picture.

## Dashboard grid

The Overview's summary cards and panels share one grid. `OverviewViewModel.Tiles` holds a `DashboardTileViewModel`
per visible card (its content, `Order`, `ColumnSpan`, `RowSpan` and limits); the collection only gains and loses
cards, and the panel orders them by `Order`, so moving a card never rebuilds the others' templates (their charts keep
running). Sizes are saved per card in `Dashboard.Cards[].Columns`/`Rows`, only when they differ from the default
(`DashboardCatalog.SizeOf`: cards 1 × 2, panels 2 × 3, at most 6 × 8).

`DashboardLayout` (Core, unit-tested) is the math:

- the column count for a width (6, 4, 2 or 1: even counts keep two-column panels side by side);
- placement in order, left to right and top to bottom, each card after the one before it;
- the row height (the smallest that fits every card at its minimum rows, so about half a summary card);
- where a dragged card lands: the place in the order whose position is nearest its top-left corner, with
  hysteresis so it doesn't flicker;
- snapping a resize to whole cells.

`DashboardGrid` (App) measures each card at its width to set the row height, then at its final size; this is why
the storage list sits in a `HeightCap`. It arranges the cards and glides them to new places with a translate
animation (none with Reduce motion or when only the window size changed). Dragging starts after the system drag
distance from anywhere in a card that isn't a control (buttons, toggles and scroll bars keep working), or from the
grip (`DashboardGrid.Handle="Move"`); the corner (`Handle="Resize"`) resizes. During a drag the panel holds the
mouse capture, draws a dashed outline of the target (`OnRender`), auto-scrolls the page near its edges and cancels on
Esc or lost capture. On release it calls `MoveTileCommand` or `ResizeTileCommand`; the bound order and sizes then
match the preview, so nothing jumps. Each card's right-click menu runs the same resize commands one step at a time.

Pages that scroll at the window's right edge use `ScrollViewer.Window`: a 24 px bar laid over the page's right
margin whose right 6 px stay clear of the window's resize border (`ResizeBorderThickness`). `ScrollBarHover` widens
its thumb from 8 to 14 px while the pointer is on the bar and narrows it again when the wheel is used.

## Health

`HealthMonitor` (Core, unit-tested) keeps the last 60 readings of each input it needs: the selected GPU's temperature,
hot spot, memory junction, driver slowdown temperature, NVML clock event reasons and fan; the CPU temperature and fan
(the board fans `PcFans` classifies as CPU/AIO/radiator); memory load and commit charge; the fullest volume; drive
wear, spare and temperature. `EvaluateGpu`, `EvaluateCpu`, `EvaluateMemory` (load, commit charge, module speed for
information) and `EvaluateStorage` (space, wear, drive temperature) turn them into a `HealthReport` of `HealthCheck`s:
any Problem makes the status *Needs attention*, any Notice or unreadable check *Good*, otherwise *Excellent*.
Temperature levels follow the user's alert rules (as on the Overview cards). Thermal slowdown counts from 25 % of the
minute (a problem from 75 %); hardware slowdown or power brake from 5 %; power-limit slowdowns are normal and ignored.

`SystemHealthViewModel` (one per app, `PageContext.Health`) is fed every frame by the shell, also while the window is
hidden, and updates its `HealthReportViewModel`s in place. The Graphics, Processor, Memory and Storage pages show
their part with the `HealthCard` template; the Overview's `health` dashboard card (`HealthPanelViewModel`, 6 × 2 by
default, placed after the summary cards; older layouts get it there through `DashboardCatalog.Normalize`) shows all
four.

## Drive and internet speed tests

Both engines are in Core (`Benchmarks/`) and unit-tested; the view models reach them through `IDiskBenchmarkService`
and `ISpeedTestService` (`PageContext.Tools`), so tests swap in fakes.

- `DiskBenchmark` writes a temporary file (256 MB, 1 GB or 4 GB) in the chosen drive's test folder (the temp folder on
  the system drive, `X:\Coreloom benchmark` elsewhere), opened with `FILE_FLAG_NO_BUFFERING` (so the drive is measured,
  not the file cache) and `DeleteOnClose` (so it disappears even if the app is closed mid-run). It checks free space
  first, writes the file once, then runs CrystalDiskMark's usual tests for up to 4 s each with async
  `RandomAccess` I/O on 4 KiB-aligned pinned buffers: sequential 1 MiB with 8 requests in flight (at most two passes,
  to limit wear), random 4 KiB with 32 and with 1. `SpeedRating` colors results by test type (NVMe-class green,
  SATA-SSD-class blue, hard-drive-class orange or red) on a square-root gauge scale.
- `InternetSpeedTest` uses Cloudflare's speed test endpoints (`/meta`, `/__down?bytes=`, `/__up`): 12 latency
  samples after a warm-up (median and jitter, minus the server's own time from `Server-Timing`), then 10 s of
  download and 10 s of upload over six HTTP/1.1 connections, counting bytes as they arrive or are sent and leaving
  out the first 20 % while connections ramp up. The live reading is the last second.

## Benchmarking (1.5.0)

The engines are in Core (`Benchmarks/CpuBenchmark.cs`, `StressTest.cs`, `BenchmarkResults.cs`, `BenchmarkTuning.cs`,
`GpuTests.cs`) and Hardware (`Gpu/Direct3D.cs`, `Gpu/GpuBenchmark.cs`); the page reaches them through
`IHardwareBenchmarkService` (`PageContext.Tools.Benchmarks`), whose portable default runs the CPU parts and has no
GPU (tests swap in fakes), and whose Windows implementation (`App/Services/BenchmarkService.cs`) adds Direct3D and the
results file.

- **CPU benchmark.** Eight `CpuTest`s, each with a deterministic `CpuWorker` per thread (its own data from a seeded
  xorshift generator, so every PC does the same work): Deflate compression and decompression of 1 MB of generated
  text, tokenizing and counting the words of 1 MB in an open-addressing hash table plus phrase searches, SHA-256 and
  AES-GCM over 1 MB, Dijkstra on a 256 × 256 weighted grid, a double-precision ray tracer (8 spheres, a floor, shadows,
  three bounces) at 320 × 180, a 384 × 384 FP32 matrix multiply with `Vector<T>` (four rows of B per pass), a
  13-tap separable Gaussian blur of a 1280 × 720 float image with `Vector<T>`, and an N-body step of 1,024 bodies in
  FP64 (reset each run). `CpuBenchmark.Measure` runs a test on N dedicated threads that warm up, meet at a `Barrier`
  and then count iterations for a fixed time; single-core runs at normal priority on one thread, multi-core at below
  normal on one thread per logical processor. A test's score is its rate ÷ its reference × 1,000; the single- and
  multi-core scores are geometric means. The references are the single-thread rates of the 2.8 GHz Xeon core Coreloom
  is developed on (so that core scores 1,000 per test); a GitHub Windows runner's Ice Lake core scores about 1,700.
  Memory bandwidth (every thread copying its own 8 MB back and forth) and latency (a dependent random walk through 64 MB,
  a single Sattolo cycle) are measured for the details only.
- **GPU benchmark.** `Direct3D` is a minimal Direct3D 11 wrapper called through the COM function tables with function
  pointers (no wrapper library): DXGI adapter listing (`EnumAdapters1`/`GetDesc1`, software adapters left out),
  `D3D11CreateDevice` on the chosen adapter (feature level 11.1 or 11.0, single-threaded), HLSL compiled at run time with
  Windows' `d3dcompiler_47.dll`, structured buffers with UAVs, an off-screen RGBA8 render target, constant buffers, an
  event query to wait for the GPU, and read-backs through staging resources. The workloads: *Lighting* (a full-screen
  pixel shader, a wave surface lit by 64 moving lights, 2560 × 1440), *Ray marching* (a signed-distance scene of six
  sphere/box blends with soft shadows and ambient occlusion, up to 96 steps, 1920 × 1080), *Compute* (a million threads
  of four dependent FP32 multiply-add chains, 256 FLOP per loop, 256 loops), *Memory bandwidth* (a 128 MB uint4 copy)
  and *Physics* (an N-body step of 32,768 bodies with 64-body groupshared tiles). Each is batched to about 30 ms per
  wait (so no submission runs long), warmed up, then timed for 3 s. A GPU test scores 10,000 points at its reference
  rate (`BenchmarkScale.Gpu`, since 1.5.1; 1,000 in 1.5.0), so high-end cards score in the hundreds of thousands like
  Geekbench's OpenCL scores. References are estimates for a ~2.5 TFLOPS, ~110 GB/s card, worked out from each
  shader's instruction count, not measured on such a card. Every workload's
  answer (a checksum of the frame or the buffer) is the same on every run, which the stress test relies on. Nothing is
  presented on screen and no game is touched.
- **Results.** `BenchmarkResult` holds the kind, score and parts, every test's rate and points, the readings during
  the run (`ReadingRecorder`: lowest, average and highest per sensor, from the telemetry engine's latest frame each
  second), the tuning notes, the PC (`BenchmarkSystem`) and, for stress tests, the loops, errors and verdict.
  Each result records its scale (`PointsAtReference`); `BenchmarkScale.Upgrade` multiplies the points of GPU results
  saved by 1.5.0 (no scale recorded) by ten as they're loaded, rates untouched. The page's Processor and Graphics card
  cards show the latest result of their kind; the Best badge is only in the results list.
  `BenchmarkHistory` keeps them newest first, picks each kind's best (highest score, earliest on a tie; stress tests
  are never best) and trims to 200 while keeping the bests; `BenchmarkStore` saves `benchmarks.json` in the settings
  folder atomically and keeps an unreadable file as `.bad`.
- **Tuning notes** (`BenchmarkTuning`) only interpret readings: the fastest core clock, the average % Processor
  Performance as boost, PBO from `AmdTuning.Assess` over the run's package power (PPT when the SMU reports it) against
  AMD's stock limit, peak power, core voltage, the lowest % Performance Limit (held back), the hottest reading; for the
  graphics card its peak core and memory clocks against NVML's rated maximums, the power limit against the default
  (raised, default or lowered, in percent), peak power, voltage, the NVML clock-limit reasons seen, and the hottest
  core and hot-spot readings.
- **Stress test.** `IStressLoad` loads work flat out and check answers: `CpuStressLoad` alternates the matrix and ray
  tracing workers on every logical processor and compares each answer with the one worked out before the start;
  `GpuStressLoad` alternates batches of ray-marched frames and N-body steps on its own thread and re-checks both
  answers every 5 s; a driver reset ends it. The page samples every second: `StressLoops` turns the work count into
  10-second loops (5 s for runs under five minutes), `StressGuard` stops at the processor at 100 °C or the graphics
  core at 95 °C for 5 s, and the first calculation error stops the run. `StressScoring` judges it like 3DMark: stability
  is the slowest loop over the fastest, and it passes only when it ran to the end with no errors, two or more loops and
  97 % or more.

## Devices

`DevicesViewModel` (the *Devices* page, below Storage) reaches Windows through `IDeviceService` (`PageContext.Tools`;
`UnsupportedDeviceService` elsewhere, fakes in tests). It refreshes Bluetooth every 5 s and wired devices every 15 s
while the page is open (driven by the frames it receives), and right away when the service raises `Changed`
(marshalled to the UI thread, coalesced over 350 ms). Cards are updated in place and reordered with moves, so nothing
flickers or replays its animation. Core holds the testable parts: `DeviceKinds` (type from the Bluetooth Class of
Device, the LE GAP Appearance or the name; labels, icons, groups), `BluetoothDeviceList.Merge` (one card per container,
so dual-mode devices show once; connected first), `WiredDevices.Build` and `BatteryLevels`.

The app targets `net10.0-windows10.0.19041.0` for the Windows SDK projection; `Microsoft.Windows.SDK.NET.dll` is kept
out of ReadyToRun to keep downloads small. `WindowsDeviceService`:

- **Bluetooth devices**: `DeviceInformation.FindAllAsync` with `BluetoothDevice`/`BluetoothLEDevice.GetDeviceSelectorFromPairingState(true)`,
  then one cached `BluetoothDevice`/`BluetoothLEDevice` per device for the name, `ConnectionStatus` (and its
  `ConnectionStatusChanged` event), `ClassOfDevice` or `Appearance`. The container and battery come from the device tree:
  Bluetooth device nodes carry the address in their instance id, and Windows records the battery of headsets and other
  reporting devices in property `{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2`; LE devices without it are read from the GATT
  Battery Service (at most every 2 minutes). `BluetoothGetDeviceInfo` gives Classic devices' last-used time.
- **Connect**: for Classic devices the Bluetooth audio drivers' kernel-streaming filters are found (KSCATEGORY_AUDIO
  interfaces under BTHENUM/BTHHFENUM whose path has the address or whose node shares the container) and sent
  `KSPROPERTY_ONESHOT_RECONNECT` (KSPROPSETID_BtAudio), the request behind Windows' own Connect button; otherwise the
  device is paged (uncached SDP query). LE devices get a `GattSession` with `MaintainConnection` and an uncached GATT
  query. Either way the service waits up to 8–15 s for `ConnectionStatus` to change.
- **Disconnect**: `KSPROPERTY_ONESHOT_DISCONNECT` for audio, else `IOCTL_BTH_DISCONNECT_DEVICE` on the radio.
- **Adapter**: `Radio.GetRadiosAsync`/`SetStateAsync` and `StateChanged`; looked up again every 30 s while none is found.
- **Wired devices**: every present node from `CM_Get_Device_ID_List`, with container, class, names, parent, problem
  code and `DN_REMOVABLE` (nodes inside the PC are skipped after reading their container). `WiredDevices.Build` keeps
  containers other than the PC's own that have a USB, HID, display, SCSI or PCI node and no Bluetooth node, names them
  from the USB product string or the most telling readable node, and leaves out hubs, USB host controllers and PCIe
  bridges on their own. "Controller" in a name only makes a game controller when the name or the device tree says so
  (an Xbox driver, a HID game controller); other HID-only devices are *Accessory*, lights go under *Lighting*.
- **Layout**: Bluetooth cards sit in an `AdaptiveGrid` and wired groups in `StackedColumns`, both with 270–330 px
  columns, so the two sections share one grid; the column math (`CardColumns.Fit`) and the stacking into the shortest
  column (`CardColumns.Stack`) live in Core and are unit-tested.
- **Eject**: `CM_Request_Device_EjectW` on the removable node with a veto buffer (so Windows shows no dialog); vetoes
  become "an app is using it".

Struct sizes are checked against the Windows SDK (BLUETOOTH_DEVICE_INFO 560 bytes, KSPROPERTY 24, DEVPROPKEY 20) before
use (`DeviceNative.LayoutIsValid`). Events log under "Devices".

## Smart Optimize

`OptimizeViewModel` (the *Smart Optimize* page, above Performance History) runs Smart Scan through four parts, each
also a card of its own (laid out by `StackedColumns` with `InOrder` and `FillColumns`, so each card is as tall as its
content, and only *Speed up*, marked `CanFill`, grows to line the column bottoms up): `CleanupViewModel`, `ProtectionViewModel`, `MemoryOptimizeViewModel` and `SpeedUpViewModel`.
Everything Windows-specific is behind `IOptimizeService`; the app's `WindowsOptimizeService` implements it and the
default `UnsupportedOptimizeService` makes the page explain itself where it can't work.

- **Cleanup.** `WindowsJunk.Categories` lists the folders (Core); `JunkCleaner` measures and deletes. It never follows
  reparse points (a junction could lead anywhere), checks every file is under its root, keeps temp files younger than
  24 hours and Windows Update downloads younger than 10 days, skips read-only and in-use files, and removes only empty
  folders that are old enough. Shader caches, the thumbnail cache, Update downloads and the Recycle Bin are off by
  default and say why. The Recycle Bin goes through `SHQueryRecycleBin`/`SHEmptyRecycleBin`.
- **Protection.** Microsoft Defender's own tools: `Get-MpComputerStatus`, `Get-MpThreat` and Security Center's
  antivirus list through a PowerShell script (`DefenderScripts.Status`, run with `-EncodedCommand`, UTF-8 JSON parsed
  by `DefenderScripts.ParseStatus`), `MpCmdRun -Scan -ScanType 1` for a quick scan (cancelled with `-Scan -Cancel`),
  `MpCmdRun -SignatureUpdate`, and `Remove-MpThreat`. When another antivirus is in charge it is named and Defender's
  actions are disabled.
- **Memory.** `K32EmptyWorkingSet` on every process it may open except the foreground app, Coreloom and Windows' core
  processes; optionally the standby list is purged (`NtSetSystemInformation`, `SeProfileSingleProcessPrivilege`).
  The result is available memory after a short settle minus before. Since 1.4.6 each process is a `MemoryTrimEntry`
  (Core, `MemoryTrim`): trimmed (working set before and after from `K32GetProcessMemoryInfo`, and what was
  reclaimed), skipped (with why, decided before the process is opened) or failed (the Windows error in words); nothing
  is ever ended, so "ended" is always 0. Every entry is logged as `Memory: …` under *Optimize*, with a summary line;
  the memory card lists them with a trimmed/skipped/failed filter and *Open logs*.
- **Logs (1.4.6).** `LogFile` (Core) reads Coreloom's rolling log files (shared read, newest 20,000 lines) back into
  `LogRecord`s; `LogViewerViewModel` (*Settings › Logs*) filters by text, lowest level and category, shows the newest
  1,500, and copies or opens the folder. `PageContext.OpenLogs` lets a page open it pre-filtered.
- **Speed.** Startup apps from the Run keys (user, machine, 32-bit) and both Startup folders, switched off and on with
  the same `StartupApproved` values Task Manager writes (`StartupApproved`, Core). Maintenance runs
  `DnsFlushResolverCache` and `defrag.exe /C /O /H`.

## In-game overlay

Settings live in `Overlay` (`OverlaySettings`: on/off, hotkey as a virtual-key code with modifiers, shown items from
`OverlayCatalog`, position, layout, text and background look). `OverlayViewModel` (`PageContext.Overlay`) groups the
shown readings into lines (FPS, GPU, CPU, RAM), leaves out what the PC doesn't report, formats them with the usual
`MetricViewModel`s and tints them at warning levels. It is fed every frame by the shell and ticked twice a second by
the app for frame rates. `OverlaySettingsViewModel` is the Graphics page's settings card; its preview renders the same
`OverlayContent` template as the overlay window.

In the app, `OverlayService` owns the pieces:

- `OverlayWindow`: a topmost, layered, transparent window with `WS_EX_TRANSPARENT` (clicks pass through),
  `WS_EX_NOACTIVATE` and `WS_EX_TOOLWINDOW` (no focus, no taskbar or Alt+Tab). Every tick it is placed on the monitor
  of the foreground window (`MonitorFromWindow`, per-monitor DPI for the margin) and re-asserted topmost every 2 s.
- `EtwFrameRateSource`: a real-time ETW session ("Coreloom-FrameRate", performance-counter timestamps, one set of
  buffers, 100 ms flush) with the Microsoft-Windows-DXGI (Present start, events 42 and 55) and Microsoft-Windows-D3D9
  (event 1) providers. A background thread runs `ProcessTrace`; the callback hands each present's process id and
  timestamp to `FrameRateTracker` (Core, unit-tested), which keeps 4096 frames per process and gives FPS over the
  second up to the latest frame, the average frame time and the 1% low over ten seconds. It is shared with the
  Gaming page and runs only while someone holds a lease (`Acquire`/`Release` by owner: the overlay while it is on and
  shows a frame-rate item, the Gaming page while it is open and a library game runs); a stale session of the same
  name is stopped first. Struct layouts are
  checked against the Windows SDK sizes before starting (`EtwNative.LayoutIsValid`).
- `GlobalHotkey`: a `WH_KEYBOARD_LL` hook on its own thread with a message loop, so a busy UI never delays typing.
  It never swallows keys (unlike `RegisterHotKey`), ignores auto-repeat, matches modifiers exactly, pauses while the
  settings card captures a new hotkey, and is only installed once the overlay has been turned on
  (`OverlaySettings.ActiveHotkey`).

The foreground process comes from `GetForegroundWindow`; Coreloom itself shows no frame rate.

Since 1.4.1 the overlay is a squircle of smoked glass (no outline since 1.4.3), the FPS value is the headline, and the
FPS line carries the frame-time graph (`OverlayCatalog.FrameGraph`, offered once to existing setups through
`OverlaySettings.Revision`). Up to 1.4.4 it also carried a green mark with the frame limit while
`GamingSettings.ActiveFrameCap(program)` was above zero for the foreground program; 1.4.5 removed it (the limit is still
the graph's dashed target line, from the same value). The text shadow is set on the text only.

**The frame-time graph (1.4.3).** `FrameGraphModel` (Core, unit-tested) reads `IFrameTimeFeed` (over
`FrameRateTracker.RecentFrameTimes`, which also gives how long ago the newest frame was shown) and places each frame at
the moment it was shown across one second, the graph trailing real time by a delay that follows the lateness of ETW's
50 ms batches (rising at once, easing back), with an eased scale; `FrameGraphRaster` (Core, unit-tested) draws it into
premultiplied BGRA pixels with its own anti-aliasing: baseline (a clearer flat line, `IdleAlpha`, while there are no
frames, so the graph's place always shows), fading fill, line (per-pixel maximum coverage so joins
don't double), stutter marks (over 1.6× the median, red over 2.5×) and the dashed limiter target. The overlay window is
drawn by WPF in software (`RenderMode.SoftwareOnly`, so it never queues work on the graphics card), and a software WPF
target isn't vsync-paced: WPF schedules its frames with a 10 ms `DispatcherTimer`, which fires on the 15.6 ms system
tick, so a graph drawn by WPF stepped at ≤ 64 uneven frames a second. So in the overlay the `FrameGraph` element only
holds the place, and `FrameGraphLayer` draws the graph from its own below-normal thread into a `LayeredSurface`
(Hardware/Native: a click-through per-pixel-alpha popup owned by the overlay window, placed by the UI thread with
`UpdateLayeredWindow` giving position and size, filled from the drawing thread with `UpdateLayeredWindow` naming the
position and size it already has, which never waits on the UI thread; an update that leaves them out is accepted but
shows nothing, which is why 1.4.3 and 1.4.4 showed no graph, found by `OverlayVisibilityTests` reading the screen on
the Windows runner), woken by a `PreciseTimer` (a `CREATE_WAITABLE_TIMER_HIGH_RESOLUTION` waitable timer; where its short waits keep waking
late on average, as on one CI runner, it holds `timeBeginPeriod(1)` while doing short waits) at
`FrameGraphModel.RedrawRate` (the display's refresh, or an even fraction ≤ 144 Hz: 120 on 240 Hz) while frames come
in and 4 times a second otherwise. The settings preview (not `FrameGraph.IsLive`) draws the same pixels into a
`WriteableBitmap`, with a still sample trace.

**Where it sits and how big it is (1.4.7).** `OverlaySettings` keeps the place as one of nine anchors (`Center` added)
plus `OffsetX`/`OffsetY` in pixels at 100 % scaling, optionally one `OverlayPlacement` per game program
(`PerGamePosition`, `GamePlacements`, newest per program, at most 200), and the size as `Size` (Compact 0.85, Standard
1, Expanded 1.25, or `Custom` with `Scale` 50–250 %) with `AutoScale` following the monitor's height (1080 DIP = 100 %,
0.85–1.35, `OverlayPlacements.ScreenScale`) and `GroupSizes` for one line at a time (S/M/L). `OverlayPlacements`
(Core, unit-tested) locates the window from anchor, margin and offset, clamped to the monitor's work area, and turns a
drop point back into the nearest anchor (by thirds) plus the offset (`FromDrop`). `OverlayViewModel.Factor` is the size
× the screen scale; every font size, graph size, spacing and the corner radius derive from it, and each line's group
multiplies its own. *Move on screen* (or Shift + the hotkey, `GlobalHotkey`'s second action) sets `IsMoving`: the
window drops `WS_EX_TRANSPARENT`, shows a dashed outline, a resize grip (a `Thumb` working from screen deltas over the
DPI so it doesn't feed back on itself), the S/M/L button per line and *Done*; `DragMove` moves it and `SaveAdjusted`
stores where it was dropped, for the game last in front when per-game places are on. Leaving move mode stores a resized
scale as `Custom`.

**Staying out of the game's way (1.4.3).** Besides the software-drawn window and the graph thread: `OverlayService`
claims the top again only when a visible topmost window of another program overlaps the overlay (`GetWindow`
`GW_HWNDPREV` walk) rather than every 2 s, and while the overlay is up and another program is in front it sets
Coreloom's priority class to below normal (back to normal when Coreloom is in front or the overlay is off). The ETW
consumer thread runs below normal.

## Gaming

`GamingViewModel` (the *Gaming* page, above Graphics) has four parts; everything Windows-specific is behind
`IGamingService` (`WindowsGamingService` in the app, `UnsupportedGamingService` elsewhere). Settings live in `Gaming`
(`GamingSettings`: list or grid, sort order, the frame limiter's target, the power plans to restore, the last driver
version seen; `DownloadArtwork` is only kept for older settings files).

- **Library.** Core parses each launcher's own files, unit-tested with samples: Valve's text KeyValues
  (`KeyValues`) for Steam's `libraryfolders.vdf` and `appmanifest_*.acf` (fully installed games only, tools such as
  redistributables, Proton and dedicated servers left out; `LastPlayed` and `SizeOnDisk` read), Epic's
  `Manifests\*.item` JSON (DLC, engines and unfinished installs left out) and its base64 `catcache.bin` for cover
  links, GOG's `goggame-*.info` play tasks, and Xbox's `MicrosoftGame.config`. Ubisoft Connect, EA app, Battle.net and
  Riot games are recognised from Windows' Uninstall keys (`LauncherGames`). The app adds GOG's registry list and Xbox
  packages from `PackageManager.FindPackagesForUser` that carry a `MicrosoftGame.config`. Pictures: Steam's library
  cache (`appcache\librarycache`, both the old `{appid}_library_600x900.jpg` names and the newer per-game folders),
  Xbox's package logos and splash, covers downloaded earlier, else the program's icon saved as a 256 px PNG
  (`Icon.ExtractIcon`). Since 1.4.4 every game without a cover or banner gets one looked up automatically after each
  scan (`WindowsGamingService.DownloadArtworkAsync`, saved to `%LOCALAPPDATA%\Coreloom\Artwork`): the store's own
  catalog pictures (Epic), else Steam's, for the game's own Steam app or the Steam app with the same name
  (`SteamStore`, Core, unit-tested: the store search `store.steampowered.com/api/storesearch` sends only the name; names
  match only when equal once case, ™/®, apostrophes, punctuation and accents are set aside, or equal without an
  edition, so a DLC or another game of the series never counts; `IStoreBrowseService/GetItems` with `include_assets`
  gives the picture files, which carry a hash for newer games, then the long-standing addresses), the 600 × 900 cover
  first, else the wide store header as the banner. A game nothing is found for gets a `.none` marker and is looked up
  again after a week; a network failure before any store answers leaves no marker, and three in a row stop the round.
  The page shows *Finding pictures for your games…* only when it takes longer than 400 ms. `GameLibrary` merges (by id and folder), sorts, searches and finds the running game: a
  process whose file is inside a game's folder (the most specific folder wins; launchers, crash reporters and
  anti-cheat services don't count), checked every 3 s while the page is open. *Play* starts the launcher's link, the
  package's app id or the program through `ShellLaunch`, which asks the desktop's shell (`IShellDispatch2.ShellExecute`
  on the desktop window) so games run as the user, not elevated; Explorer is the fallback. Covers are decoded off the
  UI thread at their shown size (`AsyncImage`).
- **Boosts.** `GamingTweakViewModel` switches, optimistic until the next read confirms them. HAGS: support and current
  state from the graphics kernel (`D3DKMTQueryAdapterInfo`, `KMTQAITYPE_WDDM_2_7_CAPS`), the choice in
  `GraphicsDrivers\HwSchMode` (2 on, 1 off; a restart is pending while the two differ). Ultimate Performance:
  `powercfg /list` parsed by GUID (language-independent), `-duplicatescheme e9a42b02-…` once, `/setactive`; the plan
  that was active is remembered and restored when it's switched off. Game Mode: `GameBar\AutoGameModeEnabled`.
  Windowed optimizations: `SwapEffectUpgradeEnable` in `DirectX\UserGpuPreferences\DirectXUserGlobalSettings`, keeping
  the other entries (Windows 11). Background recording: `GameDVR\HistoricalCaptureEnabled`. NVIDIA power mode:
  NVAPI. Since 1.4.3 each card has a category pill (Latency, Performance, Frame stability, Smoothness, Visual
  fidelity) and, for the newer ones, a recommendation and what it doesn't do. **G-SYNC**: the global profile's
  `VRRFEATUREINDICATOR` (0x1094F157), `VRRREQUESTSTATE` (0x1094F1F7) and `VRR_MODE` (0x1194F158) set to 1/2/2
  (fullscreen and windowed) in one session, plus Windows 11's `VRROptimizeEnable`; off restores the driver defaults
  (fullscreen only). `NvAPI_DISP_GetDisplayIdByDisplayName` + `NvAPI_Disp_GetVRRInfo` (`NV_GET_VRR_INFO_V1`, 24 bytes)
  on the fastest display: not offered when variable refresh isn't possible, a hint when it isn't turned on for the
  display. The recommendation names the frame limit and refresh rate (`GSyncSetup`). **Shader cache**:
  `PS_SHADERDISKCACHE_MAX_SIZE` (0x00AC8497, megabytes; the driver's default is 0x4000) set to the chosen 10 GB /
  100 GB / unlimited (`GamingSettings.ShaderCacheSize`, `ShaderCacheSize`), on when a limit of 10 GB or more was set
  (`NvApi.GetGlobalSetting` tells a set value from the driver's own via `isCurrentPredefined`); off restores the
  default; a new size applies at once while on. 1.4.3–1.4.6 also had an **Auto HDR** switch (`AutoHDREnable` in the
  same DirectX settings); 1.4.7 replaced it with intelligent CPU core scheduling (below), and Auto HDR is left to
  Windows' Settings. Every supported boost counts in the ring and *Boost now* turns each one on, Ultimate Performance
  included (1.4.7–1.4.8 left it out on a multi-CCD Ryzen X3D, so the ring read 8/8 with nine switches on); on such a
  processor `GamingTweakViewModel.Advice` explains on hover that it stops the core parking AMD's driver relies on.
- **Frame limiter.** The NVIDIA driver's frame limiter (`FRL_FPS`, 0x10835002) in the global profile through NVAPI's
  driver settings (`NvApi`: `nvapi_QueryInterface` function pointers, ids from NVIDIA's public `nvapi_interface.h`,
  the 12,320-byte `NVDRS_SETTING`). *Automatic* is refresh − refresh² ÷ 3600 (`FrameCap.AutoTarget`, as NVIDIA Reflex
  uses), from the fastest attached display. While a library game runs the page leases the frame-rate source and grades
  pacing from its 1% low against the average (`FramePacing`).
- **Per-game limits (1.4.1).** The limiter's *Apply to* picker chooses all games or one. A game's limit goes into the
  driver profile of its programs: `GameExecutables.Pick` (Core, unit-tested) takes the running program in the game's
  folder, the store's launch program, Unreal's `*-Shipping.exe`, else the largest program that isn't a helper;
  `NvApi.SetForApplication` uses `NvAPI_DRS_FindApplicationByName` (NVIDIA's own profile when it lists the program;
  `NVDRS_APPLICATION` V4 down to V1 for older drivers) or creates a "Coreloom: name" profile with the program
  (`CreateProfile`, `CreateApplication`), sets `FRL_FPS` there and saves; removing deletes the setting and an emptied
  Coreloom profile. `GamingSettings.GameFrameCaps` remembers each game's limit and programs, and `GlobalFrameCap` the
  all-games limit last read, for the chips and the overlay.
- **Games added by hand (1.4.6).** `CustomGame` entries (`GamingSettings.CustomGames`, normalized by `CustomGames`,
  Core, with Windows paths parsed by hand so the tests run on Linux) become `GameEntry`s of `GameStore.Custom` ("Added
  by you"), merged ahead of launcher games. `GameEditorViewModel` adds, edits and removes them (program picked with an
  open-file dialog, name suggested from the program's version info, picture or the program's icon). `FindRunning`
  matches a custom game by its exact program first, and never by a broad folder (Desktop, Downloads, a drive root);
  `GameExecutables.Pick` puts the chosen program first for its own limit.
- **Adaptive frame pacing (1.4.6).** Off until the user turns on its switch (`GamingSettings.AdaptivePacing`).
  `AdaptivePacingService` (app) ticks once a second: the game in front from `GamingModeService`, its newest second of
  frames from the shared ETW frame-rate source (leased as "adaptive pacing", released 30 s after the last game), and
  the graphics card's utilization. `PacingSecond.From` (Core) sums up a second (frame rate, median and 99th
  percentile frame time, spikes over 1.6× the median, the longest frame). `AdaptivePacingController` (view model)
  maps the program to a library game, leaves alone a game with a limit the user set, and runs an
  `AdaptivePacingSession` (Core, unit-tested): two minutes of seconds (under 10 FPS or with a frame over 500 ms left
  out); after 45 s the sustainable rate is the frame rate reached in 80 % of seconds, but no more than the median 1%
  low ÷ 0.85; `PacingLimits` snaps it under the refresh rate (G-SYNC/FreeSync: `FrameCap.AutoTarget`, steps of 5) or
  to a whole fraction of it (fixed refresh), in whole rendered frames and at least 40 rendered frames a second with
  frame generation (set per game: 2×, 3×, 4×). A game without a limit gets that; one missing its limit (median under
  97 %, more than 10 % of seconds under 95 %, or 1% lows under 80 %) is lowered by at most 15 %; one holding it with
  the graphics card under 70 % busy is raised by up to 10 %, never to or above a limit it failed before
  (`FailedAt`); changes under 5 % are skipped. After a change 12 s of frames show whether the driver took it: if not,
  nothing more changes until the game's next start. Limits go through `ApplyGameCapAsync` (the per-game path above)
  marked `GameFrameCap.Adaptive`; turning the switch off removes those. `GamingSettings.AdaptivePacingGames` keeps
  what was learned (limit, failed limit, sustained rate, frame generation, sessions). Without an NVIDIA driver it
  suggests the limit instead. Each change and session end is logged under *Gaming*.
- **Intelligent CPU core scheduling (1.4.7).** A boost (`GamingTweak.CoreScheduling`, Coreloom's own switch:
  `GamingSettings.CoreScheduling`); its *Details* (`GamingTweakViewModel.DetailsCommand`) open a card over the page
  (1.4.7 had it under the shader cache), and a test's progress or a result to keep shows under the boost
  (`CoreSchedulingController.BoostNote`, as the boost's status note). `CoreLayout.Parse` (Core, unit-tested with
  synthetic buffers) reads `GetLogicalProcessorInformationEx(RelationAll)`: cores (efficiency class, group masks) and
  L3 caches; logical processors are numbered by group and number as Windows' per-processor counters are. Cores are
  grouped by their L3: two or more L3s with one at least 1.5× the smallest, on cores of the same efficiency class, are
  a Ryzen X3D's cache and frequency CCDs; differing efficiency classes are performance and efficiency cores (also Zen 5
  with Zen 5c, where the faster cores have the larger L3); otherwise CCDs alike, or one group. The preferred cores are
  the cache, else the performance, else the first CCD; affinity masks need everything in processor group 0.
  `CoreSchedulingTicker` (app) ticks once a second while it's on: the game in front (`GamingModeService`), the
  per-logical-processor loads from the latest sensor frame (`cpu/thread/N/load`), and frames from the shared ETW
  source only while a test runs (leased as "core scheduling"). `CorePlacement` keeps each group's share of the load
  over 20 readings; `CoreScheduling.Judge` calls it good at 70 % on the preferred cores, mostly elsewhere under 45 %.
  `CoreScheduling.Checks` validates what AMD's scheduling needs on an X3D: the `amd3dvcache` driver and
  `amd3dvcacheSvc` service (service manager, query only), `HKLM\SYSTEM\CurrentControlSet\Services\amd3dvcache\Preferences`
  `DefaultType` (0 frequency first, AMD's default; 1 cache first) and per-program keys under `Preferences\App`
  (`EndsWith`, `Type`), Game Mode (which AMD's driver uses to spot games) and a power plan that lets cores park (not
  High or Ultimate Performance; shown in grey, as a trade-off rather than an error, since 1.4.9). All of this is read
  only. `AffinityTrial` (Core, unit-tested) is the A/B test the
  user starts: Windows' choice, preferred, Windows' choice, preferred, 30 s of gameplay each (the first 3 s of a round
  and seconds under 10 FPS or with a frame over 500 ms left out); proven when both preferred rounds beat the Windows
  rounds before them and FPS gains ≥ 3 % with 1% lows ≥ −1 %, or 1% lows gain ≥ 5 % with FPS ≥ −1 %. A proven
  result is offered (*Keep* / *Not now*); kept profiles are `GamingSettings.CoreProfiles` (program file name, cores,
  gains; never frequency or efficiency cores). `CoreSchedulingController` applies a profile when its game comes in
  front, unless AMD's driver has a per-program preference for it, its affinity was set elsewhere
  (`ProcessAffinity.IsCustom`), or, with AMD's driver running, 10 readings show ≥ 80 % of the load already on the cache
  cores. Affinity is `SetProcessAffinityMask` with `PROCESS_SET_INFORMATION` (as Task Manager does; a refusal, e.g. by
  anti-cheat, is reported). Turning it off, or closing Coreloom, sets every changed game back to the system mask; a
  test that isn't proven also drops that game's old profile. Everything is logged under *Gaming*.
- **Shader cache.** NVAPI's `PS_SHADERDISKCACHE_MAX_SIZE` (0xFFFFFFFF = unlimited; off restores the driver default),
  sizes of the DirectX, NVIDIA, AMD, Intel and Steam pre-cache folders (`ShaderCaches.Locations`), and a notice when
  the driver version (NVAPI) differs from the last one seen. Nothing is pre-compiled by running code in games.

## Processor details (1.4.6)

- **CPUID.** `CpuIdNative` (Hardware) runs CPUID through `X86Base.CpuId` (no driver, no rights); `CpuIdInfo.Read`
  (Core, unit-tested with register values) decodes vendor, family/model/stepping (extended fields), the brand string,
  instruction sets (SSE4.2, AVX, AVX2, AVX-512, FMA3, AES, SHA, BMI, VAES) and features (AMD: Core Performance Boost
  0x80000007 EDX bit 9, hardware P-states bit 7, invariant TSC bit 8, effective-frequency bit 10, RAPL bit 14, CPPC
  0x80000008 EBX bit 27; Intel: Turbo and HWP from leaf 6). `CpuProvider` adds them to the processor's properties
  (`CPUID` is "Family 17h · model 60h · stepping 1", read back by `CpuSignature.TryParse`).
- **Every processor.** PDH's `% Processor Performance` (boost, the clock relative to base) and `% Performance Limit`
  (under 100 % when power, heat or policy holds the processor back) are published as `cpu/performance` and
  `cpu/perf-limit`.
- **AMD.** `AmdProcessors` (Core) maps family 17h/19h/1Ah model ranges to architecture and codename (Zen to Zen 5),
  the model suffix to desktop, desktop with graphics, laptop or handheld, and desktop models to AMD's rated TDP and
  stock PPT (1.35 × TDP). `SensorMapping.MapCpu` publishes LibreHardwareMonitor's SMU power-table and SVI2 sensors
  where the processor has them (LibreHardwareMonitor 0.9.6 has the layout of one firmware version each of the Ryzen
  2000 APU, Ryzen 3000, Ryzen 5000 and Ryzen 7000/9000 desktop tables): `cpu/power/ppt`, `cpu/current/tdc`, `cpu/current/edc`, cores/SoC/misc power, SoC current
  and voltage, `cpu/clock/fabric`, `uncore`, `memory`, SoC and I/O-die temperatures; `CpuProvider` gives each a reason
  when missing. `AmdProcessorViewModel` shows the ones reported and groups the others by reason. `AmdTuning` (Core)
  compares the last hour of PPT (or package power) with the stock limit to say whether PBO looks on (three readings
  over 108 %), held at stock (five near it) or not known yet, and lists power limits, PBO, Curve Optimizer, Eco mode
  and manual tuning, each read-only or unavailable (`TuningControl.CanChange` is always false) with where to change
  it. Nothing writes to the SMU. Since 1.4.7 the card sits right above the per-core table and is compact: the segment
  as caption (the CPUID signature on the architecture badge's tooltip), readings as tiles with the ones not reported on
  one line (`MissingText`, reasons in `MissingToolTip`), and each tuning control as one row with a short `Summary` and a
  status pill (the full explanation on hover).

## Sensor columns and nested scrolling

`SensorTableViewModel` also exposes `FirstColumn`/`SecondColumn`. `SensorColumns.Split` balances the groups by height
(a group title counts as 1.6 rows), keeps lists of up to 8 rows in one column, and splits a group ("Temperatures,
continued") only when that is clearly more even than moving whole groups. Scroll areas inside a page (the GPU
specifications, the Overview storage list) use `b:ScrollChain.Enabled`, which hands the mouse wheel to the page once the
inner list reaches its end.

## Vendor marks

`HardwareVendors.Detect` (Core) identifies NVIDIA, AMD or Intel from the vendor string first, then the device name.
`BrandMark` shows a logo file from `%AppData%\Coreloom\Vendors` or the embedded `assets/vendors/` set, and a
plain-text badge otherwise. No logos are bundled.

## Privileges

Coreloom's manifest is `requireAdministrator`: Windows shows a UAC prompt each time the app is started by
hand, and the process always holds an elevated token. CoreloomDiag stays `asInvoker` (run it from an elevated
terminal to include the admin-only sensors).

- **Without PawnIO:** NVML, PDH, memory, network, SMBIOS, process lists and storage queries all work.
- **Smart Optimize** relies on the elevation for the Windows temp folder, machine-wide startup apps, trimming other
  apps' memory, clearing the standby list, Defender's threat removal and drive optimization.
- **With PawnIO (the app is already elevated):** LibreHardwareMonitor's low-level CPU and Super I/O sensors
  need both. If the driver is missing Coreloom explains that; it never installs drivers.
- **Starting at sign-in:** *Start with Windows* creates a Task Scheduler logon task (`HighestAvailable`, named
  `Coreloom startup`). A Run-key entry would trigger a UAC prompt at every sign-in for an app that requires
  administrator rights, so Coreloom does not use one. The app is already elevated when it creates or removes the
  task, so there is no extra prompt, and it refreshes the task at launch if the executable moved. The
  uninstaller calls `Coreloom.exe --remove-startup-task` (one elevation prompt) because only an elevated process
  can delete that task.
- **Restart:** relaunching from the elevated process (settings changes, `--wait-for <pid>`) does not prompt
  again, because the child of an elevated parent inherits the elevation.
- **Installer:** per-user and not elevated, so `Coreloom.exe` is launched with `shellexec` and an in-app update
  (started by the elevated app) inherits elevation. A manual install over a running instance requires exiting
  Coreloom first, because a non-elevated installer cannot close an elevated app.

## Startup splash and handoff

A launch that opens a window shows the Coreloom splash first and fades into the real main window once that window
has painted. The design is the Coreloom splash opener kit (`assets/branding/splash/splash-screen-preview.png`),
recreated in native XAML; no browser or web view is involved.

- **When.** `App.Start` starts the splash right after the single-instance check and settings load. It is skipped
  for `--minimized`, for the sign-in task with *Start minimized*, for a second launch (which hands over to the
  running copy and exits) and for the uninstaller's `--remove-startup-task`. `SplashPolicy` (Core, unit-tested)
  holds these rules and the timings.
- **Own UI thread.** `SplashScreenHost` runs `SplashWindow` on a dedicated background STA thread with its own
  dispatcher. WPF animations tick on their owner thread, so the loading line keeps moving while the main thread
  builds the dashboard. The thread can't keep the process alive, and its window is destroyed with it. The window
  sets `InheritanceBehavior = SkipToThemeNext` and defines everything it uses locally, because App.xaml's resources
  belong to the main thread; `check-xaml.py` enforces both.
- **Window.** 800 × 450 (16:9) with squircle corners, borderless and layered (`AllowsTransparency`, so the whole
  window can fade), centred on the monitor with the mouse cursor, not topmost, no taskbar button,
  `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE` so it never appears in Alt+Tab or takes focus. `SplashWaves` draws six
  ribbons of light from sine waves every frame (a lit edge in five widths over a sheet, faded toward the middle). The
  icon is the dark-mode one with the dark theme, on a breathing halo; the wordmark uses the embedded Inter with
  CSS-style letter spacing (`TrackedText`). The loading bar moves on with each status the app reports
  (`SplashPolicy.ProgressTarget`: per-stage steps, a slow creep within a stage, never past 92 % until the handoff,
  which fills it), with sliding stripes and a blurred glow.
- **Readiness.** `StartupHandoff` creates the main window's handle, cloaks it (`DWMWA_CLOAK`), and shows it: the
  window lays out and renders but isn't displayed. Its `ContentRendered` event, plus two composition frames, is
  the ready signal. The dashboard does not wait for sensor data or network calls.
- **Handoff.** The splash stays up at least 850 ms from its own first frame, so a fast start doesn't flash it.
  Then the splash is ordered just above the window (only if the window is in the foreground, so it never jumps
  over another app), the window is uncloaked with its frame and background already drawn, and the splash fades
  out over 360 ms while the window's content fades in. With reduced motion (Coreloom's setting or Windows'
  *Animation effects* off) there is no entrance, no moving line and no fade: the window appears fully painted.
  If the splash hasn't reached the screen by the time the window is ready, it is cancelled instead of shown late.
  While cloaked (normally well under a second) the main window already has keyboard focus.
- **Failure paths.** A fatal startup error switches the splash to its error look behind the existing error
  dialog, and every exit path closes it. A 10 s watchdog only covers a main window that never reports a first
  frame. If cloaking isn't available, the window is shown directly. Splash problems are logged and never stop
  the app.
- **Timings** are logged under `Startup`: when the splash reached the screen and when the window was revealed,
  both measured from process start.

## Rendering, fonts and lifetime

- **Text:** Inter (UI) and JetBrains Mono (numbers and technical values) ship embedded in the assembly as
  `Resource`s (SIL OFL 1.1; licence texts ship next to the app). `TextFormattingMode="Display"` with fixed
  hinting and ClearType hints on opaque cards keeps small text sharp; custom-drawn chart text uses
  `FormattedText` in Display mode.
- **Animation rate:** WPF animates at 60 fps unless told otherwise, so `App` raises
  `Timeline.DesiredFrameRate`'s default to the fastest attached display (60–240 Hz, read with
  `EnumDisplaySettings`). The rate is a request, not a guarantee: WPF composites in software and the DWM still
  decides what reaches the screen. Number tweens stay at 60 fps because each tick re-lays-out a `TextBlock`.
- **Page cache:** `PageHost` keeps one view per page view model, so navigating back is instant. A page that is not
  shown is unloaded, which detaches every chart's event handlers (`Loaded`/`Unloaded` symmetry); charts
  re-attach when the page returns.
- **Memory:** history is a bounded ring per metric (12 bytes per point, one hour). Bound immutable items derive
  from `StaticRecord`/`StaticItem`, so WPF uses its weak event path for them. Settings › Diagnostics shows the
  process working set, the managed heap and the handle count so growth is visible.

## Updates

`GitHubReleaseClient` lists releases from `api.github.com`. This is anonymous: the 60 requests per hour
limit is far above Coreloom's check frequency. `UpdatesViewModel` runs the whole flow as a small state machine
(`Idle → Checking → UpToDate | Available → Downloading → Ready → Installing`, plus `Failed` and `NotConfigured`).

1. **Check** (20 s after start, then every 6 h when the chosen frequency says a check is due, or on *Check for
   updates*): pick the newest non-draft release, skipping pre-releases unless the user opted in and skipping a
   version the user chose to skip.
2. **Download** (automatic when *Update automatically* is on, or as the first half of *Install update*): with HTTP
   range resume, into `%LOCALAPPDATA%\Coreloom\Updates` (`Data\Updates` for a portable copy). Copies installed
   with the installer download the `-Setup.exe` asset; every other copy (portable or just unzipped) downloads the
   `-Portable.zip` asset (`IUpdateInstaller.PackageKind`).
3. **Verify:** SHA-256 against `SHA256SUMS.txt` from the same release (constant-time comparison). The verified
   release is saved as `pending-update.json` next to the download, and its hash is recorded under
   `HKLM\SOFTWARE\Coreloom\Updates` (`IUpdateTrustStore`; each non-installed copy uses its own
   `Copies\<folder id>` subkey). The downloads folder is writable by the user, so after a restart only the HKLM
   hash, which needs administrator rights to change, makes the file trustworthy.
4. **Install** when the user clicks *Install update* (Settings or the tray; it downloads first when needed), when
   Coreloom next starts, or while it sits in the tray and the PC is idle (no input for 15 minutes, CPU and GPU
   quiet for 10; see `UpdateInstallPolicy`). Right before installing, the download is hashed again and held open
   read-only (`VerifiedPackage`), so the file that was checked is the file that runs or is unpacked.
   - **Installed copies:** Coreloom starts the installer with `UpdateInstallOptions` (`/SILENT` or
   `/VERYSILENT`, `/SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /UPDATE=1`, and `/MINIMIZED=1` for a
   background install), then exits. Inno Setup replaces the files and relaunches Coreloom with `--updated`
   (and `--minimized`). If Setup can't finish, it rolls back and starts the old version with `--update-failed`,
   which stops automatic attempts for that version; automatic attempts are also capped at two.
   - **Portable and other copies** (`FolderUpdate`): the zip is unpacked into `.coreloom-update\new` inside the app
     folder (zip-slip, size and link checks; it must contain `Coreloom.exe` and `Coreloom.dll`). Coreloom starts
     that new `Coreloom.exe --apply-update --wait-for <pid>` (plus `--minimized --quiet` in the background) and
     exits. The helper shows the splash ("Updating Coreloom to …"), waits for the old process, takes the
     single-instance lock, moves each file it replaces into `.coreloom-update\backup` and copies the new one in
     (`Coreloom.exe` last, locked files retried for a few seconds). It never touches `Data\` or `portable.txt`. On
     any failure every change is undone. It then starts `Coreloom.exe --updated` (or the old version with
     `--update-failed`) and exits; the next start deletes `.coreloom-update` in the background. `--apply-update` is
     a contract between versions: the running version starts the *next* version's executable with it.
5. **After the update:** `UpdateSettings.RecordLaunch` notices the newer version, the window shows an
   *Updated to …* banner, and *Settings → Updates* shows *What's new in this version* from the saved notes. When
   the update left no notes behind (installed by 1.0.0's updater, which predates all this, or by hand), they are
   fetched once from the release for the running version's tag. Downloads of finished versions are deleted.

`ReleaseNotesSummary` turns a release's Markdown notes into that short summary: the opening sentence plus up to
five bullets (from a *Highlights* section, else the Keep a Changelog sections, tagged New/Improved/Fixed),
each cut to its bold lead and first sentence. The update UI never sends users to GitHub to get a new version. The
repository is embedded at build time as
`AssemblyMetadata("UpdateRepository")`. A build without it simply has no update checks.

## Extending

- **New sensor source:** add an `ITelemetryProvider` (see CONTRIBUTING.md).
- **FPS / frame times:** a future `Coreloom.PresentMon` provider based on ETW. It needs administrator rights
  or membership in *Performance Log Users*, so it belongs behind an explicit opt-in.
- **Other GPU vendors:** AMD and Intel GPUs already flow through LibreHardwareMonitor's mappings. A native
  ADLX or IGCL provider could be added with a higher priority, the same way NVML outranks
  LibreHardwareMonitor for NVIDIA.
