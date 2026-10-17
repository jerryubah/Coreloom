# Testing Coreloom on your PC

Coreloom was written and unit-tested in a Linux build environment that had no Windows, no GPU and no
access to NuGet. The Windows app has **not been built or run yet** (see [VERIFICATION.md](VERIFICATION.md)).
This checklist takes you from a fresh clone to a verified build on the target machine:

- Windows 11 x64
- PNY GeForce RTX 5090
- AMD Ryzen 9 9950X3D
- 64 GB RAM
- ASRock X870E Nova WiFi

Plan for about an hour. Note anything that doesn't match the **Expect** lines; that's exactly what needs
fixing.

## 0. Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0): `dotnet --version` should print 10.0.1xx.
- Git.
- Optional, for comparison: [HWiNFO64](https://www.hwinfo.com/) (sensors-only mode) and the NVIDIA app
  overlay.
- Optional, for CPU temperature and motherboard sensors: [PawnIO](https://pawnio.eu). Install it only if
  you want those sensors; Coreloom works without it.
- Optional, for building the installer: [Inno Setup 6](https://jrsoftware.org/isinfo.php).

## 1. Build and run the tests

```powershell
git clone https://github.com/<you>/Coreloom.git
cd Coreloom
./build/build.ps1
```

**Expect:** the restore, build and all tests succeed. On Windows, the integration tests in
`Coreloom.Hardware.Tests` really run, against PDH, storage, SMBIOS, NVML and processes on your machine. On
Linux they are skipped.

If the build fails, copy the first error and stop here; everything else depends on it. The most likely
spots are listed in [VERIFICATION.md](VERIFICATION.md#highest-risk-areas).

## 2. CoreloomDiag as a standard user

Run this from a normal (non-administrator) terminal:

```powershell
dotnet run --project src/Coreloom.Diagnostics -c Release -- --samples 30 --compare --out diag-standard.md
```

**Expect:**

- **GPU**: utilization, temperature, core and memory clock, VRAM used/total, power, power limit, fan %,
  P-state and clock-limit reasons are all present from **NVML**. Hot spot, memory junction, fan RPM and
  voltage come from LibreHardwareMonitor, or show as unavailable with a reason.
- **`--compare` section**: NVML and `nvidia-smi` agree on temperature (±1 °C), clocks (±1 bin), power
  (±5 %) and memory.
- **CPU**: utilization and the derived clock are present. Temperature, package power and per-core clocks
  show as unavailable, with the reason "requires administrator rights and the PawnIO driver".
- **Memory**: 64 GB total. Modules are listed from SMBIOS.
- **Storage**: every disk is listed with read/write throughput, and NVMe temperature where Windows exposes
  it to standard users.
- **Network**: the active adapter is the one with your default gateway.
- **Overhead**: the "Overhead while sampling" line prints Coreloom's own CPU use. The design budget is well
  under 1 % of total CPU at a 1 s interval. Please report what you see.

## 3. CoreloomDiag as administrator, with PawnIO installed

Run this from an elevated terminal:

```powershell
dotnet run --project src/Coreloom.Diagnostics -c Release -- --samples 30 --lhm-report --out diag-admin.md
```

**Expect:**

- CPU temperature from LibreHardwareMonitor's "Core (Tctl/Tdie)" sensor.
- CCD1 and CCD2 temperatures.
- Package power and per-core clocks.
- Motherboard fans, voltages and temperatures.

Compare a few values with HWiNFO64. The **Manual comparison checklist** section of the report lists the
tolerances to expect. If CPU temperature is still missing, look in the `--lhm-report` section for the
sensor names LibreHardwareMonitor actually reports. The mapping lives in
`src/Coreloom.Hardware/SensorMapping.cs` and may need a name added.

> The `--lhm-report` section is LibreHardwareMonitor's raw output and can include drive or memory serial
> numbers. Review it before sharing.

## 4. The app

```powershell
dotnet run --project src/Coreloom.App -c Release
```

Work through each area and tick what works.

**Startup splash** (use the built `Coreloom.exe`, the installed copy and the portable zip at least once each)

- [ ] Double-click `Coreloom.exe` and accept the UAC prompt: the dark Coreloom splash (icon, "Coreloom",
      "SYSTEM PERFORMANCE", a thin moving blue line) appears centred on the monitor with your mouse pointer and
      fades in gently. No white or black flash before it.
- [ ] The splash fades into the dashboard; the dashboard never appears empty, white or half-drawn first.
- [ ] While the splash is up, the taskbar shows no extra Coreloom button and Alt+Tab doesn't list the splash.
      Afterwards there is exactly one Coreloom taskbar button, with the Coreloom icon.
- [ ] Cold start (first launch after a reboot) and warm start (launch again right after exiting): the splash shows
      in both, stays up at least a moment on the fast one, and doesn't hang around after the window is ready.
      The log has two `Startup` lines with the timings; note them.
- [ ] Display scaling 125 %, 150 % and 200 % (*Settings → System → Display → Scale*): the icon and text are sharp
      and nothing is cut off.
- [ ] Two monitors: start Coreloom with the mouse on each monitor; the splash follows the mouse. (If the dashboard
      was last open on the other monitor, the handoff happens across monitors; that is expected.)
- [ ] Windows *Settings → Accessibility → Visual effects → Animation effects* off, or Coreloom's *Reduce motion*
      on: the splash appears without the fade/rise, the line is still (not moving) and the hand-off has no fade.
- [ ] Windows in light mode and dark mode: the splash stays dark (brand design); the dashboard follows its theme.
- [ ] With Coreloom already running, start it again: no splash; the running window comes to the front.
- [ ] `Coreloom.exe --minimized`, and *Start with Windows* with *Start minimized*: no splash; Coreloom goes straight
      to the notification area. Opening it from the tray shows the window without a splash.
- [ ] After closing Coreloom (tray → Exit), Task Manager (Details) shows no Coreloom process left behind.
- [ ] Start Menu and desktop shortcuts from the installer launch with the splash as well.

**Window and appearance**

- [ ] The window opens with dark Mica (or the solid dark background if Mica is off).
- [ ] Minimize, maximize and close work. Hovering maximize shows the Windows 11 Snap Layouts flyout.
- [ ] A maximized window has no clipped edges.
- [ ] The sidebar collapses with its button, and on its own below about 980 px window width.
- [ ] *Settings → Appearance*: light/dark/system themes animate. Accent swatches and a custom hex like
      `#FF5C8A` apply everywhere. *Reduce motion* stops the animations.

**Overview**

- [ ] The header shows your motherboard name as the title (for example *ASRock X870E Nova WiFi*), the graphics card
      underneath, and the specs: processor, cores, memory (size, type and speed, modules), storage, graphics memory,
      driver, Windows version and BIOS. Values match *Settings → Diagnostics* and Task Manager.
- [ ] The PC picture shows your GPU model on the card and the CPU temperature on the pump screen, both crisp and
      sitting where the render's text was.
- [ ] *Customize* opens a card over the Overview (no page change). Its *PC lighting* swatches change the glow behind
      it: Blue, Purple, Red, Green, Rainbow (cycles slowly through the colors) and Accent color (follows *Settings →
      Appearance*). The choice is kept after a restart.
- [ ] The card closes with its X, *Done*, Esc or a click outside it.
- [ ] The fans turn. Hover the picture: the tooltip says which fan readings they follow. Speed up your fans (a stress
      test or a fan curve) and the picture's fans speed up within a few seconds, their blades blurring more as they go
      faster. Without fan readings the tooltip says so and the fans idle.
- [ ] *Settings → Appearance → Reduce motion* stops the fans. Minimizing, closing to the tray or switching pages
      stops all animation (Task Manager: Coreloom's GPU use drops to about zero).
- [ ] (Optional) Save a render of your build as `pc.png` in `%AppData%\Coreloom`, restart, and check it replaces the
      picture (no color dots) and looks sharp; delete it to get the render back.
- [ ] Narrow the window: the two spec columns stack into one and nothing overlaps.
- [ ] The six cards show live values with gauges and sparklines.
- [ ] The numbers match the NVIDIA overlay and Task Manager within the usual tolerances.
- [ ] *Customize*: hiding and reordering cards and the min/max/avg switch update the dashboard behind the card right
      away and persist after a restart. *Reset layout* brings back every card in the default order and size.
      *Settings* no longer has a *Dashboard* section.
- [ ] *Customize › Show the PC picture* off: the picture goes, the text moves left and the lighting colors grey out.
      On again: it's back with its lighting and fans. The choice is kept after a restart.
- [ ] Default layout in a wide window: six summary cards in a row, then rows of three panels, as before.
- [ ] Hover a card: a grip appears at the top and a corner mark at the bottom right. Drag a card (from the grip or
      anywhere that isn't a control): it follows the mouse with a shadow, a dashed outline shows where it will land,
      the other cards glide out of the way, and on release it snaps into place. The order is kept after a restart.
- [ ] Drag a summary card among the panels and a panel among the summary cards: both work.
- [ ] Drag a card's corner: the card grows and shrinks in whole grid steps, with a dashed outline following the
      mouse, and the cards around it reflow. Make a chart full width and a card taller; the chart and the sparkline
      grow with them, and the storage list shows more drives when its card is taller.
- [ ] Esc during a drag or resize puts everything back. Holding a card near the top or bottom of the page scrolls it.
- [ ] Right-click a card: *Wider*, *Narrower*, *Taller*, *Shorter*, *Reset size* and *Hide card* work, and the ones
      that can't apply are greyed out.
- [ ] Clicking the chart legend, scrolling the storage list and hovering charts still work and don't start a drag.
- [ ] Narrow the window: the grid goes to 4, then 2, then 1 column, without overlaps; widen it again and your sizes
      come back.
- [ ] The page scroll bar (Overview, GPU, CPU, Memory, Storage, Network) can be grabbed without the resize arrow
      appearing, also when maximized. Pointing at it widens it; using the mouse wheel over it slims it again until the
      mouse moves. Dragging the window's right edge still resizes the window.

**Health**

- [ ] The Overview shows a *System health* card under the summary cards (also after updating from 1.2.7), with the
      graphics card, processor, and memory and drives side by side. It says *Checking…* for a few seconds after start.
- [ ] The Graphics and Processor pages show a health card under the gauges. A cool, idle PC reads *Excellent*; without
      PawnIO the processor reads *Good* with "Temperature not available".
- [ ] Under a long stress test the graphics card stays *Excellent* unless it gets warm (it then says *Good* and
      "Running warm"). Clock slowdowns from heat show up there; power-limit slowdowns don't.
- [ ] The health card can be moved, resized and hidden like the other dashboard cards.
- [ ] The Overview's card shows four parts: graphics card, processor, memory and storage. The Memory and Storage
      pages have their own health card (memory use and virtual memory; free space, drive wear and drive temperature).

**Gauge colors**

- [ ] Every gauge (Overview summary cards, the Graphics, Processor and Memory page gauges) is green when idle or cool,
      blue in the normal range, orange when heavy and red at the limit. Under a stress test the color glides from one
      to the next with the arc; it doesn't jump.
- [ ] The memory donuts (Memory page, Overview) and the drive rings and bars (Storage page, Overview) use the same
      colors: green for plenty free, red for nearly full.
- [ ] Switching between dark and light themes recolors the gauges right away.

**Memory page layout**

- [ ] The four tiles next to the memory donut fill the card's height, two by two, all the same size; in a narrower
      window the tiles move under the donut. The charts and the two bottom cards line up in equal heights.

**Drive speed test (Storage page)**

- [ ] Pick a drive and *1 GB*, then *Start test*: a progress bar, the read or write gauge moving with the live speed
      and the running row breathing. It takes about half a minute.
- [ ] Results resemble CrystalDiskMark's SEQ1M Q8T1, RND4K Q32T1 and RND4K Q1T1 on the same drive (within roughly
      10–20 %). An NVMe drive reads green, a SATA SSD blue, a hard drive orange or red.
- [ ] *Cancel* stops within a second or two; no `coreloom-*.tmp` file is left in the temp folder or in
      `X:\Coreloom benchmark` (the folder is removed again).
- [ ] A drive with less free space than the test needs says so instead of starting.

**Internet speed test (Network page)**

- [ ] *Start test*: ping and jitter first, then the gauge follows the download and then the upload speed. The results
      resemble speed.cloudflare.com's in a browser. The server line shows Cloudflare, the location and your provider.
- [ ] Unplug the network or turn off Wi-Fi and start a test: a short explanation instead of a hang.

**Devices**

- [ ] *Devices* sits below *Storage* in the sidebar. The Bluetooth card shows the adapter on, with counts that match
      Windows' *Bluetooth & devices* page.
- [ ] Every paired Bluetooth device has a card with the right icon (headphones, mouse, controller…), *Connected* or *Not
      connected*, and the battery where Windows shows one. Connected devices come first; others say when they were last
      connected.
- [ ] Headphones: switch them to your phone, then press *Connect* on the card. They come back to the PC within about 15
      seconds (sound plays on them). *Disconnect* drops them; Windows' *Bluetooth & devices* agrees.
- [ ] A keyboard, mouse or controller: *Disconnect* drops it; *Connect* either reconnects or explains what to do.
- [ ] Turn a device off and on: its card changes within a second or two, without pressing *Refresh*.
- [ ] The Bluetooth switch turns Bluetooth off and on (Windows' switch follows). *Pair a new device* opens Bluetooth
      settings.
- [ ] *Wired and USB devices* lists your keyboard, mouse, monitors, webcam, USB drives… with sensible names and groups,
      and nothing from inside the PC. Plug in a USB drive: it appears within ~15 s; *Eject* says it can be unplugged, and
      with a file open from it, says it's in use.
- [ ] Search narrows both lists; the × clears it. The box is wide; clicking in it hides the grey hint and the cursor
      blinks right after the magnifier, where typing starts; clicking anywhere outside the box (or Esc) stops typing in it.
- [ ] "Bluetooth devices" and "Wired and USB devices" are larger headings with a subtitle, above the group names.
- [ ] The Bluetooth switch sits right next to "Bluetooth / Bluetooth is on".
- [ ] Wired groups are columns on the same grid as the Bluetooth cards (left edges line up), stacking under each other
      to fill the width. Only real gamepads are under *Controllers*; RGB/LED controllers and lights are under
      *Lighting*; USB controller cards aren't listed; no device shows a garbled name.
- [ ] Cards are compact (roughly 270–330 px wide) and keep that width as you resize the window or search; they wrap
      onto new rows instead of stretching. Long names end in "…" and show in full on hover.

**Smart Optimize**

- [ ] *Smart Optimize* sits above *Performance History* in the sidebar.
- [ ] The four cards stack in two columns (Cleanup over Free up memory, Protection over Speed up) with no large empty
      areas inside them; the two columns end at the same height; Speed up shows more startup apps when it has room.
      Narrow the window: they become one column.
- [ ] The banner shows your accent as a gradient (try another accent in Settings: it follows), with a lit, glowing ring
      before the first scan and no word repeated under "Ready when you are". Light theme looks right too.
- [ ] *Smart Scan*: the ring fills while the comet circles; the four tiles fill in one after another (Cleanup,
      Protection, Memory, Speed). The headline shows how much junk was found.
- [ ] Compare the Cleanup sizes with Disk Cleanup / Storage Sense where they overlap. *Clean up and optimize* frees
      about that much (files in use are skipped and said so), trims memory and flushes DNS; the tiles get checks.
- [ ] The Recycle Bin, shader caches, thumbnail cache and Update downloads are unticked by default; ticking the Recycle
      Bin and cleaning empties it.
- [ ] Protection matches Windows Security (real-time protection, definitions version and age, last scan). *Quick
      scan* runs (a few minutes), *Stop scan* stops it, *Update definitions* works. With another antivirus installed,
      the card names it.
- [ ] (Optional) Download the EICAR test file: Protection shows *Threats found* and *Remove threats* clears it.
- [ ] *Free up RAM* reports what it freed; the game or app in front isn't trimmed.
- [ ] Startup apps match Task Manager's *Startup apps*; switching one off here shows *Disabled* there (and back).
- [ ] *Flush DNS cache* and *Optimize drives* report success.

**Design overhaul (1.4.1)**

- [ ] Every card, button, field, menu, tooltip and the sidebar has smooth squircle corners (no hard tangent where the
      corner meets the edge), at 100, 125, 150 and 200 % scaling; no jagged or doubled edges, no clipped content.
- [ ] Dark theme: the background is near-black but not black, cards a step lighter, text easy to read. Light theme
      still looks right.
- [ ] The Overview's summary cards, the PC header, Memory/Storage/Health panels, the page gauge cards, the speed test
      and benchmark cards, the Smart Optimize, Gaming and Devices banners and the frame limiter glow faintly in their
      color from the bottom. Moving the pointer over them lights the edge near the pointer in that color, following it
      smoothly, brighter near the edge, fading out when the pointer leaves; the content never glows, and nothing lags.
- [ ] The sidebar floats with a gap all round, rounded and glassy with a bright rim; collapsing it (button or a
      narrow window) still works. With Mica on (Windows 11), the desktop tint shows through the sidebar; text there is
      smooth (no colored fringes). With Mica off, it shows the two soft lights.
- [ ] Gauges are tick rings: lit ticks brighten toward a glowing head, colors still green/blue/orange/red, values
      glide. The page gauge cards and speed gauges have a soft orb inside. Spinners are still smooth arcs.
- [ ] Every switch: pressing swells the knob into a glass lens; releasing slides it across with a little overshoot.
      Keyboard (Space) still toggles; disabled switches are dimmed.
- [ ] Devices and Gaming search: clicking the box drops the panel (*Start typing…*); typing lists *Show all matches*
      and the best results with icons; Up/Down, Enter and clicking pick one (the box fills with its name and the page
      shows it); × clears; Esc or clicking elsewhere closes the panel; Ctrl+K and Ctrl+F jump to the box.
- [ ] Scrolling with the wheel and touchpad glides on every page; a list inside a page (startup apps, specifications)
      scrolls first, then the page. Dragging the scroll bar or using the keyboard is still immediate.
- [ ] Text: page titles and big numbers look clean (not blocky); small text stays sharp with ClearType.
- [ ] Splash: 16:9 with flowing light, the dark-mode icon with the dark theme (the white-tile icon with the light theme),
      the striped bar moving on through *Reading system information…* and *Opening dashboard…*, then fading into the
      app. With Reduce motion, no flowing light or stripes.
- [ ] Dark theme: the window, taskbar and tray icons and the sidebar/About logos are the dark-mode icon; switching to
      the light theme swaps them back at once.

**Design fixes (1.4.2)**

- [ ] Collapse the sidebar (button, or a narrow window): every icon is whole and centered in its button; the logo and
      the expand button line up with them; an update badge, if showing, shows its whole icon.
- [ ] Hover the cards that glow (dashboard summary cards, the PC header, page gauge cards, banners, frame limiter): the
      edge lights in the color already inside the card (green Graphics, blue Processor, your accent on the banners),
      as a soft glow just outside the edge with a bright line on it; no purple or second shade; it doesn't flicker
      when the pointer crosses to a neighbouring card, and buttons right next to a card still click.
- [ ] Temperatures: on Graphics and Processor, and the dashboard's GPU and CPU temperature cards, an idle PC shows a
      blue gauge and glow; under load it turns teal-green, then amber-orange near the warning level and red at the
      limit, fading between them. Other gauges keep the green/blue/orange/red scale. Check the light theme too.
- [ ] Gaming › Frame limiter: open *Apply to*, then turn the mouse wheel over the list and over the page: the list stays
      attached right under its box and the page doesn't move (a long list scrolls itself). Same with the limit picker
      and a Settings picker.
- [ ] Devices and Gaming search: type a letter; the list is exactly as wide as the search box, directly under it,
      centered (also with *Settings › Devices › Pen and Windows Ink* / *Tablet settings* set to right-handed, if the PC
      has it). Scrolling the page with the list open keeps it under the box.
- [ ] Dark theme: the Start menu entry, the desktop shortcut (if installed) and Coreloom pinned to the taskbar show the
      dark-mode icon after a few seconds (a pinned taskbar button may need sign-out or *Restart Explorer* to redraw);
      switching to the light theme puts the usual icon back. A shortcut you gave a custom icon keeps it.

**Overlay and boosts (1.4.3)**

- [ ] No search box (Devices, Gaming) shows a *Ctrl K* badge any more; Ctrl+K and Ctrl+F still jump to the box.
- [ ] The renamed *Frame limiter*: the Gaming card, its notes and messages, the sidebar search and the overlay settings
      say *Frame limiter* (nothing says *Frame pacer*).
- [ ] The in-game overlay has no outline: just the smoked glass, with the same corners and look as the preview in its
      settings (compare them side by side at a few opacities and both themes).
- [ ] The frame-time graph in a game glides smoothly and reacts at once: it moves on evenly (not in steps), a camera
      flick or a loading hitch shows as a spike within a moment, and the line follows small changes in frame time.
      It looks the same as the preview (line, fill, amber/red spikes, dashed limiter line) and follows the overlay's
      color and opacity. Move the overlay to another corner or monitor: the graph moves with it, exactly in place.
- [ ] Performance with the overlay on: play a demanding game for a few minutes with the overlay off, then on (same
      scene): average FPS and 1% low stay within run-to-run variation, frame times stay as flat (the game's own graph,
      or NVIDIA's/CapFrameX if you have one), and there is no new hitching. Task Manager › Details: *Coreloom.exe*
      shows *Below normal* priority while the game is in front and *Normal* again when Coreloom is in front.
      The log (*Settings › Diagnostics › Open log folder*) says "Frame-time graph drawing on its own thread (precise
      timer)".
- [ ] Gaming › Boosts: each card shows a category pill (*Latency*, *Performance*, *Frame stability*, *Smoothness* in
      green, *Visual fidelity*); the three new cards also show a *Recommended* line and what they don't do.
- [ ] *G-SYNC & adaptive refresh* (NVIDIA, G-SYNC or G-SYNC Compatible display): on sets NVIDIA Control Panel ›
      *Set up G-SYNC* to *Enable for windowed and full screen mode*; the recommendation names your frame limit and refresh rate (e.g. 237 FPS on 240 Hz). Off puts it back to
      full screen only. With G-SYNC switched off for the display in the control panel, the card says so. On a display
      without variable refresh, the card explains it can't be used.
- [ ] *NVIDIA shader cache*: pick 10 GB / 100 GB / Unlimited in *Size*, switch it on: NVIDIA Control Panel ›
      *Shader Cache Size* shows that size; *Limit now* under the picker matches. Changing the size while it's on
      applies at once. Off puts *Shader Cache Size* back to *Driver Default*.
- [ ] *Auto HDR* (Windows 11): with an HDR display and HDR on, the switch matches *Settings › Display › Graphics ›
      Auto HDR*. With HDR off it says to turn on HDR; without an HDR display it explains it can't be used. *Boost now*
      leaves Auto HDR alone and the ring counts the other boosts (e.g. 8/8).

**New in 1.4.6**

*Memory clean-up and logs*

- [ ] Smart Optimize › *Free up RAM*: after it runs, *Process details* lists every process with PID, memory before and
      after and what was reclaimed (trimmed), or why it was skipped (the app in front, Coreloom, Windows' own) or
      failed (e.g. access denied for a protected process). The filter switches between all, trimmed, skipped and
      failed; the totals line says "ended 0" (Coreloom never ends a process).
- [ ] *Open logs* on that card opens *Settings › Logs* filtered to the memory entries; each line has a timestamp.
- [ ] *Settings › Logs*: search finds text in messages; the level and category boxes filter; *Clear filters* resets them;
      *Copy* puts the shown lines on the clipboard; *Open logs* opens the log folder. *Settings › Diagnostics* has
      *View logs*.

*Games added by hand*

- [ ] Gaming › *Add game*: pick a game's .exe (try one Coreloom didn't find, e.g. an itch.io or emulator game). The
      card suggests a name; change it, pick a picture or *Use icon* (the program's icon), and save. The game shows with
      "Added by you", in the list and the grid, and *Play* starts it.
- [ ] *Edit* on its card changes the name, program or picture; *Remove from my games* takes it out (and its own
      frame limit).
- [ ] While it runs, it shows as playing, the overlay counts its frames, and *Apply to* › the game sets a limit for
      its program (check in the NVIDIA Control Panel › Manage 3D settings › Program settings).

*Lighter while gaming (please measure on the Ryzen 4000 / GTX 1650 PC)*

- [ ] *Settings › Monitoring*: *Sensor refresh interval* offers 0.25 s, 0.5 s, 1 s, 2 s (lighter) and 5 s (lightest);
      *Lighter monitoring while gaming* is on, with its interval (1 s, 2 s or 5 s).
- [ ] With a game in front, the status bar shows the slower interval and "lighter while gaming"; the log says "Game
      in front: … lighter monitoring". Alt-Tab out: the interval comes back.
- [ ] *Settings › Diagnostics* shows what each part costs (sensor reading, window and overlay text, overlay graph,
      overlay frame counter) and the interval in effect.
- [ ] Overhead, in the same in-game benchmark or a fixed spot, three runs each, noting average FPS and 1% low
      (from the game, CapFrameX or PresentMon, not from Coreloom's own overlay):
      1. Coreloom closed (also from the tray);
      2. Coreloom running in the tray, overlay off;
      3. Coreloom with the overlay on (FPS and graph);
      4. as 3 with *Lighter monitoring while gaming* off and the refresh interval at 0.5 s (the heaviest setting).
      Send the numbers with a screenshot of *Diagnostics* taken during run 3.

*AMD processor card*

- [ ] On an AMD PC, Processor shows *AMD processor* with the architecture and codename (e.g. "Zen 2 · Renoir" for a
      Ryzen 4000), the type (laptop or desktop) and "Family … · model … · stepping …"; instruction sets and features
      as chips. Specifications list CPUID, instruction sets and features (on Intel too).
- [ ] The readings it reports show as tiles (as administrator with PawnIO: package power, and PPT, TDC, EDC, SoC,
      fabric and memory clocks on models whose SMU table LibreHardwareMonitor knows); the others are listed under
      *Not reported* with why.
- [ ] *Limits and tuning*: a desktop with a known model shows "Stock PPT … W"; a laptop says the maker sets the
      limits. After a heavy all-core load for a minute (a render or compile), PBO says "Looks off" or "Likely on".
      Curve Optimizer, Eco mode and manual clocks are unavailable, with where to change them; nothing can be changed.
- [ ] Every processor: *Boost* (over 100 % while boosting) and *Performance limit* (100 % "Not held back"; lower on
      battery or when hot) tiles.

*Adaptive frame pacing (NVIDIA)*

- [ ] Gaming › *Frame limiter*: *Adaptive frame pacing* with "Top pick", "Frame stability · 1% lows · Smoothness" and
      its description, off.
- [ ] Turn it on and play a library game for a minute or two: the status counts "Measuring: … of 45 seconds of play",
      then says it set the game's limit (e.g. "Set Cyberpunk 2077's limit to 90 FPS"), and the chip under *Apply to*
      shows "90 FPS · adaptive". The log (*Gaming*) has each step.
- [ ] Either the frame rate settles at the limit within ~15 s, or the status says the limit applies from the game's
      next start; restart the game and check it is limited.
- [ ] Under *Apply to* › the game, *Frame generation in …* (2×, 3×, 4×) changes the limits it picks (whole rendered
      frames; at least 80 FPS with 2×).
- [ ] A game you gave a limit yourself: the status says adaptive pacing leaves it alone.
- [ ] Turn it off: its limits are removed (chip gone, NVIDIA Control Panel back to the all-games limit).
- [ ] Without an NVIDIA driver: it suggests a limit to set in AMD Software instead of setting one.

**Fixes (1.4.5)**

- [ ] On the desktop (no game in front, FPS "—"), the overlay shows the graph's place as a flat line under the FPS line.
- [ ] In a game, the frame-time graph shows and glides under the FPS line, the same look as the preview; it follows
      the overlay when it moves to another corner or monitor.
- [ ] With the frame limiter on, there's no green pill with the limit next to FPS any more.

**Fixes (1.4.4)**

- [ ] Gaming › *G-SYNC & adaptive refresh* with G-SYNC on: no yellow warning under the card.
- [ ] Devices and Gaming search: clicking the empty box shows just *Start typing…* (no second line about Ctrl+K).
- [ ] Gaming › Your games: Assassin's Creed and any other game that showed initials gets its cover (or the store's wide
      picture) within a few seconds of opening the page, with *Finding pictures for your games…* showing meanwhile
      if it takes a moment. The pictures stay after a restart (they're in `%LOCALAPPDATA%\Coreloom\Artwork`). A game
      that isn't on Steam (e.g. Valorant) keeps its initials.

**Gaming**

- [ ] Frame limiter *Apply to*: pick a game, choose a limit and switch it on: the NVIDIA Control Panel (*Manage 3D
      settings › Program settings*) shows *Max Frame Rate* for that game's program (in NVIDIA's own profile for it, or a
      "Coreloom: game" profile), and nothing changes for other games. The game stays at that limit after a restart of
      it; the chip under the picker shows it, clicking the chip shows it, × removes it (and the Coreloom profile).
- [ ] *Gaming* sits right above *Graphics* in the sidebar; *Settings › Updates* is now the last item in Settings, below
      *About* (the update dot still shows there, and the sidebar's update badge still opens it).
- [ ] *Your games* lists the games installed with each launcher you use (Steam, Epic, GOG, Xbox / Game Pass, Ubisoft,
      EA, Battle.net, Riot). Nothing that isn't a game (Steamworks redistributables, Proton, SteamVR, Unreal Engine,
      dedicated servers) and no DLC as separate entries. Note any game that is missing or doubled.
- [ ] Steam games show their library art in both views; others show their cover, banner, icon or initials. *List*
      and *Grid* switch views (kept after a restart); search, and *Recently played / Name / Size* order, work.
- [ ] Games without a picture (Ubisoft, EA, Battle.net, Riot, GOG, Epic without a cover) get one by themselves a few
      seconds after the page opens, without asking; note any game that shows another game's picture (it should rather
      keep its initials).
- [ ] *Play* starts the game through its launcher and the game is **not** elevated (Task Manager › Details ›
      *Elevated* column says No). The folder button opens the game's folder.
- [ ] With a library game running and the page open: the banner shows *Now playing* with its FPS, the game's card or
      cover is marked *Playing*, and the frame limiter card shows FPS, 1% low and consistency close to the overlay's.
- [ ] Boosts: each switch matches Windows (Settings › Display › Graphics for HAGS and windowed games, Control Panel
      power plans, Settings › Gaming for Game Mode and *Record what happened*) and the NVIDIA Control Panel (*Power
      management mode*, *Shader Cache Size*, *Set up G-SYNC*). Flip each on and off and check it there.
- [ ] HAGS shows *Restart to finish* after switching it, and *On*/*Off* correctly after the restart.
- [ ] Ultimate Performance creates and activates the plan; switching it off goes back to the plan you had before.
- [ ] *Boost now* turns on everything that was off (except Auto HDR) and says how many; the ring shows e.g. 8/8.
- [ ] Frame limiter: switching it on sets *Max Frame Rate* in the NVIDIA Control Panel (global) to the shown limit (138
      on a 144 Hz display); a game started afterwards stays at that limit with flat frame times; switching it off
      sets *Max Frame Rate* back to Off. Picking another limit in the list changes it.
- [ ] Shader cache: the switch matches *Shader Cache Size* (10 GB or more) in the NVIDIA Control Panel; the sizes look
      plausible next to `%LOCALAPPDATA%\NVIDIA\DXCache` and `D3DSCache`. After a driver update the notice appears once
      and *Got it* hides it.
- [ ] On a PC without an NVIDIA card: the frame limiter, power mode, shader-cache and G-SYNC switches explain why they
      can't be used.

**In-game overlay**

- [ ] (1.4.1, 1.4.3) The overlay is a rounded glass panel without an outline; FPS is bigger and bold; a frame-time graph sits
      under the FPS line (after it in the one-line layout) and moves smoothly; stutters show as amber/red spikes. With
      the frame limiter limiting the game (all games or its own limit), a dashed green line on the graph shows the
      limit (since 1.4.5 there is no green mark next to FPS). The graph can be turned off in the overlay's settings, where the preview shows
      a sample trace.
- [ ] *Graphics*: the *In-game overlay* switch sits on the same line as *Clock limits*; the clock limits text wraps when
      the window is narrow. The switch turns the overlay on and off, and the End badge shows the hotkey.
- [ ] Turned on, the overlay appears in the top-left corner with FPS, GPU, CPU and RAM lines and a rounded, see-through
      background. It never takes focus, clicks pass through it, and it's not in the taskbar or Alt+Tab.
- [ ] Start a DirectX 11 or 12 game (windowed or borderless): FPS, frame time and 1% low match the game's own counter
      or the NVIDIA overlay within a few percent. Alt+Tab to the desktop: FPS turns to "—".
- [ ] Try a Vulkan game and an exclusive-fullscreen game; note whether FPS and the overlay show.
- [ ] Press End in the game: the overlay hides; again: it shows. The game still receives End. Before the overlay was
      ever turned on, End did nothing.
- [ ] *Customize*: every checkbox, position (eight cells), layout, slider, color dot and hex box changes the preview and
      the real overlay right away; the choices are kept after a restart. *Reset look* restores the defaults but keeps
      the switch and hotkey.
- [ ] Hotkey: click the hotkey button, press Ctrl+Shift+O; it shows "Ctrl + Shift + O" and works in a game. Esc while
      waiting cancels; *None* turns the hotkey off; *End* brings it back.
- [ ] With two monitors, the overlay follows the game to the monitor it runs on, and keeps its distance from the edge
      at different scaling.
- [ ] With the overlay on, Coreloom's CPU use stays low (Settings › Diagnostics), and turning it off stops the
      "Coreloom-FrameRate" trace session (`logman query -ets` doesn't list it).

**Detail pages**

- [ ] The GPU, CPU, Memory, Storage and Network pages are populated, and *Raw sensors* lists
      LibreHardwareMonitor's sensors.
- [ ] *GPU*: the Specifications card sits next to *VRAM usage* with the same height; its list scrolls inside the card,
      and once it reaches the end the wheel scrolls the page.
- [ ] *GPU* and *Processor*: long sensor lists show as two columns of similar height (a split group continues as
      "…, continued"); in a narrow window they stack. The *Raw sensors* switch still works.
- [ ] Nothing is shown under *Settings* at the bottom of the sidebar.
- [ ] Hovering a chart shows the crosshair and tooltip, and the 60s/5m/15m/1h selector and pause work.
- [ ] *Performance History*: picking metrics stacks the charts, hover is synchronized, and *Export CSV*
      writes a file.

**Vendor marks**

- [ ] The *Processor* page shows an *AMD* mark next to the CPU name, and the *GPU* page shows an *NVIDIA* mark next
      to the GPU name. Without logo files these are plain-text badges; that is expected.
- [ ] (Optional) Put `nvidia.png` and `amd.png` (official logos you downloaded, transparent PNG) in
      `%AppData%\Coreloom\Vendors`, restart, and check they replace the badges and read well in dark and light
      themes. Add `-on-dark` / `-on-light` versions if one theme is hard to read.

**Tray**

- [ ] The tray tooltip shows CPU/GPU/RAM.
- [ ] Right-click shows Open, Pause, Settings, Check for updates and Exit.
- [ ] Minimizing and closing hide to the tray (by default). *Exit* quits.

**Single instance and startup**

- [ ] Starting Coreloom a second time brings the first window forward instead of opening a second copy.
- [ ] *Settings → Desktop → Start with Windows*: after signing out and back in, Coreloom starts (minimized if
      chosen).
- [ ] Starting Coreloom by hand shows the Windows administrator prompt once; *Settings → Diagnostics* says
      "Running as administrator".
- [ ] With *Start with Windows* on, a *Coreloom startup* task exists in Task Scheduler (Run level: highest).
      After signing out and back in, Coreloom starts **without** a UAC prompt.
- [ ] Turning *Start with Windows* off removes that task, and no *Vigil* entry remains under
      *Settings → Apps → Startup*.
- [ ] If you used Vigil before: the first start imports your settings from `%APPDATA%\Vigil`.

**Memory and speed (leave it running)**

- [ ] Open *Settings → Diagnostics* and note the working set, managed heap and handle count. Click through every
      page twice, then leave Coreloom open for an hour: the managed heap should settle and the handle count
      should stay flat. Steady growth is a leak: export a diagnostic report and send it.
- [ ] Coreloom's CPU use in *Settings › Diagnostics* stays around 1 % or less at a 1 s interval; it should drop to about zero
      while the window is minimized to the tray.
- [ ] Page switches feel instant and charts keep scrolling smoothly. The log's first lines show the animation
      rate in use (for example "animation 240 fps").

**Icon**

- [ ] `Coreloom.exe` shows the Coreloom icon in File Explorer, the taskbar, Alt+Tab and the tray. If an old icon
      shows, run a freshly named copy or restart Explorer; Windows caches icons.

**Alerts**

- [ ] *Settings → Alerts*: add a rule, for example GPU temperature above 30 °C for 5 seconds.
- [ ] A Windows notification appears.
- [ ] The notification doesn't repeat until the value drops back and the cooldown passes.

**Sleep and drivers**

- [ ] Sleep and resume the PC. Values continue within about 10 seconds.
- [ ] (Optional) Update or reinstall the NVIDIA driver while Coreloom runs. GPU values come back by themselves.
      *Settings → Diagnostics* shows NVML recovering.

**Overhead**

- [ ] With the dashboard open, Coreloom's own usage in *Settings › Diagnostics* stays low (note the numbers).
- [ ] When minimized, Task Manager shows Coreloom's CPU close to zero.

## 5. Installer and portable build

```powershell
./build/build.ps1 -Version 1.0.0-beta.1 -Package
```

**Expect:** `artifacts/release` contains these files:

- `Coreloom-1.0.0-beta.1-win-x64-Setup.exe` (needs Inno Setup)
- `...-Portable.zip`
- `SHA256SUMS.txt`

Then check the following:

- [ ] The installer runs without an administrator prompt, installs to
      `%LOCALAPPDATA%\Programs\Coreloom` and starts Coreloom (Windows shows its administrator prompt for the app).
- [ ] The installer, the Start menu entry, the desktop shortcut and the uninstall entry show the Coreloom icon.
- [ ] Uninstalling from *Settings → Apps* removes the program (and, after one administrator prompt, the
      *Coreloom startup* task) and keeps your settings.
- [ ] Unzipping the portable build elsewhere and running it creates a `Data` folder next to `Coreloom.exe`.

## 6. Automatic updates (after publishing two releases)

Both kinds of copy update themselves: an **installed** copy (from `Coreloom-<version>-win-x64-Setup.exe`) runs the
new installer, a **portable** copy (the unzipped `…-Portable.zip`) unpacks the new zip and copies it over its own
folder. Test both. First check that each release has its files: the release page should list `…-Setup.exe`,
`…-Portable.zip` and `SHA256SUMS.txt`. If a release only shows "Source code", open *Actions → Release → Run
workflow*, enter its tag (e.g. `v1.0.0`) and run it; the files are added to the release.

**Getting onto the first version with *Install update*.** Versions up to 1.1.0 can't do this yet:

- An installed 1.0.0 or 1.1.0 updates itself with its own button (*Download and install* or *Restart to update*).
- A portable 1.0.0 or 1.1.0 only links to GitHub. Update it by hand once: download the new `…-Portable.zip`, exit
  Coreloom from the tray, and extract the zip over the old folder, replacing files (the `Data` folder with your
  settings stays). From then on it updates itself.

**Install update in action.** Use pre-releases so nobody else sees the test versions (only copies with *Include
pre-release versions* on do), and delete them afterwards.

1. In each copy, open *Settings → Updates*. **Expect:** *You're up to date*, *Update automatically* on.
2. Turn on *Include pre-release versions* and turn *Update automatically* **off** (so you can see the button before
   anything downloads).
3. Add a `## [<next version>-test.1]` section (e.g. `1.2.1-test.1`) to `CHANGELOG.md` with a sentence and a few
   bullets (see RELEASING.md), commit, then tag and push `v<that version>`. Wait for the Release workflow to finish.
4. Click *Check for updates*.

**Expect:**

- [ ] *Coreloom <version> is available*, then a *What's new* card with your sentence and bullets and two buttons:
      **Install update** and *Skip this version*. Nothing links to GitHub.
- [ ] The sidebar badge says *Update available*, and the tray menu has *Install update to <version>*.
- [ ] **Install update**: a progress bar while it downloads and verifies, then *Coreloom will close and reopen in a
      moment*. Coreloom closes and comes back by itself on the new version with the *Coreloom was updated to …*
      banner. *Settings → About* shows the new version.
      - Installed copy: a small Setup progress window appears in between.
      - Portable copy: the splash appears with *Updating Coreloom to <version>*.
- [ ] Portable copy: your settings (theme, dashboard layout) are unchanged, `portable.txt` is still there, and a
      few seconds after it reopens there's no `.coreloom-update` folder next to `Coreloom.exe` (it's hidden; turn on
      hidden items in Explorer to check).
- [ ] The tray item works too: publish another test version, then right-click the tray icon → *Install update to …*.

Then the automatic paths, with *Update automatically* back on (publish another pre-release for each):

- [ ] **At the next start:** let it download, then exit Coreloom from the tray and start it again. It installs
      before the dashboard opens and comes back on its own.
- [ ] **While idle:** let it download, close the window to the tray and leave the PC alone for about 20 minutes
      (no games or renders running). Coreloom updates and returns to the tray; a notification says it was updated.
- [ ] *Skip this version* hides the badge, and the skipped version isn't offered again by automatic checks.

When you're done, delete the test pre-releases (and their tags) on GitHub and turn *Include pre-release versions*
off again.

## 7. What to send back

Send the following:

- `diag-standard.md` and `diag-admin.md` (computer and user names are already redacted).
- The checklist above, with failures marked.
- Screenshots of anything that looks wrong.
- The log folder: *Settings → Diagnostics → Open log folder*.
