# Coreloom macOS readings — X64, macOS Sequoia 15.7.9

Model: Mac mini (Macmini6,2); firmware 1.00; virtual machine: True
Sensors: SMC (1 fan, 13 temperature keys, system power, CPU power)

## Providers
- Coreloom process: Running —  (0.2 ms)
- Mac sensors: Running — Mac mini (Macmini6,2); 1 fan(s); 13 temperature groups; sensors: SMC (1 fan, 13 temperature keys, system power, CPU power) (34.8 ms)
- macOS processor counters: Running — Intel Core i7-8700B: 4 threads; sensors: SMC (1 fan, 13 temperature keys, system power, CPU power) (0.1 ms)
- macOS memory: Running — 14 GB physical memory (0.0 ms)
- macOS storage: Running — 1 disk(s), 1 volume(s) (0.1 ms)
- macOS network: Running — 1 interface(s), primary en0 (0.2 ms)
- macOS graphics drivers: Running — Apple Paravirtualized Graphics Device (0.3 ms)

## Devices
- **system** iad01-dz251-dbb7da28-edf9-4d0d-8da9-bb5a88bfbe61-82E9858DE3E7 (Mac, Apple): Computer name = iad01-dz251-dbb7da28-edf9-4d0d-8da9-bb5a88bfbe61-82E9858DE3E7; Operating system = macOS Sequoia 15.7.9; Version = 15.7.9 (24G830); Model = Mac mini (Macmini6,2); Chip = Intel Core i7-8700B; Memory = 14 GB; Firmware = 1.00
- **board** Mac mini (Mac, Apple): Model = Mac mini; Model identifier = Macmini6,2; Firmware = 1.00; Fan range = 1700 – 5000 RPM
- **cpu** Intel Core i7-8700B (Processor, Intel): Cores = 4; Threads = 4; Base clock = 3.19 GHz; Architecture = x86-64; L2 cache = 256 KB; L3 cache = 12 MB; Instruction sets = FMA, SSE4.2, AES, AVX, AVX2
- **memory** Memory (System memory, ): Installed = 14 GB; Architecture = Separate system memory; Page size = 4 KB; Swap = Dynamic, encrypted
- **disk/0** VEERTU ANKA (SATA HDD, ): Capacity = 325 GB; Interface = SATA; Type = Rotational; Location = Internal; Disk number = 0; BSD name = disk0
- **vol/DISK1S5** Macintosh HD (Volume, ): Mount point = /; File system = APFS; Device = disk1s5s1; Disk number = 0
- **net/en0** Ethernet (Ethernet, ): Interface = en0; Type = Ethernet; Status = Connected; Route = Primary (default route); IPv4 address = 192.168.64.2; Hardware address = 82:e9:85:8d:e3:e7
- **gpu/0** Apple Paravirtualized Graphics Device (Virtual GPU, Apple): Memory = Dedicated; Graphics API = Metal; Driver = AppleParavirtAccelerator

## Readings
- `app/cpu` Coreloom CPU usage [Load]: 1.655
- `app/managed` Coreloom managed heap [Data]: 8219704
- `app/memory` Coreloom working set [Data]: 56692736
- `app/private` Coreloom private memory [Data]: 33296384
- `board/fan/fan` Fan [Fan]: 0
- `board/temp/cpu-core-1` CPU core 1 [Temperature]: 81
- `board/temp/cpu-core-2` CPU core 2 [Temperature]: 85
- `board/temp/cpu-core-3` CPU core 3 [Temperature]: 85
- `board/temp/cpu-core-4` CPU core 4 [Temperature]: 92
- `board/temp/cpu-core-5` CPU core 5 [Temperature]: 85
- `board/temp/cpu-core-6` CPU core 6 [Temperature]: 92
- `board/temp/cpu-die-peci` CPU die (PECI) [Temperature]: 97.445
- `board/temp/cpu-peci` CPU PECI [Temperature]: 93.609
- `board/temp/cpu-proximity` CPU proximity [Temperature]: 45
- `board/temp/memory-proximity` Memory proximity [Temperature]: 31.5
- `board/temp/platform-controller-hub` Platform controller hub [Temperature]: 43
- `board/temp/ssd` SSD [Temperature]: 36.188
- `board/temp/wi-fi` Wi-Fi [Temperature]: 24.125
- `cpu/load` CPU utilization [Load]: 75.054
- `cpu/load/max-thread` Busiest thread [Load]: 85.345
- `cpu/power` CPU power [Power]: 71.305
- `cpu/temp` CPU temperature [Temperature]: 97.445
- `cpu/thread/0/load` Thread 1 [Load]: 85.345
- `cpu/thread/1/load` Thread 2 [Load]: 73.504
- `cpu/thread/2/load` Thread 3 [Load]: 68.103
- `cpu/thread/3/load` Thread 4 [Load]: 73.276
- `disk/0/active` Active time [Load]: 46.619
- `disk/0/read` Read speed [Throughput]: 4204598.369
- `disk/0/write` Write speed [Throughput]: 9000851.297
- `disk/total/read` Total disk read [Throughput]: 4204598.369
- `disk/total/write` Total disk write [Throughput]: 9000851.297
- `gpu/0/vram/used` VRAM used [Data]: 33991104
- `mem/app` App memory [Data]: 2605359104
- `mem/available` Available [Data]: 10911207424
- `mem/cache` Cached files [Data]: 8311693312
- `mem/compressed` Compressed [Data]: 0
- `mem/load` Memory utilization [Load]: 27.415
- `mem/pressure` Memory pressure [State]: 1
- `mem/swap/total` Swap size [Data]: 0
- `mem/swap/used` Swap used [Data]: 0
- `mem/total` Physical memory [Data]: 15032385536
- `mem/used` Memory used [Data]: 4121178112
- `mem/wired` Wired memory [Data]: 1515819008
- `net/active/down` Download [Throughput]: 0
- `net/active/rx-session` Received this session [Data]: 27648
- `net/active/tx-session` Sent this session [Data]: 5120
- `net/active/up` Upload [Throughput]: 875.962
- `net/en0/down` Ethernet download [Throughput]: 0
- `net/en0/rx-session` Ethernet received [Data]: 27648
- `net/en0/tx-session` Ethernet sent [Data]: 5120
- `net/en0/up` Ethernet upload [Throughput]: 875.962
- `sys/power` System power [Power]: 87.809
- `sys/uptime` System uptime [Duration]: 152.075
- `vol/DISK1S5/free` Macintosh HD: available [Data]: 116137940034
- `vol/DISK1S5/load` Macintosh HD: used % [Load]: 66.687
- `vol/DISK1S5/total` Macintosh HD: capacity [Data]: 348622118912
- `vol/DISK1S5/used` Macintosh HD: used [Data]: 232484178878
