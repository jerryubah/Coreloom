# Coreloom macOS readings — Arm64, macOS Sequoia 15.8.2

Model: Apple Virtual Machine 1 (VirtualMac2,1); firmware mBoot-20457.41.3; virtual machine: True
Sensors: no hardware sensors (virtual machine)

## Providers
- Coreloom process: Running —  (2.6 ms)
- Mac sensors: Running — Apple Virtual Machine 1 (VirtualMac2,1); no fans reported; 0 temperature groups; sensors: no hardware sensors (virtual machine) (0.2 ms)
- macOS processor counters: Running — Apple M1 (Virtual): 3 threads; sensors: no hardware sensors (virtual machine) (0.1 ms)
- macOS memory: Running — 7 GB physical memory (0.1 ms)
- macOS storage: Running — 1 disk(s), 1 volume(s) (0.4 ms)
- macOS network: Running — 2 interface(s), primary en0 (0.5 ms)
- macOS graphics drivers: Running — Apple Paravirtual GPU (0.4 ms)

## Devices
- **system** sjc22-bm205-08fd98fc-535f-43c7-aedb-e647ed35c24c-C6579119C4DD (Mac, Apple): Computer name = sjc22-bm205-08fd98fc-535f-43c7-aedb-e647ed35c24c-C6579119C4DD; Operating system = macOS Sequoia 15.8.2; Version = 15.8.2 (24H232); Model = Apple Virtual Machine 1 (VirtualMac2,1); Chip = Apple M1 (Virtual); Memory = 7 GB; Firmware = mBoot-20457.41.3
- **board** Apple Virtual Machine 1 (Mac, Apple): Model = Apple Virtual Machine 1; Model identifier = VirtualMac2,1; Firmware = mBoot-20457.41.3
- **cpu** Apple M1 (Virtual) (Processor, Apple): Cores = 3; Threads = 3; Architecture = Apple silicon (arm64); L2 cache (P-cores) = 12 MB
- **memory** Unified memory (Unified memory, Apple): Installed = 7 GB; Architecture = Unified memory (shared by the processor, GPU and Neural Engine); Page size = 16 KB; Swap = Dynamic, encrypted
- **disk/0** Internal disk (SSD, ): Capacity = 325 GB; Interface = Unknown; Type = Solid state; Location = Internal; Disk number = 0; BSD name = disk0
- **vol/DISK2S1** Macintosh HD (Volume, ): Mount point = /; File system = APFS; Device = disk2s1s1; Disk number = 0
- **net/en1** Ethernet Adapter (en1) (Ethernet, ): Interface = en1; Type = Ethernet; Status = Not connected; Hardware address = 8e:54:67:ce:75:4a
- **net/en0** Ethernet (Ethernet, ): Interface = en0; Type = Ethernet; Status = Connected; Route = Primary (default route); IPv4 address = 192.168.64.7; Hardware address = c6:57:91:19:c4:dd
- **gpu/0** Apple Paravirtual GPU (Virtual GPU, Apple): Memory = Unified (shared with the processor); Graphics API = Metal; Driver = AppleParavirtGPU

## Readings
- `app/cpu` Coreloom CPU usage [Load]: 0.317
- `app/managed` Coreloom managed heap [Data]: 15728080
- `app/memory` Coreloom working set [Data]: 111312896
- `app/private` Coreloom private memory [Data]: 81397568
- `cpu/clock/avg` Average clock [Clock]: — Not available in a virtual machine: macOS gives virtual Macs no hardware sensors.
- `cpu/clock/e-cores` Efficiency cores clock [Clock]: — Not available in a virtual machine: macOS gives virtual Macs no hardware sensors.
- `cpu/clock/p-cores` Performance cores clock [Clock]: — Not available in a virtual machine: macOS gives virtual Macs no hardware sensors.
- `cpu/load` CPU utilization [Load]: 100
- `cpu/load/max-thread` Busiest thread [Load]: 100
- `cpu/power` CPU power [Power]: — Not available in a virtual machine: macOS gives virtual Macs no hardware sensors.
- `cpu/power/ane` Neural Engine power [Power]: — Not available in a virtual machine: macOS gives virtual Macs no hardware sensors.
- `cpu/temp` CPU temperature [Temperature]: — Not available in a virtual machine: macOS gives virtual Macs no hardware sensors.
- `cpu/thread/0/load` Thread 1 [Load]: 100
- `cpu/thread/1/load` Thread 2 [Load]: 100
- `cpu/thread/2/load` Thread 3 [Load]: 100
- `disk/0/active` Active time [Load]: 19.713
- `disk/0/read` Read speed [Throughput]: 9578295.38
- `disk/0/write` Write speed [Throughput]: 0
- `disk/total/read` Total disk read [Throughput]: 9578295.38
- `disk/total/write` Total disk write [Throughput]: 0
- `gpu/0/clock/core` GPU clock [Clock]: — Not available in a virtual machine: macOS gives virtual Macs no hardware sensors.
- `gpu/0/power` GPU power [Power]: — Not available in a virtual machine: macOS gives virtual Macs no hardware sensors.
- `gpu/0/temp` GPU temperature [Temperature]: — Not available in a virtual machine: macOS gives virtual Macs no hardware sensors.
- `gpu/0/vram/used` Memory in use (unified) [Data]: 35720320
- `mem/app` App memory [Data]: 3032236032
- `mem/available` Available [Data]: 3308535808
- `mem/cache` Cached files [Data]: 3223306240
- `mem/compressed` Compressed [Data]: 328974336
- `mem/load` Memory utilization [Load]: 55.981
- `mem/power` Memory power [Power]: — Not available in a virtual machine: macOS gives virtual Macs no hardware sensors.
- `mem/pressure` Memory pressure [State]: 1
- `mem/swap/total` Swap size [Data]: 0
- `mem/swap/used` Swap used [Data]: 0
- `mem/total` Physical memory [Data]: 7516192768
- `mem/used` Memory used [Data]: 4207656960
- `mem/wired` Wired memory [Data]: 846446592
- `net/active/down` Download [Throughput]: 36416.596
- `net/active/rx-session` Received this session [Data]: 47104
- `net/active/tx-session` Sent this session [Data]: 3072
- `net/active/up` Upload [Throughput]: 1734.124
- `net/en0/down` Ethernet download [Throughput]: 36416.596
- `net/en0/rx-session` Ethernet received [Data]: 47104
- `net/en0/tx-session` Ethernet sent [Data]: 3072
- `net/en0/up` Ethernet upload [Throughput]: 1734.124
- `net/en1/down` Ethernet Adapter (en1) download [Throughput]: 0
- `net/en1/rx-session` Ethernet Adapter (en1) received [Data]: 0
- `net/en1/tx-session` Ethernet Adapter (en1) sent [Data]: 0
- `net/en1/up` Ethernet Adapter (en1) upload [Throughput]: 0
- `sys/uptime` System uptime [Duration]: 95.171
- `vol/DISK2S1/free` Macintosh HD: available [Data]: 48192104768
- `vol/DISK2S1/load` Macintosh HD: used % [Load]: 85.953
- `vol/DISK2S1/total` Macintosh HD: capacity [Data]: 343073095680
- `vol/DISK2S1/used` Macintosh HD: used [Data]: 294880990912
