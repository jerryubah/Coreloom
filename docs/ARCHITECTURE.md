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
- **Settings** are JSON in `%APPDATA%\Coreloom\settings.json`, or `Data\` in portable mode.
  - Writes are atomic: write a temp file, then replace.
  - Writes are debounced by 600 ms.
  - A corrupt file is kept as `settings.json.bad` and defaults are used.

## Design system (1.4.1)

- **Squircles.** `Squircle.Create` (Controls/SquircleBorder.cs) builds Figma's corner smoothing (figma-squircle,
  "preserve smoothing"): with smoothing ξ (0.6, Apple's) a corner starts (1 + ξ)·r from the vertex, a circular arc of
  90°·(1 − ξ) sits in the middle and two cubic Béziers lead into it; when the side is too short the straight lead-in is
  shortened, never the arc. `SquircleBorder` derives from `Border` (so the `Card`/`Card.Inset` styles and every
  `Border` template still apply) and replaces `OnRender`: shadow (a few widening translucent outlines, no blur effect),
  background, the glow, border, lit edge and sheen, all on the squircle; `ClipContent` clips children through
  `GetLayoutClip`. Every card and rounded container in the XAML, and the button, field, menu, tooltip, segmented and
  navigation templates, are `c:SquircleBorder`.
- **Glow cards.** `GlowKey` names a color (a series such as `Gpu`, a status such as `Success`, `Accent`); the card is lit
  from below by a radial gradient clipped to its shape and its lower rim brightens. `Card.Glow` adds `EdgeGlow`: the
  pointer's position (eased toward the pointer each frame on `CompositionTarget.Rendering`, only while hovered or fading)
  centres a radial gradient in the glow color and its warmer neighbour, stroked along the rim in four widths from a
  14 px halo to a 1.3 px line, clipped inside the card; it brightens as the pointer nears the edge.
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
  (exponential, ~165 ms) and stops when something else scrolls.
- **Text.** Large text uses `TextFormattingMode.Ideal` (Display mode distorts big glyphs); headline numbers are Inter
  Bold with tabular digits.
- **Icons.** `build/dark-icon.py` makes the dark-mode icon from the approved one (the mark's shape found by colour with
  thin highlights closed in, its outline anti-aliased from the original, put on a navy glass tile with a blue rim) into
  `assets/branding/dark`. `ThemeService` publishes `Image.AppIcon`/`Image.AppIconLarge` and raises `AppIconChanged`, on
  which the app swaps the window and tray icons (`AppIcons`).

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
content and the column bottoms line up): `CleanupViewModel`, `ProtectionViewModel`, `MemoryOptimizeViewModel` and `SpeedUpViewModel`.
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
  The result is available memory after a short settle minus before.
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

Since 1.4.1 the overlay is a squircle of smoked glass with a rim, the FPS value is the headline, and the FPS line
carries the frame-time graph (`OverlayCatalog.FrameGraph`, offered once to existing setups through
`OverlaySettings.Revision`) and the frame pacer's mark. `FrameGraph` reads `OverlayViewModel.FrameTimes`
(`IFrameTimeFeed` over `IFrameRateSource.RecentFrameTimes`, i.e. `FrameRateTracker.RecentFrameTimes`) about 30 times a
second while shown: a line over a soft fill with an eased scale, stutters (over 1.6× the median) tinted amber or red,
and the pacing target dashed. The mark shows when `GamingSettings.ActiveFrameCap(program)` is above zero for the
foreground program (`OverlayService` passes its file name). The settings preview draws a sample trace
(`FrameGraph.IsLive` is set only on the overlay window).

## Gaming

`GamingViewModel` (the *Gaming* page, above Graphics) has four parts; everything Windows-specific is behind
`IGamingService` (`WindowsGamingService` in the app, `UnsupportedGamingService` elsewhere). Settings live in `Gaming`
(`GamingSettings`: list or grid, sort order, the frame pacer's target, the power plans to restore, whether covers may
be downloaded, the last driver version seen).

- **Library.** Core parses each launcher's own files, unit-tested with samples: Valve's text KeyValues
  (`KeyValues`) for Steam's `libraryfolders.vdf` and `appmanifest_*.acf` (fully installed games only, tools such as
  redistributables, Proton and dedicated servers left out; `LastPlayed` and `SizeOnDisk` read), Epic's
  `Manifests\*.item` JSON (DLC, engines and unfinished installs left out) and its base64 `catcache.bin` for cover
  links, GOG's `goggame-*.info` play tasks, and Xbox's `MicrosoftGame.config`. Ubisoft Connect, EA app, Battle.net and
  Riot games are recognised from Windows' Uninstall keys (`LauncherGames`). The app adds GOG's registry list and Xbox
  packages from `PackageManager.FindPackagesForUser` that carry a `MicrosoftGame.config`. Pictures: Steam's library
  cache (`appcache\librarycache`, both the old `{appid}_library_600x900.jpg` names and the newer per-game folders),
  Xbox's package logos and splash, covers downloaded earlier, else the program's icon saved as a 256 px PNG
  (`Icon.ExtractIcon`). Missing covers are downloaded (to `%LOCALAPPDATA%\Coreloom\Artwork`) only after the user
  presses *Download covers*. `GameLibrary` merges (by id and folder), sorts, searches and finds the running game: a
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
  NVAPI.
- **Frame pacer.** The NVIDIA driver's frame limiter (`FRL_FPS`, 0x10835002) in the global profile through NVAPI's
  driver settings (`NvApi`: `nvapi_QueryInterface` function pointers, ids from NVIDIA's public `nvapi_interface.h`,
  the 12,320-byte `NVDRS_SETTING`). *Automatic* is refresh − refresh² ÷ 3600 (`FrameCap.AutoTarget`, as NVIDIA Reflex
  uses), from the fastest attached display. While a library game runs the page leases the frame-rate source and grades
  pacing from its 1% low against the average (`FramePacing`).
- **Per-game limits (1.4.1).** The pacer's *Apply to* picker chooses all games or one. A game's limit goes into the
  driver profile of its programs: `GameExecutables.Pick` (Core, unit-tested) takes the running program in the game's
  folder, the store's launch program, Unreal's `*-Shipping.exe`, else the largest program that isn't a helper;
  `NvApi.SetForApplication` uses `NvAPI_DRS_FindApplicationByName` (NVIDIA's own profile when it lists the program;
  `NVDRS_APPLICATION` V4 down to V1 for older drivers) or creates a "Coreloom: name" profile with the program
  (`CreateProfile`, `CreateApplication`), sets `FRL_FPS` there and saves; removing deletes the setting and an emptied
  Coreloom profile. `GamingSettings.GameFrameCaps` remembers each game's limit and programs, and `GlobalFrameCap` the
  all-games limit last read, for the chips and the overlay.
- **Shader cache.** NVAPI's `PS_SHADERDISKCACHE_MAX_SIZE` (0xFFFFFFFF = unlimited; off restores the driver default),
  sizes of the DirectX, NVIDIA, AMD, Intel and Steam pre-cache folders (`ShaderCaches.Locations`), and a notice when
  the driver version (NVAPI) differs from the last one seen. Nothing is pre-compiled by running code in games.

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
