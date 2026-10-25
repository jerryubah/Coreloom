# Coreloom macOS readings — X64, macOS Sequoia 15.7.9

Model: Mac mini (Macmini6,2); firmware 1.00; virtual machine: True
Sensors: SMC (1 fan, 13 temperature keys, system power, CPU power)

## Providers
- Coreloom process: Running —  (0.2 ms)
- Mac sensors: Running — Mac mini (Macmini6,2); 1 fan(s); 13 temperature groups; sensors: SMC (1 fan, 13 temperature keys, system power, CPU power) (34.2 ms)
- macOS processor counters: Running — Intel Core i7-8700B: 4 threads; sensors: SMC (1 fan, 13 temperature keys, system power, CPU power) (0.1 ms)
- macOS memory: Running — 14 GB physical memory (0.0 ms)
- macOS storage: Running — 1 disk(s), 1 volume(s) (0.1 ms)
- macOS network: Running — 1 interface(s), primary en0 (0.1 ms)
- macOS graphics drivers: Running — Apple Paravirtualized Graphics Device (0.3 ms)

## Devices
- **system** sjc20-av657-0655539b-3054-4262-a3e1-0626025d0754-92FC18B3F71E (Mac, Apple): Computer name = sjc20-av657-0655539b-3054-4262-a3e1-0626025d0754-92FC18B3F71E; Operating system = macOS Sequoia 15.7.9; Version = 15.7.9 (24G830); Model = Mac mini (Macmini6,2); Chip = Intel Core i7-8700B; Memory = 14 GB; Firmware = 1.00
- **board** Mac mini (Mac, Apple): Model = Mac mini; Model identifier = Macmini6,2; Firmware = 1.00; Fan range = 1700 – 5000 RPM
- **cpu** Intel Core i7-8700B (Processor, Intel): Cores = 4; Threads = 4; Base clock = 3.19 GHz; Architecture = x86-64; L2 cache = 256 KB; L3 cache = 12 MB; Instruction sets = FMA, SSE4.2, AES, AVX, AVX2
- **memory** Memory (System memory, ): Installed = 14 GB; Architecture = Separate system memory; Page size = 4 KB; Swap = Dynamic, encrypted
- **disk/0** VEERTU ANKA (SATA HDD, ): Capacity = 325 GB; Interface = SATA; Type = Rotational; Location = Internal; Disk number = 0; BSD name = disk0
- **vol/DISK1S5** Macintosh HD (Volume, ): Mount point = /; File system = APFS; Device = disk1s5s1; Disk number = 0
- **net/en0** Ethernet (Ethernet, ): Interface = en0; Type = Ethernet; Status = Connected; Route = Primary (default route); IPv4 address = 192.168.64.6; Hardware address = 92:fc:18:b3:f7:1e
- **gpu/0** Apple Paravirtualized Graphics Device (Virtual GPU, Apple): Memory = Dedicated; Graphics API = Metal; Driver = AppleParavirtAccelerator

## Readings
- `app/cpu` Coreloom CPU usage [Load]: 1.575
- `app/managed` Coreloom managed heap [Data]: 8165288
- `app/memory` Coreloom working set [Data]: 56647680
- `app/private` Coreloom private memory [Data]: 33239040
- `board/fan/fan` Fan [Fan]: 0
- `board/temp/cpu-core-1` CPU core 1 [Temperature]: 82
- `board/temp/cpu-core-2` CPU core 2 [Temperature]: 85
- `board/temp/cpu-core-3` CPU core 3 [Temperature]: 85
- `board/temp/cpu-core-4` CPU core 4 [Temperature]: 79
- `board/temp/cpu-core-5` CPU core 5 [Temperature]: 88
- `board/temp/cpu-core-6` CPU core 6 [Temperature]: 76
- `board/temp/cpu-die-peci` CPU die (PECI) [Temperature]: 97.223
- `board/temp/cpu-peci` CPU PECI [Temperature]: 90.031
- `board/temp/cpu-proximity` CPU proximity [Temperature]: 43.75
- `board/temp/memory-proximity` Memory proximity [Temperature]: 33.625
- `board/temp/platform-controller-hub` Platform controller hub [Temperature]: 44
- `board/temp/ssd` SSD [Temperature]: 35.813
- `board/temp/wi-fi` Wi-Fi [Temperature]: 25.5
- `cpu/load` CPU utilization [Load]: 96.312
- `cpu/load/max-thread` Busiest thread [Load]: 96.552
- `cpu/power` CPU power [Power]: 55.93
- `cpu/temp` CPU temperature [Temperature]: 97.223
- `cpu/thread/0/load` Thread 1 [Load]: 95.69
- `cpu/thread/1/load` Thread 2 [Load]: 96.552
- `cpu/thread/2/load` Thread 3 [Load]: 96.491
- `cpu/thread/3/load` Thread 4 [Load]: 96.522
- `disk/0/active` Active time [Load]: 5.664
- `disk/0/read` Read speed [Throughput]: 1485877.14
- `disk/0/write` Write speed [Throughput]: 10636880.106
- `disk/total/read` Total disk read [Throughput]: 1485877.14
- `disk/total/write` Total disk write [Throughput]: 10636880.106
- `gpu/0/vram/used` VRAM used [Data]: 25634240
- `mem/app` App memory [Data]: 2697912320
- `mem/available` Available [Data]: 10786713600
- `mem/cache` Cached files [Data]: 8413782016
- `mem/compressed` Compressed [Data]: 0
- `mem/load` Memory utilization [Load]: 28.244
- `mem/pressure` Memory pressure [State]: 1
- `mem/swap/total` Swap size [Data]: 0
- `mem/swap/used` Swap used [Data]: 0
- `mem/total` Physical memory [Data]: 15032385536
- `mem/used` Memory used [Data]: 4245671936
- `mem/wired` Wired memory [Data]: 1547759616
- `net/active/down` Download [Throughput]: 0
- `net/active/rx-session` Received this session [Data]: 0
- `net/active/tx-session` Sent this session [Data]: 0
- `net/active/up` Upload [Throughput]: 0
- `net/en0/down` Ethernet download [Throughput]: 0
- `net/en0/rx-session` Ethernet received [Data]: 0
- `net/en0/tx-session` Ethernet sent [Data]: 0
- `net/en0/up` Ethernet upload [Throughput]: 0
- `sys/power` System power [Power]: 69.336
- `sys/uptime` System uptime [Duration]: 162.367
- `vol/DISK1S5/free` Macintosh HD: available [Data]: 116073633410
- `vol/DISK1S5/load` Macintosh HD: used % [Load]: 66.705
- `vol/DISK1S5/total` Macintosh HD: capacity [Data]: 348622118912
- `vol/DISK1S5/used` Macintosh HD: used [Data]: 232548485502
