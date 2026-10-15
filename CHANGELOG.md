# Changelog

All notable changes to Coreloom (formerly Vigil) are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses
[Semantic Versioning](https://semver.org/). The release workflow copies the section that matches the tag
into the GitHub release notes, so keep each version's heading in the form `## [x.y.z]`.

## [Unreleased]

## [1.4.0] - 2026-10-14

A new Gaming tab brings your games and the settings that make them run best into one place: your library as cards or covers, one-click boosts for Windows and the graphics card, a frame pacer for even frame times on high-refresh displays, and shader-cache care for fewer stutters.

### Highlights

- **Gaming tab.** A new tab above Graphics. The banner shows how many boosts are on, the game you're playing with its frame rate, your display's refresh rate, the frame limit and the graphics driver, and *Boost now* turns on everything that's off.
- **Your games, as cards or covers.** Installed games from Steam, Epic Games, GOG, Xbox, Ubisoft Connect, EA app, Battle.net and Riot, as a list of cards or a grid of covers. Search, sort by recently played, name or size, start a game with *Play*, or open its folder. Covers come from your launchers' own picture caches; games without one can download it from their store, only when you ask.
- **Performance boosts.** Switches for hardware-accelerated GPU scheduling (HAGS), the Ultimate Performance power plan, Game Mode, Windows 11's optimizations for windowed games, turning off Xbox Game Bar's background recording, and the NVIDIA driver's *Prefer maximum performance* mode. Each switch shows whether it's on and can be turned off again; switching Ultimate Performance off goes back to the plan you had.
- **Frame pacer.** A frame-rate limit for every game, set in the NVIDIA driver's own high-precision limiter, so nothing is added to games and it's safe with anti-cheat. *Automatic* picks a limit just under your refresh rate (138 FPS on 144 Hz, 224 on 240 Hz) so G-SYNC or FreeSync stays engaged and frames never queue up. While you play, the card shows the game's FPS, 1% low and how evenly its frames arrive.
- **Shader cache.** An *Unlimited shader cache* switch makes the NVIDIA driver keep every pipeline it compiles, so games don't compile the same shaders again mid-play. The card shows how much is cached and tells you when a new driver has reset the caches.

### In detail

- Settings › Updates moved to the bottom of the Settings list, below About.
- HAGS takes effect after a restart; the switch says *Restart to finish* until then.
- Games start through their own launcher as you, not as administrator, even though Coreloom runs elevated.
- The frame pacer and shader-cache switch need an NVIDIA graphics card. On AMD graphics the card explains where AMD Software has the same settings.
- Coreloom doesn't pre-compile shaders by injecting into games, as some tools do: that is exactly what anti-cheat systems ban for.

### Feedback

Found a bug or a game that's missing from your library? [Open an issue](https://github.com/jerryubah/Coreloom/issues) and attach a CoreloomDiag report.

## [1.3.5] - 2026-10-14

Smart Optimize's cards now fit their content with no empty space, and the Devices page reads more clearly.

### Highlights

- **Smart Optimize cards fit their content.** The four cards stack in two columns, Cleanup over Free up memory and Protection over Speed up, each only as tall as what's in it. The bottoms of the two columns line up, and Speed up's list of startup apps uses the extra room. In a narrow window the cards drop to one column.
- **Smart Optimize moved up.** It now sits above Performance History in the sidebar.
- **Search starts where you'd expect.** The grey hint in the device search box disappears as soon as you click in it, so the blinking cursor sits plainly right after the magnifier, where your typing begins.
- **Clearer headings on the Devices page.** "Bluetooth devices" and "Wired and USB devices" are now larger section headings with a short subtitle, a step above the group names like "Keyboards and mice".

### Feedback

Found a bug or a device that's listed wrong? [Open an issue](https://github.com/jerryubah/Coreloom/issues) and attach a CoreloomDiag report.

## [1.3.4] - 2026-10-14

The Devices page lines everything up on one grid and sorts wired devices more sensibly, and the Smart Optimize banner gets a vivid accent gradient with a ring you can actually see.

### Highlights

- **Wired devices line up.** Each group of wired devices (keyboards and mice, audio, displays, lighting, storage…) is now a column on the same grid as the Bluetooth cards above, and groups stack under each other to fill the width instead of leaving most of the page empty.
- **Smarter grouping.** A device is only listed under *Controllers* when it's really a game controller, so RGB "LED controllers" go under the new *Lighting* group (with lights like a Litra Beam), a monitor's USB link goes under *Other devices*, audio interfaces like a Scarlett go under *Audio*, and USB controller cards aren't listed as devices at all. Devices that report a garbled name get a readable one.
- **Bluetooth switch and search.** The Bluetooth switch sits right next to the Bluetooth title. The search box is wider, the text you type starts right after the magnifier, and clicking anywhere outside the box (or pressing Esc) leaves it. The same goes for every text box in the app.
- **Smart Optimize banner.** The banner washes in your accent color as a gradient, and the ring is drawn as a glowing sweep from a lighter shade through your accent to a warmer one. The ring is lit before you scan, a bright comet circles it while working, and it turns green, orange or red with the result. The main button shares the gradient.

### In detail

- The gradients are made from the accent color you pick in Settings, so they follow it and the light and dark themes.
- *Scan again* steps back to a plain button when it sits next to *Clean up and optimize*.
- The word under "Ready when you are" in the ring is gone; it only repeated the headline.
- The Eject button on a drive is now a compact icon button (with a tooltip), leaving more room for the drive's name.

### Feedback

Found a bug or a device that's listed wrong? [Open an issue](https://github.com/jerryubah/Coreloom/issues) and attach a CoreloomDiag report.

## [1.3.3] - 2026-10-14

The Bluetooth cards on the Devices page are now compact, so more of your devices fit on screen at once.

### Highlights

- **Compact device cards.** Bluetooth cards are about half as wide and a little shorter. They keep the same width however many devices you have and wrap onto new rows, so a wide window now shows five or six devices in a row instead of three.
- **Status in one place.** The connected pill now sits in the card's status row next to the battery, instead of repeating *Connected* twice. Disconnected devices show *Last connected …* in the pill, and the pill reads *Connecting…* or *Disconnecting…* while a request is running.
- **Room for long names.** Device names use the card's full width, and hovering a name or the status shows it in full when it's cut short.
- **Wired devices match.** The wired and USB device tiles use the same compact width.

### Feedback

Found a bug or a device that's listed wrong? [Open an issue](https://github.com/jerryubah/Coreloom/issues) and attach a CoreloomDiag report.

## [1.3.2] - 2026-10-14

A new Devices page shows everything connected to your PC and lets you connect and disconnect Bluetooth devices with one click.

### Highlights

- **Devices page.** A new tab below Storage lists your Bluetooth devices as cards and your wired devices grouped by type: keyboards and mice, controllers, audio, displays, cameras, storage and more.
- **Connect and disconnect.** Every Bluetooth card shows what the device is, a live connected indicator, and a Connect or Disconnect button. Connect brings back headphones and speakers that switched to your phone or another computer.
- **Battery at a glance.** Devices that report their battery show it on the same green-to-red scale as the gauges, and a note warns when a connected device runs low.
- **Bluetooth switch and pairing.** Turn Bluetooth on or off right on the page; *Pair a new device* opens Windows' Bluetooth settings.
- **Safely remove drives.** USB drives get an Eject button, and wired devices with a driver problem are flagged.

### In detail

- Connect for headphones and speakers sends the same request as the Connect button in Windows' Bluetooth settings. Keyboards, mice and controllers are called too, though most reconnect by themselves when you turn them on or press a button.
- Cards update as soon as Windows reports a device connecting or disconnecting, and every few seconds while the page is open. Connected devices come first; others show when they were last connected.
- Search finds devices by name or type in both lists.
- Devices paired over both Bluetooth Classic and Bluetooth LE show as one card.
- If a device doesn't answer, the card says why and what to try. Ejecting a drive that's in use says which app is holding it when Windows tells.

### Feedback

Found a bug or a device that's listed wrong? [Open an issue](https://github.com/jerryubah/Coreloom/issues) and attach a CoreloomDiag report.

## [1.3.1] - 2026-10-14

Smart Optimize cleans up, protects and speeds up your PC in one click, the Storage and Network pages can test drive and internet speed, and every gauge now speaks the same colors.

### Highlights

- **Smart Optimize.** A new page below Performance History. *Smart Scan* finds junk files, checks Microsoft Defender's protection and threats, memory and startup apps; *Clean up and optimize* then frees the space, removes threats, trims memory and refreshes the network cache. Each part has its own card for more control.
- **One color scale for every gauge.** Green when idle or cool, blue in the normal range, orange when heavy and red at the limit, gliding from one to the next as the value moves. The memory and drive rings use it too.
- **Drive speed test.** On the Storage page: sequential and random read and write speeds, like CrystalDiskMark, with live gauges.
- **Internet speed test.** On the Network page: download, upload, ping and jitter, with a live gauge.
- **Health for memory and storage.** The Memory and Storage pages get health cards, and the Overview's *System health* card now shows four parts.

### In detail

- Cleanup looks at temporary files, Windows temporary files, browser caches (Chrome, Edge, Brave, Vivaldi, Opera, Firefox), crash dumps and error reports, the thumbnail cache, shader caches, Windows Update downloads and the Recycle Bin. Only those folders are touched: never through links, never temp files younger than a day, never files in use. The last four are off by default and say why.
- Protection uses Microsoft Defender itself: its status and threats, *Quick scan*, *Update definitions* and *Remove threats*. If another antivirus is in charge, the card names it.
- *Free up RAM* trims memory background apps aren't using (never the app in front), with an option to clear the standby cache too.
- *Speed up* lists your startup apps with switches (the same ones as Task Manager, so turning an app off doesn't uninstall it) and offers *Flush DNS cache* and *Optimize drives*.
- The drive test uses a temporary file of 256 MB, 1 GB or 4 GB that bypasses the file cache and is deleted afterwards, also if Coreloom closes mid-test. Results are colored by what's fast for that kind of test.
- The internet test uses Cloudflare's speed test servers and shows the server location and your provider. It's the only time Coreloom contacts a server other than GitHub, and only when you start it.
- Memory health checks memory use and how close virtual memory (the commit charge) is to its limit; storage health checks free space, drive wear and drive temperature.
- The Memory page's tiles now fill the height next to the memory donut, all the same size, and the cards line up.

### Feedback

Found a bug or a sensor that reads wrong? [Open an issue](https://github.com/jerryubah/Coreloom/issues) and attach a CoreloomDiag report.

## [1.3.0] - 2026-10-13

A new in-game overlay shows your frame rate and sensors on top of games, and health cards tell you at a glance how your PC is doing.

### Highlights

- **In-game overlay.** See FPS, frame time and 1% lows next to GPU load, temperature, power and fan speed, CPU load and temperature, RAM and more, on top of your games, much like the NVIDIA or Steam overlay.
- **On from the Graphics page.** The overlay switch sits next to the clock limits. *Customize* picks what it shows, where it sits and how it looks: text size, thickness, color and opacity, background color and opacity, and rounded corners, with a live preview.
- **A hotkey.** Press End, or a key or combination of your choice, to show or hide the overlay, also in the middle of a game.
- **Health at a glance.** New health cards on the Overview, Graphics and Processor pages rate the graphics card, processor, and memory and drives as Excellent, Good or Needs attention, and say why.

### In detail

- Frame rates come from Windows' own graphics events, the way PresentMon and the Xbox Game Bar measure them, so nothing is injected into games and anti-cheat isn't involved. They work in DirectX 9 to 12 games, and in Vulkan and OpenGL games when the graphics driver presents them through DirectX (NVIDIA's default).
- Eight positions (corners, top and bottom center, left and right middle), stacked lines or a single line, labels on or off, distance from the edge, warning colors for temperatures and load, and a text shadow.
- The overlay appears on the screen of the game you're playing, lets clicks through to the game and never takes focus. It shows over windowed and borderless games and most fullscreen ones; if a game in true exclusive fullscreen hides it, switch the game to borderless.
- The hotkey starts working once you've turned the overlay on for the first time, so End does nothing for people who don't use it. The key still reaches the game.
- Health looks at the last minute, so a brief spike doesn't change it: temperatures against your alert levels, clock slowdowns from heat or power problems, hot spot and memory temperatures, fans that stop while warm, memory and disk space, and drive wear.
- The Overview's *System health* card can be moved, resized or hidden like the other cards.

### Feedback

Found a bug or a sensor that reads wrong? [Open an issue](https://github.com/jerryubah/Coreloom/issues) and attach a CoreloomDiag report.

## [1.2.7] - 2026-10-13

Arrange the Overview your way: move and resize its cards right on the dashboard.

### Highlights

- **Drag cards into place.** Drag any card or panel on the Overview to move it. The others make room as you go, and it snaps into the nearest spot when you let go.
- **Resize cards.** Drag a card's bottom-right corner to make it wider or taller, one grid step at a time. Make a chart full width, a summary card twice as wide or the storage list taller.
- **A scroll bar you can grab.** The page scroll bar is wider and sits clear of the window's edge, so you no longer get the resize arrow. It widens further when you point at it and slims down again when you use the mouse wheel.
- **Hide the PC picture.** A new switch in *Customize* shows or hides the PC picture in the header. It's on by default.

### In detail

- Summary cards and panels now share one grid, so you can mix them: put the CPU temperature card next to the temperatures chart, for example. The header with your PC stays at the top.
- Right-click a card to make it wider, narrower, taller or shorter, reset its size or hide it.
- Esc cancels a move or resize. Hold a card near the top or bottom of the page to scroll while you drag.
- In a narrower window the grid has 4, 2 or 1 columns and wide cards fit the width; your sizes come back when the window is wide again.
- *Customize › Reset layout* now resets sizes too. With the picture hidden, its lighting colors are greyed out.

### Feedback

Found a bug or a sensor that reads wrong? [Open an issue](https://github.com/jerryubah/Coreloom/issues) and attach a CoreloomDiag report.

## [1.2.6] - 2026-10-13

Customizing the Overview now happens right on the Overview.

### Highlights

- **Customize in place.** *Customize* opens a card over the Overview instead of taking you to Settings. Pick the PC's lighting, show, hide and reorder cards, and watch the dashboard change behind it.
- **Lighting moved into Customize.** The glow colors (blue, purple, red, green, rainbow or your accent) now sit in the *PC lighting* section of the Customize card, so the header is cleaner.

### In detail

- Close the card with its X, *Done*, Esc or a click outside it. Every change is saved as you make it.
- *Reset layout* brings back every card and panel in the default order.
- The *Dashboard* section in Settings is gone; everything it had is in the Customize card.

### Feedback

Found a bug or a sensor that reads wrong? [Open an issue](https://github.com/jerryubah/Coreloom/issues) and attach a CoreloomDiag report.

## [1.2.5] - 2026-10-13

The PC on the Overview gets a premium new look: pick its glow color and watch its fans turn with yours.

### Highlights

- **A new PC picture.** The Overview header now shows a rendered PC with glass, mesh and lit fans, with your graphics card's model on the card and your CPU temperature on the pump screen.
- **Pick the glow.** Use the dots next to the PC to light it blue, purple, red or green, cycle through a rainbow, or match your accent color.
- **Fans that follow yours.** The PC's fans turn with your real fan speeds: the top fans follow your CPU fan, the bottom ones your case fans.

### In detail

- The fans speed up and slow down with the readings, blur as they go faster and stop when your fans stop. Hover the PC to see which readings they follow.
- Fan speeds come from the motherboard and need PawnIO. Without them, the fans turn at an idle pace.
- The fans only turn while the Overview is on screen and stand still with *Reduce motion* on, so Coreloom stays light in the background.

### Good to know

- A picture of your own build (`pc.png` in `%AppData%\Coreloom`) still replaces the render. It's shown as it is, without the color picker or turning fans.

### Feedback

Found a bug or a sensor that reads wrong? [Open an issue](https://github.com/jerryubah/Coreloom/issues) and attach a CoreloomDiag report.

## [1.2.0] - 2026-10-13

Updating Coreloom now takes one click, and portable copies can update themselves too.

### Highlights

- **One-click updates.** When a new version is out, click *Install update* in *Settings › Updates* or the tray menu and Coreloom does the rest.
- **Portable copies update themselves.** Copies from the portable zip now update in place and keep their settings in the `Data` folder.

### In detail

- *Install update* downloads the new version, checks it against the release's SHA-256 checksums, closes Coreloom, installs the new version over the old one and opens it again. No more trips to GitHub.
- Automatic updates now work the same way for installed and portable copies: they download in the background and install the next time Coreloom starts or while your PC is idle.
- If an update can't replace a file, every change is undone and the previous version opens again.

### Good to know

- **Installed copies of 1.1.0** pick up this update on their own, or right away with *Check for updates*.
- **Portable copies of 1.1.0 or earlier need one last update by hand.** Replace the folder's files with the new portable zip below, keeping the `Data` folder. After that, *Install update* works for them too.
- Portable copies unpack updates into a hidden `.coreloom-update` folder next to `Coreloom.exe`, so keep yours in a folder you can write to.

### Feedback

Found a bug or a sensor that reads wrong? [Open an issue](https://github.com/jerryubah/Coreloom/issues) and attach a CoreloomDiag report.

## [1.1.0] - 2026-10-13

Coreloom now keeps itself up to date, and the Overview opens with your PC and its specs at a glance.

### Added
- **Automatic updates.** Coreloom downloads new versions in the background, checks them against the release's SHA-256 checksums and installs them the next time it starts, while your PC is idle, or right away with *Restart to update*. You can turn this off in *Settings › Updates*.
- **Your PC at a glance.** The Overview opens with a header for this PC: the motherboard name, the graphics card and the key specs (processor, cores, memory, storage, graphics memory, driver, Windows and BIOS), next to a drawing of the build that follows your hardware (memory sticks, graphics card size and model) and your accent color, with the CPU temperature on the pump screen. You can show a render or photo of your own build instead: save it as `pc.png` in `%AppData%\Coreloom`.
- **What's new.** Every update comes with a short summary of the release: in *Settings › Updates*, in the notification, and in a banner after Coreloom has updated.
- **Update badge.** The sidebar, the *Updates* section in Settings and the tray menu show when a new version is available, downloading or ready to install.
- **Redesigned Settings › Updates** with *Check for updates*, download progress, *Restart to update*, *Skip this version* and the update preferences in one place.

### Security
- **Safer installs.** A downloaded installer only runs if its SHA-256 matches a hash that only administrators can change, it stays locked from that check until it starts, and downloads never write through links placed in the updates folder.

### Changed
- **Tidier Graphics page.** Specifications now sit next to the VRAM chart at the same height as the charts and scroll inside the card when the list is long.
- **Sensors in two columns.** Long sensor lists on the Graphics and Processor pages are split into two balanced columns instead of one long card, and cards fit their content instead of stretching to their neighbour.
- After a background update, Coreloom returns to the notification area and says it was updated. If an update can't finish, the installer rolls back and starts the previous version again, and Coreloom stops retrying that version on its own.
- Portable copies are told about new versions and link to the release page; they're never changed in place.
- Coreloom's own CPU use is no longer shown under Settings in the sidebar; it's in *Settings › Diagnostics*.

### Fixed
- The release workflow adds its installer, portable zip and checksums to a release that already exists (for example one created on GitHub's website) instead of failing, and can be re-run for an existing tag from the Actions tab.

## [1.0.0] - 2026-10-13

**The first release of Coreloom**: a native Windows 11 hardware monitor with a calm, modern dashboard for your GPU, CPU, memory, storage and network.

Coreloom only *reads* sensors. It never changes clocks, voltages, fan curves or firmware, never installs drivers or services, and never sends your data anywhere.

### Highlights

- **Overview dashboard.** Six live cards (GPU utilization, GPU temperature, CPU utilization, CPU temperature, RAM and GPU power), each with a radial gauge, a sparkline, threshold colors and min/max/avg. Below them sit large charts and memory, storage and network panels. Hide and reorder the cards; your layout is saved.
- **Detail pages** for the GPU, CPU, memory, storage and network:
  - GPU clocks, VRAM, power and power limit, fans, hot spot and memory junction temperatures, clock-limit reasons and video engine load, with multi-GPU selection;
  - per-thread CPU graphs, a per-core table and top processes;
  - installed memory modules;
  - per-disk activity, temperature and volumes;
  - per-adapter network traffic.
- **Performance History.** Compare up to six metrics on stacked, time-aligned charts with synchronized hover. Pick a 60 s, 5 min, 15 min or 1 h window and export to CSV.
- **Honest data.** Every value is labelled *measured* or *derived* and names its source (NVML, Windows performance counters or LibreHardwareMonitor). Missing sensors tell you why they're missing. Nothing is estimated or faked.
- **Smooth, sharp interface.** Inter and JetBrains Mono typography, animation that keeps up with your display's refresh rate (up to 240 Hz), and charts that never hide a peak. Dark and light themes with a custom accent color, Mica, Snap Layouts, a collapsible sidebar and a reduced-motion mode.
- **Startup splash** that fades into the dashboard the moment it's ready.
- **Alerts.** Per-metric rules with a sustain time, hysteresis and a cooldown, delivered as Windows notifications.
- **Tray and startup.** A tray icon with a live tooltip, minimize or close to tray, single instance, and *Start with Windows*.
- **In-app updates** from GitHub Releases. Downloads resume if interrupted and are SHA-256 verified before anything runs. You can skip a version or opt in to pre-releases.
- **Light on your system.** Sensors are polled on their own thread (every second by default, adjustable), nothing is drawn while Coreloom is minimized, and its own CPU and memory use is shown in the sidebar.
- **CoreloomDiag**, a command-line companion that writes a Markdown report of your hardware, every sensor and its source, and Coreloom's own overhead.

### Hardware support

- **NVIDIA GPUs** through NVML, which comes with the NVIDIA driver.
- **AMD and Intel GPUs** through LibreHardwareMonitor.
- **CPU temperatures, package power and motherboard sensors** need the free, open-source [PawnIO](https://pawnio.eu) driver. Coreloom never installs it for you: install it yourself, then restart Coreloom. Everything else works without it, and the affected cards explain what's missing.

### Requirements

- Windows 11 x64, version 23H2 or later.
- Nothing else. The installer and the portable build include the .NET runtime.

### Good to know

- **Coreloom runs as administrator** so it can read the sensor driver. Windows asks for approval when you start it by hand; with *Start with Windows* on, it starts at sign-in without a prompt. It still only reads sensors.
- **Windows SmartScreen may warn you on first run** ("Windows protected your PC") because this release isn't code-signed yet. Choose *More info* → *Run anyway*, and compare the file with `SHA256SUMS.txt` if you'd like to confirm it's genuine.
- **No telemetry.** The only network request Coreloom makes is the optional update check to GitHub.
- **Vendor logos aren't bundled**, since they're trademarks. The CPU and GPU names show a text badge, or the official logo if you add the file yourself (see `assets/vendors/README.md`).
- Alert thresholds are reminders, not safety limits. Modern CPUs and GPUs protect themselves by throttling.

### Feedback

Found a bug or a sensor that reads wrong? [Open an issue](https://github.com/jerryubah/Coreloom/issues) and attach a CoreloomDiag report. It's the fastest way to get it fixed:

```powershell
CoreloomDiag.exe --samples 30 --out coreloom-report.md
```
