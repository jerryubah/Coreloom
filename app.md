# Coreloom 1.9.3 on macOS 15.7.9 (x64): a run

- Running after 81 s and 12 pages:
```
  PID    RSS      VSZ  %CPU ELAPSED COMMAND
 8138 275620 37613264  36.7   01:21 /Applications/Coreloom.app/Contents/MacOS/Coreloom
```
- Other Coreloom processes left (the page launches must hand over and quit): 0
- Quit cleanly on SIGTERM

## Connect
- `GET /api/info`: `{"app":"Coreloom","version":"1.9.3","platform":"macos","name":"iad01-ar513-910a59d0-9347-4caf-ae34-d04e2f8005f3-D6D45CE65F06","pc":"CZPJ87KSkRMBgRkH25zh-g","pairing":{"link":false,"code":false,"paused":false}}`
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
- From this Mac's network address 192.168.64.10: 200 (answered only when the address is a private one)
- Menu bar readings: cpu, memory, network shown (log)
- With Settings › Connect showing: `{"app":"Coreloom","version":"1.9.3","platform":"macos","name":"iad01-ar513-910a59d0-9347-4caf-ae34-d04e2f8005f3-D6D45CE65F06","pc":"CZPJ87KSkRMBgRkH25zh-g","pairing":{"link":true,"code":true,"paused":false}}`
- Quit cleanly on SIGTERM with Connect on

## Backgrounds
- Image (aurora): 2026-11-05 13:43:42.279 INF [Appearance] Background: Image aurora drawn at 1360×880 (frost 340×220) in 568 ms.
- SeeThrough: 2026-11-05 13:43:50.662 INF [Appearance] Background: See-through.

## Readings window
- Shown (log); picture: 95-readings-window.png

## Log (errors and warnings)
```
```

## Log (first 60 lines)
```
2026-11-05 13:41:59.426 INF [App] Coreloom 1.9.3+e801863ca8ef00a907661ecb2d0fe4d72603fd5d starting (args: ; bundle: /Applications/Coreloom.app; macOS 15.7.9, X64, .NET 10.0.14, PID 8138; Apple silicon: False; virtual machine: True).
2026-11-05 13:41:59.899 INF [App] First launch: Mac defaults applied.
2026-11-05 13:42:03.251 INF [Telemetry] Telemetry started (7 providers, interval 1000 ms).
2026-11-05 13:42:03.348 INF [Sensors] Sensors: SMC (1 fan, 13 temperature keys, system power, CPU power).
2026-11-05 13:42:04.272 INF [Telemetry] Coreloom process: Running in 4 ms
2026-11-05 13:42:04.272 INF [Telemetry] Mac sensors: Running in 169 ms — Mac mini (Macmini6,2); 1 fan(s); 13 temperature groups; sensors: SMC (1 fan, 13 temperature keys, system power, CPU power)
2026-11-05 13:42:04.272 INF [Telemetry] macOS processor counters: Running in 149 ms — Intel Core i7-8700B: 4 threads; sensors: SMC (1 fan, 13 temperature keys, system power, CPU power)
2026-11-05 13:42:04.272 INF [Telemetry] macOS memory: Running in 88 ms — 14 GB physical memory
2026-11-05 13:42:04.272 INF [Telemetry] macOS storage: Running in 193 ms — 1 disk(s), 1 volume(s)
2026-11-05 13:42:04.272 INF [Telemetry] macOS network: Running in 55 ms — 1 interface(s), primary en0
2026-11-05 13:42:04.272 INF [Telemetry] macOS graphics drivers: Running in 8 ms — Apple Paravirtualized Graphics Device
2026-11-05 13:42:13.678 INF [Appearance] Background: Gradient coreloom drawn at 1360×880 (frost 340×220) in 493 ms.
2026-11-05 13:42:15.136 INF [Window] Title bar: glass 10,10 232×860; close 26,22 14×16; minimize 46,22 14×16; zoom 66,22 14×16; collapse (220,31) → AvnView; first page (126,121) → AvnView; range (1201,57) → AvnView; freeze (1315,57) → AvnView; glass top (126,14) → AvnView
2026-11-05 13:42:15.323 INF [Alerts] CPU temperature: CPU temperature is 98 °C — above your 97 °C threshold for 10 s.
2026-11-05 13:42:20.085 INF [Gaming] Found 0 installed games in 257 ms.
2026-11-05 13:43:20.336 INF [App] Coreloom 1.9.3+e801863ca8ef00a907661ecb2d0fe4d72603fd5d starting (args: ; bundle: /Applications/Coreloom.app; macOS 15.7.9, X64, .NET 10.0.14, PID 9337; Apple silicon: False; virtual machine: True).
2026-11-05 13:43:23.157 INF [Connect] Listening on port 47101 (20 app files).
2026-11-05 13:43:23.220 INF [Notifications] Notifications aren't allowed (System Settings › Notifications › Coreloom).
2026-11-05 13:43:23.397 INF [Telemetry] Telemetry started (7 providers, interval 1000 ms).
2026-11-05 13:43:23.404 INF [Telemetry] Coreloom process: Running in 1 ms
2026-11-05 13:43:23.486 INF [Sensors] Sensors: SMC (1 fan, 13 temperature keys, system power, CPU power).
2026-11-05 13:43:24.236 INF [Appearance] Background: Gradient coreloom drawn at 1360×880 (frost 340×220) in 63 ms.
2026-11-05 13:43:24.407 INF [Telemetry] Mac sensors: Running in 167 ms — Mac mini (Macmini6,2); 1 fan(s); 13 temperature groups; sensors: SMC (1 fan, 13 temperature keys, system power, CPU power)
2026-11-05 13:43:24.407 INF [Telemetry] macOS processor counters: Running in 134 ms — Intel Core i7-8700B: 4 threads; sensors: SMC (1 fan, 13 temperature keys, system power, CPU power)
2026-11-05 13:43:24.407 INF [Telemetry] macOS memory: Running in 84 ms — 14 GB physical memory
2026-11-05 13:43:24.407 INF [Telemetry] macOS storage: Running in 158 ms — 1 disk(s), 1 volume(s)
2026-11-05 13:43:24.407 INF [Telemetry] macOS network: Running in 76 ms — 1 interface(s), primary en0
2026-11-05 13:43:24.407 INF [Telemetry] macOS graphics drivers: Running in 5 ms — Apple Paravirtualized Graphics Device
2026-11-05 13:43:24.486 INF [MenuBar] Readings in the menu bar: cpu, memory, network.
2026-11-05 13:43:26.144 INF [Window] Title bar: glass 10,10 232×860; close 26,22 14×16; minimize 46,22 14×16; zoom 66,22 14×16; collapse (220,31) → AvnView; first page (126,121) → AvnView; range (1201,57) → AvnView; freeze (1315,57) → AvnView; glass top (126,14) → AvnView
2026-11-05 13:43:37.691 INF [App] Coreloom 1.9.3+e801863ca8ef00a907661ecb2d0fe4d72603fd5d starting (args: ; bundle: /Applications/Coreloom.app; macOS 15.7.9, X64, .NET 10.0.14, PID 9689; Apple silicon: False; virtual machine: True).
2026-11-05 13:43:40.572 INF [Connect] Listening on port 47101 (20 app files).
2026-11-05 13:43:40.638 INF [Notifications] Notifications aren't allowed (System Settings › Notifications › Coreloom).
2026-11-05 13:43:40.815 INF [Telemetry] Telemetry started (7 providers, interval 1000 ms).
2026-11-05 13:43:40.825 INF [Telemetry] Coreloom process: Running in 2 ms
2026-11-05 13:43:40.909 INF [Sensors] Sensors: SMC (1 fan, 13 temperature keys, system power, CPU power).
2026-11-05 13:43:41.827 INF [Telemetry] Mac sensors: Running in 164 ms — Mac mini (Macmini6,2); 1 fan(s); 13 temperature groups; sensors: SMC (1 fan, 13 temperature keys, system power, CPU power)
2026-11-05 13:43:41.827 INF [Telemetry] macOS processor counters: Running in 134 ms — Intel Core i7-8700B: 4 threads; sensors: SMC (1 fan, 13 temperature keys, system power, CPU power)
2026-11-05 13:43:41.827 INF [Telemetry] macOS memory: Running in 87 ms — 14 GB physical memory
2026-11-05 13:43:41.827 INF [Telemetry] macOS storage: Running in 171 ms — 1 disk(s), 1 volume(s)
2026-11-05 13:43:41.827 INF [Telemetry] macOS network: Running in 72 ms — 1 interface(s), primary en0
2026-11-05 13:43:41.827 INF [Telemetry] macOS graphics drivers: Running in 14 ms — Apple Paravirtualized Graphics Device
2026-11-05 13:43:41.897 INF [MenuBar] Readings in the menu bar: cpu, memory, network.
2026-11-05 13:43:42.279 INF [Appearance] Background: Image aurora drawn at 1360×880 (frost 340×220) in 568 ms.
2026-11-05 13:43:46.553 INF [App] Coreloom 1.9.3+e801863ca8ef00a907661ecb2d0fe4d72603fd5d starting (args: ; bundle: /Applications/Coreloom.app; macOS 15.7.9, X64, .NET 10.0.14, PID 9780; Apple silicon: False; virtual machine: True).
2026-11-05 13:43:49.482 INF [Connect] Listening on port 47101 (20 app files).
2026-11-05 13:43:49.555 INF [Notifications] Notifications aren't allowed (System Settings › Notifications › Coreloom).
2026-11-05 13:43:49.775 INF [Telemetry] Telemetry started (7 providers, interval 1000 ms).
2026-11-05 13:43:49.783 INF [Telemetry] Coreloom process: Running in 2 ms
2026-11-05 13:43:49.864 INF [Sensors] Sensors: SMC (1 fan, 13 temperature keys, system power, CPU power).
2026-11-05 13:43:50.662 INF [Appearance] Background: See-through.
2026-11-05 13:43:50.785 INF [Telemetry] Mac sensors: Running in 150 ms — Mac mini (Macmini6,2); 1 fan(s); 13 temperature groups; sensors: SMC (1 fan, 13 temperature keys, system power, CPU power)
2026-11-05 13:43:50.785 INF [Telemetry] macOS processor counters: Running in 132 ms — Intel Core i7-8700B: 4 threads; sensors: SMC (1 fan, 13 temperature keys, system power, CPU power)
2026-11-05 13:43:50.785 INF [Telemetry] macOS memory: Running in 84 ms — 14 GB physical memory
2026-11-05 13:43:50.785 INF [Telemetry] macOS storage: Running in 164 ms — 1 disk(s), 1 volume(s)
2026-11-05 13:43:50.785 INF [Telemetry] macOS network: Running in 40 ms — 1 interface(s), primary en0
2026-11-05 13:43:50.785 INF [Telemetry] macOS graphics drivers: Running in 12 ms — Apple Paravirtualized Graphics Device
2026-11-05 13:43:50.854 INF [MenuBar] Readings in the menu bar: cpu, memory, network.
2026-11-05 13:43:52.625 INF [Window] Title bar: glass 10,10 232×860; close 26,22 14×16; minimize 46,22 14×16; zoom 66,22 14×16; collapse (220,31) → AvnView; first page (126,121) → AvnView; range (1201,57) → AvnView; freeze (1315,57) → AvnView; glass top (126,14) → AvnView
2026-11-05 13:43:59.609 INF [App] Coreloom 1.9.3+e801863ca8ef00a907661ecb2d0fe4d72603fd5d starting (args: ; bundle: /Applications/Coreloom.app; macOS 15.7.9, X64, .NET 10.0.14, PID 10020; Apple silicon: False; virtual machine: True).
```
