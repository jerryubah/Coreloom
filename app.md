# Coreloom 1.9.3 on macOS 15.8.2 (arm64): a run

- Running after 79 s and 12 pages:
```
  PID    RSS      VSZ  %CPU ELAPSED COMMAND
 3870 430208 413792240  10.4   01:19 /Applications/Coreloom.app/Contents/MacOS/Coreloom
```
- Other Coreloom processes left (the page launches must hand over and quit): 0
- Quit cleanly on SIGTERM

## Connect
- `GET /api/info`: `{"app":"Coreloom","version":"1.9.3","platform":"macos","name":"sjc22-bt144-e97f03f7-2485-4965-bee8-80fe32a10f12-2E49895E87B9","pc":"uXaSCEPRgoLwuk5l5ulozw","pairing":{"link":false,"code":false,"paused":false}}`
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
- With Settings › Connect showing: `{"app":"Coreloom","version":"1.9.3","platform":"macos","name":"sjc22-bt144-e97f03f7-2485-4965-bee8-80fe32a10f12-2E49895E87B9","pc":"uXaSCEPRgoLwuk5l5ulozw","pairing":{"link":true,"code":true,"paused":false}}`
- Quit cleanly on SIGTERM with Connect on

## Backgrounds
- Image (aurora): 2026-11-05 13:39:10.705 INF [Appearance] Background: Image aurora drawn at 943×634 (frost 236×159) in 126 ms.
- SeeThrough: 2026-11-05 13:39:18.694 INF [Appearance] Background: See-through.

## Readings window
- Shown (log); picture: 95-readings-window.png

## Log (errors and warnings)
```
2026-11-05 13:37:55.358 WRN [Updates] Update check failed.
```

## Log (first 60 lines)
```
2026-11-05 13:37:33.492 INF [App] Coreloom 1.9.3+e801863ca8ef00a907661ecb2d0fe4d72603fd5d starting (args: ; bundle: /Applications/Coreloom.app; macOS 15.8.2, Arm64, .NET 10.0.14, PID 3870; Apple silicon: True; virtual machine: True).
2026-11-05 13:37:33.688 INF [App] First launch: Mac defaults applied.
2026-11-05 13:37:34.909 INF [Telemetry] Telemetry started (7 providers, interval 1000 ms).
2026-11-05 13:37:34.910 INF [Telemetry] Coreloom process: Running in 1 ms
2026-11-05 13:37:34.931 INF [Sensors] Sensors: no hardware sensors (virtual machine).
2026-11-05 13:37:35.912 INF [Telemetry] Mac sensors: Running in 33 ms — Apple Virtual Machine 1 (VirtualMac2,1); no fans reported; 0 temperature groups; sensors: no hardware sensors (virtual machine)
2026-11-05 13:37:35.912 INF [Telemetry] macOS processor counters: Running in 7 ms — Apple M1 (Virtual): 3 threads; sensors: no hardware sensors (virtual machine)
2026-11-05 13:37:35.912 INF [Telemetry] macOS memory: Running in 12 ms — 7 GB physical memory
2026-11-05 13:37:35.912 INF [Telemetry] macOS storage: Running in 29 ms — 1 disk(s), 1 volume(s)
2026-11-05 13:37:35.912 INF [Telemetry] macOS network: Running in 41 ms — 2 interface(s), primary en0
2026-11-05 13:37:35.912 INF [Telemetry] macOS graphics drivers: Running in 21 ms — Apple Paravirtual GPU
2026-11-05 13:37:40.881 INF [Appearance] Background: Gradient coreloom drawn at 943×631 (frost 236×158) in 76 ms.
2026-11-05 13:37:42.759 INF [Window] Title bar: glass 10,10 76×611; close 21,22 14×16; minimize 41,22 14×16; zoom 61,22 14×16; first page (48,163) → AvnView; range (784,57) → AvnView; freeze (898,57) → AvnView; glass top (48,14) → AvnView
2026-11-05 13:37:54.286 INF [Gaming] Found 0 installed games in 501 ms.
2026-11-05 13:37:55.358 WRN [Updates] Update check failed.
    Coreloom.Core.Updates.UpdateException: GitHub rate limit reached. Try again later.
   at Coreloom.Core.Updates.GitHubReleaseClient.ThrowIfRateLimited(HttpResponseMessage response)
   at Coreloom.Core.Updates.GitHubReleaseClient.GetLatestAsync(Boolean includePreReleases, CancellationToken cancellationToken)
   at Coreloom.ViewModels.UpdatesViewModel.<>c__DisplayClass143_0.<<CheckAsync>b__1>d.MoveNext()
--- End of stack trace from previous location ---
   at Coreloom.Core.Updates.UpdateChecker.CheckAsync(Boolean includePreReleases, String skippedVersion, CancellationToken cancellationToken)
2026-11-05 13:38:52.006 INF [App] Coreloom 1.9.3+e801863ca8ef00a907661ecb2d0fe4d72603fd5d starting (args: ; bundle: /Applications/Coreloom.app; macOS 15.8.2, Arm64, .NET 10.0.14, PID 5473; Apple silicon: True; virtual machine: True).
2026-11-05 13:38:52.795 INF [Connect] Listening on port 47101 (20 app files).
2026-11-05 13:38:52.823 INF [Notifications] Notifications aren't allowed (System Settings › Notifications › Coreloom).
2026-11-05 13:38:53.019 INF [Telemetry] Telemetry started (7 providers, interval 1000 ms).
2026-11-05 13:38:53.043 INF [Sensors] Sensors: no hardware sensors (virtual machine).
2026-11-05 13:38:54.017 INF [Telemetry] Coreloom process: Running in 1 ms
2026-11-05 13:38:54.017 INF [Telemetry] Mac sensors: Running in 34 ms — Apple Virtual Machine 1 (VirtualMac2,1); no fans reported; 0 temperature groups; sensors: no hardware sensors (virtual machine)
2026-11-05 13:38:54.017 INF [Telemetry] macOS processor counters: Running in 24 ms — Apple M1 (Virtual): 3 threads; sensors: no hardware sensors (virtual machine)
2026-11-05 13:38:54.017 INF [Telemetry] macOS memory: Running in 23 ms — 7 GB physical memory
2026-11-05 13:38:54.017 INF [Telemetry] macOS storage: Running in 38 ms — 1 disk(s), 1 volume(s)
2026-11-05 13:38:54.017 INF [Telemetry] macOS network: Running in 31 ms — 2 interface(s), primary en0
2026-11-05 13:38:54.017 INF [Telemetry] macOS graphics drivers: Running in 4 ms — Apple Paravirtual GPU
2026-11-05 13:38:54.364 INF [MenuBar] Readings in the menu bar: cpu, memory, network.
2026-11-05 13:38:54.556 INF [Appearance] Background: Gradient coreloom drawn at 943×634 (frost 236×159) in 88 ms.
2026-11-05 13:38:56.439 INF [Window] Title bar: glass 10,10 76×614; close 21,22 14×16; minimize 41,22 14×16; zoom 61,22 14×16; first page (48,163) → AvnView; range (784,57) → AvnView; freeze (898,57) → AvnView; glass top (48,14) → AvnView
2026-11-05 13:39:08.285 INF [App] Coreloom 1.9.3+e801863ca8ef00a907661ecb2d0fe4d72603fd5d starting (args: ; bundle: /Applications/Coreloom.app; macOS 15.8.2, Arm64, .NET 10.0.14, PID 5756; Apple silicon: True; virtual machine: True).
2026-11-05 13:39:09.221 INF [Connect] Listening on port 47101 (20 app files).
2026-11-05 13:39:09.255 INF [Notifications] Notifications aren't allowed (System Settings › Notifications › Coreloom).
2026-11-05 13:39:09.417 INF [Telemetry] Telemetry started (7 providers, interval 1000 ms).
2026-11-05 13:39:09.418 INF [Telemetry] Coreloom process: Running in 1 ms
2026-11-05 13:39:09.429 INF [Sensors] Sensors: no hardware sensors (virtual machine).
2026-11-05 13:39:10.421 INF [Telemetry] Mac sensors: Running in 15 ms — Apple Virtual Machine 1 (VirtualMac2,1); no fans reported; 0 temperature groups; sensors: no hardware sensors (virtual machine)
2026-11-05 13:39:10.421 INF [Telemetry] macOS processor counters: Running in 2 ms — Apple M1 (Virtual): 3 threads; sensors: no hardware sensors (virtual machine)
2026-11-05 13:39:10.421 INF [Telemetry] macOS memory: Running in 5 ms — 7 GB physical memory
2026-11-05 13:39:10.421 INF [Telemetry] macOS storage: Running in 25 ms — 1 disk(s), 1 volume(s)
2026-11-05 13:39:10.421 INF [Telemetry] macOS network: Running in 28 ms — 2 interface(s), primary en0
2026-11-05 13:39:10.421 INF [Telemetry] macOS graphics drivers: Running in 3 ms — Apple Paravirtual GPU
2026-11-05 13:39:10.500 INF [MenuBar] Readings in the menu bar: cpu, memory, network.
2026-11-05 13:39:10.705 INF [Appearance] Background: Image aurora drawn at 943×634 (frost 236×159) in 126 ms.
2026-11-05 13:39:12.581 INF [Window] Title bar: glass 10,10 76×614; close 21,22 14×16; minimize 41,22 14×16; zoom 61,22 14×16; first page (48,163) → AvnView; range (784,57) → AvnView; freeze (898,57) → AvnView; glass top (48,14) → AvnView
2026-11-05 13:39:16.440 INF [App] Coreloom 1.9.3+e801863ca8ef00a907661ecb2d0fe4d72603fd5d starting (args: ; bundle: /Applications/Coreloom.app; macOS 15.8.2, Arm64, .NET 10.0.14, PID 5992; Apple silicon: True; virtual machine: True).
2026-11-05 13:39:17.359 INF [Connect] Listening on port 47101 (20 app files).
2026-11-05 13:39:17.385 INF [Notifications] Notifications aren't allowed (System Settings › Notifications › Coreloom).
2026-11-05 13:39:17.510 INF [Telemetry] Telemetry started (7 providers, interval 1000 ms).
2026-11-05 13:39:17.519 INF [Sensors] Sensors: no hardware sensors (virtual machine).
2026-11-05 13:39:18.511 INF [Telemetry] Coreloom process: Running in 0 ms
2026-11-05 13:39:18.511 INF [Telemetry] Mac sensors: Running in 14 ms — Apple Virtual Machine 1 (VirtualMac2,1); no fans reported; 0 temperature groups; sensors: no hardware sensors (virtual machine)
2026-11-05 13:39:18.511 INF [Telemetry] macOS processor counters: Running in 1 ms — Apple M1 (Virtual): 3 threads; sensors: no hardware sensors (virtual machine)
2026-11-05 13:39:18.511 INF [Telemetry] macOS memory: Running in 2 ms — 7 GB physical memory
```
