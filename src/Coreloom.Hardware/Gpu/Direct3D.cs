using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Coreloom.Core.Benchmarks;

namespace Coreloom.Hardware.Gpu;

/// <summary>
/// A small Direct3D 11 device for the GPU benchmark and stress test: compute shaders, full-screen pixel shaders drawn
/// into an off-screen texture, buffers and read-backs. It's called through the COM interfaces' function tables
/// directly (no wrapper library), so only the handful of calls the benchmark needs are here. Nothing is drawn on
/// screen, and nothing touches a game: it's Coreloom's own device, used only while a benchmark or stress test runs.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed unsafe partial class Direct3D : IDisposable
{
    // Device function-table slots (ID3D11Device).
    private const int CreateBufferSlot = 3, CreateTexture2DSlot = 5, CreateUavSlot = 8, CreateRtvSlot = 9;
    private const int CreateVertexShaderSlot = 12, CreatePixelShaderSlot = 15, CreateComputeShaderSlot = 18, CreateQuerySlot = 24;
    private const int GetDeviceRemovedReasonSlot = 39;

    // Context function-table slots (ID3D11DeviceContext).
    private const int PSSetShaderSlot = 9, VSSetShaderSlot = 11, DrawSlot = 13, MapSlot = 14, UnmapSlot = 15;
    private const int PSSetConstantBuffersSlot = 16, IASetPrimitiveTopologySlot = 24, EndSlot = 28, GetDataSlot = 29;
    private const int OMSetRenderTargetsSlot = 33, DispatchSlot = 41, RSSetViewportsSlot = 44, CopyResourceSlot = 47;
    private const int UpdateSubresourceSlot = 48, CSSetUavsSlot = 68, CSSetShaderSlot = 69, CSSetConstantBuffersSlot = 71;
    private const int ClearStateSlot = 110, FlushSlot = 111;

    private const int SFalse = 1;
    private const uint SoftwareAdapter = 2; // DXGI_ADAPTER_FLAG_SOFTWARE
    private const uint MicrosoftVendor = 0x1414;

    private static readonly Guid DxgiFactory1 = new("770aae78-f26f-4dba-a829-253c83d1b387");

    private void* _device;
    private void* _context;
    private void* _event;
    private readonly List<nint> _owned = [];

    private Direct3D(void* device, void* context, GpuAdapter adapter)
    {
        _device = device;
        _context = context;
        Adapter = adapter;
        var desc = new QueryDesc { Query = 0 }; // D3D11_QUERY_EVENT
        void* query;
        Check(Call(_device, CreateQuerySlot, &desc, &query), "create a GPU query");
        _event = query;
    }

    public GpuAdapter Adapter { get; }

    /// <summary>The graphics cards Windows offers, best first (the most dedicated memory), without software ones.</summary>
    public static List<GpuAdapter> Adapters()
    {
        var adapters = new List<GpuAdapter>();
        ForEachAdapter((adapter, info) =>
        {
            adapters.Add(info);
            return false;
        });
        return adapters.Where(a => !a.IsSoftware).OrderByDescending(a => a.Memory).ToList();
    }

    /// <summary>
    /// A device on the chosen graphics card (by default the one with the most dedicated memory), or on WARP — Windows'
    /// software renderer — when <paramref name="software"/> is true.
    /// </summary>
    public static Direct3D Create(GpuAdapter? choice = null, bool software = false)
    {
        void* device = null, context = null;
        if (software)
        {
            CreateDevice(null, 5 /* D3D_DRIVER_TYPE_WARP */, &device, &context);
            return new Direct3D(device, context, new GpuAdapter("Microsoft Basic Render Driver (WARP)", MicrosoftVendor, 0, 0, 0, true));
        }

        GpuAdapter? best = choice ?? Adapters().FirstOrDefault()
            ?? throw new InvalidOperationException("Windows doesn't list a graphics card that can run the benchmark.");
        GpuAdapter? used = null;
        nint createdDevice = 0, createdContext = 0;
        ForEachAdapter((adapter, info) =>
        {
            if (info.Luid != best.Luid)
            {
                return false;
            }

            void* d, c;
            CreateDevice(adapter, 0 /* D3D_DRIVER_TYPE_UNKNOWN: the adapter's own driver */, &d, &c);
            (createdDevice, createdContext, used) = ((nint)d, (nint)c, info);
            return true;
        });

        return used is null
            ? throw new InvalidOperationException($"{best.Name} isn't available any more.")
            : new Direct3D((void*)createdDevice, (void*)createdContext, used);
    }

    private static void CreateDevice(void* adapter, int driverType, void** device, void** context)
    {
        int* levels = stackalloc int[] { 0xb100, 0xb000 }; // feature levels 11.1 and 11.0 (compute shaders 5.0)
        int level;
        int hr = D3D11CreateDevice(adapter, driverType, 0, 0x1 /* SINGLETHREADED */, levels, 2, 7, device, &level, context);
        if (hr == unchecked((int)0x80070057))
        {
            hr = D3D11CreateDevice(adapter, driverType, 0, 0x1, levels + 1, 1, 7, device, &level, context); // no 11.1 runtime
        }

        Check(hr, "create a Direct3D 11 device");
    }

    /// <summary>Walks DXGI's adapters; <paramref name="visit"/> returns true to stop (the adapter is released after).</summary>
    private static void ForEachAdapter(AdapterVisitor visit)
    {
        void* factory;
        Guid iid = DxgiFactory1;
        Check(CreateDXGIFactory1(&iid, &factory), "list the graphics cards");
        try
        {
            for (uint i = 0; ; i++)
            {
                void* adapter;
                int hr = ((delegate* unmanaged[Stdcall]<void*, uint, void**, int>)Slot(factory, 12))(factory, i, &adapter); // EnumAdapters1
                if (hr == unchecked((int)0x887A0002))
                {
                    break; // DXGI_ERROR_NOT_FOUND: no more
                }

                Check(hr, "list the graphics cards");
                try
                {
                    AdapterDesc1 desc;
                    Check(((delegate* unmanaged[Stdcall]<void*, AdapterDesc1*, int>)Slot(adapter, 10))(adapter, &desc), "read a graphics card's name");
                    string name = new string(desc.Description, 0, 128).TrimEnd('\0').Trim();
                    bool software = (desc.Flags & SoftwareAdapter) != 0 || desc.VendorId == MicrosoftVendor;
                    var info = new GpuAdapter(name, desc.VendorId, desc.DeviceId, desc.DedicatedVideoMemory, ((long)desc.LuidHigh << 32) | desc.LuidLow, software);
                    if (visit(adapter, info))
                    {
                        break;
                    }
                }
                finally
                {
                    Release(adapter);
                }
            }
        }
        finally
        {
            Release(factory);
        }
    }

    private delegate bool AdapterVisitor(void* adapter, GpuAdapter info);

    // ----- Direct3D 12 shader model (DirectStorage's GPU decompression, since 1.8.4) -------------------------------

    private static readonly Guid D3D12Device = new("189819f1-1db6-4b57-be54-1821339b85f7");

    /// <summary>
    /// The highest Direct3D 12 shader model of a graphics card (0x60 = 6.0), or null when it can't make a Direct3D 12
    /// device. <paramref name="choice"/> defaults to the card with the most dedicated memory; <paramref name="includeSoftware"/>
    /// lets the tests ask Windows' software adapter. Asks for up to 6.6 and gets the highest the driver supports.
    /// </summary>
    public static (GpuAdapter Adapter, int? ShaderModel)? HighestShaderModel(GpuAdapter? choice = null, bool includeSoftware = false)
    {
        GpuAdapter? best = choice;
        if (best is null)
        {
            var all = new List<GpuAdapter>();
            ForEachAdapter((_, info) =>
            {
                all.Add(info);
                return false;
            });
            best = all.Where(a => includeSoftware || !a.IsSoftware).OrderBy(a => a.IsSoftware).ThenByDescending(a => a.Memory).FirstOrDefault();
        }

        if (best is null)
        {
            return null;
        }

        (GpuAdapter Adapter, int? ShaderModel)? result = null;
        ForEachAdapter((adapter, info) =>
        {
            if (info.Luid != best.Luid)
            {
                return false;
            }

            result = (info, ShaderModelOf(adapter));
            return true;
        });
        return result;
    }

    private static int? ShaderModelOf(void* adapter)
    {
        void* device;
        Guid iid = D3D12Device;
        int hr;
        try
        {
            hr = D3D12CreateDevice(adapter, 0xb000 /* feature level 11.0, the least Direct3D 12 accepts */, &iid, &device);
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            return null; // no Direct3D 12 on this Windows
        }

        if (hr < 0 || device == null)
        {
            return null;
        }

        try
        {
            // ID3D12Device::CheckFeatureSupport (slot 13) with D3D12_FEATURE_SHADER_MODEL (7): ask for the highest model
            // this code knows; a runtime that doesn't know it answers E_INVALIDARG, so step down until it accepts.
            foreach (int asked in new[] { 0x66, 0x65, 0x64, 0x63, 0x62, 0x61, 0x60, 0x51 })
            {
                int model = asked;
                int check = ((delegate* unmanaged[Stdcall]<void*, int, void*, uint, int>)Slot(device, 13))(device, 7, &model, sizeof(int));
                if (check >= 0)
                {
                    return model;
                }
            }

            return 0x51; // a Direct3D 12 device always has 5.1
        }
        finally
        {
            Release(device);
        }
    }

    // ----- Shaders --------------------------------------------------------------------------------------------------

    public nint ComputeShader(string source, string entry) => CreateShader(source, entry, "cs_5_0", CreateComputeShaderSlot);

    public nint VertexShader(string source, string entry) => CreateShader(source, entry, "vs_5_0", CreateVertexShaderSlot);

    public nint PixelShader(string source, string entry) => CreateShader(source, entry, "ps_5_0", CreatePixelShaderSlot);

    private nint CreateShader(string source, string entry, string target, int slot)
    {
        byte[] code = Compile(source, entry, target);
        void* shader;
        fixed (byte* bytes = code)
        {
            Check(((delegate* unmanaged[Stdcall]<void*, void*, nuint, void*, void**, int>)Slot(_device, slot))(_device, bytes, (nuint)code.Length, null, &shader), $"create the {entry} shader");
        }

        return Own(shader);
    }

    /// <summary>Compiles HLSL with Windows' shader compiler (d3dcompiler_47.dll, part of Windows 10 and 11).</summary>
    public static byte[] Compile(string source, string entry, string target)
    {
        byte[] text = Encoding.ASCII.GetBytes(source);
        byte[] entryName = Encoding.ASCII.GetBytes(entry + "\0");
        byte[] profile = Encoding.ASCII.GetBytes(target + "\0");
        void* code = null, errors = null;
        int hr;
        fixed (byte* src = text, e = entryName, t = profile)
        {
            hr = D3DCompile(src, (nuint)text.Length, null, null, null, e, t, 1u << 15 /* OPTIMIZATION_LEVEL3 */, 0, &code, &errors);
        }

        try
        {
            if (hr < 0 || code is null)
            {
                string message = errors is null ? string.Empty : Marshal.PtrToStringAnsi((nint)BlobPointer(errors), (int)BlobSize(errors)) ?? string.Empty;
                throw new InvalidOperationException($"The {entry} shader didn't compile (0x{hr:X8}): {message.Trim()}");
            }

            return new ReadOnlySpan<byte>(BlobPointer(code), (int)BlobSize(code)).ToArray();
        }
        finally
        {
            Release(code);
            Release(errors);
        }
    }

    private static void* BlobPointer(void* blob) => ((delegate* unmanaged[Stdcall]<void*, void*>)Slot(blob, 3))(blob);

    private static nuint BlobSize(void* blob) => ((delegate* unmanaged[Stdcall]<void*, nuint>)Slot(blob, 4))(blob);

    // ----- Resources ------------------------------------------------------------------------------------------------

    /// <summary>A structured buffer the GPU reads and writes (with its unordered-access view), filled from <paramref name="data"/>.</summary>
    public (nint Buffer, nint View) StructuredBuffer<T>(ReadOnlySpan<T> data, int stride)
        where T : unmanaged
    {
        fixed (T* pointer = data)
        {
            return StructuredBuffer(pointer, data.Length * sizeof(T), stride);
        }
    }

    /// <summary>A structured buffer of <paramref name="bytes"/> the GPU reads and writes, its contents left as they are.</summary>
    public (nint Buffer, nint View) StructuredBuffer(int bytes, int stride) => StructuredBuffer(null, bytes, stride);

    private (nint Buffer, nint View) StructuredBuffer(void* data, int bytes, int stride)
    {
        var desc = new BufferDesc { ByteWidth = (uint)bytes, Usage = 0, BindFlags = 0x80 /* UNORDERED_ACCESS */, MiscFlags = 0x40 /* STRUCTURED */, StructureByteStride = (uint)stride };
        void* buffer;
        var init = new SubresourceData { SysMem = data };
        Check(Call(_device, CreateBufferSlot, &desc, data is null ? null : &init, &buffer), "create a GPU buffer");
        Own(buffer);
        var view = new UavDesc { Format = 0, Dimension = 1 /* BUFFER */, FirstElement = 0, NumElements = (uint)(bytes / stride) };
        void* uav;
        Check(Call(_device, CreateUavSlot, buffer, &view, &uav), "create a GPU buffer view");
        return ((nint)buffer, Own(uav));
    }

    /// <summary>A buffer the CPU can read a copy of a GPU buffer from.</summary>
    public nint StagingBuffer(int bytes, int stride)
    {
        var desc = new BufferDesc { ByteWidth = (uint)bytes, Usage = 3 /* STAGING */, CpuAccessFlags = 0x20000 /* READ */, MiscFlags = 0x40, StructureByteStride = (uint)stride };
        void* buffer;
        Check(Call(_device, CreateBufferSlot, &desc, null, &buffer), "create a read-back buffer");
        return Own(buffer);
    }

    /// <summary>A 16-byte-aligned constant buffer.</summary>
    public nint ConstantBuffer(int bytes)
    {
        var desc = new BufferDesc { ByteWidth = (uint)((bytes + 15) & ~15), Usage = 0, BindFlags = 0x4 /* CONSTANT_BUFFER */ };
        void* buffer;
        Check(Call(_device, CreateBufferSlot, &desc, null, &buffer), "create a constant buffer");
        return Own(buffer);
    }

    /// <summary>An off-screen RGBA8 render target (with its view) and a texture to read it back from.</summary>
    public (nint Texture, nint View, nint Staging) RenderTarget(int width, int height)
    {
        var desc = new Texture2DDesc
        {
            Width = (uint)width, Height = (uint)height, MipLevels = 1, ArraySize = 1, Format = 28 /* R8G8B8A8_UNORM */,
            SampleCount = 1, Usage = 0, BindFlags = 0x20 /* RENDER_TARGET */,
        };
        void* texture;
        Check(Call(_device, CreateTexture2DSlot, &desc, null, &texture), "create a render target");
        Own(texture);
        void* view;
        Check(Call(_device, CreateRtvSlot, texture, null, &view), "create a render target view");
        Own(view);
        desc.Usage = 3;
        desc.BindFlags = 0;
        desc.CpuAccessFlags = 0x20000;
        void* staging;
        Check(Call(_device, CreateTexture2DSlot, &desc, null, &staging), "create a read-back texture");
        return ((nint)texture, (nint)view, Own(staging));
    }

    // ----- Commands -------------------------------------------------------------------------------------------------

    public void SetConstants<T>(nint buffer, in T value)
        where T : unmanaged
    {
        fixed (T* pointer = &value)
        {
            ((delegate* unmanaged[Stdcall]<void*, void*, uint, void*, void*, uint, uint, void>)Slot(_context, UpdateSubresourceSlot))(_context, (void*)buffer, 0, null, pointer, 0, 0);
        }
    }

    /// <summary>Binds a compute shader with its constant buffer and buffers (u0, u1…).</summary>
    public void UseCompute(nint shader, nint constants, params nint[] views)
    {
        ((delegate* unmanaged[Stdcall]<void*, void*, void*, uint, void>)Slot(_context, CSSetShaderSlot))(_context, (void*)shader, null, 0);
        void* cb = (void*)constants;
        ((delegate* unmanaged[Stdcall]<void*, uint, uint, void**, void>)Slot(_context, CSSetConstantBuffersSlot))(_context, 0, 1, &cb);
        fixed (nint* uavs = views)
        {
            ((delegate* unmanaged[Stdcall]<void*, uint, uint, nint*, uint*, void>)Slot(_context, CSSetUavsSlot))(_context, 0, (uint)views.Length, uavs, null);
        }
    }

    public void Dispatch(uint groups) =>
        ((delegate* unmanaged[Stdcall]<void*, uint, uint, uint, void>)Slot(_context, DispatchSlot))(_context, groups, 1, 1);

    /// <summary>Binds a full-screen draw: the vertex and pixel shaders, the constants and the render target.</summary>
    public void UseDraw(nint vertexShader, nint pixelShader, nint constants, nint target, int width, int height)
    {
        ((delegate* unmanaged[Stdcall]<void*, void*, void*, uint, void>)Slot(_context, VSSetShaderSlot))(_context, (void*)vertexShader, null, 0);
        ((delegate* unmanaged[Stdcall]<void*, void*, void*, uint, void>)Slot(_context, PSSetShaderSlot))(_context, (void*)pixelShader, null, 0);
        void* cb = (void*)constants;
        ((delegate* unmanaged[Stdcall]<void*, uint, uint, void**, void>)Slot(_context, PSSetConstantBuffersSlot))(_context, 0, 1, &cb);
        ((delegate* unmanaged[Stdcall]<void*, int, void>)Slot(_context, IASetPrimitiveTopologySlot))(_context, 4 /* TRIANGLELIST */);
        void* rtv = (void*)target;
        ((delegate* unmanaged[Stdcall]<void*, uint, void**, void*, void>)Slot(_context, OMSetRenderTargetsSlot))(_context, 1, &rtv, null);
        var viewport = new Viewport { Width = width, Height = height, MaxDepth = 1 };
        ((delegate* unmanaged[Stdcall]<void*, uint, Viewport*, void>)Slot(_context, RSSetViewportsSlot))(_context, 1, &viewport);
    }

    /// <summary>Draws one full-screen triangle (three vertices, no vertex buffer: the shader makes them).</summary>
    public void DrawFullScreen() =>
        ((delegate* unmanaged[Stdcall]<void*, uint, uint, void>)Slot(_context, DrawSlot))(_context, 3, 0);

    public void Unbind() => ((delegate* unmanaged[Stdcall]<void*, void>)Slot(_context, ClearStateSlot))(_context);

    /// <summary>Waits until the GPU has finished everything sent so far (spinning, so the wait ends promptly).</summary>
    public void Finish()
    {
        ((delegate* unmanaged[Stdcall]<void*, void*, void>)Slot(_context, EndSlot))(_context, _event);
        var spin = default(SpinWait);
        while (true)
        {
            int done;
            int hr = ((delegate* unmanaged[Stdcall]<void*, void*, void*, uint, uint, int>)Slot(_context, GetDataSlot))(_context, _event, &done, sizeof(int), 0);
            if (hr != SFalse)
            {
                if (hr < 0)
                {
                    int reason = ((delegate* unmanaged[Stdcall]<void*, int>)Slot(_device, GetDeviceRemovedReasonSlot))(_device);
                    throw new GpuLostException(reason < 0 ? reason : hr);
                }

                return;
            }

            spin.SpinOnce(sleep1Threshold: -1);
        }
    }

    public void Flush() => ((delegate* unmanaged[Stdcall]<void*, void>)Slot(_context, FlushSlot))(_context);

    /// <summary>Copies a GPU buffer to a read-back buffer and sums it as 32-bit words (a checksum of the GPU's answer).</summary>
    public long Checksum(nint source, nint staging, int bytes)
    {
        ((delegate* unmanaged[Stdcall]<void*, void*, void*, void>)Slot(_context, CopyResourceSlot))(_context, (void*)staging, (void*)source);
        MappedSubresource mapped;
        Check(((delegate* unmanaged[Stdcall]<void*, void*, uint, int, uint, MappedSubresource*, int>)Slot(_context, MapSlot))(_context, (void*)staging, 0, 1 /* READ */, 0, &mapped), "read a result back from the GPU");
        try
        {
            long sum = 0;
            var words = new ReadOnlySpan<uint>(mapped.Data, bytes / 4);
            for (int i = 0; i < words.Length; i++)
            {
                sum = (sum * 31) + words[i];
            }

            return sum;
        }
        finally
        {
            ((delegate* unmanaged[Stdcall]<void*, void*, uint, void>)Slot(_context, UnmapSlot))(_context, (void*)staging, 0);
        }
    }

    /// <summary>Copies the render target to its read-back texture and sums its pixels.</summary>
    public long TextureChecksum(nint texture, nint staging, int width, int height)
    {
        ((delegate* unmanaged[Stdcall]<void*, void*, void*, void>)Slot(_context, CopyResourceSlot))(_context, (void*)staging, (void*)texture);
        MappedSubresource mapped;
        Check(((delegate* unmanaged[Stdcall]<void*, void*, uint, int, uint, MappedSubresource*, int>)Slot(_context, MapSlot))(_context, (void*)staging, 0, 1, 0, &mapped), "read a frame back from the GPU");
        try
        {
            long sum = 0;
            for (int y = 0; y < height; y++)
            {
                var row = new ReadOnlySpan<uint>((byte*)mapped.Data + ((long)y * mapped.RowPitch), width);
                for (int x = 0; x < width; x++)
                {
                    sum = (sum * 31) + row[x];
                }
            }

            return sum;
        }
        finally
        {
            ((delegate* unmanaged[Stdcall]<void*, void*, uint, void>)Slot(_context, UnmapSlot))(_context, (void*)staging, 0);
        }
    }

    // ----- Plumbing -------------------------------------------------------------------------------------------------

    private nint Own(void* pointer)
    {
        _owned.Add((nint)pointer);
        return (nint)pointer;
    }

    private static void* Slot(void* instance, int index) => (*(void***)instance)[index];

    /// <summary>Calls a method that takes two pointers and returns an HRESULT.</summary>
    private static int Call(void* instance, int slot, void* a, void* b) =>
        ((delegate* unmanaged[Stdcall]<void*, void*, void*, int>)Slot(instance, slot))(instance, a, b);

    /// <summary>Calls a method that takes three pointers and returns an HRESULT.</summary>
    private static int Call(void* instance, int slot, void* a, void* b, void* c) =>
        ((delegate* unmanaged[Stdcall]<void*, void*, void*, void*, int>)Slot(instance, slot))(instance, a, b, c);

    private static void Release(void* instance)
    {
        if (instance is not null)
        {
            ((delegate* unmanaged[Stdcall]<void*, uint>)Slot(instance, 2))(instance);
        }
    }

    private static void Check(int hr, string what)
    {
        if (hr < 0)
        {
            throw hr is unchecked((int)0x887A0005) or unchecked((int)0x887A0006) or unchecked((int)0x887A0007)
                ? new GpuLostException(hr)
                : new InvalidOperationException($"Couldn't {what} (0x{hr:X8}).");
        }
    }

    public void Dispose()
    {
        if (_context is not null)
        {
            Unbind();
            Flush();
        }

        for (int i = _owned.Count - 1; i >= 0; i--)
        {
            Release((void*)_owned[i]);
        }

        _owned.Clear();
        Release(_event);
        Release(_context);
        Release(_device);
        _event = _context = _device = null;
    }

    [LibraryImport("dxgi.dll")]
    private static partial int CreateDXGIFactory1(Guid* riid, void** factory);

    [LibraryImport("d3d12.dll")]
    private static partial int D3D12CreateDevice(void* adapter, int minimumFeatureLevel, Guid* riid, void** device);

    [LibraryImport("d3d11.dll")]
    private static partial int D3D11CreateDevice(void* adapter, int driverType, nint software, uint flags, int* featureLevels, uint levels, uint sdkVersion, void** device, int* featureLevel, void** context);

    [LibraryImport("d3dcompiler_47.dll")]
    private static partial int D3DCompile(void* source, nuint size, byte* name, void* defines, void* include, byte* entry, byte* target, uint flags1, uint flags2, void** code, void** errors);

    [StructLayout(LayoutKind.Sequential)]
    private struct BufferDesc
    {
        public uint ByteWidth, Usage, BindFlags, CpuAccessFlags, MiscFlags, StructureByteStride;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SubresourceData
    {
        public void* SysMem;
        public uint Pitch, SlicePitch;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Texture2DDesc
    {
        public uint Width, Height, MipLevels, ArraySize, Format, SampleCount, SampleQuality, Usage, BindFlags, CpuAccessFlags, MiscFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UavDesc
    {
        public uint Format, Dimension, FirstElement, NumElements, Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct QueryDesc
    {
        public uint Query, MiscFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Viewport
    {
        public float X, Y, Width, Height, MinDepth, MaxDepth;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MappedSubresource
    {
        public void* Data;
        public uint RowPitch, DepthPitch;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AdapterDesc1
    {
        public fixed char Description[128];
        public uint VendorId, DeviceId, SubSysId, Revision;
        public nuint DedicatedVideoMemory, DedicatedSystemMemory, SharedSystemMemory;
        public uint LuidLow;
        public int LuidHigh;
        public uint Flags;
    }
}

/// <summary>The graphics driver reset or the card stopped responding (Windows' "device removed").</summary>
public sealed class GpuLostException(int hresult)
    : Exception($"The graphics card stopped responding and Windows reset its driver (0x{hresult:X8}).")
{
    public int Code { get; } = hresult;
}
