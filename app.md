# Coreloom 1.6.0 on macOS 15.7.9 (arm64): a run

- Running after 78 s and 12 pages:
```
  PID    RSS      VSZ  %CPU ELAPSED COMMAND
 3872 379536 413131616   9.6   01:17 /Applications/Coreloom.app/Contents/MacOS/Coreloom
```
- Other Coreloom processes left (the page launches must hand over and quit): 0
- Quit cleanly on SIGTERM

## Log (errors and warnings)
```
```

## Log (first 60 lines)
```
2026-10-25 12:56:21.722 INF [App] Coreloom 1.6.0+e3104db1de721dcd317bfad4b48ae45a25d70cf1 starting (args: ; bundle: /Applications/Coreloom.app; macOS 15.7.9, Arm64, .NET 10.0.13, PID 3872; Apple silicon: True; virtual machine: True).
2026-10-25 12:56:21.839 INF [App] First launch: Mac defaults applied.
2026-10-25 12:56:22.602 INF [Telemetry] Telemetry started (7 providers, interval 1000 ms).
2026-10-25 12:56:22.611 INF [Sensors] Sensors: no hardware sensors (virtual machine).
2026-10-25 12:56:23.604 INF [Telemetry] Coreloom process: Running in 1 ms
2026-10-25 12:56:23.604 INF [Telemetry] Mac sensors: Running in 12 ms — Apple Virtual Machine 1 (VirtualMac2,1); no fans reported; 0 temperature groups; sensors: no hardware sensors (virtual machine)
2026-10-25 12:56:23.604 INF [Telemetry] macOS processor counters: Running in 9 ms — Apple M1 (Virtual): 3 threads; sensors: no hardware sensors (virtual machine)
2026-10-25 12:56:23.604 INF [Telemetry] macOS memory: Running in 7 ms — 7 GB physical memory
2026-10-25 12:56:23.604 INF [Telemetry] macOS storage: Running in 22 ms — 1 disk(s), 1 volume(s)
2026-10-25 12:56:23.605 INF [Telemetry] macOS network: Running in 20 ms — 2 interface(s), primary en0
2026-10-25 12:56:23.605 INF [Telemetry] macOS graphics drivers: Running in 4 ms — Apple Paravirtual GPU
2026-10-25 12:56:41.337 INF [Probe] Overview header layout:
SquircleBorder bounds=0, 0, 801, 206 desired=801, 265 measureValid=True arrangeValid=True
  Grid bounds=15, 15, 763, 176 desired=763, 219 measureValid=True arrangeValid=True rows=[]
    MacIllustration bounds=4, 0, 232, 176 desired=236, 176 measureValid=True arrangeValid=True
    Grid bounds=258, 56, 505, 63 desired=527, 219 measureValid=True arrangeValid=True rows=[27,20,16]
      TextBlock bounds=0, 0, 379, 27 desired=251, 27 measureValid=True arrangeValid=True "Apple Virtual Machine 1"
      TextBlock bounds=0, 30, 379, 17 desired=147, 20 measureValid=True arrangeValid=True "Apple Paravirtual GPU"
      Button bounds=395, 0, 110, 28 desired=126, 28 measureValid=True arrangeValid=True
        SquircleBorder bounds=0, 0, 110, 28 desired=110, 28 measureValid=True arrangeValid=True
          ContentPresenter bounds=11, 6, 88, 16 desired=88, 16 measureValid=True arrangeValid=True
            StackPanel bounds=0, 0, 88, 16 desired=88, 16 measureValid=True arrangeValid=True
              Path bounds=0, 1, 14, 14 desired=14, 14 measureValid=True arrangeValid=True
              TextBlock bounds=22, 0, 66, 16 desired=74, 16 measureValid=True arrangeValid=True "Customize"
      AdaptiveGrid bounds=0, 63, 505, 0 desired=505, 160 measureValid=True arrangeValid=True
        ItemsControl bounds=0, 0, 505, 96 desired=178, 96 measureValid=True arrangeValid=True
          Border bounds=0, 0, 505, 96 desired=178, 96 measureValid=True arrangeValid=True
            ItemsPresenter bounds=0, 0, 505, 96 desired=178, 96 measureValid=True arrangeValid=True
              StackPanel bounds=0, 0, 505, 96 desired=178, 96 measureValid=True arrangeValid=True
                ContentPresenter bounds=0, 0, 505, 24 desired=178, 24 measureValid=True arrangeValid=True
                  Grid bounds=0, 4, 505, 16 desired=178, 24 measureValid=True arrangeValid=True rows=[]
                ContentPresenter bounds=0, 24, 505, 24 desired=148, 24 measureValid=True arrangeValid=True
                  Grid bounds=0, 4, 505, 16 desired=148, 24 measureValid=True arrangeValid=True rows=[]
                ContentPresenter bounds=0, 48, 505, 24 desired=146, 24 measureValid=True arrangeValid=True
                  Grid bounds=0, 4, 505, 16 desired=146, 24 measureValid=True arrangeValid=True rows=[]
                ContentPresenter bounds=0, 72, 505, 24 desired=174, 24 measureValid=True arrangeValid=True
                  Grid bounds=0, 4, 505, 16 desired=174, 24 measureValid=True arrangeValid=True rows=[]
        ItemsControl bounds=0, 96, 505, 48 desired=210, 48 measureValid=True arrangeValid=True
          Border bounds=0, 0, 505, 48 desired=210, 48 measureValid=True arrangeValid=True
            ItemsPresenter bounds=0, 0, 505, 48 desired=210, 48 measureValid=True arrangeValid=True
              StackPanel bounds=0, 0, 505, 48 desired=210, 48 measureValid=True arrangeValid=True
                ContentPresenter bounds=0, 0, 505, 24 desired=210, 24 measureValid=True arrangeValid=True
                  Grid bounds=0, 4, 505, 16 desired=210, 24 measureValid=True arrangeValid=True rows=[]
                ContentPresenter bounds=0, 24, 505, 24 desired=196, 24 measureValid=True arrangeValid=True
                  Grid bounds=0, 4, 505, 16 desired=196, 24 measureValid=True arrangeValid=True rows=[]
2026-10-25 12:56:42.596 INF [Gaming] Found 0 installed games in 459 ms.
```
