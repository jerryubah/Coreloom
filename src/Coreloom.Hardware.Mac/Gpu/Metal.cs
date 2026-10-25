using System.Runtime.InteropServices;
using Coreloom.Core.Benchmarks;
using Coreloom.Hardware.Mac.Native;

namespace Coreloom.Hardware.Mac.Gpu;

/// <summary>
/// A small Metal host for Coreloom's GPU tests: one device, one command queue, shaders compiled from source, off-screen
/// render targets and compute buffers. Nothing is drawn on screen. Every Objective-C call is made inside an autorelease
/// pool by the caller (see <see cref="ObjC.Pool"/>).
/// </summary>
internal sealed unsafe partial class Metal : IDisposable
{
    public const ulong PixelFormatRgba8Unorm = 70;
    public const ulong StorageShared = 0;
    public const ulong StorageManaged = 1;
    public const ulong StoragePrivate = 2;
    private const ulong TextureUsageShaderRead = 1;
    private const ulong TextureUsageRenderTarget = 4;
    private const ulong CommandBufferStatusError = 5;

    [StructLayout(LayoutKind.Sequential)]
    public struct Size3
    {
        public nuint Width, Height, Depth;

        public Size3(nuint width, nuint height, nuint depth)
        {
            Width = width;
            Height = height;
            Depth = depth;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Origin3
    {
        public nuint X, Y, Z;
    }

    [LibraryImport(Libraries.Metal)]
    private static partial nint MTLCreateSystemDefaultDevice();

    [LibraryImport(Libraries.Metal)]
    private static partial nint MTLCopyAllDevices();

    [LibraryImport(Libraries.ObjC, EntryPoint = "objc_msgSend")]
    private static partial void SendDispatch(nint receiver, nint selector, Size3 groups, Size3 threads);

    [LibraryImport(Libraries.ObjC, EntryPoint = "objc_msgSend")]
    private static partial void SendDraw(nint receiver, nint selector, ulong primitive, ulong start, ulong count);

    [LibraryImport(Libraries.ObjC, EntryPoint = "objc_msgSend")]
    private static partial void SendSetBytes(nint receiver, nint selector, void* bytes, ulong length, ulong index);

    [LibraryImport(Libraries.ObjC, EntryPoint = "objc_msgSend")]
    private static partial void SendSetBuffer(nint receiver, nint selector, nint buffer, ulong offset, ulong index);

    [LibraryImport(Libraries.ObjC, EntryPoint = "objc_msgSend")]
    private static partial nint SendTextureDescriptor(nint receiver, nint selector, ulong format, ulong width, ulong height, [MarshalAs(UnmanagedType.U1)] bool mipmapped);

    [LibraryImport(Libraries.ObjC, EntryPoint = "objc_msgSend")]
    private static partial nint SendNewBuffer(nint receiver, nint selector, ulong length, ulong options);

    [LibraryImport(Libraries.ObjC, EntryPoint = "objc_msgSend")]
    private static partial nint SendNewBufferWithBytes(nint receiver, nint selector, void* bytes, ulong length, ulong options);

    [LibraryImport(Libraries.ObjC, EntryPoint = "objc_msgSend")]
    private static partial void SendCopyTextureToBuffer(nint receiver, nint selector, nint texture, ulong slice, ulong level, Origin3 origin, Size3 size, nint buffer, ulong offset, ulong bytesPerRow, ulong bytesPerImage);

    [LibraryImport(Libraries.ObjC, EntryPoint = "objc_msgSend")]
    private static partial nint SendIndex(nint receiver, nint selector, ulong index);

    [LibraryImport(Libraries.ObjC, EntryPoint = "objc_msgSend")]
    private static partial void SendCopyBuffer(nint receiver, nint selector, nint source, ulong sourceOffset, nint target, ulong targetOffset, ulong size);

    private readonly nint _device;
    private readonly nint _queue;
    private readonly List<nint> _owned = [];
    private nint _commandBuffer;

    private Metal(nint device)
    {
        _device = device;
        _queue = ObjC.Send(device, ObjC.Sel("newCommandQueue"));
        if (_queue == 0)
        {
            throw new InvalidOperationException("Metal couldn't create a command queue.");
        }

        Adapter = Describe(device);
        IsUnified = ObjC.SendBool(device, ObjC.Sel("hasUnifiedMemory"));
    }

    public GpuAdapter Adapter { get; }

    public bool IsUnified { get; }

    static Metal() => NativeLibrary.Load(Libraries.Metal);

    /// <summary>Every Metal GPU (the built-in one first, then discrete and external ones).</summary>
    public static IReadOnlyList<GpuAdapter> Adapters()
    {
        using var pool = new ObjC.Pool();
        var result = new List<GpuAdapter>();
        nint all = MTLCopyAllDevices();
        if (all == 0)
        {
            nint single = MTLCreateSystemDefaultDevice();
            if (single != 0)
            {
                result.Add(Describe(single));
                ObjC.Release(single);
            }

            return result;
        }

        ulong count = ObjC.SendULong(all, ObjC.Sel("count"));
        for (ulong i = 0; i < count; i++)
        {
            result.Add(Describe(SendIndex(all, ObjC.Sel("objectAtIndex:"), i)));
        }

        ObjC.Release(all);
        return result.OrderBy(a => a.IsSoftware).ThenByDescending(a => a.Memory).ToList();
    }

    private static GpuAdapter Describe(nint device)
    {
        string name = ObjC.ToStringValue(ObjC.Send(device, ObjC.Sel("name"))) ?? "GPU";
        ulong registry = ObjC.SendULong(device, ObjC.Sel("registryID"));
        double memory = ObjC.SendULong(device, ObjC.Sel("recommendedMaxWorkingSetSize"));
        uint vendor = name.StartsWith("Apple", StringComparison.Ordinal) ? 0x106Bu
            : name.Contains("AMD", StringComparison.OrdinalIgnoreCase) || name.Contains("Radeon", StringComparison.OrdinalIgnoreCase) ? 0x1002u
            : name.Contains("Intel", StringComparison.OrdinalIgnoreCase) ? 0x8086u
            : name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ? 0x10DEu
            : 0u;
        return new GpuAdapter(name, vendor, 0, memory, (long)registry, false);
    }

    /// <summary>A Metal device for <paramref name="adapter"/> (matched by registry id), else the system's default GPU.</summary>
    public static Metal Create(GpuAdapter? adapter)
    {
        using var pool = new ObjC.Pool();
        nint chosen = 0;
        if (adapter is not null && MTLCopyAllDevices() is var all && all != 0)
        {
            ulong count = ObjC.SendULong(all, ObjC.Sel("count"));
            for (ulong i = 0; i < count && chosen == 0; i++)
            {
                nint device = SendIndex(all, ObjC.Sel("objectAtIndex:"), i);
                if ((long)ObjC.SendULong(device, ObjC.Sel("registryID")) == adapter.Luid)
                {
                    chosen = ObjC.Send(device, ObjC.Sel("retain"));
                }
            }

            ObjC.Release(all);
        }

        if (chosen == 0)
        {
            chosen = MTLCreateSystemDefaultDevice();
        }

        if (chosen == 0)
        {
            throw new InvalidOperationException("This Mac has no Metal GPU.");
        }

        return new Metal(chosen);
    }

    private nint Own(nint obj)
    {
        if (obj != 0)
        {
            _owned.Add(obj);
        }

        return obj;
    }

    private static string ErrorText(nint error) =>
        error == 0 ? "unknown error" : ObjC.ToStringValue(ObjC.Send(error, ObjC.Sel("localizedDescription"))) ?? "unknown error";

    /// <summary>Compiles Metal Shading Language source (fast math, as the D3D shaders are compiled).</summary>
    public nint Library(string source)
    {
        nint error = 0;
        nint options = ObjC.Send(ObjC.Send(ObjC.Class("MTLCompileOptions"), ObjC.Sel("alloc")), ObjC.Sel("init"));
        nint library = ObjC.Send(_device, ObjC.Sel("newLibraryWithSource:options:error:"), ObjC.NSString(source), options, (nint)(&error));
        ObjC.Release(options);
        if (library == 0)
        {
            throw new InvalidOperationException($"Metal couldn't compile Coreloom's shaders: {ErrorText(error)}");
        }

        return Own(library);
    }

    public nint Function(nint library, string name)
    {
        nint function = ObjC.Send(library, ObjC.Sel("newFunctionWithName:"), ObjC.NSString(name));
        return function == 0 ? throw new InvalidOperationException($"The shader {name} is missing.") : Own(function);
    }

    public nint ComputePipeline(nint function)
    {
        nint error = 0;
        nint pipeline = ObjC.Send(_device, ObjC.Sel("newComputePipelineStateWithFunction:error:"), function, (nint)(&error));
        return pipeline == 0 ? throw new InvalidOperationException($"Metal couldn't build a compute pipeline: {ErrorText(error)}") : Own(pipeline);
    }

    public nint RenderPipeline(nint vertex, nint fragment)
    {
        nint descriptor = ObjC.Send(ObjC.Send(ObjC.Class("MTLRenderPipelineDescriptor"), ObjC.Sel("alloc")), ObjC.Sel("init"));
        ObjC.SendVoid(descriptor, ObjC.Sel("setVertexFunction:"), vertex);
        ObjC.SendVoid(descriptor, ObjC.Sel("setFragmentFunction:"), fragment);
        nint attachment = SendIndex(ObjC.Send(descriptor, ObjC.Sel("colorAttachments")), ObjC.Sel("objectAtIndexedSubscript:"), 0);
        ObjC.SendVoidLong(attachment, ObjC.Sel("setPixelFormat:"), (long)PixelFormatRgba8Unorm);
        nint error = 0;
        nint pipeline = ObjC.Send(_device, ObjC.Sel("newRenderPipelineStateWithDescriptor:error:"), descriptor, (nint)(&error));
        ObjC.Release(descriptor);
        return pipeline == 0 ? throw new InvalidOperationException($"Metal couldn't build a render pipeline: {ErrorText(error)}") : Own(pipeline);
    }

    /// <summary>An off-screen RGBA8 render target the CPU can read back after a blit.</summary>
    public nint RenderTarget(int width, int height)
    {
        nint descriptor = SendTextureDescriptor(ObjC.Class("MTLTextureDescriptor"), ObjC.Sel("texture2DDescriptorWithPixelFormat:width:height:mipmapped:"),
            PixelFormatRgba8Unorm, (ulong)width, (ulong)height, false);
        ObjC.SendVoidLong(descriptor, ObjC.Sel("setUsage:"), (long)(TextureUsageRenderTarget | TextureUsageShaderRead));
        ObjC.SendVoidLong(descriptor, ObjC.Sel("setStorageMode:"), (long)StoragePrivate);
        nint texture = ObjC.Send(_device, ObjC.Sel("newTextureWithDescriptor:"), descriptor);
        return texture == 0 ? throw new InvalidOperationException("Metal couldn't create a render target.") : Own(texture);
    }

    public nint Buffer(long bytes, ulong storage = StoragePrivate)
    {
        nint buffer = SendNewBuffer(_device, ObjC.Sel("newBufferWithLength:options:"), (ulong)bytes, storage << 4);
        return buffer == 0 ? throw new InvalidOperationException($"Metal couldn't allocate {bytes / (1 << 20)} MB.") : Own(buffer);
    }

    public nint Buffer<T>(ReadOnlySpan<T> data)
        where T : unmanaged
    {
        fixed (T* p = data)
        {
            nint buffer = SendNewBufferWithBytes(_device, ObjC.Sel("newBufferWithBytes:length:options:"), p, (ulong)(data.Length * sizeof(T)), StorageShared << 4);
            return buffer == 0 ? throw new InvalidOperationException("Metal couldn't allocate a buffer.") : Own(buffer);
        }
    }

    public static nint Contents(nint buffer) => ObjC.Send(buffer, ObjC.Sel("contents"));

    /// <summary>The command buffer the next work goes into (created on first use, committed by <see cref="Finish"/>).</summary>
    public nint Commands
    {
        get
        {
            if (_commandBuffer == 0)
            {
                _commandBuffer = ObjC.Send(ObjC.Send(_queue, ObjC.Sel("commandBuffer")), ObjC.Sel("retain"));
            }

            return _commandBuffer;
        }
    }

    /// <summary>Draws one full-screen triangle into <paramref name="target"/> with <paramref name="pipeline"/>.</summary>
    public void Draw(nint pipeline, nint target, void* constants, int constantBytes)
    {
        nint pass = ObjC.Send(ObjC.Class("MTLRenderPassDescriptor"), ObjC.Sel("renderPassDescriptor"));
        nint color = SendIndex(ObjC.Send(pass, ObjC.Sel("colorAttachments")), ObjC.Sel("objectAtIndexedSubscript:"), 0);
        ObjC.SendVoid(color, ObjC.Sel("setTexture:"), target);
        ObjC.SendVoidLong(color, ObjC.Sel("setLoadAction:"), 0); // don't care: every pixel is drawn
        ObjC.SendVoidLong(color, ObjC.Sel("setStoreAction:"), 1);
        nint encoder = ObjC.Send(Commands, ObjC.Sel("renderCommandEncoderWithDescriptor:"), pass);
        ObjC.SendVoid(encoder, ObjC.Sel("setRenderPipelineState:"), pipeline);
        SendSetBytes(encoder, ObjC.Sel("setFragmentBytes:length:atIndex:"), constants, (ulong)constantBytes, 0);
        SendDraw(encoder, ObjC.Sel("drawPrimitives:vertexStart:vertexCount:"), 3, 0, 3);
        ObjC.SendVoid(encoder, ObjC.Sel("endEncoding"));
    }

    /// <summary>Starts a compute pass; <see cref="Dispatch"/> adds work to it and <see cref="EndCompute"/> closes it.</summary>
    public nint BeginCompute(nint pipeline, ReadOnlySpan<nint> buffers, void* constants, int constantBytes)
    {
        nint encoder = ObjC.Send(Commands, ObjC.Sel("computeCommandEncoder"));
        ObjC.SendVoid(encoder, ObjC.Sel("setComputePipelineState:"), pipeline);
        for (int i = 0; i < buffers.Length; i++)
        {
            SendSetBuffer(encoder, ObjC.Sel("setBuffer:offset:atIndex:"), buffers[i], 0, (ulong)i);
        }

        SendSetBytes(encoder, ObjC.Sel("setBytes:length:atIndex:"), constants, (ulong)constantBytes, (ulong)buffers.Length);
        return encoder;
    }

    public static void Dispatch(nint encoder, uint groups, uint threadsPerGroup) =>
        SendDispatch(encoder, ObjC.Sel("dispatchThreadgroups:threadsPerThreadgroup:"), new Size3(groups, 1, 1), new Size3(threadsPerGroup, 1, 1));

    public static void EndCompute(nint encoder) => ObjC.SendVoid(encoder, ObjC.Sel("endEncoding"));

    /// <summary>Copies a render target into a CPU-visible buffer (tightly packed rows).</summary>
    public void CopyToBuffer(nint texture, int width, int height, nint buffer)
    {
        nint blit = ObjC.Send(Commands, ObjC.Sel("blitCommandEncoder"));
        SendCopyTextureToBuffer(blit, ObjC.Sel("copyFromTexture:sourceSlice:sourceLevel:sourceOrigin:sourceSize:toBuffer:destinationOffset:destinationBytesPerRow:destinationBytesPerImage:"),
            texture, 0, 0, default, new Size3((nuint)width, (nuint)height, 1), buffer, 0, (ulong)width * 4, (ulong)width * 4 * (ulong)height);
        ObjC.SendVoid(blit, ObjC.Sel("endEncoding"));
    }

    /// <summary>Copies one buffer into another (private ↔ CPU-visible).</summary>
    public void CopyBuffer(nint source, nint target, long bytes)
    {
        nint blit = ObjC.Send(Commands, ObjC.Sel("blitCommandEncoder"));
        SendCopyBuffer(blit, ObjC.Sel("copyFromBuffer:sourceOffset:toBuffer:destinationOffset:size:"), source, 0, target, 0, (ulong)bytes);
        ObjC.SendVoid(blit, ObjC.Sel("endEncoding"));
    }

    /// <summary>A GPU-only buffer filled with <paramref name="data"/> (uploaded through a temporary shared buffer).</summary>
    public nint PrivateBuffer<T>(ReadOnlySpan<T> data)
        where T : unmanaged
    {
        long bytes = (long)data.Length * sizeof(T);
        nint upload = Buffer(data);
        nint buffer = Buffer(bytes);
        CopyBuffer(upload, buffer, bytes);
        Finish();
        return buffer;
    }

    /// <summary>Commits what was queued and waits for the GPU to finish it.</summary>
    public void Finish()
    {
        if (_commandBuffer == 0)
        {
            return;
        }

        nint buffer = _commandBuffer;
        _commandBuffer = 0;
        ObjC.SendVoid(buffer, ObjC.Sel("commit"));
        ObjC.SendVoid(buffer, ObjC.Sel("waitUntilCompleted"));
        ulong status = ObjC.SendULong(buffer, ObjC.Sel("status"));
        string? error = status == CommandBufferStatusError ? ErrorText(ObjC.Send(buffer, ObjC.Sel("error"))) : null;
        ObjC.Release(buffer);
        if (error is not null)
        {
            throw new InvalidOperationException($"The GPU stopped the work: {error}");
        }
    }

    /// <summary>A checksum of 32-bit words (the same formula the Windows benchmark uses).</summary>
    public static long Checksum(nint shared, int bytes)
    {
        long sum = 0;
        var words = new ReadOnlySpan<uint>((void*)Contents(shared), bytes / 4);
        foreach (uint word in words)
        {
            sum = (sum * 31) + word;
        }

        return sum;
    }

    public void Dispose()
    {
        using var pool = new ObjC.Pool();
        Finish();
        foreach (nint obj in _owned)
        {
            ObjC.Release(obj);
        }

        _owned.Clear();
        ObjC.Release(_queue);
        ObjC.Release(_device);
    }
}
