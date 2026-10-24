using System;
using Adamantium.Imaging;

namespace Adamantium.Graphics.Core;

/// <summary>
/// How the shared memory/semaphore handles were produced. The producer (a renderer, possibly on another
/// API) picks this; the importing side maps it to the matching Vulkan external-handle type.
/// </summary>
public enum SharedHandleType
{
    /// <summary>Opaque NT handle, Windows (VK_EXTERNAL_MEMORY_HANDLE_TYPE_OPAQUE_WIN32_BIT). Also the handoff
    /// type for an OpenGL consumer/producer on Windows (GL_EXT_memory_object_win32).</summary>
    OpaqueWin32,
    /// <summary>Opaque POSIX file descriptor, Linux/macOS-via-MoltenVK (VK_EXTERNAL_MEMORY_HANDLE_TYPE_OPAQUE_FD_BIT).
    /// Also the handoff type for an OpenGL consumer/producer on those platforms (GL_EXT_memory_object_fd).</summary>
    OpaqueFd,
    /// <summary>A D3D11 texture's shared NT handle, Windows (VK_EXTERNAL_MEMORY_HANDLE_TYPE_D3D11_TEXTURE_BIT).</summary>
    D3D11Texture,
    /// <summary>A D3D12 resource's shared NT handle, Windows (VK_EXTERNAL_MEMORY_HANDLE_TYPE_D3D12_RESOURCE_BIT).</summary>
    D3D12Resource
    // Native Metal (MTLTexture) interop would need VK_EXT_metal_objects, which the binding does not currently
    // generate; on macOS use OpaqueFd (MoltenVK) until that extension is added to the generator.
}

/// <summary>Describes an externally produced surface - exported memory and semaphores - so a consumer such as
/// <c>RenderTargetPanel</c> can import it zero-copy.</summary>
public sealed class SharedSurfaceDescriptor
{
    private long _submitted;

    /// <summary>The kind of handle in <see cref="MemoryHandle"/> (decides the Vulkan handle type on import).</summary>
    public SharedHandleType HandleType { get; set; } = SharedHandleType.OpaqueWin32;

    /// <summary>Shared handle to the surface's device memory, exported by the producer (NT handle or POSIX fd).</summary>
    public IntPtr MemoryHandle { get; set; }

    /// <summary>Size in bytes of the exported allocation (used to validate the import against local requirements).</summary>
    public ulong AllocationSize { get; set; }

    public uint Width { get; set; }
    public uint Height { get; set; }
    public SurfaceFormat Format { get; set; }

    /// <summary>Producer→consumer semaphore (signaled when a frame is ready); imported and waited on before sampling.</summary>
    public IntPtr ProduceSemaphoreHandle { get; set; }

    /// <summary>Consumer→producer semaphore (signaled after sampling); lets the producer reuse the surface.</summary>
    public IntPtr ConsumeSemaphoreHandle { get; set; }

    /// <summary>Number of surfaces the producer rotates through (1 = single-buffered).</summary>
    public uint BufferCount { get; set; } = 1;

    /// <summary>The surfaces the producer rotates through besides the one this descriptor names; empty for one. Each
    /// frame is numbered across all of them, so the consumer takes the one whose produce timeline is furthest.</summary>
    public SharedSurfaceDescriptor[] Others { get; set; } = [];

    /// <summary>Raised each time the consumer asks for a frame; a producer in the same process paces by it.</summary>
    public SharedSurfaceDemand Demand { get; } = new();

    /// <summary>The newest frame a producer in the same process has submitted, finished or not; 0 from a producer in
    /// another process. A consumer tells by it that a frame is on its way.</summary>
    public ulong Submitted => (ulong)System.Threading.Interlocked.Read(ref _submitted);

    /// <summary>For a producer in the same process: frame <paramref name="frame"/> is submitted.</summary>
    public void NoteSubmitted(ulong frame) => System.Threading.Interlocked.Exchange(ref _submitted, (long)frame);
}
