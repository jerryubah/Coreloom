# Coreloom 1.8.6 on macOS 15.7.9 (x64): a run

- Running after 78 s and 12 pages:
```
  PID    RSS      VSZ  %CPU ELAPSED COMMAND
 5608 295472 37193520   7.6   01:18 /Applications/Coreloom.app/Contents/MacOS/Coreloom
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
- Image (aurora): 2026-10-31 01:10:02.057 INF [Appearance] Background: Image aurora drawn at 1360×880 (frost 340×220) in 654 ms.
- SeeThrough: 2026-10-31 01:10:15.468 INF [Appearance] Background: See-through.

## Readings window
- Shown (log); picture: 95-readings-window.png

## Log (errors and warnings)
```
```

## Log (first 60 lines)
```
2026-10-31 01:08:22.874 INF [App] Coreloom 1.8.6+83cec87096e82bc195c5a703ac5479163e8335ee starting (args: ; bundle: /Applications/Coreloom.app; macOS 15.7.9, X64, .NET 10.0.14, PID 5608; Apple silicon: False; virtual machine: True).
2026-10-31 01:08:23.133 INF [App] First launch: Mac defaults applied.
2026-10-31 01:08:24.931 INF [Telemetry] Telemetry started (7 providers, interval 1000 ms).
2026-10-31 01:08:25.014 INF [Sensors] Sensors: SMC (1 fan, 13 temperature keys, system power, CPU power).
2026-10-31 01:08:25.940 INF [Telemetry] Coreloom process: Running in 1 ms
2026-10-31 01:08:25.940 INF [Telemetry] Mac sensors: Running in 155 ms — Mac mini (Macmini6,2); 1 fan(s); 13 temperature groups; sensors: SMC (1 fan, 13 temperature keys, system power, CPU power)
2026-10-31 01:08:25.940 INF [Telemetry] macOS processor counters: Running in 123 ms — Intel Core i7-8700B: 4 threads; sensors: SMC (1 fan, 13 temperature keys, system power, CPU power)
2026-10-31 01:08:25.940 INF [Telemetry] macOS memory: Running in 76 ms — 14 GB physical memory
2026-10-31 01:08:25.940 INF [Telemetry] macOS storage: Running in 144 ms — 1 disk(s), 1 volume(s)
2026-10-31 01:08:25.940 INF [Telemetry] macOS network: Running in 91 ms — 1 interface(s), primary en0
2026-10-31 01:08:25.940 INF [Telemetry] macOS graphics drivers: Running in 4 ms — Apple Paravirtualized Graphics Device
2026-10-31 01:08:32.385 INF [Appearance] Background: Gradient coreloom drawn at 1360×880 (frost 340×220) in 77 ms.
2026-10-31 01:08:34.279 INF [Window] Title bar: glass 10,10 232×860; close 26,22 14×16; minimize 46,22 14×16; zoom 66,22 14×16; collapse (220,31) → AvnView; first page (126,121) → AvnView; range (1201,57) → AvnView; freeze (1315,57) → AvnView; glass top (126,14) → AvnView
2026-10-31 01:08:43.506 INF [Gaming] Found 0 installed games in 180 ms.
2026-10-31 01:09:02.993 INF [Alerts] CPU temperature: CPU temperature is 101 °C — above your 97 °C threshold for 10 s.
2026-10-31 01:09:41.840 INF [App] Coreloom 1.8.6+83cec87096e82bc195c5a703ac5479163e8335ee starting (args: ; bundle: /Applications/Coreloom.app; macOS 15.7.9, X64, .NET 10.0.14, PID 7096; Apple silicon: False; virtual machine: True).
2026-10-31 01:09:43.719 INF [Connect] Listening on port 47101 (20 app files).
2026-10-31 01:09:43.761 INF [Notifications] Notifications aren't allowed (System Settings › Notifications › Coreloom).
2026-10-31 01:09:43.949 INF [Telemetry] Telemetry started (7 providers, interval 1000 ms).
2026-10-31 01:09:44.013 INF [Sensors] Sensors: SMC (1 fan, 13 temperature keys, system power, CPU power).
2026-10-31 01:09:44.525 INF [Appearance] Background: Gradient coreloom drawn at 1360×880 (frost 340×220) in 54 ms.
2026-10-31 01:09:44.892 INF [Telemetry] Coreloom process: Running in 1 ms
2026-10-31 01:09:44.892 INF [Telemetry] Mac sensors: Running in 120 ms — Mac mini (Macmini6,2); 1 fan(s); 13 temperature groups; sensors: SMC (1 fan, 13 temperature keys, system power, CPU power)
2026-10-31 01:09:44.892 INF [Telemetry] macOS processor counters: Running in 103 ms — Intel Core i7-8700B: 4 threads; sensors: SMC (1 fan, 13 temperature keys, system power, CPU power)
2026-10-31 01:09:44.892 INF [Telemetry] macOS memory: Running in 103 ms — 14 GB physical memory
2026-10-31 01:09:44.892 INF [Telemetry] macOS storage: Running in 116 ms — 1 disk(s), 1 volume(s)
2026-10-31 01:09:44.892 INF [Telemetry] macOS network: Running in 14 ms — 1 interface(s), primary en0
2026-10-31 01:09:44.892 INF [Telemetry] macOS graphics drivers: Running in 4 ms — Apple Paravirtualized Graphics Device
2026-10-31 01:09:44.942 INF [MenuBar] Readings in the menu bar: cpu, memory, network.
2026-10-31 01:09:46.455 INF [Window] Title bar: glass 10,10 232×860; close 26,22 14×16; minimize 46,22 14×16; zoom 66,22 14×16; collapse (220,31) → AvnView; first page (126,121) → AvnView; range (1201,57) → AvnView; freeze (1315,57) → AvnView; glass top (126,14) → AvnView
2026-10-31 01:09:55.925 INF [Alerts] CPU temperature: CPU temperature is 99 °C — above your 97 °C threshold for 10 s.
2026-10-31 01:09:58.711 INF [App] Coreloom 1.8.6+83cec87096e82bc195c5a703ac5479163e8335ee starting (args: ; bundle: /Applications/Coreloom.app; macOS 15.7.9, X64, .NET 10.0.14, PID 7362; Apple silicon: False; virtual machine: True).
2026-10-31 01:10:00.633 INF [Connect] Listening on port 47101 (20 app files).
2026-10-31 01:10:00.677 INF [Notifications] Notifications aren't allowed (System Settings › Notifications › Coreloom).
2026-10-31 01:10:00.810 INF [Telemetry] Telemetry started (7 providers, interval 1000 ms).
2026-10-31 01:10:00.883 INF [Sensors] Sensors: SMC (1 fan, 13 temperature keys, system power, CPU power).
2026-10-31 01:10:01.812 INF [Telemetry] Coreloom process: Running in 1 ms
2026-10-31 01:10:01.812 INF [Telemetry] Mac sensors: Running in 85 ms — Mac mini (Macmini6,2); 1 fan(s); 13 temperature groups; sensors: SMC (1 fan, 13 temperature keys, system power, CPU power)
2026-10-31 01:10:01.812 INF [Telemetry] macOS processor counters: Running in 109 ms — Intel Core i7-8700B: 4 threads; sensors: SMC (1 fan, 13 temperature keys, system power, CPU power)
2026-10-31 01:10:01.812 INF [Telemetry] macOS memory: Running in 71 ms — 14 GB physical memory
2026-10-31 01:10:01.812 INF [Telemetry] macOS storage: Running in 89 ms — 1 disk(s), 1 volume(s)
2026-10-31 01:10:01.812 INF [Telemetry] macOS network: Running in 42 ms — 1 interface(s), primary en0
2026-10-31 01:10:01.812 INF [Telemetry] macOS graphics drivers: Running in 3 ms — Apple Paravirtualized Graphics Device
2026-10-31 01:10:01.861 INF [MenuBar] Readings in the menu bar: cpu, memory, network.
2026-10-31 01:10:02.057 INF [Appearance] Background: Image aurora drawn at 1360×880 (frost 340×220) in 654 ms.
2026-10-31 01:10:03.387 INF [Window] Title bar: glass 10,10 232×860; close 26,22 14×16; minimize 46,22 14×16; zoom 66,22 14×16; collapse (220,31) → AvnView; first page (126,121) → AvnView; range (1201,57) → AvnView; freeze (1315,57) → AvnView; glass top (126,14) → AvnView
2026-10-31 01:10:12.567 INF [App] Coreloom 1.8.6+83cec87096e82bc195c5a703ac5479163e8335ee starting (args: ; bundle: /Applications/Coreloom.app; macOS 15.7.9, X64, .NET 10.0.14, PID 7573; Apple silicon: False; virtual machine: True).
2026-10-31 01:10:14.567 INF [Connect] Listening on port 47101 (20 app files).
2026-10-31 01:10:14.612 INF [Notifications] Notifications aren't allowed (System Settings › Notifications › Coreloom).
2026-10-31 01:10:14.852 INF [Telemetry] Telemetry started (7 providers, interval 1000 ms).
2026-10-31 01:10:14.917 INF [Sensors] Sensors: SMC (1 fan, 13 temperature keys, system power, CPU power).
2026-10-31 01:10:15.468 INF [Appearance] Background: See-through.
2026-10-31 01:10:15.758 INF [Telemetry] Coreloom process: Running in 1 ms
2026-10-31 01:10:15.758 INF [Telemetry] Mac sensors: Running in 130 ms — Mac mini (Macmini6,2); 1 fan(s); 13 temperature groups; sensors: SMC (1 fan, 13 temperature keys, system power, CPU power)
2026-10-31 01:10:15.758 INF [Telemetry] macOS processor counters: Running in 35 ms — Intel Core i7-8700B: 4 threads; sensors: SMC (1 fan, 13 temperature keys, system power, CPU power)
2026-10-31 01:10:15.758 INF [Telemetry] macOS memory: Running in 70 ms — 14 GB physical memory
2026-10-31 01:10:15.758 INF [Telemetry] macOS storage: Running in 71 ms — 1 disk(s), 1 volume(s)
2026-10-31 01:10:15.758 INF [Telemetry] macOS network: Running in 42 ms — 1 interface(s), primary en0
2026-10-31 01:10:15.758 INF [Telemetry] macOS graphics drivers: Running in 3 ms — Apple Paravirtualized Graphics Device
2026-10-31 01:10:15.807 INF [MenuBar] Readings in the menu bar: cpu, memory, network.
```
