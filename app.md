# Coreloom 1.6.0 on macOS 15.7.9 (x64): a run

- Running after 79 s and 12 pages:
```
  PID    RSS      VSZ  %CPU ELAPSED COMMAND
 7911 245700 37151512   3.6   01:19 /Applications/Coreloom.app/Contents/MacOS/Coreloom
```
- Other Coreloom processes left (the page launches must hand over and quit): 0
- Quit cleanly on SIGTERM

## Log (errors and warnings)
```
```

## Log (first 60 lines)
```
2026-10-25 12:57:10.742 INF [App] Coreloom 1.6.0+e3104db1de721dcd317bfad4b48ae45a25d70cf1 starting (args: ; bundle: /Applications/Coreloom.app; macOS 15.7.9, X64, .NET 10.0.13, PID 7911; Apple silicon: False; virtual machine: True).
2026-10-25 12:57:10.949 INF [App] First launch: Mac defaults applied.
2026-10-25 12:57:13.010 INF [Telemetry] Telemetry started (7 providers, interval 1000 ms).
2026-10-25 12:57:13.087 INF [Sensors] Sensors: SMC (1 fan, 13 temperature keys, system power, CPU power).
2026-10-25 12:57:14.018 INF [Telemetry] Coreloom process: Running in 3 ms
2026-10-25 12:57:14.018 INF [Telemetry] Mac sensors: Running in 130 ms — Mac mini (Macmini6,2); 1 fan(s); 13 temperature groups; sensors: SMC (1 fan, 13 temperature keys, system power, CPU power)
2026-10-25 12:57:14.018 INF [Telemetry] macOS processor counters: Running in 119 ms — Intel Core i7-8700B: 4 threads; sensors: SMC (1 fan, 13 temperature keys, system power, CPU power)
2026-10-25 12:57:14.018 INF [Telemetry] macOS memory: Running in 71 ms — 14 GB physical memory
2026-10-25 12:57:14.018 INF [Telemetry] macOS storage: Running in 144 ms — 1 disk(s), 1 volume(s)
2026-10-25 12:57:14.018 INF [Telemetry] macOS network: Running in 35 ms — 1 interface(s), primary en0
2026-10-25 12:57:14.018 INF [Telemetry] macOS graphics drivers: Running in 14 ms — Apple Paravirtualized Graphics Device
2026-10-25 12:57:25.056 INF [Alerts] CPU temperature: CPU temperature is 97 °C — above your 97 °C threshold for 10 s.
2026-10-25 12:57:31.487 INF [Gaming] Found 0 installed games in 179 ms.
2026-10-25 12:57:33.164 INF [Probe] Overview header layout:
SquircleBorder bounds=0, 0, 1048, 206 desired=1048, 222 measureValid=False arrangeValid=False
  Grid bounds=15, 15, 1010, 176 desired=1010, 176 measureValid=False arrangeValid=False rows=[]
    MacIllustration bounds=4, 0, 232, 176 desired=236, 176 measureValid=False arrangeValid=False
    Grid bounds=258, 8, 752, 159 desired=774, 171 measureValid=False arrangeValid=False rows=[27,20,112]
      TextBlock bounds=0, 0, 626, 27 desired=97, 27 measureValid=False arrangeValid=False "Mac mini"
      TextBlock bounds=0, 30, 626, 17 desired=254, 20 measureValid=False arrangeValid=False "Apple Paravirtualized Graphics Device"
      Button bounds=642, 0, 110, 28 desired=126, 28 measureValid=False arrangeValid=False
        SquircleBorder bounds=0, 0, 110, 28 desired=110, 28 measureValid=False arrangeValid=False
          ContentPresenter bounds=11, 6, 88, 16 desired=88, 16 measureValid=False arrangeValid=False
            StackPanel bounds=0, 0, 88, 16 desired=88, 16 measureValid=False arrangeValid=False
              Path bounds=0, 1, 14, 14 desired=14, 14 measureValid=False arrangeValid=False
              TextBlock bounds=22, 0, 66, 16 desired=74, 16 measureValid=False arrangeValid=False "Customize"
      AdaptiveGrid bounds=0, 63, 752, 96 desired=752, 112 measureValid=False arrangeValid=False
        ItemsControl bounds=0, 0, 356, 96 desired=199, 96 measureValid=False arrangeValid=False
          Border bounds=0, 0, 356, 96 desired=199, 96 measureValid=False arrangeValid=False
            ItemsPresenter bounds=0, 0, 356, 96 desired=199, 96 measureValid=False arrangeValid=False
              StackPanel bounds=0, 0, 356, 96 desired=199, 96 measureValid=False arrangeValid=False
                ContentPresenter bounds=0, 0, 356, 24 desired=199, 24 measureValid=False arrangeValid=False
                  Grid bounds=0, 4, 356, 16 desired=199, 24 measureValid=False arrangeValid=False rows=[]
                ContentPresenter bounds=0, 24, 356, 24 desired=159, 24 measureValid=False arrangeValid=False
                  Grid bounds=0, 4, 356, 16 desired=159, 24 measureValid=False arrangeValid=False rows=[]
                ContentPresenter bounds=0, 48, 356, 24 desired=119, 24 measureValid=False arrangeValid=False
                  Grid bounds=0, 4, 356, 16 desired=119, 24 measureValid=False arrangeValid=False rows=[]
                ContentPresenter bounds=0, 72, 356, 24 desired=189, 24 measureValid=False arrangeValid=False
                  Grid bounds=0, 4, 356, 16 desired=189, 24 measureValid=False arrangeValid=False rows=[]
        ItemsControl bounds=396, 0, 356, 96 desired=259, 72 measureValid=False arrangeValid=False
          Border bounds=0, 0, 356, 96 desired=259, 72 measureValid=False arrangeValid=False
            ItemsPresenter bounds=0, 0, 356, 96 desired=259, 72 measureValid=False arrangeValid=False
              StackPanel bounds=0, 0, 356, 96 desired=259, 72 measureValid=False arrangeValid=False
                ContentPresenter bounds=0, 0, 356, 24 desired=190, 24 measureValid=False arrangeValid=False
                  Grid bounds=0, 4, 356, 16 desired=190, 24 measureValid=False arrangeValid=False rows=[]
                ContentPresenter bounds=0, 24, 356, 24 desired=259, 24 measureValid=False arrangeValid=False
                  Grid bounds=0, 4, 356, 16 desired=259, 24 measureValid=False arrangeValid=False rows=[]
                ContentPresenter bounds=0, 48, 356, 24 desired=154, 24 measureValid=False arrangeValid=False
                  Grid bounds=0, 4, 356, 16 desired=154, 24 measureValid=False arrangeValid=False rows=[]
```
