# Coreloom 1.8.6 on macOS 15.7.9 (x64): a run

- Running after 79 s and 12 pages:
```
  PID    RSS      VSZ  %CPU ELAPSED COMMAND
11813 335968 37754400   7.1   01:19 /Applications/Coreloom.app/Contents/MacOS/Coreloom
```
- Other Coreloom processes left (the page launches must hand over and quit): 0
- Quit cleanly on SIGTERM

## Connect
- `GET /api/info`: `{"app":"Coreloom","version":"1.8.6","platform":"macos","name":"sjc20-bb655-8bf9e678-ecb8-4d13-a728-2816ed5f65d4-0AA41904A388","pc":"-QToGBRm1fxFsoQgDbOVMg","pairing":{"link":false,"code":false,"paused":false}}`
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
- From this Mac's network address 192.168.64.4: 200 (answered only when the address is a private one)
- Menu bar readings: cpu, memory, network shown (log)
- With Settings › Connect showing: `{"app":"Coreloom","version":"1.8.6","platform":"macos","name":"sjc20-bb655-8bf9e678-ecb8-4d13-a728-2816ed5f65d4-0AA41904A388","pc":"-QToGBRm1fxFsoQgDbOVMg","pairing":{"link":true,"code":true,"paused":false}}`
- Quit cleanly on SIGTERM with Connect on

## Backgrounds
- Image (aurora): 2026-10-31 01:15:55.562 INF [Appearance] Background: Image aurora drawn at 1360×880 (frost 340×220) in 224 ms.
- SeeThrough: 2026-10-31 01:16:08.432 INF [Appearance] Background: See-through.

## Readings window
- Shown (log); picture: 95-readings-window.png

## Log (errors and warnings)
```
```

## Log (first 60 lines)
```
2026-10-31 01:14:17.312 INF [App] Coreloom 1.8.6+83cec87096e82bc195c5a703ac5479163e8335ee starting (args: ; bundle: /Applications/Coreloom.app; macOS 15.7.9, X64, .NET 10.0.14, PID 11813; Apple silicon: False; virtual machine: True).
2026-10-31 01:14:19.084 INF [Connect] Listening on port 47101 (20 app files).
2026-10-31 01:14:19.127 INF [Notifications] Notifications aren't allowed (System Settings › Notifications › Coreloom).
2026-10-31 01:14:19.272 INF [Telemetry] Telemetry started (7 providers, interval 1000 ms).
2026-10-31 01:14:19.274 INF [Overlay] Hotkey ⌥⌘O registered.
2026-10-31 01:14:19.350 INF [Sensors] Sensors: SMC (1 fan, 13 temperature keys, system power, CPU power).
2026-10-31 01:14:19.912 INF [Appearance] Background: Gradient coreloom drawn at 1360×880 (frost 340×220) in 47 ms.
2026-10-31 01:14:20.278 INF [Telemetry] Coreloom process: Running in 1 ms
2026-10-31 01:14:20.278 INF [Telemetry] Mac sensors: Running in 126 ms — Mac mini (Macmini6,2); 1 fan(s); 13 temperature groups; sensors: SMC (1 fan, 13 temperature keys, system power, CPU power)
2026-10-31 01:14:20.278 INF [Telemetry] macOS processor counters: Running in 113 ms — Intel Core i7-8700B: 4 threads; sensors: SMC (1 fan, 13 temperature keys, system power, CPU power)
2026-10-31 01:14:20.278 INF [Telemetry] macOS memory: Running in 113 ms — 14 GB physical memory
2026-10-31 01:14:20.278 INF [Telemetry] macOS storage: Running in 130 ms — 1 disk(s), 1 volume(s)
2026-10-31 01:14:20.278 INF [Telemetry] macOS network: Running in 23 ms — 1 interface(s), primary en0
2026-10-31 01:14:20.278 INF [Telemetry] macOS graphics drivers: Running in 3 ms — Apple Paravirtualized Graphics Device
2026-10-31 01:14:20.359 INF [Overlay] Readings window shown.
2026-10-31 01:14:20.360 INF [MenuBar] Readings in the menu bar: cpu, memory, network.
2026-10-31 01:14:21.848 INF [Window] Title bar: glass 10,10 232×860; close 26,22 14×16; minimize 46,22 14×16; zoom 66,22 14×16; collapse (220,31) → AvnView; first page (126,121) → AvnView; range (1201,57) → AvnView; freeze (1315,57) → AvnView; glass top (126,14) → AvnView
2026-10-31 01:14:37.574 INF [Gaming] Found 0 installed games in 165 ms.
2026-10-31 01:15:36.127 INF [App] Coreloom 1.8.6+83cec87096e82bc195c5a703ac5479163e8335ee starting (args: ; bundle: /Applications/Coreloom.app; macOS 15.7.9, X64, .NET 10.0.14, PID 13102; Apple silicon: False; virtual machine: True).
2026-10-31 01:15:37.817 INF [Connect] Listening on port 47101 (20 app files).
2026-10-31 01:15:37.863 INF [Notifications] Notifications aren't allowed (System Settings › Notifications › Coreloom).
2026-10-31 01:15:37.977 INF [Telemetry] Telemetry started (7 providers, interval 1000 ms).
2026-10-31 01:15:37.979 INF [Overlay] Hotkey ⌥⌘O registered.
2026-10-31 01:15:37.982 INF [Telemetry] Coreloom process: Running in 1 ms
2026-10-31 01:15:38.049 INF [Sensors] Sensors: SMC (1 fan, 13 temperature keys, system power, CPU power).
2026-10-31 01:15:38.532 INF [Appearance] Background: Gradient coreloom drawn at 1360×880 (frost 340×220) in 44 ms.
2026-10-31 01:15:38.984 INF [Telemetry] Mac sensors: Running in 114 ms — Mac mini (Macmini6,2); 1 fan(s); 13 temperature groups; sensors: SMC (1 fan, 13 temperature keys, system power, CPU power)
2026-10-31 01:15:38.984 INF [Telemetry] macOS processor counters: Running in 105 ms — Intel Core i7-8700B: 4 threads; sensors: SMC (1 fan, 13 temperature keys, system power, CPU power)
2026-10-31 01:15:38.984 INF [Telemetry] macOS memory: Running in 68 ms — 14 GB physical memory
2026-10-31 01:15:38.984 INF [Telemetry] macOS storage: Running in 129 ms — 1 disk(s), 1 volume(s)
2026-10-31 01:15:38.984 INF [Telemetry] macOS network: Running in 15 ms — 1 interface(s), primary en0
2026-10-31 01:15:38.984 INF [Telemetry] macOS graphics drivers: Running in 23 ms — Apple Paravirtualized Graphics Device
2026-10-31 01:15:39.063 INF [Overlay] Readings window shown.
2026-10-31 01:15:39.064 INF [MenuBar] Readings in the menu bar: cpu, memory, network.
2026-10-31 01:15:40.477 INF [Window] Title bar: glass 10,10 232×860; close 26,22 14×16; minimize 46,22 14×16; zoom 66,22 14×16; collapse (220,31) → AvnView; first page (126,121) → AvnView; range (1201,57) → AvnView; freeze (1315,57) → AvnView; glass top (126,14) → AvnView
2026-10-31 01:15:53.027 INF [App] Coreloom 1.8.6+83cec87096e82bc195c5a703ac5479163e8335ee starting (args: ; bundle: /Applications/Coreloom.app; macOS 15.7.9, X64, .NET 10.0.14, PID 13423; Apple silicon: False; virtual machine: True).
2026-10-31 01:15:54.685 INF [Connect] Listening on port 47101 (20 app files).
2026-10-31 01:15:54.726 INF [Notifications] Notifications aren't allowed (System Settings › Notifications › Coreloom).
2026-10-31 01:15:54.834 INF [Telemetry] Telemetry started (7 providers, interval 1000 ms).
2026-10-31 01:15:54.836 INF [Overlay] Hotkey ⌥⌘O registered.
2026-10-31 01:15:54.839 INF [Telemetry] Coreloom process: Running in 1 ms
2026-10-31 01:15:54.906 INF [Sensors] Sensors: SMC (1 fan, 13 temperature keys, system power, CPU power).
2026-10-31 01:15:55.562 INF [Appearance] Background: Image aurora drawn at 1360×880 (frost 340×220) in 224 ms.
2026-10-31 01:15:55.839 INF [Telemetry] Mac sensors: Running in 115 ms — Mac mini (Macmini6,2); 1 fan(s); 13 temperature groups; sensors: SMC (1 fan, 13 temperature keys, system power, CPU power)
2026-10-31 01:15:55.840 INF [Telemetry] macOS processor counters: Running in 106 ms — Intel Core i7-8700B: 4 threads; sensors: SMC (1 fan, 13 temperature keys, system power, CPU power)
2026-10-31 01:15:55.840 INF [Telemetry] macOS memory: Running in 67 ms — 14 GB physical memory
2026-10-31 01:15:55.840 INF [Telemetry] macOS storage: Running in 129 ms — 1 disk(s), 1 volume(s)
2026-10-31 01:15:55.840 INF [Telemetry] macOS network: Running in 17 ms — 1 interface(s), primary en0
2026-10-31 01:15:55.840 INF [Telemetry] macOS graphics drivers: Running in 23 ms — Apple Paravirtualized Graphics Device
2026-10-31 01:15:55.918 INF [Overlay] Readings window shown.
2026-10-31 01:15:55.918 INF [MenuBar] Readings in the menu bar: cpu, memory, network.
2026-10-31 01:15:57.323 INF [Window] Title bar: glass 10,10 232×860; close 26,22 14×16; minimize 46,22 14×16; zoom 66,22 14×16; collapse (220,31) → AvnView; first page (126,121) → AvnView; range (1201,57) → AvnView; freeze (1315,57) → AvnView; glass top (126,14) → AvnView
2026-10-31 01:16:06.044 INF [App] Coreloom 1.8.6+83cec87096e82bc195c5a703ac5479163e8335ee starting (args: ; bundle: /Applications/Coreloom.app; macOS 15.7.9, X64, .NET 10.0.14, PID 13589; Apple silicon: False; virtual machine: True).
2026-10-31 01:16:07.718 INF [Connect] Listening on port 47101 (20 app files).
2026-10-31 01:16:07.758 INF [Notifications] Notifications aren't allowed (System Settings › Notifications › Coreloom).
2026-10-31 01:16:07.874 INF [Telemetry] Telemetry started (7 providers, interval 1000 ms).
2026-10-31 01:16:07.876 INF [Overlay] Hotkey ⌥⌘O registered.
2026-10-31 01:16:07.879 INF [Telemetry] Coreloom process: Running in 1 ms
2026-10-31 01:16:07.945 INF [Sensors] Sensors: SMC (1 fan, 13 temperature keys, system power, CPU power).
2026-10-31 01:16:08.432 INF [Appearance] Background: See-through.
```
