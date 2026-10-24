// Developer probe: what a macOS machine exposes to an unprivileged app (sensors, IOReport, SMC, GPU, Metal).
#import <Foundation/Foundation.h>
#import <IOKit/IOKitLib.h>
#import <Metal/Metal.h>
#include <sys/sysctl.h>

typedef struct __IOHIDEventSystemClient *IOHIDEventSystemClientRef;
typedef struct __IOHIDServiceClient *IOHIDServiceClientRef;
typedef struct __IOHIDEvent *IOHIDEventRef;
IOHIDEventSystemClientRef IOHIDEventSystemClientCreate(CFAllocatorRef);
int IOHIDEventSystemClientSetMatching(IOHIDEventSystemClientRef, CFDictionaryRef);
CFArrayRef IOHIDEventSystemClientCopyServices(IOHIDEventSystemClientRef);
CFTypeRef IOHIDServiceClientCopyProperty(IOHIDServiceClientRef, CFStringRef);
IOHIDEventRef IOHIDServiceClientCopyEvent(IOHIDServiceClientRef, int64_t, int32_t, int64_t);
double IOHIDEventGetFloatValue(IOHIDEventRef, int32_t);

typedef struct IOReportSubscription *IOReportSubscriptionRef;
CFDictionaryRef IOReportCopyAllChannels(uint64_t, uint64_t);
CFDictionaryRef IOReportCopyChannelsInGroup(CFStringRef, CFStringRef, uint64_t, uint64_t, uint64_t);
IOReportSubscriptionRef IOReportCreateSubscription(void *, CFMutableDictionaryRef, CFMutableDictionaryRef *, uint64_t, CFTypeRef);
CFDictionaryRef IOReportCreateSamples(IOReportSubscriptionRef, CFMutableDictionaryRef, CFTypeRef);
CFDictionaryRef IOReportCreateSamplesDelta(CFDictionaryRef, CFDictionaryRef, CFTypeRef);
CFStringRef IOReportChannelGetGroup(CFDictionaryRef);
CFStringRef IOReportChannelGetSubGroup(CFDictionaryRef);
CFStringRef IOReportChannelGetChannelName(CFDictionaryRef);
CFStringRef IOReportChannelGetUnitLabel(CFDictionaryRef);
int32_t IOReportChannelGetFormat(CFDictionaryRef);
int64_t IOReportSimpleGetIntegerValue(CFDictionaryRef, int32_t);
int32_t IOReportStateGetCount(CFDictionaryRef);
CFStringRef IOReportStateGetNameForIndex(CFDictionaryRef, int32_t);
int64_t IOReportStateGetResidency(CFDictionaryRef, int32_t);

typedef struct { char major, minor, build, reserved[1]; UInt16 release; } SMCVers;
typedef struct { UInt16 version, length; UInt32 cpuPLimit, gpuPLimit, memPLimit; } SMCPLimit;
typedef struct { UInt32 dataSize; UInt32 dataType; char dataAttributes; } SMCKeyInfo;
typedef struct { UInt32 key; SMCVers vers; SMCPLimit pLimitData; SMCKeyInfo keyInfo; char result; char status; char data8; UInt32 data32; unsigned char bytes[32]; } SMCKeyData;

static UInt32 Key(const char *s) { return ((UInt32)s[0] << 24) | ((UInt32)s[1] << 16) | ((UInt32)s[2] << 8) | (UInt32)s[3]; }

static void smc(void) {
    printf("\n== SMC (struct size %zu)\n", sizeof(SMCKeyData));
    io_service_t svc = IOServiceGetMatchingService(kIOMainPortDefault, IOServiceMatching("AppleSMC"));
    if (!svc) { printf("no AppleSMC service\n"); return; }
    io_connect_t conn = 0;
    kern_return_t kr = IOServiceOpen(svc, mach_task_self(), 0, &conn);
    printf("IOServiceOpen: 0x%x\n", kr);
    if (kr) return;
    const char *keys[] = {"#KEY", "FNum", "F0Ac", "F0Mx", "TC0P", "TC0D", "Tp09", "Tp01", "TG0P", "PSTR", "PCPC", "PPBR", "B0TE"};
    for (int i = 0; i < 13; i++) {
        SMCKeyData in = {0}, out = {0};
        size_t outSize = sizeof(out);
        in.key = Key(keys[i]); in.data8 = 9;
        kr = IOConnectCallStructMethod(conn, 2, &in, sizeof(in), &out, &outSize);
        if (kr || out.result) { printf("%s: info kr=0x%x result=%d\n", keys[i], kr, out.result); continue; }
        UInt32 size = out.keyInfo.dataSize, type = out.keyInfo.dataType;
        SMCKeyData in2 = {0}, out2 = {0}; outSize = sizeof(out2);
        in2.key = in.key; in2.keyInfo.dataSize = size; in2.data8 = 5;
        kr = IOConnectCallStructMethod(conn, 2, &in2, sizeof(in2), &out2, &outSize);
        printf("%s: type %c%c%c%c size %u kr=0x%x bytes", keys[i], (type>>24)&0xff, (type>>16)&0xff, (type>>8)&0xff, type&0xff, size, kr);
        for (UInt32 b = 0; b < size && b < 8; b++) printf(" %02x", out2.bytes[b]);
        if (type == Key("flt ")) { float f; memcpy(&f, out2.bytes, 4); printf(" = %.2f", f); }
        printf("\n");
    }
    IOServiceClose(conn);
}

static void hid(void) {
    printf("\n== HID temperature sensors\n");
    IOHIDEventSystemClientRef client = IOHIDEventSystemClientCreate(kCFAllocatorDefault);
    if (!client) { printf("no client\n"); return; }
    NSDictionary *match = @{@"PrimaryUsagePage": @(0xff00), @"PrimaryUsage": @(5)};
    IOHIDEventSystemClientSetMatching(client, (__bridge CFDictionaryRef)match);
    CFArrayRef services = IOHIDEventSystemClientCopyServices(client);
    long n = services ? CFArrayGetCount(services) : 0;
    printf("%ld sensors\n", n);
    for (long i = 0; i < n; i++) {
        IOHIDServiceClientRef s = (IOHIDServiceClientRef)CFArrayGetValueAtIndex(services, i);
        NSString *name = (__bridge_transfer NSString *)IOHIDServiceClientCopyProperty(s, CFSTR("Product"));
        IOHIDEventRef e = IOHIDServiceClientCopyEvent(s, 15, 0, 0);
        double v = e ? IOHIDEventGetFloatValue(e, 15 << 16) : -1;
        if (e) CFRelease(e);
        printf("  %s = %.1f\n", name.UTF8String, v);
    }
    // Voltage / current / power sensors (usage page 0xff08, usages 2/3)
    for (int usage = 1; usage <= 4; usage++) {
        NSDictionary *m2 = @{@"PrimaryUsagePage": @(0xff08), @"PrimaryUsage": @(usage)};
        IOHIDEventSystemClientSetMatching(client, (__bridge CFDictionaryRef)m2);
        CFArrayRef s2 = IOHIDEventSystemClientCopyServices(client);
        long n2 = s2 ? CFArrayGetCount(s2) : 0;
        printf("page ff08 usage %d: %ld services\n", usage, n2);
        for (long i = 0; i < n2 && i < 12; i++) {
            IOHIDServiceClientRef s = (IOHIDServiceClientRef)CFArrayGetValueAtIndex(s2, i);
            NSString *name = (__bridge_transfer NSString *)IOHIDServiceClientCopyProperty(s, CFSTR("Product"));
            IOHIDEventRef e = IOHIDServiceClientCopyEvent(s, 25, 0, 0);
            double v = e ? IOHIDEventGetFloatValue(e, 25 << 16) : -1;
            if (e) CFRelease(e);
            printf("  %s = %.3f\n", name.UTF8String, v);
        }
    }
}

static void ioreport(void) {
    printf("\n== IOReport\n");
    CFDictionaryRef all = IOReportCopyAllChannels(0, 0);
    NSArray *channels = all ? ((__bridge NSDictionary *)all)[@"IOReportChannels"] : nil;
    printf("%lu channels in all groups\n", (unsigned long)channels.count);
    NSMutableDictionary *groups = [NSMutableDictionary new];
    for (NSDictionary *ch in channels) {
        NSString *g = (__bridge NSString *)IOReportChannelGetGroup((__bridge CFDictionaryRef)ch) ?: @"?";
        NSString *sg = (__bridge NSString *)IOReportChannelGetSubGroup((__bridge CFDictionaryRef)ch) ?: @"";
        NSString *k = [NSString stringWithFormat:@"%@ / %@", g, sg];
        groups[k] = @([groups[k] intValue] + 1);
    }
    for (NSString *k in [groups.allKeys sortedArrayUsingSelector:@selector(compare:)]) printf("  %s: %d\n", k.UTF8String, [groups[k] intValue]);

    NSArray *wanted = @[@"Energy Model", @"CPU Stats", @"GPU Stats"];
    CFMutableDictionaryRef merged = NULL;
    for (NSString *g in wanted) {
        CFDictionaryRef c = IOReportCopyChannelsInGroup((__bridge CFStringRef)g, NULL, 0, 0, 0);
        if (!c) { printf("group %s: none\n", g.UTF8String); continue; }
        if (!merged) merged = CFDictionaryCreateMutableCopy(kCFAllocatorDefault, 0, c);
        else {
            NSMutableArray *a = [(((__bridge NSDictionary *)merged)[@"IOReportChannels"] ?: @[]) mutableCopy];
            NSArray *more = ((__bridge NSDictionary *)c)[@"IOReportChannels"];
            if (more) [a addObjectsFromArray:more];
            CFDictionarySetValue(merged, CFSTR("IOReportChannels"), (__bridge CFArrayRef)a);
        }
    }
    if (!merged) return;
    CFMutableDictionaryRef subbed = NULL;
    IOReportSubscriptionRef sub = IOReportCreateSubscription(NULL, merged, &subbed, 0, NULL);
    if (!sub) { printf("no subscription\n"); return; }
    CFDictionaryRef a = IOReportCreateSamples(sub, subbed, NULL);
    [NSThread sleepForTimeInterval:0.5];
    CFDictionaryRef b = IOReportCreateSamples(sub, subbed, NULL);
    CFDictionaryRef d = IOReportCreateSamplesDelta(a, b, NULL);
    NSArray *items = ((__bridge NSDictionary *)d)[@"IOReportChannels"];
    int shown = 0;
    for (NSDictionary *ch in items) {
        CFDictionaryRef c = (__bridge CFDictionaryRef)ch;
        NSString *g = (__bridge NSString *)IOReportChannelGetGroup(c);
        NSString *sg = (__bridge NSString *)IOReportChannelGetSubGroup(c);
        NSString *nm = (__bridge NSString *)IOReportChannelGetChannelName(c);
        NSString *unit = (__bridge NSString *)IOReportChannelGetUnitLabel(c);
        int32_t fmt = IOReportChannelGetFormat(c);
        if (shown++ > 120) break;
        if (fmt == 1) printf("  [%s/%s] %s = %lld %s\n", g.UTF8String, sg.UTF8String, nm.UTF8String, IOReportSimpleGetIntegerValue(c, 0), unit.UTF8String ?: "");
        else if (fmt == 2) {
            int32_t n = IOReportStateGetCount(c);
            printf("  [%s/%s] %s states=%d:", g.UTF8String, sg.UTF8String, nm.UTF8String, n);
            for (int i = 0; i < n && i < 20; i++) printf(" %s=%lld", ((__bridge NSString *)IOReportStateGetNameForIndex(c, i)).UTF8String, IOReportStateGetResidency(c, i));
            printf("\n");
        } else printf("  [%s/%s] %s format %d\n", g.UTF8String, sg.UTF8String, nm.UTF8String, fmt);
    }
}

static void registry(const char *cls, BOOL props) {
    printf("\n== %s\n", cls);
    io_iterator_t it;
    if (IOServiceGetMatchingServices(kIOMainPortDefault, IOServiceMatching(cls), &it)) return;
    io_object_t o; int count = 0;
    while ((o = IOIteratorNext(it))) {
        io_name_t name; IORegistryEntryGetName(o, name);
        CFMutableDictionaryRef p = NULL;
        IORegistryEntryCreateCFProperties(o, &p, kCFAllocatorDefault, 0);
        NSDictionary *d = (__bridge_transfer NSDictionary *)p;
        if (strcmp(cls, "AppleARMIODevice") == 0 && strcmp(name, "pmgr") != 0) { IOObjectRelease(o); continue; }
        printf("- %s\n", name);
        for (NSString *k in [d.allKeys sortedArrayUsingSelector:@selector(compare:)]) {
            id v = d[k];
            if ([k containsString:@"voltage-states"]) { printf("    %s: %lu bytes\n", k.UTF8String, (unsigned long)[(NSData *)v length]); continue; }
            if (props || [k isEqualToString:@"PerformanceStatistics"] || [k isEqualToString:@"model"] || [k containsString:@"core-count"] || [k isEqualToString:@"Statistics"])
                printf("    %s: %s\n", k.UTF8String, [[v description] stringByReplacingOccurrencesOfString:@"\n" withString:@" "].UTF8String);
        }
        IOObjectRelease(o);
        if (++count > 6) break;
    }
}

int main(void) {
    @autoreleasepool {
        char buf[256]; size_t len = sizeof(buf);
        if (!sysctlbyname("machdep.cpu.brand_string", buf, &len, NULL, 0)) printf("CPU: %s\n", buf);
        len = sizeof(buf); if (!sysctlbyname("hw.model", buf, &len, NULL, 0)) printf("Model: %s\n", buf);
        id<MTLDevice> dev = MTLCreateSystemDefaultDevice();
        printf("Metal: %s unified=%d maxWS=%llu\n", dev ? dev.name.UTF8String : "(none)", dev ? dev.hasUnifiedMemory : 0, dev ? dev.recommendedMaxWorkingSetSize : 0);
        hid();
        smc();
        ioreport();
        registry("IOAccelerator", NO);
        registry("AppleARMIODevice", NO);
        registry("IOBlockStorageDriver", NO);
    }
    return 0;
}
