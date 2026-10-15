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

    private const string LogCategory = "NVAPI";
    private const int Ok = 0;
    private const int SettingNotFound = -160;

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
