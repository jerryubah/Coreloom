# Troubleshooting

Start with *Settings → Diagnostics*. It shows:

- whether Coreloom is running as administrator;
- whether PawnIO is installed;
- every sensor source with its state and last error;
- Coreloom's own overhead.

From there you can open the log folder or export a report.

## CPU temperature, CPU power or motherboard fans show "Unavailable"

On current Windows versions, these sensors need a kernel driver. Coreloom reads them through
LibreHardwareMonitor and the **PawnIO** driver.

1. Install PawnIO from [pawnio.eu](https://pawnio.eu). Coreloom never installs drivers itself.
2. Restart Coreloom (it always runs as administrator, so nothing else is needed).
3. If CPU temperature is still missing, go to *Settings → Monitoring* and make sure *Use LibreHardwareMonitor*
   is on.
4. Export a diagnostic report, or run `CoreloomDiag.exe --lhm-report` as administrator, and open an issue.
   Sensor names change between CPU generations and may need a new mapping.

## GPU values are missing (NVIDIA)

Coreloom loads `nvml.dll`, which comes with the NVIDIA driver, from `C:\Windows\System32` or
`C:\Program Files\NVIDIA Corporation\NVSMI`.

1. Check that `nvidia-smi` works in a terminal. If it doesn't, reinstall the NVIDIA driver.
2. After a driver update, Coreloom re-initializes NVML automatically within about a minute. You can also use
   *Settings → Diagnostics → Rescan hardware*.
3. Hot spot temperature, memory junction temperature, fan RPM and core voltage aren't part of NVML. They
   come from LibreHardwareMonitor, which needs to be enabled.

## The splash stays up, or the window doesn't appear after it

The splash only goes away once the main window has drawn its first frame, so a splash that stays up means startup
is still working or stuck.

1. Wait a little: a first start after installing or updating can be slower (Windows scans the new files).
2. Open `%LOCALAPPDATA%\Coreloom\Logs\coreloom.log`. Lines starting with `Startup` show when the splash appeared
   and when the main window was revealed; a warning there names what failed.
3. If Coreloom is stuck, end it in Task Manager (Details tab; Task Manager may need to run as administrator,
   because Coreloom is elevated), then start it again and send the log.
4. To rule the splash out, start Coreloom with `--minimized` (it opens in the notification area without a splash)
   and open it from the tray icon.

## Values differ from another tool

- Tools sample at different moments. Compare averages over a few seconds, not single readings.
- "GPU temperature" here is the core sensor; hot spot is shown separately.
- AMD's Tctl can include an offset on some CPUs. Coreloom labels which sensor it uses (hover a value).
- Clock speeds can be "current", "effective" or "derived (Task Manager style)". Coreloom labels each one.

Run `CoreloomDiag.exe --compare` for an automatic NVML vs `nvidia-smi` comparison.

## High CPU or memory use

- *Settings › Diagnostics* shows Coreloom's own usage; it's measured, not estimated.
- Try a 2 s polling interval (*Settings → Monitoring*). Turn off per-thread graphs or smooth scrolling.
- *Scan USB fan and pump controllers* can make LibreHardwareMonitor slower on some systems. It's off by
  default.
- If usage stays high, attach a CoreloomDiag report to an issue. It includes per-provider poll times.

## The window doesn't appear

- Coreloom may be running in the notification area. Click its tray icon, which may be in the overflow (^)
  menu.
- Starting Coreloom again brings the running copy to the front.
- If it still doesn't appear, check the latest log in `%LOCALAPPDATA%\Coreloom\Logs` (or `Data\Logs` next
  to a portable copy).

## Updates

- *"Update checks are not configured"*: the build had no GitHub repository embedded. Official releases
  always have one.
- *"Checksum mismatch"*: the download was corrupted or altered. It's discarded and nothing is installed.
  Try again later.
- Portable copies of 1.1.0 or earlier can't update themselves (they show *View on GitHub* or *Download from
  GitHub*). Replace the folder's files with the new portable zip once, keeping the `Data` folder; from then on
  *Install update* works for them too.
- *"Coreloom couldn't finish installing it automatically"*: the update was rolled back (often because a file was in
  use, for example by an antivirus scan) and the old version started again. Coreloom won't
  retry that version on its own; use *Install update* in *Settings → Updates* to try again. The log
  (`Updates` lines) names the file that couldn't be replaced.
- *"The update couldn't be unpacked next to Coreloom"* (portable copies): the update is unpacked into a hidden
  `.coreloom-update` folder next to `Coreloom.exe`, so that folder must be writable and the disk needs about
  twice the app's size free.
- An update downloaded but never installs: automatic installs happen when Coreloom starts or while the PC is idle
  with Coreloom in the tray. *Install update* installs it right away. Check that *Update automatically* is on.
- *"The downloaded update was missing or damaged"*: the file in `%LOCALAPPDATA%\Coreloom\Updates` changed after it
  was verified, so it was deleted rather than run. *Check for updates* downloads it again.
- Corporate proxies are honored through the Windows system proxy settings.

## Coreloom on your phone (Connect)

- *The phone can't reach the computer*: both must be on the same network (a phone on mobile data, or on a guest
  Wi-Fi that keeps devices apart, can't reach it). Try the address from *Settings › Connect* in the phone's browser.
  Coreloom only answers private addresses (192.168.x.x, 10.x.x.x, 172.16–31.x.x and the like).
- *Windows says the network is Public*: Windows keeps phones out of Public networks. *Settings › Connect › Network
  settings* opens Windows' network settings: set the network to *Private*. Coreloom's firewall rule
  (*Coreloom Connect*, Windows Defender Firewall › Advanced settings › Inbound rules) covers private and domain
  networks only.
- *Another security suite's firewall*: allow `Coreloom.exe` on port 47101 (TCP, incoming) on your home network.
- *A Mac with the firewall on*: if you clicked *Don't Allow* when macOS asked, allow Coreloom in *System Settings ›
  Network › Firewall › Options*.
- *"Port 47101 is in use by another app"*: another program took Coreloom's port. Quit it, or turn Connect off and
  on once it's closed.
- *The pairing code is refused*: codes change every five minutes and after each use; five wrong codes replace it,
  and ten wrong tries in a minute pause pairing for a minute. Use the code shown now, or scan the QR code.
- *The phone says it was removed*: the phone was removed in *Settings › Connect*, or Coreloom's paired phones were
  reset. Pair it again.
- The paired phones are kept in `connect.json` in the settings folder; deleting it unpairs every phone.

## Reset everything

1. Exit Coreloom from the tray.
2. Delete `%APPDATA%\Coreloom` (settings) and `%LOCALAPPDATA%\Coreloom` (logs and downloads).
3. If you used *Start with Windows*, also delete the *Coreloom startup* task in Task Scheduler (and, if you used
   Vigil before the rename, the *Vigil (elevated startup)* task and the *Vigil* entry under Startup apps).
