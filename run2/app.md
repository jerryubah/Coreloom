# Coreloom 1.8.6 on macOS 15.7.9 (x64): a run

- Running after 79 s and 12 pages:
```
  PID    RSS      VSZ  %CPU ELAPSED COMMAND
 8951 333688 37741484  12.7   01:19 /Applications/Coreloom.app/Contents/MacOS/Coreloom
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
- Image (aurora): 2026-10-31 01:13:03.531 INF [Appearance] Background: Image aurora drawn at 1360×880 (frost 340×220) in 225 ms.
- SeeThrough: 2026-10-31 01:13:16.806 INF [Appearance] Background: See-through.

## Readings window
- Shown (log); picture: 95-readings-window.png

## Log (errors and warnings)
```
```

## Log (first 60 lines)
```
2026-10-31 01:11:24.842 INF [App] Coreloom 1.8.6+83cec87096e82bc195c5a703ac5479163e8335ee starting (args: ; bundle: /Applications/Coreloom.app; macOS 15.7.9, X64, .NET 10.0.14, PID 8951; Apple silicon: False; virtual machine: True).
2026-10-31 01:11:26.635 INF [Connect] Listening on port 47101 (20 app files).
2026-10-31 01:11:26.680 INF [Notifications] Notifications aren't allowed (System Settings › Notifications › Coreloom).
2026-10-31 01:11:26.816 INF [Telemetry] Telemetry started (7 providers, interval 1000 ms).
2026-10-31 01:11:26.818 INF [Overlay] Hotkey ⌥⌘O registered.
2026-10-31 01:11:26.887 INF [Sensors] Sensors: SMC (1 fan, 13 temperature keys, system power, CPU power).
2026-10-31 01:11:27.420 INF [Appearance] Background: Gradient coreloom drawn at 1360×880 (frost 340×220) in 50 ms.
2026-10-31 01:11:27.825 INF [Telemetry] Coreloom process: Running in 2 ms
2026-10-31 01:11:27.825 INF [Telemetry] Mac sensors: Running in 116 ms — Mac mini (Macmini6,2); 1 fan(s); 13 temperature groups; sensors: SMC (1 fan, 13 temperature keys, system power, CPU power)
2026-10-31 01:11:27.825 INF [Telemetry] macOS processor counters: Running in 107 ms — Intel Core i7-8700B: 4 threads; sensors: SMC (1 fan, 13 temperature keys, system power, CPU power)
2026-10-31 01:11:27.825 INF [Telemetry] macOS memory: Running in 67 ms — 14 GB physical memory
2026-10-31 01:11:27.825 INF [Telemetry] macOS storage: Running in 119 ms — 1 disk(s), 1 volume(s)
2026-10-31 01:11:27.825 INF [Telemetry] macOS network: Running in 31 ms — 1 interface(s), primary en0
2026-10-31 01:11:27.825 INF [Telemetry] macOS graphics drivers: Running in 11 ms — Apple Paravirtualized Graphics Device
2026-10-31 01:11:27.912 INF [Overlay] Readings window shown.
2026-10-31 01:11:27.913 INF [MenuBar] Readings in the menu bar: cpu, memory, network.
2026-10-31 01:11:29.349 INF [Window] Title bar: glass 10,10 232×860; close 26,22 14×16; minimize 46,22 14×16; zoom 66,22 14×16; collapse (220,31) → AvnView; first page (126,121) → AvnView; range (1201,57) → AvnView; freeze (1315,57) → AvnView; glass top (126,14) → AvnView
2026-10-31 01:11:41.860 INF [Alerts] CPU temperature: CPU temperature is 98 °C — above your 97 °C threshold for 10 s.
2026-10-31 01:11:45.165 INF [Gaming] Found 0 installed games in 157 ms.
2026-10-31 01:12:44.141 INF [App] Coreloom 1.8.6+83cec87096e82bc195c5a703ac5479163e8335ee starting (args: ; bundle: /Applications/Coreloom.app; macOS 15.7.9, X64, .NET 10.0.14, PID 10171; Apple silicon: False; virtual machine: True).
2026-10-31 01:12:45.835 INF [Connect] Listening on port 47101 (20 app files).
2026-10-31 01:12:45.876 INF [Notifications] Notifications aren't allowed (System Settings › Notifications › Coreloom).
2026-10-31 01:12:45.991 INF [Telemetry] Telemetry started (7 providers, interval 1000 ms).
2026-10-31 01:12:45.993 INF [Overlay] Hotkey ⌥⌘O registered.
2026-10-31 01:12:45.996 INF [Telemetry] Coreloom process: Running in 1 ms
2026-10-31 01:12:46.062 INF [Sensors] Sensors: SMC (1 fan, 13 temperature keys, system power, CPU power).
2026-10-31 01:12:46.556 INF [Appearance] Background: Gradient coreloom drawn at 1360×880 (frost 340×220) in 45 ms.
2026-10-31 01:12:46.997 INF [Telemetry] Mac sensors: Running in 113 ms — Mac mini (Macmini6,2); 1 fan(s); 13 temperature groups; sensors: SMC (1 fan, 13 temperature keys, system power, CPU power)
2026-10-31 01:12:46.997 INF [Telemetry] macOS processor counters: Running in 105 ms — Intel Core i7-8700B: 4 threads; sensors: SMC (1 fan, 13 temperature keys, system power, CPU power)
2026-10-31 01:12:46.997 INF [Telemetry] macOS memory: Running in 67 ms — 14 GB physical memory
2026-10-31 01:12:46.997 INF [Telemetry] macOS storage: Running in 116 ms — 1 disk(s), 1 volume(s)
2026-10-31 01:12:46.997 INF [Telemetry] macOS network: Running in 23 ms — 1 interface(s), primary en0
2026-10-31 01:12:46.997 INF [Telemetry] macOS graphics drivers: Running in 17 ms — Apple Paravirtualized Graphics Device
2026-10-31 01:12:47.076 INF [Overlay] Readings window shown.
2026-10-31 01:12:47.077 INF [MenuBar] Readings in the menu bar: cpu, memory, network.
2026-10-31 01:12:48.498 INF [Window] Title bar: glass 10,10 232×860; close 26,22 14×16; minimize 46,22 14×16; zoom 66,22 14×16; collapse (220,31) → AvnView; first page (126,121) → AvnView; range (1201,57) → AvnView; freeze (1315,57) → AvnView; glass top (126,14) → AvnView
2026-10-31 01:13:00.993 INF [App] Coreloom 1.8.6+83cec87096e82bc195c5a703ac5479163e8335ee starting (args: ; bundle: /Applications/Coreloom.app; macOS 15.7.9, X64, .NET 10.0.14, PID 10491; Apple silicon: False; virtual machine: True).
2026-10-31 01:13:02.647 INF [Connect] Listening on port 47101 (20 app files).
2026-10-31 01:13:02.688 INF [Notifications] Notifications aren't allowed (System Settings › Notifications › Coreloom).
2026-10-31 01:13:02.804 INF [Telemetry] Telemetry started (7 providers, interval 1000 ms).
2026-10-31 01:13:02.805 INF [Overlay] Hotkey ⌥⌘O registered.
2026-10-31 01:13:02.875 INF [Sensors] Sensors: SMC (1 fan, 13 temperature keys, system power, CPU power).
2026-10-31 01:13:03.531 INF [Appearance] Background: Image aurora drawn at 1360×880 (frost 340×220) in 225 ms.
2026-10-31 01:13:03.809 INF [Telemetry] Coreloom process: Running in 1 ms
2026-10-31 01:13:03.809 INF [Telemetry] Mac sensors: Running in 113 ms — Mac mini (Macmini6,2); 1 fan(s); 13 temperature groups; sensors: SMC (1 fan, 13 temperature keys, system power, CPU power)
2026-10-31 01:13:03.809 INF [Telemetry] macOS processor counters: Running in 104 ms — Intel Core i7-8700B: 4 threads; sensors: SMC (1 fan, 13 temperature keys, system power, CPU power)
2026-10-31 01:13:03.809 INF [Telemetry] macOS memory: Running in 67 ms — 14 GB physical memory
2026-10-31 01:13:03.809 INF [Telemetry] macOS storage: Running in 124 ms — 1 disk(s), 1 volume(s)
2026-10-31 01:13:03.809 INF [Telemetry] macOS network: Running in 47 ms — 1 interface(s), primary en0
2026-10-31 01:13:03.809 INF [Telemetry] macOS graphics drivers: Running in 2 ms — Apple Paravirtualized Graphics Device
2026-10-31 01:13:03.892 INF [Overlay] Readings window shown.
2026-10-31 01:13:03.893 INF [MenuBar] Readings in the menu bar: cpu, memory, network.
2026-10-31 01:13:05.292 INF [Window] Title bar: glass 10,10 232×860; close 26,22 14×16; minimize 46,22 14×16; zoom 66,22 14×16; collapse (220,31) → AvnView; first page (126,121) → AvnView; range (1201,57) → AvnView; freeze (1315,57) → AvnView; glass top (126,14) → AvnView
2026-10-31 01:13:14.456 INF [App] Coreloom 1.8.6+83cec87096e82bc195c5a703ac5479163e8335ee starting (args: ; bundle: /Applications/Coreloom.app; macOS 15.7.9, X64, .NET 10.0.14, PID 10736; Apple silicon: False; virtual machine: True).
2026-10-31 01:13:16.137 INF [Connect] Listening on port 47101 (20 app files).
2026-10-31 01:13:16.179 INF [Notifications] Notifications aren't allowed (System Settings › Notifications › Coreloom).
2026-10-31 01:13:16.295 INF [Telemetry] Telemetry started (7 providers, interval 1000 ms).
2026-10-31 01:13:16.296 INF [Overlay] Hotkey ⌥⌘O registered.
2026-10-31 01:13:16.364 INF [Sensors] Sensors: SMC (1 fan, 13 temperature keys, system power, CPU power).
2026-10-31 01:13:16.806 INF [Appearance] Background: See-through.
```
