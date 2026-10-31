# Coreloom 1.8.6 on macOS 15.7.9 (x64): a run

- Running after 78 s and 12 pages:
```
  PID    RSS      VSZ  %CPU ELAPSED COMMAND
14687 332408 37759328   5.2   01:18 /Applications/Coreloom.app/Contents/MacOS/Coreloom
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
- Image (aurora): 2026-10-31 01:18:49.619 INF [Appearance] Background: Image aurora drawn at 1360×880 (frost 340×220) in 226 ms.
- SeeThrough: 2026-10-31 01:19:02.588 INF [Appearance] Background: See-through.

## Readings window
- Shown (log); picture: 95-readings-window.png

## Log (errors and warnings)
```
```

## Log (first 60 lines)
```
2026-10-31 01:17:11.429 INF [App] Coreloom 1.8.6+83cec87096e82bc195c5a703ac5479163e8335ee starting (args: ; bundle: /Applications/Coreloom.app; macOS 15.7.9, X64, .NET 10.0.14, PID 14687; Apple silicon: False; virtual machine: True).
2026-10-31 01:17:13.121 INF [Connect] Listening on port 47101 (20 app files).
2026-10-31 01:17:13.161 INF [Notifications] Notifications aren't allowed (System Settings › Notifications › Coreloom).
2026-10-31 01:17:13.311 INF [Telemetry] Telemetry started (7 providers, interval 1000 ms).
2026-10-31 01:17:13.314 INF [Overlay] Hotkey ⌥⌘O registered.
2026-10-31 01:17:13.370 INF [Sensors] Sensors: SMC (1 fan, 13 temperature keys, system power, CPU power).
2026-10-31 01:17:13.888 INF [Appearance] Background: Gradient coreloom drawn at 1360×880 (frost 340×220) in 45 ms.
2026-10-31 01:17:14.301 INF [Telemetry] Coreloom process: Running in 1 ms
2026-10-31 01:17:14.301 INF [Telemetry] Mac sensors: Running in 118 ms — Mac mini (Macmini6,2); 1 fan(s); 13 temperature groups; sensors: SMC (1 fan, 13 temperature keys, system power, CPU power)
2026-10-31 01:17:14.301 INF [Telemetry] macOS processor counters: Running in 77 ms — Intel Core i7-8700B: 4 threads; sensors: SMC (1 fan, 13 temperature keys, system power, CPU power)
2026-10-31 01:17:14.301 INF [Telemetry] macOS memory: Running in 38 ms — 14 GB physical memory
2026-10-31 01:17:14.301 INF [Telemetry] macOS storage: Running in 107 ms — 1 disk(s), 1 volume(s)
2026-10-31 01:17:14.301 INF [Telemetry] macOS network: Running in 20 ms — 1 interface(s), primary en0
2026-10-31 01:17:14.301 INF [Telemetry] macOS graphics drivers: Running in 21 ms — Apple Paravirtualized Graphics Device
2026-10-31 01:17:14.377 INF [Overlay] Readings window shown.
2026-10-31 01:17:14.378 INF [MenuBar] Readings in the menu bar: cpu, memory, network.
2026-10-31 01:17:15.826 INF [Window] Title bar: glass 10,10 232×860; close 26,22 14×16; minimize 46,22 14×16; zoom 66,22 14×16; collapse (220,31) → AvnView; first page (126,121) → AvnView; range (1201,57) → AvnView; freeze (1315,57) → AvnView; glass top (126,14) → AvnView
2026-10-31 01:17:31.702 INF [Gaming] Found 0 installed games in 145 ms.
2026-10-31 01:18:30.291 INF [App] Coreloom 1.8.6+83cec87096e82bc195c5a703ac5479163e8335ee starting (args: ; bundle: /Applications/Coreloom.app; macOS 15.7.9, X64, .NET 10.0.14, PID 15843; Apple silicon: False; virtual machine: True).
2026-10-31 01:18:31.970 INF [Connect] Listening on port 47101 (20 app files).
2026-10-31 01:18:32.012 INF [Notifications] Notifications aren't allowed (System Settings › Notifications › Coreloom).
2026-10-31 01:18:32.125 INF [Telemetry] Telemetry started (7 providers, interval 1000 ms).
2026-10-31 01:18:32.126 INF [Overlay] Hotkey ⌥⌘O registered.
2026-10-31 01:18:32.130 INF [Telemetry] Coreloom process: Running in 1 ms
2026-10-31 01:18:32.198 INF [Sensors] Sensors: SMC (1 fan, 13 temperature keys, system power, CPU power).
2026-10-31 01:18:32.688 INF [Appearance] Background: Gradient coreloom drawn at 1360×880 (frost 340×220) in 62 ms.
2026-10-31 01:18:33.132 INF [Telemetry] Mac sensors: Running in 116 ms — Mac mini (Macmini6,2); 1 fan(s); 13 temperature groups; sensors: SMC (1 fan, 13 temperature keys, system power, CPU power)
2026-10-31 01:18:33.133 INF [Telemetry] macOS processor counters: Running in 107 ms — Intel Core i7-8700B: 4 threads; sensors: SMC (1 fan, 13 temperature keys, system power, CPU power)
2026-10-31 01:18:33.133 INF [Telemetry] macOS memory: Running in 69 ms — 14 GB physical memory
2026-10-31 01:18:33.133 INF [Telemetry] macOS storage: Running in 118 ms — 1 disk(s), 1 volume(s)
2026-10-31 01:18:33.133 INF [Telemetry] macOS network: Running in 16 ms — 1 interface(s), primary en0
2026-10-31 01:18:33.133 INF [Telemetry] macOS graphics drivers: Running in 24 ms — Apple Paravirtualized Graphics Device
2026-10-31 01:18:33.205 INF [Overlay] Readings window shown.
2026-10-31 01:18:33.206 INF [MenuBar] Readings in the menu bar: cpu, memory, network.
2026-10-31 01:18:34.618 INF [Window] Title bar: glass 10,10 232×860; close 26,22 14×16; minimize 46,22 14×16; zoom 66,22 14×16; collapse (220,31) → AvnView; first page (126,121) → AvnView; range (1201,57) → AvnView; freeze (1315,57) → AvnView; glass top (126,14) → AvnView
2026-10-31 01:18:47.063 INF [App] Coreloom 1.8.6+83cec87096e82bc195c5a703ac5479163e8335ee starting (args: ; bundle: /Applications/Coreloom.app; macOS 15.7.9, X64, .NET 10.0.14, PID 16143; Apple silicon: False; virtual machine: True).
2026-10-31 01:18:48.732 INF [Connect] Listening on port 47101 (20 app files).
2026-10-31 01:18:48.774 INF [Notifications] Notifications aren't allowed (System Settings › Notifications › Coreloom).
2026-10-31 01:18:48.886 INF [Telemetry] Telemetry started (7 providers, interval 1000 ms).
2026-10-31 01:18:48.887 INF [Overlay] Hotkey ⌥⌘O registered.
2026-10-31 01:18:48.958 INF [Sensors] Sensors: SMC (1 fan, 13 temperature keys, system power, CPU power).
2026-10-31 01:18:49.619 INF [Appearance] Background: Image aurora drawn at 1360×880 (frost 340×220) in 226 ms.
2026-10-31 01:18:49.892 INF [Telemetry] Coreloom process: Running in 1 ms
2026-10-31 01:18:49.892 INF [Telemetry] Mac sensors: Running in 119 ms — Mac mini (Macmini6,2); 1 fan(s); 13 temperature groups; sensors: SMC (1 fan, 13 temperature keys, system power, CPU power)
2026-10-31 01:18:49.892 INF [Telemetry] macOS processor counters: Running in 109 ms — Intel Core i7-8700B: 4 threads; sensors: SMC (1 fan, 13 temperature keys, system power, CPU power)
2026-10-31 01:18:49.892 INF [Telemetry] macOS memory: Running in 68 ms — 14 GB physical memory
2026-10-31 01:18:49.892 INF [Telemetry] macOS storage: Running in 123 ms — 1 disk(s), 1 volume(s)
2026-10-31 01:18:49.892 INF [Telemetry] macOS network: Running in 39 ms — 1 interface(s), primary en0
2026-10-31 01:18:49.892 INF [Telemetry] macOS graphics drivers: Running in 6 ms — Apple Paravirtualized Graphics Device
2026-10-31 01:18:49.972 INF [Overlay] Readings window shown.
2026-10-31 01:18:49.973 INF [MenuBar] Readings in the menu bar: cpu, memory, network.
2026-10-31 01:18:51.377 INF [Window] Title bar: glass 10,10 232×860; close 26,22 14×16; minimize 46,22 14×16; zoom 66,22 14×16; collapse (220,31) → AvnView; first page (126,121) → AvnView; range (1201,57) → AvnView; freeze (1315,57) → AvnView; glass top (126,14) → AvnView
2026-10-31 01:19:00.142 INF [App] Coreloom 1.8.6+83cec87096e82bc195c5a703ac5479163e8335ee starting (args: ; bundle: /Applications/Coreloom.app; macOS 15.7.9, X64, .NET 10.0.14, PID 16305; Apple silicon: False; virtual machine: True).
2026-10-31 01:19:01.839 INF [Connect] Listening on port 47101 (20 app files).
2026-10-31 01:19:01.881 INF [Notifications] Notifications aren't allowed (System Settings › Notifications › Coreloom).
2026-10-31 01:19:02.002 INF [Telemetry] Telemetry started (7 providers, interval 1000 ms).
2026-10-31 01:19:02.003 INF [Overlay] Hotkey ⌥⌘O registered.
2026-10-31 01:19:02.007 INF [Telemetry] Coreloom process: Running in 1 ms
2026-10-31 01:19:02.072 INF [Sensors] Sensors: SMC (1 fan, 13 temperature keys, system power, CPU power).
2026-10-31 01:19:02.588 INF [Appearance] Background: See-through.
```
