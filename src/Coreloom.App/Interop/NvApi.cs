using System.Runtime.InteropServices;
using Coreloom.Core.Diagnostics;

namespace Coreloom.App.Interop;

/// <summary>
/// The few NVIDIA driver settings (NVAPI "DRS", the same store the NVIDIA Control Panel and Profile Inspector use)
/// the Gaming page reads and changes in the global (base) profile: the frame-rate limiter, the shader cache size and the
/// power management mode. NVAPI ships with the NVIDIA driver (nvapi64.dll); without it everything reports unavailable.
/// Function and setting ids are from NVIDIA's public NVAPI headers (nvapi_interface.h, NvApiDriverSettings.h).
/// </summary>
internal static unsafe partial class NvApi
{
    /// <summary>"Frame Rate Limiter" (Max Frame Rate in the Control Panel): 0 off, otherwise frames per second.</summary>
    public const uint FrameRateLimiter = 0x10835002;

    /// <summary>"Shader disk cache maximum size" in MB; 0xFFFFFFFF = Unlimited.</summary>
    public const uint ShaderCacheMaxSize = 0x00AC8497;

    public const uint ShaderCacheUnlimited = 0xFFFFFFFF;

    /// <summary>"Power management mode": 1 = Prefer maximum performance.</summary>
    public const uint PowerManagementMode = 0x1057EB71;

    public const uint PreferMaximumPerformance = 1;

    /// <summary>Profiles Coreloom creates for games the driver has no profile for are named "Coreloom: game name".</summary>
    public const string ProfilePrefix = "Coreloom: ";

    private const string LogCategory = "NVAPI";
    private const int Ok = 0;
    private const int IncompatibleStructVersion = -9;
    private const int SettingNotFound = -160;
    private const int ExecutableNotFound = -166;

    // nvapi_interface.h
    private const uint IdInitialize = 0x0150E828;
    private const uint IdGetDriverAndBranchVersion = 0x2926AAAD;
    private const uint IdCreateSession = 0x0694D52E;
    private const uint IdDestroySession = 0xDAD9CFF8;
    private const uint IdLoadSettings = 0x375DBD6B;
    private const uint IdSaveSettings = 0xFCBC7E14;
    private const uint IdGetBaseProfile = 0xDA8466A0;
    private const uint IdGetSetting = 0x73BF8338;
    private const uint IdSetSetting = 0x577DD202;
    private const uint IdRestoreProfileDefaultSetting = 0x53F0381E;
    private const uint IdFindApplicationByName = 0xEEE566B2;
    private const uint IdCreateProfile = 0xCC176068;
    private const uint IdDeleteProfile = 0x17093206;
    private const uint IdGetProfileInfo = 0x61CD6FD6;
    private const uint IdFindProfileByName = 0x7E4A9A0B;
    private const uint IdCreateApplication = 0x4347A9DE;
    private const uint IdDeleteProfileSetting = 0xE4A26362;

    // NvAPI_UnicodeString: 2048 UTF-16 code units.
    private const int UnicodeChars = 2048;
    private const int UnicodeBytes = UnicodeChars * 2;

    // NVDRS_PROFILE_V1: version, profileName, gpuSupport (bit field), isPredefined, numOfApps, numOfSettings.
    private const int ProfileSize = 4 + UnicodeBytes + 16;
    private const uint ProfileVersion = ProfileSize | (1u << 16);
    private const int ProfileNumSettingsOffset = 4 + UnicodeBytes + 12;

    // NVDRS_APPLICATION_V1…V4: version, isPredefined, appName, userFriendlyName, launcher, then (V2) fileInFolder,
    // (V3) a bit field, (V4) commandLine. Newer drivers take V4; older ones are tried in turn.
    private const int AppNameOffset = 8;
    private const int AppFriendlyNameOffset = AppNameOffset + UnicodeBytes;
    private static readonly (int Size, uint Version)[] ApplicationVersions =
    [
        (8 + (4 * UnicodeBytes) + 4 + UnicodeBytes, 4),
        (8 + (4 * UnicodeBytes) + 4, 3),
        (8 + (4 * UnicodeBytes), 2),
        (8 + (3 * UnicodeBytes), 1),
    ];

    // NVDRS_SETTING_V1 (pack 4): version, settingName[2048 UTF-16], settingId, settingType, settingLocation,
    // isCurrentPredefined, isPredefinedValid, predefined value union (4100 bytes), current value union (4100 bytes).
    private const int SettingSize = 12320;
    private const uint SettingVersion = SettingSize | (1u << 16);
    private const int SettingIdOffset = 4100;
    private const int SettingTypeOffset = 4104;
    private const int CurrentValueOffset = 8220;

    private static readonly object Gate = new();
    private static bool _tried;
    private static bool _available;

    /// <summary>The NVIDIA driver's NVAPI is present and initialised.</summary>
    public static bool IsAvailable
    {
        get
        {
            lock (Gate)
            {
                if (!_tried)
                {
                    _tried = true;
                    try
                    {
                        var init = (delegate* unmanaged[Cdecl]<int>)Query(IdInitialize);
                        _available = init is not null && init() == Ok;
                    }
                    catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
                    {
                        _available = false;
                    }

                    Log.Info(LogCategory, _available ? "NVAPI is available." : "NVAPI isn't available (no NVIDIA driver).");
                }

                return _available;
            }
        }
    }

    /// <summary>"581.57", or null.</summary>
    public static string? DriverVersion()
    {
        if (!IsAvailable)
        {
            return null;
        }

        var get = (delegate* unmanaged[Cdecl]<uint*, byte*, int>)Query(IdGetDriverAndBranchVersion);
        if (get is null)
        {
            return null;
        }

        uint version;
        byte* branch = stackalloc byte[64];
        return get(&version, branch) == Ok && version > 0
            ? $"{version / 100}.{version % 100:00}"
            : null;
    }

    /// <summary>A DWORD setting of the global profile; null when NVAPI is missing, the driver default when not set.</summary>
    public static uint? GetGlobal(uint settingId, uint defaultValue)
    {
        uint? result = null;
        WithSession(save: false, (session, profile) =>
        {
            var get = (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, uint, byte*, int>)Query(IdGetSetting);
            byte* setting = stackalloc byte[SettingSize];
            new Span<byte>(setting, SettingSize).Clear();
            *(uint*)setting = SettingVersion;
            int status = get(session, profile, settingId, setting);
            result = status == Ok ? *(uint*)(setting + CurrentValueOffset) : status == SettingNotFound ? defaultValue : null;
            return status is Ok or SettingNotFound;
        });
        return result;
    }

    /// <summary>Sets a DWORD setting in the global profile and saves it. Returns false when the driver refused.</summary>
    public static bool SetGlobal(uint settingId, uint value) => WithSession(save: true, (session, profile) =>
    {
        var set = (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, byte*, int>)Query(IdSetSetting);
        byte* setting = stackalloc byte[SettingSize];
        new Span<byte>(setting, SettingSize).Clear();
        *(uint*)setting = SettingVersion;
        *(uint*)(setting + SettingIdOffset) = settingId;
        *(uint*)(setting + SettingTypeOffset) = 0; // NVDRS_DWORD_TYPE
        *(uint*)(setting + CurrentValueOffset) = value;
        int status = set(session, profile, setting);
        if (status != Ok)
        {
            Log.Warn(LogCategory, $"Setting 0x{settingId:X8} to {value} failed ({status}).");
        }

        return status == Ok;
    });

    /// <summary>Puts a setting of the global profile back to the driver's default.</summary>
    public static bool RestoreGlobal(uint settingId) => WithSession(save: true, (session, profile) =>
    {
        var restore = (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, uint, int>)Query(IdRestoreProfileDefaultSetting);
        int status = restore(session, profile, settingId);
        return status is Ok or SettingNotFound; // not set: already the default
    });

    /// <summary>
    /// A DWORD setting of the profile the driver uses for a program ("Cyberpunk2077.exe"): null when NVAPI is missing,
    /// the program has no profile, or the setting isn't set in it (the global value applies then).
    /// </summary>
    public static uint? GetForApplication(string executable, uint settingId)
    {
        uint? result = null;
        WithSession(save: false, (session, _) =>
        {
            if (FindApplication(session, executable, out var profile) != Ok)
            {
                return true;
            }

            var get = (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, uint, byte*, int>)Query(IdGetSetting);
            byte* setting = (byte*)NativeMemory.AllocZeroed(SettingSize);
            try
            {
                *(uint*)setting = SettingVersion;
                if (get(session, profile, settingId, setting) == Ok)
                {
                    result = *(uint*)(setting + CurrentValueOffset);
                }
            }
            finally
            {
                NativeMemory.Free(setting);
            }

            return true;
        });
        return result;
    }

    /// <summary>
    /// Sets a DWORD setting for one program and saves it: in the profile the driver already has for it (NVIDIA's own
    /// profiles list most games), or in a new "Coreloom: name" profile with the program added. Only that program is
    /// affected; the global value stays for every other one. Returns false when the driver refused.
    /// </summary>
    public static bool SetForApplication(string executable, string friendlyName, uint settingId, uint value) => WithSession(save: true, (session, _) =>
    {
        int found = FindApplication(session, executable, out var profile);
        if (found == ExecutableNotFound && !CreateApplicationProfile(session, executable, friendlyName, out profile))
        {
            return false;
        }
        else if (found is not (Ok or ExecutableNotFound))
        {
            Log.Warn(LogCategory, $"Looking up the profile of {executable} failed ({found}).");
            return false;
        }

        var set = (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, byte*, int>)Query(IdSetSetting);
        byte* setting = (byte*)NativeMemory.AllocZeroed(SettingSize);
        try
        {
            *(uint*)setting = SettingVersion;
            *(uint*)(setting + SettingIdOffset) = settingId;
            *(uint*)(setting + SettingTypeOffset) = 0; // NVDRS_DWORD_TYPE
            *(uint*)(setting + CurrentValueOffset) = value;
            int status = set(session, profile, setting);
            if (status != Ok)
            {
                Log.Warn(LogCategory, $"Setting 0x{settingId:X8} to {value} for {executable} failed ({status}).");
            }

            return status == Ok;
        }
        finally
        {
            NativeMemory.Free(setting);
        }
    });

    /// <summary>
    /// Removes a setting from a program's profile (the global value applies to it again) and saves. A profile Coreloom
    /// created is deleted once nothing is left in it. True when nothing was set or it was removed.
    /// </summary>
    public static bool RemoveForApplication(string executable, uint settingId) => WithSession(save: true, (session, _) =>
    {
        int found = FindApplication(session, executable, out var profile);
        if (found == ExecutableNotFound)
        {
            return true;
        }

        if (found != Ok)
        {
            return false;
        }

        var delete = (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, uint, int>)Query(IdDeleteProfileSetting);
        int status = delete(session, profile, settingId);
        if (status is not (Ok or SettingNotFound))
        {
            Log.Warn(LogCategory, $"Removing 0x{settingId:X8} for {executable} failed ({status}).");
            return false;
        }

        DeleteIfOwnAndEmpty(session, profile);
        return true;
    });

    /// <summary>The profile that lists the program; NVAPI's status (Ok, or ExecutableNotFound when there is none).</summary>
    private static int FindApplication(IntPtr session, string executable, out IntPtr profile)
    {
        profile = IntPtr.Zero;
        var find = (delegate* unmanaged[Cdecl]<IntPtr, char*, IntPtr*, byte*, int>)Query(IdFindApplicationByName);
        if (find is null)
        {
            return ExecutableNotFound;
        }

        char* name = stackalloc char[UnicodeChars];
        Write(name, executable);
        byte* application = (byte*)NativeMemory.AllocZeroed((nuint)ApplicationVersions[0].Size);
        try
        {
            int status = IncompatibleStructVersion;
            foreach (var (size, version) in ApplicationVersions)
            {
                new Span<byte>(application, ApplicationVersions[0].Size).Clear();
                *(uint*)application = (uint)size | (version << 16);
                IntPtr found;
                status = find(session, name, &found, application);
                if (status != IncompatibleStructVersion)
                {
                    profile = found;
                    break;
                }
            }

            return status;
        }
        finally
        {
            NativeMemory.Free(application);
        }
    }

    /// <summary>Creates (or reuses) the "Coreloom: name" profile and adds the program to it.</summary>
    private static bool CreateApplicationProfile(IntPtr session, string executable, string friendlyName, out IntPtr profile)
    {
        profile = IntPtr.Zero;
        var create = (delegate* unmanaged[Cdecl]<IntPtr, byte*, IntPtr*, int>)Query(IdCreateProfile);
        var findProfile = (delegate* unmanaged[Cdecl]<IntPtr, char*, IntPtr*, int>)Query(IdFindProfileByName);
        var addApplication = (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, byte*, int>)Query(IdCreateApplication);
        if (create is null || findProfile is null || addApplication is null)
        {
            return false;
        }

        string profileName = ProfilePrefix + (string.IsNullOrWhiteSpace(friendlyName) ? executable : friendlyName.Trim());
        char* name = stackalloc char[UnicodeChars];
        Write(name, profileName);
        IntPtr handle;
        if (findProfile(session, name, &handle) != Ok)
        {
            byte* info = stackalloc byte[ProfileSize];
            new Span<byte>(info, ProfileSize).Clear();
            *(uint*)info = ProfileVersion;
            Write((char*)(info + 4), profileName);
            int created = create(session, info, &handle);
            if (created != Ok)
            {
                Log.Warn(LogCategory, $"Creating the profile \"{profileName}\" failed ({created}).");
                return false;
            }
        }

        byte* application = (byte*)NativeMemory.AllocZeroed((nuint)ApplicationVersions[0].Size);
        try
        {
            int status = IncompatibleStructVersion;
            foreach (var (size, version) in ApplicationVersions)
            {
                new Span<byte>(application, ApplicationVersions[0].Size).Clear();
                *(uint*)application = (uint)size | (version << 16);
                Write((char*)(application + AppNameOffset), executable.ToLowerInvariant());
                Write((char*)(application + AppFriendlyNameOffset), friendlyName);
                status = addApplication(session, handle, application);
                if (status != IncompatibleStructVersion)
                {
                    break;
                }
            }

            if (status != Ok)
            {
                Log.Warn(LogCategory, $"Adding {executable} to \"{profileName}\" failed ({status}).");
                return false;
            }

            Log.Info(LogCategory, $"Created the driver profile \"{profileName}\" for {executable}.");
            profile = handle;
            return true;
        }
        finally
        {
            NativeMemory.Free(application);
        }
    }

    /// <summary>Deletes a profile Coreloom created once it holds no settings any more.</summary>
    private static void DeleteIfOwnAndEmpty(IntPtr session, IntPtr profile)
    {
        var getInfo = (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, byte*, int>)Query(IdGetProfileInfo);
        var deleteProfile = (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, int>)Query(IdDeleteProfile);
        if (getInfo is null || deleteProfile is null)
        {
            return;
        }

        byte* info = stackalloc byte[ProfileSize];
        new Span<byte>(info, ProfileSize).Clear();
        *(uint*)info = ProfileVersion;
        if (getInfo(session, profile, info) != Ok)
        {
            return;
        }

        string name = new((char*)(info + 4));
        uint settings = *(uint*)(info + ProfileNumSettingsOffset);
        if (name.StartsWith(ProfilePrefix, StringComparison.Ordinal) && settings == 0 && deleteProfile(session, profile) == Ok)
        {
            Log.Info(LogCategory, $"Deleted the empty driver profile \"{name}\".");
        }
    }

    /// <summary>Copies text into an NvAPI_UnicodeString (cut to fit, always terminated).</summary>
    private static void Write(char* buffer, string text)
    {
        int length = Math.Min(text.Length, UnicodeChars - 1);
        text.AsSpan(0, length).CopyTo(new Span<char>(buffer, UnicodeChars));
        buffer[length] = '\0';
    }

    private static bool WithSession(bool save, Func<IntPtr, IntPtr, bool> action)
    {
        if (!IsAvailable)
        {
            return false;
        }

        lock (Gate)
        {
            var create = (delegate* unmanaged[Cdecl]<IntPtr*, int>)Query(IdCreateSession);
            var destroy = (delegate* unmanaged[Cdecl]<IntPtr, int>)Query(IdDestroySession);
            var load = (delegate* unmanaged[Cdecl]<IntPtr, int>)Query(IdLoadSettings);
            var saveSettings = (delegate* unmanaged[Cdecl]<IntPtr, int>)Query(IdSaveSettings);
            var baseProfile = (delegate* unmanaged[Cdecl]<IntPtr, IntPtr*, int>)Query(IdGetBaseProfile);
            if (create is null || destroy is null || load is null || saveSettings is null || baseProfile is null
                || Query(IdGetSetting) == null || Query(IdSetSetting) == null || Query(IdRestoreProfileDefaultSetting) == null)
            {
                return false;
            }

            IntPtr session;
            if (create(&session) != Ok)
            {
                return false;
            }

            try
            {
                IntPtr profile;
                if (load(session) != Ok || baseProfile(session, &profile) != Ok)
                {
                    return false;
                }

                if (!action(session, profile))
                {
                    return false;
                }

                if (save)
                {
                    int saved = saveSettings(session);
                    if (saved != Ok)
                    {
                        Log.Warn(LogCategory, $"Saving the driver settings failed ({saved}).");
                        return false;
                    }
                }

                return true;
            }
            finally
            {
                destroy(session);
            }
        }
    }

    private static void* Query(uint id) => (void*)QueryInterface(id);

    [LibraryImport("nvapi64.dll", EntryPoint = "nvapi_QueryInterface")]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    private static partial IntPtr QueryInterface(uint id);
}
