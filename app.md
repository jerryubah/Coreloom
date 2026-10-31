# Coreloom 1.8.6 on macOS 15.8.2 (arm64): a run

- Running after 78 s and 12 pages:
```
  PID    RSS      VSZ  %CPU ELAPSED COMMAND
 4045 423504 413652448  12.3   01:18 /Applications/Coreloom.app/Contents/MacOS/Coreloom
```
- Other Coreloom processes left (the page launches must hand over and quit): 0
- Quit cleanly on SIGTERM

## Connect
- `GET /api/info`: `{"app":"Coreloom","version":"1.8.6","platform":"macos","name":"sjc22-bm205-08fd98fc-535f-43c7-aedb-e647ed35c24c-C6579119C4DD","pc":"FGNHaGk_wIJb-wqTs4wHRw","pairing":{"link":false,"code":false,"paused":false}}`
- `GET /`: 200
- `GET /app.js`: 200
- `GET /app.css`: 200
- `GET /connect.js`: 200
- `GET /gauge.js`: 200
- `GET /chart.js`: 200
- `GET /vendor/crypto.js`: 200
- `GET /manifest.webmanifest`: 200
- `GET /icons/apple-touch-icon.png`: 200
- `GET /fonts/Inter-SemiBold.woff2`: 200
- Another host name (`Host: evil.example.com`): 421
- From this Mac's network address 192.168.64.7: 200 (answered only when the address is a private one)
- Menu bar readings: cpu, memory, network shown (log)
- With Settings › Connect showing: `{"app":"Coreloom","version":"1.8.6","platform":"macos","name":"sjc22-bm205-08fd98fc-535f-43c7-aedb-e647ed35c24c-C6579119C4DD","pc":"FGNHaGk_wIJb-wqTs4wHRw","pairing":{"link":true,"code":true,"paused":false}}`
- Quit cleanly on SIGTERM with Connect on

## Backgrounds
- Image (aurora): 2026-10-31 00:27:18.734 INF [Appearance] Background: Image aurora drawn at 943×634 (frost 236×159) in 115 ms.
- SeeThrough: 2026-10-31 00:27:31.477 INF [Appearance] Background: See-through.

## Readings window
- Shown (log); picture: 95-readings-window.png

## Log (errors and warnings)
```
```

## Log (first 60 lines)
```
2026-10-31 00:25:42.584 INF [App] Coreloom 1.8.6+80b9ed34087e143dc41ad03aa8b0900afd066ce6 starting (args: ; bundle: /Applications/Coreloom.app; macOS 15.8.2, Arm64, .NET 10.0.14, PID 4045; Apple silicon: True; virtual machine: True).
2026-10-31 00:25:42.761 INF [App] First launch: Mac defaults applied.
2026-10-31 00:25:43.655 INF [Telemetry] Telemetry started (7 providers, interval 1000 ms).
2026-10-31 00:25:43.672 INF [Sensors] Sensors: no hardware sensors (virtual machine).
2026-10-31 00:25:44.658 INF [Telemetry] Coreloom process: Running in 1 ms
2026-10-31 00:25:44.658 INF [Telemetry] Mac sensors: Running in 22 ms — Apple Virtual Machine 1 (VirtualMac2,1); no fans reported; 0 temperature groups; sensors: no hardware sensors (virtual machine)
2026-10-31 00:25:44.658 INF [Telemetry] macOS processor counters: Running in 15 ms — Apple M1 (Virtual): 3 threads; sensors: no hardware sensors (virtual machine)
2026-10-31 00:25:44.658 INF [Telemetry] macOS memory: Running in 9 ms — 7 GB physical memory
2026-10-31 00:25:44.658 INF [Telemetry] macOS storage: Running in 31 ms — 1 disk(s), 1 volume(s)
2026-10-31 00:25:44.658 INF [Telemetry] macOS network: Running in 42 ms — 2 interface(s), primary en0
2026-10-31 00:25:44.658 INF [Telemetry] macOS graphics drivers: Running in 3 ms — Apple Paravirtual GPU
2026-10-31 00:25:50.058 INF [Appearance] Background: Gradient coreloom drawn at 943×631 (frost 236×158) in 59 ms.
2026-10-31 00:25:51.973 INF [Window] Title bar: glass 10,10 76×611; close 21,22 14×16; minimize 41,22 14×16; zoom 61,22 14×16; first page (48,163) → AvnView; range (784,57) → AvnView; freeze (898,57) → AvnView; glass top (48,14) → AvnView
2026-10-31 00:26:02.834 INF [Gaming] Found 0 installed games in 360 ms.
2026-10-31 00:27:00.230 INF [App] Coreloom 1.8.6+80b9ed34087e143dc41ad03aa8b0900afd066ce6 starting (args: ; bundle: /Applications/Coreloom.app; macOS 15.8.2, Arm64, .NET 10.0.14, PID 5898; Apple silicon: True; virtual machine: True).
2026-10-31 00:27:01.063 INF [Connect] Listening on port 47101 (20 app files).
2026-10-31 00:27:01.088 INF [Notifications] Notifications aren't allowed (System Settings › Notifications › Coreloom).
2026-10-31 00:27:01.233 INF [Telemetry] Telemetry started (7 providers, interval 1000 ms).
2026-10-31 00:27:01.242 INF [Sensors] Sensors: no hardware sensors (virtual machine).
2026-10-31 00:27:02.228 INF [Telemetry] Coreloom process: Running in 1 ms
2026-10-31 00:27:02.228 INF [Telemetry] Mac sensors: Running in 5 ms — Apple Virtual Machine 1 (VirtualMac2,1); no fans reported; 0 temperature groups; sensors: no hardware sensors (virtual machine)
2026-10-31 00:27:02.228 INF [Telemetry] macOS processor counters: Running in 11 ms — Apple M1 (Virtual): 3 threads; sensors: no hardware sensors (virtual machine)
2026-10-31 00:27:02.228 INF [Telemetry] macOS memory: Running in 10 ms — 7 GB physical memory
2026-10-31 00:27:02.228 INF [Telemetry] macOS storage: Running in 23 ms — 1 disk(s), 1 volume(s)
2026-10-31 00:27:02.228 INF [Telemetry] macOS network: Running in 26 ms — 2 interface(s), primary en0
2026-10-31 00:27:02.228 INF [Telemetry] macOS graphics drivers: Running in 3 ms — Apple Paravirtual GPU
2026-10-31 00:27:02.581 INF [MenuBar] Readings in the menu bar: cpu, memory, network.
2026-10-31 00:27:02.738 INF [Appearance] Background: Gradient coreloom drawn at 943×634 (frost 236×159) in 72 ms.
2026-10-31 00:27:04.646 INF [Window] Title bar: glass 10,10 76×614; close 21,22 14×16; minimize 41,22 14×16; zoom 61,22 14×16; first page (48,163) → AvnView; range (784,57) → AvnView; freeze (898,57) → AvnView; glass top (48,14) → AvnView
2026-10-31 00:27:16.286 INF [App] Coreloom 1.8.6+80b9ed34087e143dc41ad03aa8b0900afd066ce6 starting (args: ; bundle: /Applications/Coreloom.app; macOS 15.8.2, Arm64, .NET 10.0.14, PID 6218; Apple silicon: True; virtual machine: True).
2026-10-31 00:27:17.084 INF [Connect] Listening on port 47101 (20 app files).
2026-10-31 00:27:17.111 INF [Notifications] Notifications aren't allowed (System Settings › Notifications › Coreloom).
2026-10-31 00:27:17.256 INF [Telemetry] Telemetry started (7 providers, interval 1000 ms).
2026-10-31 00:27:17.266 INF [Sensors] Sensors: no hardware sensors (virtual machine).
2026-10-31 00:27:18.249 INF [Telemetry] Coreloom process: Running in 1 ms
2026-10-31 00:27:18.249 INF [Telemetry] Mac sensors: Running in 4 ms — Apple Virtual Machine 1 (VirtualMac2,1); no fans reported; 0 temperature groups; sensors: no hardware sensors (virtual machine)
2026-10-31 00:27:18.249 INF [Telemetry] macOS processor counters: Running in 11 ms — Apple M1 (Virtual): 3 threads; sensors: no hardware sensors (virtual machine)
2026-10-31 00:27:18.249 INF [Telemetry] macOS memory: Running in 10 ms — 7 GB physical memory
2026-10-31 00:27:18.249 INF [Telemetry] macOS storage: Running in 25 ms — 1 disk(s), 1 volume(s)
2026-10-31 00:27:18.249 INF [Telemetry] macOS network: Running in 25 ms — 2 interface(s), primary en0
2026-10-31 00:27:18.249 INF [Telemetry] macOS graphics drivers: Running in 2 ms — Apple Paravirtual GPU
2026-10-31 00:27:18.560 INF [MenuBar] Readings in the menu bar: cpu, memory, network.
2026-10-31 00:27:18.734 INF [Appearance] Background: Image aurora drawn at 943×634 (frost 236×159) in 115 ms.
2026-10-31 00:27:20.606 INF [Window] Title bar: glass 10,10 76×614; close 21,22 14×16; minimize 41,22 14×16; zoom 61,22 14×16; first page (48,163) → AvnView; range (784,57) → AvnView; freeze (898,57) → AvnView; glass top (48,14) → AvnView
2026-10-31 00:27:29.040 INF [App] Coreloom 1.8.6+80b9ed34087e143dc41ad03aa8b0900afd066ce6 starting (args: ; bundle: /Applications/Coreloom.app; macOS 15.8.2, Arm64, .NET 10.0.14, PID 6524; Apple silicon: True; virtual machine: True).
2026-10-31 00:27:29.897 INF [Connect] Listening on port 47101 (20 app files).
2026-10-31 00:27:29.922 INF [Notifications] Notifications aren't allowed (System Settings › Notifications › Coreloom).
2026-10-31 00:27:30.090 INF [Telemetry] Telemetry started (7 providers, interval 1000 ms).
2026-10-31 00:27:30.092 INF [Sensors] Sensors: no hardware sensors (virtual machine).
2026-10-31 00:27:31.074 INF [Telemetry] Coreloom process: Running in 1 ms
2026-10-31 00:27:31.074 INF [Telemetry] Mac sensors: Running in 23 ms — Apple Virtual Machine 1 (VirtualMac2,1); no fans reported; 0 temperature groups; sensors: no hardware sensors (virtual machine)
2026-10-31 00:27:31.074 INF [Telemetry] macOS processor counters: Running in 14 ms — Apple M1 (Virtual): 3 threads; sensors: no hardware sensors (virtual machine)
2026-10-31 00:27:31.074 INF [Telemetry] macOS memory: Running in 14 ms — 7 GB physical memory
2026-10-31 00:27:31.074 INF [Telemetry] macOS storage: Running in 40 ms — 1 disk(s), 1 volume(s)
2026-10-31 00:27:31.074 INF [Telemetry] macOS network: Running in 27 ms — 2 interface(s), primary en0
2026-10-31 00:27:31.074 INF [Telemetry] macOS graphics drivers: Running in 2 ms — Apple Paravirtual GPU
2026-10-31 00:27:31.398 INF [MenuBar] Readings in the menu bar: cpu, memory, network.
2026-10-31 00:27:31.477 INF [Appearance] Background: See-through.
2026-10-31 00:27:33.452 INF [Window] Title bar: glass 10,10 76×614; close 21,22 14×16; minimize 41,22 14×16; zoom 61,22 14×16; first page (48,163) → AvnView; range (784,57) → AvnView; freeze (898,57) → AvnView; glass top (48,14) → AvnView
2026-10-31 00:27:41.643 INF [App] Coreloom 1.8.6+80b9ed34087e143dc41ad03aa8b0900afd066ce6 starting (args: ; bundle: /Applications/Coreloom.app; macOS 15.8.2, Arm64, .NET 10.0.14, PID 6798; Apple silicon: True; virtual machine: True).
```
