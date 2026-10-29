# Changelog

All notable changes to Coreloom (formerly Vigil) are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses
[Semantic Versioning](https://semver.org/). The release workflow copies the section that matches the tag
into the GitHub release notes, so keep each version's heading in the form `## [x.y.z]`.

## [Unreleased]

## [1.8.2] - 2026-10-29

A background of your own behind frosted glass: dark colors, gradients and pictures, yours too, with the sidebar and every card made of the same glass and a softer shadow. And System health is exactly as tall as what it says.

### Highlights

- **Your background, all around.** *Settings › Appearance › Background* fills the whole window, behind the sidebar too, with a dark color (eight to pick from, or any other), a dark gradient (Coreloom's own follows your accent color) or a picture: six of Coreloom's own (Silk, Aurora, Nebula, Dunes, Bloom and Light trails) or one of yours.
- **Frosted glass, the same everywhere.** The sidebar and every card are now one material: frosted glass that shows a blurred copy of the background right behind it. Cards take on the colors of the picture or gradient they sit on, and the look is the same from the sidebar to the last card.
- **Pictures under a frosted layer.** A picture sits under a frosted layer so it never drowns out your readings. *Picture opacity* sets how strongly it shows through, *Frosted glass blur* how much the layer blurs it.
- **System health fits its content.** With its rows to itself, the card is exactly as tall as what it says. Placed beside a taller card, it takes that card's height and spreads its parts out to fill it.
- **A softer shadow.** The glass casts a subtle shadow now, light and close, and only around its edge, so it never darkens the glass itself. Dialogs and a card being dragged lift with a softer shadow too.
- **Storage fills its card.** Under the drives, the Overview's Storage card now graphs reading and writing over time, so it no longer has empty space beside the taller Network card.
- **Translucent (Mac) and Mica (Windows).** Seeing the desktop through the window is now one of the backgrounds, for the whole window rather than the sidebar alone.

### In detail

- Your own picture is copied into Coreloom's settings folder, so it stays when the original moves. On a Mac, HEIC pictures are converted with macOS's own sips. Nothing is sent anywhere.
- The light theme shows a pale version of the background you chose, so it stays light.
- The background is drawn once for the window's size, away from the screen's work, and each card shows its part of it, so scrolling and live charts cost no more than before.
- Insets, text boxes and gauge tracks inside the cards are see-through too, so they sit in the glass instead of on it.
- The website and the README show the new look.
- Have 1.8.1? Choose *Check for Updates* (the Coreloom menu on a Mac, *Settings › Updates* on Windows), then *Install update*.

### Feedback

Tried your own picture behind the glass? [Open an issue](https://github.com/jerryubah/Coreloom/issues) with a screenshot if something looks off.

## [1.8.1] - 2026-10-28

Readings in your Mac's menu bar, your own wallpaper on the Overview's MacBook, Bluetooth devices you can rename, and an update that's hard to miss.

### Highlights

- **Mac: readings in the menu bar.** The new *Settings › Menu bar* puts the readings you pick in the menu bar itself, each as its own item: processor and graphics load, memory, their temperatures, the fastest fan, network, storage, battery and power. Each can show its name and a slim bar, and a preview shows them before you look up.
- **Mac: your wallpaper on the MacBook.** The Overview's MacBook is a new, photo-like picture whose screen shows your Mac's own desktop picture, with the screen's light falling on the keyboard. *Customize › Show my desktop picture on the screen* switches back to Coreloom's picture in the lighting color.
- **Mac: one background, a sidebar of glass.** The window's background now runs all around, and the sidebar is a pane of glass on it, back at the top with the close, minimize and zoom buttons inside it. The collapse button sits on their line, and every icon is centered on its title.
- **An update is easy to spot.** When a new version is out, Settings gets a red dot in the sidebar and the button above it says *New update available* in Coreloom's gradient. The *What's new* card in *Settings › Updates* is redesigned: the version up front, then each change at a glance, then *Install update*.
- **Rename your Bluetooth devices.** Click the pencil on a device's card (or right-click the card) and give it a name you'll recognize. Coreloom keeps the name; Windows' and macOS's own Bluetooth settings keep the device's. Leave the name empty to go back to the device's own.
- **Leave a search box with a click.** Clicking anywhere outside a search or text box, the title bar included, or pressing Esc, leaves it, on Windows and on a Mac.

### In detail

- Mac: GPU benchmark compute scores are ten times what they were, so they read like the graphics scores. Results you saved earlier are converted, so comparisons stay fair. Windows scores are unchanged.
- The *Rainbow* lighting swatch shows the whole spectrum, and on a Mac the picture's glow is a rainbow too.
- Mac: the menu bar readings use Coreloom's own readings and refresh with them; macOS colors them for light and dark menu bars, and ⌘-drag puts them in any order. Clicking one opens *Open Coreloom*, *Menu Bar Settings…* and *Quit Coreloom*.
- Mac: the desktop picture is read on your Mac only. A picture kept in Desktop, Documents, Downloads or another app's folder shows once you choose *Show it* in Customize, since macOS asks before an app reads those folders.
- The website shows Coreloom on the computer you're reading it on, Mac or Windows, with the right download first, and the phone app on an iPhone.
- Have 1.8.0? Choose *Check for Updates* (the Coreloom menu on a Mac, *Settings › Updates* on Windows), then *Install update*.

### Feedback

Tried the menu bar readings or the new MacBook? [Open an issue](https://github.com/jerryubah/Coreloom/issues) with your Mac's model and a screenshot if something looks off.

## [1.8.0] - 2026-10-27

Your in-game overlay on your phone: the phone app's new Overlay tab shows the game's frame rate, its frame-time graph and the readings the in-game overlay shows, even with the in-game overlay off. Errors and warnings now leave by themselves after five seconds, and on a Mac the sidebar sits below the window's buttons.

### Highlights

- **The Overlay tab on your phone.** It replaces the Memory tab (memory is now at the top of the More tab). It shows what the in-game overlay shows, in its colours: the game in front, its FPS and frame time, the frame-time graph with stutters marked, and your graphics card, processor and memory readings. Tap the full-screen button to use the phone as a second screen beside your monitor, upright or sideways; the screen stays on while it's full screen.
- **Your phone, your choice of readings.** The Overlay tab starts with what the in-game overlay shows, and offers every reading this computer has, so the phone can show more (or less) than the screen does. What you choose stays on the phone; *Same as my computer* goes back to the in-game overlay's list.
- **On even when the in-game overlay is off.** The phone's overlay has its own switch. With the in-game overlay off on the computer, the phone still shows the frame rate and readings.
- **Update notice on the phone.** When your computer has a newer Coreloom, a bar at the top of the phone app says so (install it on the computer, in *Settings › Updates*). After the computer has updated, the bar offers *Reload* to bring the phone app up to date too. × closes it for that version.
- **Messages leave after five seconds.** Errors and warnings no longer stay on screen: a switch Windows refused, a failed update check, a device that couldn't connect, a benchmark that stopped, Connect's firewall note, the limited-readings banner and the like disappear after five seconds, on Windows, on a Mac and on the phone. What something *is* stays (a "Restart to finish" status, a gauge that has no reading). Messages that used to open a dialog, such as *Export failed*, now show over the page and leave too.
- **Mac: the sidebar sits below the window's buttons.** The close, minimize and zoom buttons no longer overlap the sidebar, wide or narrow; the sidebar's collapse button moved next to the Coreloom name.

### In detail

- The computer measures the frame rate only while a phone shows its Overlay tab, and sends it twice a second with each frame's time, so the phone's graph moves as the game drew. On Windows it's measured from the graphics events Windows already records, as the in-game overlay does; nothing is added to games. A Mac can't measure another app's frame rate, so there the Overlay tab shows readings only.
- Phones still only read. Choosing readings on the phone changes nothing on the computer.
- The CPU page's note about missing sensors, the Gaming page's driver notice and the low-battery banner on Devices each show for five seconds when they apply; the driver notice counts as seen afterwards.
- Have 1.7.0? Choose *Check for Updates* (the Coreloom menu on a Mac, *Settings › Updates* on Windows), then *Install update*. Then reload Coreloom on your phone (the phone's new bar offers it).

### Feedback

Tried the Overlay tab with a game? [Open an issue](https://github.com/jerryubah/Coreloom/issues) with the game, your phone and its browser, and a screenshot if something looks off.

## [1.7.0] - 2026-10-27

Coreloom on your phone: see your PC's or Mac's temperatures, load and power live on your phone, over your own network. Turn it on in the new *Settings › Connect*, scan the QR code with your phone's camera, and that's it: no app store, no account, no cloud.

### Highlights

- **Coreloom on your phone.** Your computer's readings in Coreloom's own design on an iPhone, Android phone or tablet: an Overview with the GPU and CPU gauges and health check, then Graphics, Processor (every core), Memory and More (drives, network, battery and your computer's specs), with two-minute live charts and light and dark themes. It's served by Coreloom itself and opens in the phone's browser; *Add to Home Screen* keeps an icon that opens it full screen.
- **Settings › Connect.** One switch lets phones connect. While the section is open it shows a QR code that pairs a phone the moment it's scanned, or a six-digit pairing code to type with the address. Paired phones are listed with when each last connected, and *Remove* unpairs one.
- **Private by design.** Connect answers only on private networks like your home Wi-Fi, and only phones you've paired. Pairing uses a one-time key exchange, and every reading is encrypted between your computer and your phone; nothing goes through the internet or any server. Phones can only look: nothing on your computer can be changed from a phone.

### In detail

- Connect is off until you turn it on. Pairing is open only while *Settings › Connect* is showing, each code and QR link works once, and a code changes every five minutes; five wrong tries replace it, and ten in a minute pause pairing for a minute.
- Windows: turning Connect on adds one Windows Defender Firewall rule, *Coreloom Connect*, for Coreloom on private and domain networks only (never public ones); turning Connect off or uninstalling Coreloom removes it. If your network is set to Public, Settings › Connect says so and opens Windows' network settings.
- Mac: no permission is needed to let phones connect. If the macOS firewall is on, macOS may ask once whether Coreloom may accept incoming connections.
- Coreloom listens on port 47101. While no phone is watching, Coreloom doesn't prepare any readings for one.
- Have 1.6.x? Choose *Check for Updates* (the Coreloom menu on a Mac, *Settings › Updates* on Windows), then *Install update*.

### Feedback

Tried it on your phone? [Open an issue](https://github.com/jerryubah/Coreloom/issues) with the phone and browser you used, and a screenshot if something looks off.

## [1.6.1] - 2026-10-25

On a Mac, the Overview's top card now always fits your Mac's specs.

### Highlights

- **The Overview fits on smaller Mac screens.** In a narrower window, the card with your Mac's picture and specs kept the height it had before the specs came in, so the last specs ran under the cards below it. It now grows to fit them, at any window size.

### In detail

- Have 1.6.0? Choose *Check for Updates…* in the Coreloom menu (or let the automatic check find it), then click *Install update* in *Settings › Updates*, or simply quit and reopen Coreloom.
- Windows: nothing changes in the app.

### Feedback

Something not right on your Mac? [Open an issue](https://github.com/jerryubah/Coreloom/issues) with a screenshot and the report from *Settings › Diagnostics › Export diagnostic report*.

## [1.6.0] - 2026-10-25

Coreloom now runs on the Mac, on Apple silicon and Intel with macOS 14 Sonoma or later. Every page of the Windows app is made for the Mac: its readings, its menu bar and Dock, its settings and its words. Download it from the README's buttons or the new website, drag it into Applications, and it keeps itself up to date like the Windows app.

### Highlights

- **A native Mac app.** The same pages and dashboard as on Windows, on macOS's own AppKit and Metal: a window with the traffic lights in its sidebar, a full app menu (⌘, for Settings, ⌘1–⌘9 for the pages, ⌃⌘S for the sidebar, Hide, Minimize, Zoom and Full Screen), a menu bar item with live readings, an icon on Apple's icon grid, and light, dark or the Mac's own appearance.
- **The Mac's own readings.** Apple silicon's performance and efficiency cores and their clocks, the GPU's load, renderer and tiler, the power of the CPU, GPU, Neural Engine and the whole Mac (IOReport), temperatures and fans (the SMC and the temperature sensors), memory pressure with app, wired and compressed memory and swap, batteries, drives and network. On Intel Macs, the SMC's temperatures, fans and power. Memory reads in binary units, as Activity Monitor shows it; storage in decimal units, as Finder does.
- **A picture of your Mac.** The Overview's header shows a MacBook, iMac, Mac mini, Mac Studio or Mac Pro, whichever yours is, lit in the color you pick (no Apple logo is drawn).
- **Gaming on a Mac.** Your Steam, Epic Games and Applications-folder games as cards or covers with Play, games added by hand (pick the app), and Game Boost with the switches that matter on a Mac: Game Mode, full power on the charger (Low Power Mode off, High Power Mode on where your Mac has it), keeping the Mac awake while a game runs, and Apple's Metal Performance HUD. Metal's shader caches are listed, and a macOS update that makes games compile them again is pointed out.
- **The in-game overlay on a Mac** shows Coreloom's readings over full-screen apps and games (⌥⌘O, or from the Graphics page), with what it shows, where and how big, as on Windows. Nothing is added to games, so it shows readings rather than a frame rate; Apple's Metal HUD shows that.
- **Benchmarks on Metal.** The CPU benchmark, the GPU benchmark's graphics and compute tests on Metal, and the stress test, with every result saved.
- **Smart Optimize for macOS.** App caches, logs, temporary files, Xcode's build files, shader caches, old installers in Downloads and the Trash; XProtect, Gatekeeper, System Integrity Protection, FileVault and the firewall; freeing inactive memory; login items; and refreshing the DNS cache.
- **Devices.** Bluetooth accessories with their batteries, connect, disconnect and the Bluetooth switch, and what's plugged in by USB, Thunderbolt or display cable, with Eject for drives. macOS asks once whether Coreloom may use Bluetooth, when you click *Allow Bluetooth*.
- **Updates on a Mac.** *Check for Updates…* in the Coreloom menu and the menu bar item. Updates download in the background, are checked against the release's SHA-256 checksums and their code signature, and replace the app after Coreloom quits; the old copy is put back if anything fails.
- **Download buttons and a website.** The README has a button for each Mac and for Windows, and [jerryubah.github.io/Coreloom](https://jerryubah.github.io/Coreloom/) picks the right download for your computer. Both always get the newest version.

### In detail

- Settings on a Mac: *Open at login* (a standard login item), *Open in the menu bar at login*, *Keep running in the menu bar*, *Hide the Dock icon while the window is closed*, readings in the menu bar menu, a translucent sidebar, and units like macOS's.
- Mac defaults on the first launch: the Mac's appearance, units like macOS's, CPU and GPU temperature alerts suited to Apple silicon (105 °C and 100 °C) or Intel (97 °C and 100 °C), and the readings that make sense on a Mac in the in-game overlay.
- Coreloom for Mac runs as you, with no helper, kernel extension or driver. Freeing memory, the DNS cache and the power mode ask for your password through macOS's own dialog, only when you click them.
- Coreloom for Mac isn't signed with a paid Apple developer certificate yet, so the first time you open it macOS asks you to confirm it in *System Settings › Privacy & Security* (*Open Anyway*). Updates installed from inside Coreloom don't ask again.
- Every release's Mac apps are built, signed, packaged, installed, opened on every page and photographed on GitHub's Apple silicon and Intel Macs before it's published. Those Macs are virtual machines without temperature, power or fan sensors, so real Mac hardware is the part least tried so far: reports are very welcome.
- Windows: nothing changes in the app. The release now also carries `Coreloom-Windows-Setup.exe`, the same installer under a name that always points at the newest version.

### Feedback

Something not right on your Mac? [Open an issue](https://github.com/jerryubah/Coreloom/issues) with a screenshot and the report from *Settings › Diagnostics › Export diagnostic report*.

## [1.5.1] - 2026-10-22

The Benchmarking page's cards show your latest run, and GPU scores move to a bigger scale.

### Highlights

- **The cards show your latest run.** The big number on the Processor and Graphics card cards, and the parts under it, are now from the run you did most recently, so you see a new result as soon as it finishes. The *Your best* badge is gone from the cards; the **Best** badge stays on your highest CPU and GPU scores in the results list.
- **A bigger GPU scale.** Each GPU test now scores 10,000 points at its reference rate instead of 1,000, so GPU scores are ten times higher: an RTX 5090 that scored about 37,000 now scores about 370,000, the range Geekbench's GPU (OpenCL) scores are in. The CPU scale doesn't change.

### In detail

- GPU results saved by 1.5.0 are shown on the new scale (their score, its graphics and compute parts and each test's points are multiplied by ten; the measured rates stay as they were), so old and new runs compare fairly and the Best badge goes to the right one.
- CPU scores and stress test results don't change.
- Scores are still Coreloom's own: compare them with your own runs rather than with other benchmark apps.

### Feedback

Something not right? [Open an issue](https://github.com/jerryubah/Coreloom/issues) with a screenshot and a CoreloomDiag report.

## [1.5.0] - 2026-10-22

A new **Benchmarking** page: benchmark your processor and graphics card, keep every result with a Best badge on your top scores, open any result for its clocks, voltages and overclocking, and stress test your hardware.

### Highlights

- **CPU benchmark.** Eight tests of everyday and demanding work, in the spirit of Geekbench: file compression, text processing, cryptography (SHA-256 and AES), navigation (shortest routes on a map), ray tracing, machine learning (matrix multiplication with AVX), a photo filter and physics. Each runs first on one core, then on every thread at once, for a **single-core** and a **multi-core** score. Memory bandwidth and latency are measured too. It takes about a minute.
- **GPU benchmark.** Five tests on your graphics card, in the spirit of 3DMark: two graphics tests (a surface lit by 64 moving lights at 1440p, and a ray-marched 3D scene with soft shadows at 1080p) and three compute tests (FP32 arithmetic, memory bandwidth, an N-body physics step), for a **GPU score** with **graphics** and **compute** parts. It draws off-screen on Coreloom's own Direct3D 11 device; with more than one graphics card you pick which. *Run both* runs the two benchmarks back to back.
- **Every result saved, the best one badged.** Results are kept on your PC, newest first, with a filter for CPU, GPU and stress tests. The highest CPU score and the highest GPU score wear a **Best** badge (it moves when you beat it), and each card at the top shows your best score, its parts, and how your latest run compares.
- **Details for every result.** Click a result for each test's rate and points (single- and multi-core for the CPU), the clocks, temperatures, power and voltages during the run (lowest, average, highest), and what they say about how the hardware was set up: the fastest core clock and boost, whether **Precision Boost Overdrive** looks on (from power going past AMD's stock limit), peak power, core voltage, whether anything held the processor back; and for the graphics card its peak core and memory clocks against the card's rated maximums, its **power limit against the default** (raised, default or lowered), voltage and what limited its clocks. Settings beyond stock are marked *Raised*. Plus the PC it ran on (processor, graphics card and driver, memory and speed, motherboard, BIOS, Windows).
- **Stress test.** Load the processor, the graphics card or both for 1, 5, 15, 30 or 60 minutes or until you stop. Every answer is checked against the first one, so a calculation error (an unstable overclock or undervolt) is caught; the run is split into loops and, as in 3DMark, passes with no errors and at least 97 % stability (the slowest loop against the fastest). Live temperatures, clocks, errors and loop bars while it runs; the result is saved with its verdict and readings.

### In detail

- Scores are Coreloom's own scale: each test scores 1,000 points at a reference rate and the overall scores are geometric means, so every test counts the same. The CPU reference is one core of the 2.8 GHz Intel Xeon Coreloom is built on (a GitHub build machine's Ice Lake core scores about 1,700 single-core). The GPU reference is an estimate for an entry-level gaming card (about 2.5 TFLOPS and 110 GB/s) worked out from each test's arithmetic; it hasn't been checked against real cards yet. Compare scores with your own runs, not with other benchmark apps.
- Leave the PC alone during a stress test: other work slows it down and counts against its stability.
- The stress test stops on its own at the first calculation error, if the graphics driver resets, or if the processor stays at 100 °C or the graphics card at 95 °C for five seconds. Processors and graphics cards also protect themselves by slowing down.
- Coreloom only reads clocks, voltages and power limits; it never changes them. Nothing is added to games, and nothing is drawn on screen by the GPU tests.
- Results are saved in `benchmarks.json` in Coreloom's settings folder (up to 200, always keeping the best of each kind).

### Feedback

Scores that look off, or a stress test that fails on a PC you know is stable? [Open an issue](https://github.com/jerryubah/Coreloom/issues) with the result's details and a CoreloomDiag report.

## [1.4.9] - 2026-10-17

The Gaming page's boost count matches its switches, and core scheduling's power plan note is no longer red.

### Highlights

- **The ring counts every boost.** With all nine performance boosts on, the ring now reads 9/9 (it read 8/8 because Ultimate Performance wasn't counted on a Ryzen X3D). The count follows every switch as you turn it on or off, and *Boost now* turns on every boost that's off, Ultimate Performance included.
- **A grey power plan note.** In core scheduling's *Details*, the *Power plan* pill shows your plan in grey instead of red. Hover it, or the Ultimate Performance card, for how that plan affects AMD's core parking on a Ryzen X3D.

### Feedback

Something not right? [Open an issue](https://github.com/jerryubah/Coreloom/issues) with a screenshot and a CoreloomDiag report.

## [1.4.8] - 2026-10-17

A tidier Gaming page and pages whose cards line up: rows of cards now start and end on one line, the CPU core scheduling card moved into its boost's Details, and the extra lines under Ultimate Performance and the shader cache are gone.

### Highlights

- **Performance boosts in clean rows.** The nine boosts sit in three rows of three; the cards in each row start and end on the same line, and the section ends on one straight line. In a narrower window (two columns) the last card spans the row so the bottom stays straight.
- **No yellow line under Ultimate Performance.** On a multi-CCD Ryzen X3D its advice (Balanced keeps AMD's core parking working) now shows when you hover the card, and *Boost now* still leaves it alone.
- **Frame limiter and shader cache end together.** The two cards are the same height: the frame limiter's pacing row and the shader cache list both reach the bottom. The note under the cache list moved to the ⓘ next to *On this PC*.
- **CPU core scheduling lives in its boost.** The separate card under the frame limiter is gone. *Details* on the *Intelligent CPU core scheduling* boost opens everything it had (the cores and where the game runs, AMD's checks, *Test a game*, and the games kept on chosen cores) as a card over the page. While a test runs, the boost says which round it's on, and when a result is ready it says so.
- **Cards that line up on every page.** Cards side by side now end on one line, and the extra room goes to content that can use it instead of empty space: the process lists on the Processor and Memory pages show more apps, a drive's chart grows so its volumes line up, the Memory tiles share the donut card's height, and both columns of Smart Optimize, the two halves of *All sensors* and the Bluetooth cards in a row end together.

### In detail

- The boost descriptions were trimmed so the rows are about the same height before anything is stretched.
- A new developer tool draws Coreloom's pages with sample data on GitHub's Windows machines, so their layout can be checked as rendered by WPF before a release; it isn't part of the app.

### Feedback

Something not lined up on your screen? [Open an issue](https://github.com/jerryubah/Coreloom/issues) with a screenshot and your window size and scaling.

## [1.4.7] - 2026-10-17

Intelligent CPU core scheduling for Ryzen X3D in place of Auto HDR, an overlay you can move and resize in the game, cards that fit their content, a compact AMD card and a leaner frame limiter.

### Highlights

- **Intelligent CPU core scheduling.** A new performance boost, in place of Auto HDR. It works out which cores suit games: on a multi-CCD Ryzen X3D such as the 9950X3D, the CCD with the 3D V-Cache ("cache cores") and the one that clocks higher ("frequency cores"); on hybrid processors, the performance and efficiency cores. Its card (under the shader cache) shows the cores, how much of the game's load each group carries while you play ("Cyberpunk 2077: 85 % of the load on the cache cores"), and checks what AMD's own scheduling needs: the 3D V-Cache driver and service from AMD's chipset driver, AMD's preference, Game Mode and a power plan that lets cores park. *Test a game* plays your next game 30 seconds at a time on Windows' choice and on the cache cores, four rounds, and offers a profile for that game only when the cache cores are clearly and consistently better (at least 3 % more FPS, or 5 % better 1% lows); you choose to keep it. Kept profiles apply when the game starts, and never against AMD's driver: not for a game AMD has its own preference for, not when its cores were chosen elsewhere, not when AMD's driver already keeps it on the cache cores, and never on the frequency cores. Turning it off, or closing Coreloom, gives those games Windows' choice back.
- **Move and resize the overlay in the game.** *Move on screen* in the overlay settings, or Shift + the overlay hotkey (Shift+End by default), shows an outline: drag the overlay anywhere, drag its corner to make it bigger or smaller, and use S / M / L next to each line to size that line on its own. Where you drop it is kept, also after a restart. Nine places (now with the middle), fine-tuning of X and Y, and, if you like, a place for each game.
- **Overlay sizes.** Compact, Standard, Expanded or your own scale; text, the graph and spacing follow, and *Follow the screen* keeps the overlay the same size relative to your display on 1080p, 1440p and 4K and across Windows' scaling.
- **A tidier Show section.** The overlay's readings are chips in one row per line, each line with its size, instead of a long list of checkboxes.
- **Cards fit their content.** On every page, a card is as tall as what it holds instead of being stretched to its neighbour's height: Memory, Gaming, Storage, Smart Optimize, Devices and the Processor page. Only a card whose content can use the room (Smart Optimize's *Speed up*, which then lists more startup apps) grows.
- **A compact AMD card, above Cores.** The Processor page's AMD card moved right above the per-core table and lost most of its text: the architecture as a badge (CPUID signature on hover), the readings as tiles, the readings your processor doesn't report on one line (why on hover), and the five tuning controls as one-line rows with a status pill (details on hover).
- **A leaner frame limiter.** How it works, the adaptive pacing method, why a limit and what *Apply to* covers moved to ⓘ tooltips; the pacing of the game you're playing is one row. The card is much shorter, and the core scheduling card fills the column beside it under the shader cache.

### In detail

- Core scheduling only reads AMD's 3D V-Cache driver (the `amd3dvcache` driver and `amd3dvcacheSvc` service, and the preferences under `HKLM\SYSTEM\CurrentControlSet\Services\amd3dvcache\Preferences`); it never changes them or restarts the service. A game's cores are set from outside the game, the way Task Manager's *Set affinity* does; nothing is added to the game. If Windows or the game's anti-cheat refuses, the card says so and nothing else happens. Each step is written to the log (*Gaming*).
- Where the load sits comes from Windows' per-processor counters (the ones Task Manager uses) while a game is in front, so it's a good guide rather than an exact count of the game's own threads.
- On a multi-CCD Ryzen X3D with AMD's driver installed, the *Ultimate Performance* boost now says it's better left off (it stops the core parking AMD's driver uses to keep games on the cache cores), and *Boost now* leaves it alone.
- Auto HDR is no longer a boost; turn it on in Windows' *Settings › Display › Graphics*. Coreloom leaves the setting as it was.
- Two memory modules that report the same slot (many AMD boards call one slot in each channel "DIMM 1") are now named with their channel, e.g. "DIMM 1 · Channel A".
- Overlay places are kept per game by program file name; a place saved while moving the overlay from Coreloom's own window goes to the last game that was in front.

### What wasn't measured

- Core scheduling's test and profiles haven't been run on a real Ryzen 9 9950X3D or in real games here; the layout reading, the load shares, AMD's checks, the A/B test's rules and the profile handling are tested against built Windows topology data and a simulated driver. How much a game gains on the cache cores varies by game, which is why profiles are only offered after a test in that game.
- Dragging and resizing the overlay, and the new sizes on 1440p/4K and at different Windows scaling, haven't been tried on a Windows PC here.

### Feedback

Something not right? [Open an issue](https://github.com/jerryubah/Coreloom/issues) with a screenshot and a CoreloomDiag report, or copy the lines from *Settings › Logs*.

## [1.4.6] - 2026-10-17

Adaptive frame pacing, games you add yourself, a log viewer with every process the memory clean-up touched, lighter monitoring while you play, and a detailed AMD processor card.

### Highlights

- **Adaptive frame pacing (Top pick).** A new switch at the top of Gaming › *Frame limiter*: "an intelligent engine that monitors actual frame times and automatically finds a stable FPS target for each game" (frame stability · 1% lows · smoothness). While a library game plays, Coreloom measures every frame, finds the frame rate the game holds with steady 1% lows over the last two minutes, and sets that game's own limit in the NVIDIA driver. It moves a step at a time (at most 15 % down, 10 % up, and only up when the graphics card has room), never back to a limit the game couldn't hold, and not again until a change shows in the frames. It stays just under your refresh rate with G-SYNC or FreeSync, and with frame generation (set per game: 2×, 3×, 4×) it keeps whole rendered frames and at least 40 of them a second. It's off until you turn it on, leaves alone any game you gave a limit yourself, and turning it off removes the limits it set. Without an NVIDIA driver it suggests a limit instead.
- **Add games by hand.** *Add game* on the Gaming page: pick the game's .exe, give it a name (one is suggested), a picture or the program's icon, and optional launch options. It shows as "Added by you", is recognised when it runs, gets the overlay's frame counter and its own frame limit like any other game, and can be edited or removed.
- **See every process the memory clean-up touched.** *Free up RAM* now lists each process with its PID, memory before and after and what was reclaimed, or why it was skipped or failed, with a trimmed / skipped / failed filter. Coreloom only trims; it never ends a process, and the totals say so.
- **A log viewer.** *Settings › Logs* (or *Open logs* on the memory card, or *View logs* in Diagnostics): every entry with its time, a search box, level and category filters, and copy and open-folder buttons.
- **Lighter while you play.** With a game in front, Coreloom reads sensors less often (every 2 s by default; 1 s, 2 s or 5 s, and never slower than 1 s while the overlay shows readings), at low priority, gives the game priority and stops redrawing its window behind the game. The sensor refresh interval is now labelled from 0.25 s to 5 s (lightest), and *Settings › Diagnostics* shows what each part of Coreloom costs: sensor reading, the window, the overlay's graph and its frame counter.
- **AMD processor details.** The Processor page gains an *AMD processor* card: architecture and codename (Zen to Zen 5, e.g. "Zen 2 · Renoir"), laptop or desktop, the CPUID family and model, and instruction sets and features. Where the processor reports them: PPT, TDC, EDC, cores/SoC power, SoC voltage and current, fabric, memory-controller and memory clocks, SoC and I/O-die temperatures; the others are listed with why. *Limits and tuning* shows AMD's stock power limit for desktop models, says whether Precision Boost Overdrive looks on (from package power over the last hour), and shows Curve Optimizer, Eco mode and manual clocks as unavailable with where to change them. It's read-only: Coreloom never changes processor tuning.
- **Boost and throttling on every processor.** New *Boost* (how far above base the processor runs) and *Performance limit* (under 100 % when power, heat or Windows' power policy holds it back) readings, and the processor's instruction sets and features under Specifications.

### In detail

- Adaptive pacing reads frames the same way as the overlay (Windows' graphics events; nothing is added to games) and only while it's on and a game is in front. Loading screens and pauses are left out. Each limit it sets, each session's result and why are written to the log (*Gaming*), and the limit shows as "· adaptive" under *Apply to*. Whether the driver applies a changed limit to a game that's already running depends on the game; when the frames don't show it, adaptive pacing waits for the game's next start.
- Memory clean-up skips the app in front, Coreloom and the Windows processes the desktop needs before opening them, and names the reason when Windows refuses (for example a protected process). Every process is also written to the log.
- AMD readings come from LibreHardwareMonitor (as administrator with PawnIO); which ones exist depends on the processor and its BIOS. Ryzen 4000 processors get package power, clocks and temperatures, but LibreHardwareMonitor has no SMU table for them, so TDC, EDC and PPT show as not reported. The stock limit is AMD's 1.35 × TDP for desktop models; laptop limits are set by the laptop's maker and aren't reported to Windows.
- Games added by hand are matched by their exact program first, and never by a broad folder such as the Desktop or Downloads.

### What wasn't measured

- The effect on frame rates with and without Coreloom on a lower-end PC (Ryzen 4000, GTX 1650) couldn't be measured here; *Settings › Diagnostics* now shows the numbers, and [TESTING-ON-YOUR-PC.md](docs/TESTING-ON-YOUR-PC.md) has the steps to compare.
- Adaptive pacing, the AMD card and the new memory list are tested against recorded and simulated data, not yet on real games and Ryzen processors.

### Feedback

Something not right? [Open an issue](https://github.com/jerryubah/Coreloom/issues) with a screenshot and a CoreloomDiag report, or copy the lines from *Settings › Logs*.

## [1.4.5] - 2026-10-16

The frame-time graph is back in the in-game overlay, and the frame limiter's mark next to FPS is gone.

### Highlights

- **The frame-time graph shows again.** Since 1.4.3 the graph is drawn in a small see-through window of its own laid over the overlay, so it can glide at your display's pace. That window was being filled in a way Windows accepts but doesn't put on screen, so the graph never appeared. It now shows, smooth and in place, and moves with the overlay.
- **The graph's place always shows.** With no game in front (FPS "—"), the graph is a flat line under the FPS line instead of being nearly invisible, so you can see where it is and that it's on.
- **No frame limiter mark.** The small green pill with the limit next to FPS is gone. When a game is limited, the limit still shows as the dashed green line on the graph.

### In detail

- The fix was found and checked with a new test on GitHub's Windows machines that puts a window set up like the overlay on screen, draws the graph's window over it from another thread, and reads the pixels back: before the fix the overlay's color showed through where the graph should be; after it, the graph's pixels are on screen, also after the overlay moves.

### Feedback

Something still not where it should be? [Open an issue](https://github.com/jerryubah/Coreloom/issues) with a screenshot and a CoreloomDiag report.

## [1.4.4] - 2026-10-16

Covers for every game without asking, no G-SYNC warning while G-SYNC is on, and a quieter search box.

### Highlights

- **Covers for every game, automatically.** A game that has no picture on your PC (most Ubisoft, EA, Battle.net, Riot and GOG games) now gets one by itself when the Gaming page scans your games: from its store's own pictures, else from Steam's store, where most of these games are sold too. There's no *Download covers* question any more. A game Steam doesn't have keeps its initials rather than getting another game's picture.
- **No G-SYNC warning while G-SYNC is on.** The yellow "G-SYNC looks switched off for your display" line under the G-SYNC boost is gone; the display report it relied on said "off" even with G-SYNC switched on.
- **A quieter search box.** The list under an empty search box now shows only *Start typing…*, without the Ctrl+K line. Ctrl+K and Ctrl+F still jump to the box.

### In detail

- Only the game's name (or its Steam app number) is sent: a search on `store.steampowered.com`, the game's pictures from `api.steampowered.com`, and the pictures themselves from Steam's picture servers. Names have to match exactly, apart from capitals, ™ and ®, apostrophes, punctuation, accents and the edition ("Deluxe Edition", "GOTY"), so a DLC or another game of the series is never taken for it.
- The tall 600 × 900 cover comes first; when a game has none, Steam's wide store picture is used instead. Newer games keep their pictures under addresses with a code in them, which Coreloom now asks the store for, so recent releases get their covers too.
- Pictures are saved in `%LOCALAPPDATA%\Coreloom\Artwork` and reused. A game nothing is found for is looked up again after a week; when the internet can't be reached, it simply tries again next time.

### Feedback

Something still not where it should be? [Open an issue](https://github.com/jerryubah/Coreloom/issues) with a screenshot and a CoreloomDiag report.

## [1.4.3] - 2026-10-16

A smoother, lighter in-game overlay and three new boosts: the frame-time graph now glides at your display's pace and reacts at once, the overlay drops its outline and stays out of your game's way, the frame pacer is now the frame limiter, and the Boosts section adds G-SYNC, a sized NVIDIA shader cache and Auto HDR.

### Highlights

- **A fluid frame-time graph.** The overlay's graph used to step along at a low, uneven rate. It now draws on a thread of its own, on a precise timer, at an even share of your display's refresh rate (120 times a second on a 240 Hz display), with every frame placed at the moment it was shown, so it glides smoothly and a spike shows up the moment it happens. It looks exactly like the preview in the overlay's settings.
- **Out of your game's way.** The overlay is drawn on the processor, never on the graphics card the game is using; the graph's thread and the frame-rate reader run at low priority and allocate nothing as they go; while a game is in front, Coreloom as a whole steps down to below-normal priority so the game's threads always come first; and the overlay only re-asserts itself on top when another window actually covers it.
- **No outline on the overlay.** The overlay is just the smoked glass now, the same as its preview, without the light rim around it.
- **Frame limiter.** The frame pacer is renamed *Frame limiter* everywhere: the Gaming page, its messages, search and the overlay's settings.
- **G-SYNC & adaptive refresh.** A new boost (NVIDIA) turns on G-SYNC for fullscreen and windowed games, so the display's refresh follows the frames the graphics card sends: no tearing and less stutter. It recommends keeping your frame limit just under the refresh rate (for example 237 FPS on a 240 Hz display), says when G-SYNC is switched off for your display in the NVIDIA Control Panel, and isn't offered on a display without variable refresh. It improves how frames are shown, not the frame rate itself.
- **NVIDIA shader cache, sized.** The shader cache boost now has a size: 10 GB (recommended), 100 GB for a big game library, or Unlimited, with the limit currently set shown under the picker. A bigger cache keeps compiled shaders between sessions, so fewer stutters come back; it doesn't remove every shader stutter or raise the average frame rate.
- **Auto HDR.** A new switch (Windows 11, HDR display) gives SDR games HDR brightness and color. Use a game's own HDR when it has one, and don't stack it with RTX HDR. As it changes how games look rather than how they run, *Boost now* leaves it alone and it isn't counted with the boosts.
- **Clearer boosts.** Every boost card now shows what it helps with (*Latency*, *Performance*, *Frame stability*, *Smoothness*, *Visual fidelity*), and the new ones also show a recommendation and what they don't do.
- **No Ctrl K badge.** The search boxes no longer show the *Ctrl K* badge. Ctrl+K and Ctrl+F still jump to the search box.

### In detail

- G-SYNC is set in the NVIDIA driver's global profile (the same settings as *Set up G-SYNC* and *Enable G-SYNC globally*), along with Windows 11's *Optimizations for windowed games* variable-refresh setting; turning the boost off puts the driver back to its default, fullscreen only.
- The shader cache size is the driver's *Shader Cache Size* setting. The boost counts as on when a size of 10 GB or more is set, and turning it off puts back the driver's default. Changing the size while it's on applies it at once.
- Auto HDR is Windows' own setting (*Settings › Display › Graphics*). Coreloom checks for an HDR display and asks you to turn HDR on when it's off.
- On a PC whose precise timer still wakes late (some virtual machines), the graph asks Windows for a 1 ms timer for Coreloom while it draws, as games do.
- The graph's own window is see-through to clicks, never takes focus, and is never in the taskbar or Alt+Tab, like the overlay. Nothing is injected into games.

### Feedback

Something still not where it should be? [Open an issue](https://github.com/jerryubah/Coreloom/issues) with a screenshot and a CoreloomDiag report.

## [1.4.2] - 2026-10-15

Polish for the new design: temperatures take their own colors, the edge glow matches each card, the collapsed sidebar shows its icons in full, dropdowns stay with their box, the search list sits centered under the search box, and Coreloom's shortcuts switch to the dark-mode icon too.

### Highlights

- **Temperatures in their own colors.** Temperature gauges and cards now show how warm a reading is, like a thermometer: a blue gradient when cool, teal-green when normal, amber to orange when warm and orange to red when hot. The card's glow, its sparkline and its icon take the same color, and it fades smoothly from one to the next as the temperature changes. This covers the Graphics and Processor temperature cards and the GPU and CPU temperature cards on the dashboard.
- **An edge glow in the card's own color.** Hovering a card lights its edge in the color already glowing inside it (green for graphics, blue for a cool temperature, your accent on the banners), as a soft glow spreading just outside the edge with a bright line on it. The second, shifted shade is gone, so the hero card no longer glows purple.
- **Collapsed sidebar icons in full.** With the sidebar collapsed, every icon sits centered in its button instead of being cut off on the right, and the logo, the expand button and the update badge line up with them.
- **Dropdowns stay put.** While a dropdown list is open (the frame pacer's *Apply to* and every other picker), the page under it no longer scrolls: the wheel scrolls the list itself when it is long, and otherwise does nothing. If the page moves another way, the list moves with its box, and closes once the box scrolls out of view.
- **Search list centered.** The quick-search list on the Devices and Gaming pages now sits exactly under the search box, the same width and centered, whatever Windows' left- or right-handed menu setting is.
- **Dark-mode icon on your shortcuts.** With the dark theme on, Coreloom's Start menu entry, desktop shortcut and pinned taskbar button show the dark-mode icon too, and go back to the usual icon with the light theme.

### In detail

- Windows draws a shortcut's icon from the shortcut itself, so Coreloom points its own shortcuts (named *Coreloom* and starting this copy of Coreloom.exe) at `coreloom-dark.ico`, which now installs next to the app. A shortcut you gave an icon of your own is left alone. Explorer is told to redraw them; a pinned taskbar button can take until Explorer next refreshes its icons.
- An update reinstalls the shortcuts with the usual icon; Coreloom switches them back as it starts.
- With *Reduce motion* on, a temperature's color changes without fading.

### Feedback

Something still not where it should be? [Open an issue](https://github.com/jerryubah/Coreloom/issues) with a screenshot and a CoreloomDiag report.

## [1.4.1] - 2026-10-15

A design overhaul: smooth squircle cards that glow in their own colors, a darker dark mode, a floating glass sidebar, tick-ring gauges, liquid toggles, quick search and eased scrolling, crisper text, an animated splash with a dark-mode icon, and a redesigned in-game overlay with a frame-time graph. The frame pacer can now limit one game on its own.

### Highlights

- **Squircle cards everywhere.** Every card, tile, button, field, menu and pop-up has smooth, continuously curved corners (the same corner Apple uses), drawn the same way everywhere.
- **Cards that glow.** Cards with data light up in their own color from below, with a lit lower edge and a glassy top rim: the processor card in blue, graphics in green, memory in violet, health in its status color, the page banners in your accent. Move the pointer over one and its edge lights up where the pointer is, in two shades, following it smoothly and growing brighter as you near the edge. Only the edge glows, never the content.
- **Darker dark mode.** The background is now near-black (not black), with cards, fields and tracks a step deeper, and secondary text a touch brighter so it stays easy to read.
- **Floating glass sidebar.** The sidebar floats in from the window's edges as a pane of liquid glass: a squircle with a bright rim along its top and bottom corners, over two soft lights in your accent colors (and Windows' Mica, when it's on).
- **Gauges as glowing tick rings.** Every gauge is a ring of fine ticks: lit ones brighten toward the head, which reaches a little further and glows, and the bigger gauges have a soft orb of light inside. They keep the green, blue, orange and red scale and still glide with the reading.
- **Liquid toggles.** Press a switch and its knob swells into a clear glass lens over the track; let go and it flows across with a springy overshoot as the track fills with your accent.
- **Quick search.** The search boxes on the Devices and Gaming pages drop a panel down as you type: *Show all matches* plus the best few results with their icon and details. Use the arrow keys and Enter, or click; Ctrl+K or Ctrl+F jumps to the search box from anywhere on the page.
- **Smooth scrolling.** Scrolling with the mouse wheel or touchpad glides and eases into place everywhere, and lists inside a page scroll before the page does.
- **Animated splash and dark-mode icon.** The startup splash is now 16:9, with ribbons of blue light flowing across deep navy, the icon on a breathing halo, the wordmark and a glowing striped loading bar that moves on with each startup step. A new dark-mode icon (the mark on a dark glass tile with a blue rim) replaces the light one in the window, taskbar, tray, sidebar, About page and splash whenever the dark theme is on.
- **Per-game frame limits.** The frame pacer has an *Apply to* picker: keep one limit for all games, or pick a game to give it a limit of its own, set in the NVIDIA driver's profile for that game's program. Games with their own limit show as chips under the picker, where × removes the limit again.
- **In-game overlay redesign.** The overlay is a squircle of smoked glass with a light rim, the FPS number is bigger and bold, a new frame-time graph under it shows how evenly frames arrive (stutters show as amber or red spikes), and a small green mark with the limit appears while the frame pacer limits the game in front, with the pacer's target as a dashed line on the graph.

### In detail

- Text is crisper: large text (titles, headline numbers, the splash) is laid out with its true glyph shapes, the pages sit on an opaque layer so text keeps ClearType, and the sidebar's text is smoothed in grayscale where Mica shows through it.
- Headline numbers on cards use bold Inter with fixed-width digits, so they don't jitter as they count.
- The frame-time graph is on by default for everyone who shows the frame rate on the overlay; it can be turned off with the other readings in the overlay's settings, where the preview shows a sample trace until a game runs.
- A game's own limit applies from its next start. When NVIDIA already has a profile for the game (most popular games), the limit is set there; otherwise Coreloom creates a profile named *Coreloom: game name*, and deletes it again when the limit is removed.
- The overlay knows a game is paced from the limits set in Coreloom (and the all-games limit, also when it was set in the NVIDIA Control Panel and the Gaming page has seen it since).
- With *Reduce motion* on, scrolling, the splash, the cards' edge light and the gauges move without animation.

### Feedback

Found a bug, or a game whose own limit doesn't take? [Open an issue](https://github.com/jerryubah/Coreloom/issues) and attach a CoreloomDiag report.

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
