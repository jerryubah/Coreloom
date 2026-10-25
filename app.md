# Coreloom 1.6.0 on macOS 15.7.9 (arm64): a run

- Running after 78 s and 12 pages:
```
  PID    RSS      VSZ  %CPU ELAPSED COMMAND
 4600 372592 413093456   7.7   01:18 /Applications/Coreloom.app/Contents/MacOS/Coreloom
```
- Other Coreloom processes left (the page launches must hand over and quit): 0
- Quit cleanly on SIGTERM

## Log (errors and warnings)
```
2026-10-25 13:09:02.552 WRN [Updates] Update check failed.
```

## Log (first 60 lines)
```
2026-10-25 13:08:41.485 INF [App] Coreloom 1.6.0+85514e65630bba4377562928fa3f536d960b7489 starting (args: ; bundle: /Applications/Coreloom.app; macOS 15.7.9, Arm64, .NET 10.0.13, PID 4600; Apple silicon: True; virtual machine: True).
2026-10-25 13:08:41.586 INF [App] First launch: Mac defaults applied.
2026-10-25 13:08:42.247 INF [Telemetry] Telemetry started (7 providers, interval 1000 ms).
2026-10-25 13:08:42.274 INF [Sensors] Sensors: no hardware sensors (virtual machine).
2026-10-25 13:08:43.240 INF [Telemetry] Coreloom process: Running in 1 ms
2026-10-25 13:08:43.240 INF [Telemetry] Mac sensors: Running in 38 ms — Apple Virtual Machine 1 (VirtualMac2,1); no fans reported; 0 temperature groups; sensors: no hardware sensors (virtual machine)
2026-10-25 13:08:43.240 INF [Telemetry] macOS processor counters: Running in 26 ms — Apple M1 (Virtual): 3 threads; sensors: no hardware sensors (virtual machine)
2026-10-25 13:08:43.240 INF [Telemetry] macOS memory: Running in 31 ms — 7 GB physical memory
2026-10-25 13:08:43.240 INF [Telemetry] macOS storage: Running in 15 ms — 1 disk(s), 1 volume(s)
2026-10-25 13:08:43.240 INF [Telemetry] macOS network: Running in 16 ms — 2 interface(s), primary en0
2026-10-25 13:08:43.240 INF [Telemetry] macOS graphics drivers: Running in 2 ms — Apple Paravirtual GPU
2026-10-25 13:09:00.617 INF [Probe] Overview header layout:
Ancestors:
  StackPanel bounds=32, 14, 801, 3057 desired=861, 3103 measureValid=True arrangeValid=True
  ScrollContentPresenter bounds=0, 0, 861, 551 desired=861, 545 measureValid=True arrangeValid=True
  Grid bounds=0, 0, 861, 551 desired=861, 545 measureValid=True arrangeValid=True
  ScrollViewer bounds=0, 0, 861, 551 desired=861, 545 measureValid=True arrangeValid=True
  Grid bounds=0, 0, 861, 551 desired=861, 545 measureValid=True arrangeValid=True
  ContentPresenter bounds=0, 0, 861, 551 desired=861, 545 measureValid=True arrangeValid=True
  OverviewPage bounds=0, 0, 861, 551 desired=861, 545 measureValid=True arrangeValid=True
  ContentPresenter bounds=0, 0, 861, 551 desired=861, 545 measureValid=True arrangeValid=True
  PageHost bounds=0, 82, 861, 551 desired=861, 553 measureValid=True arrangeValid=True
  Grid bounds=82, 0, 861, 633 desired=861, 627 measureValid=True arrangeValid=True
  Grid bounds=0, 0, 943, 627 desired=943, 627 measureValid=True arrangeValid=True
  ContentPresenter bounds=0, 0, 943, 627 desired=943, 627 measureValid=True arrangeValid=True
  VisualLayerManager bounds=0, 0, 943, 627 desired=943, 627 measureValid=True arrangeValid=True
  Panel bounds=0, 0, 943, 627 desired=943, 627 measureValid=True arrangeValid=True
  MainWindow bounds=0, 0, 943, 627 desired=943, 627 measureValid=True arrangeValid=True
SquircleBorder bounds=0, 0, 801, 249 desired=801, 265 measureValid=True arrangeValid=True
  Grid bounds=15, 15, 763, 219 desired=763, 219 measureValid=True arrangeValid=True rows=[]
    MacIllustration bounds=4, 22, 232, 176 desired=236, 176 measureValid=True arrangeValid=True
    DockPanel bounds=258, 6, 505, 207 desired=527, 219 measureValid=True arrangeValid=True
      Grid bounds=0, 0, 505, 47 desired=377, 47 measureValid=True arrangeValid=True rows=[27,20]
        TextBlock bounds=0, 0, 379, 27 desired=251, 27 measureValid=True arrangeValid=True "Apple Virtual Machine 1"
        TextBlock bounds=0, 30, 379, 17 desired=147, 20 measureValid=True arrangeValid=True "Apple Paravirtual GPU"
        Button bounds=395, 0, 110, 28 desired=126, 28 measureValid=True arrangeValid=True
          SquircleBorder bounds=0, 0, 110, 28 desired=110, 28 measureValid=True arrangeValid=True
            ContentPresenter bounds=11, 6, 88, 16 desired=88, 16 measureValid=True arrangeValid=True
              StackPanel bounds=0, 0, 88, 16 desired=88, 16 measureValid=True arrangeValid=True
                Path bounds=0, 1, 14, 14 desired=14, 14 measureValid=True arrangeValid=True
                TextBlock bounds=22, 0, 66, 16 desired=74, 16 measureValid=True arrangeValid=True "Customize"
      AdaptiveGrid bounds=0, 63, 505, 144 desired=505, 160 measureValid=True arrangeValid=True
        ItemsControl bounds=0, 0, 505, 96 desired=172, 96 measureValid=True arrangeValid=True
          Border bounds=0, 0, 505, 96 desired=172, 96 measureValid=True arrangeValid=True
            ItemsPresenter bounds=0, 0, 505, 96 desired=172, 96 measureValid=True arrangeValid=True
              StackPanel bounds=0, 0, 505, 96 desired=172, 96 measureValid=True arrangeValid=True
                ContentPresenter bounds=0, 0, 505, 24 desired=157, 24 measureValid=True arrangeValid=True
                  DockPanel bounds=0, 4, 505, 16 desired=157, 24 measureValid=True arrangeValid=True
                ContentPresenter bounds=0, 24, 505, 24 desired=135, 24 measureValid=True arrangeValid=True
                  DockPanel bounds=0, 4, 505, 16 desired=135, 24 measureValid=True arrangeValid=True
                ContentPresenter bounds=0, 48, 505, 24 desired=146, 24 measureValid=True arrangeValid=True
                  DockPanel bounds=0, 4, 505, 16 desired=146, 24 measureValid=True arrangeValid=True
                ContentPresenter bounds=0, 72, 505, 24 desired=172, 24 measureValid=True arrangeValid=True
                  DockPanel bounds=0, 4, 505, 16 desired=172, 24 measureValid=True arrangeValid=True
        ItemsControl bounds=0, 96, 505, 48 desired=198, 48 measureValid=True arrangeValid=True
          Border bounds=0, 0, 505, 48 desired=198, 48 measureValid=True arrangeValid=True
            ItemsPresenter bounds=0, 0, 505, 48 desired=198, 48 measureValid=True arrangeValid=True
              StackPanel bounds=0, 0, 505, 48 desired=198, 48 measureValid=True arrangeValid=True
                ContentPresenter bounds=0, 0, 505, 24 desired=198, 24 measureValid=True arrangeValid=True
                  DockPanel bounds=0, 4, 505, 16 desired=198, 24 measureValid=True arrangeValid=True
```
