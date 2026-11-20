using Adamantium.Core;
using Adamantium.Graphics.Core;
using Adamantium.Imaging;
using Adamantium.Vulkan.Core;
using Adamantium.Win32;

namespace Adamantium.Multiverse.Remote;

/// <summary>The ring of surfaces a universe host draws into, as its viewer takes it: their size, format, handle kind
/// and each surface's handles, valid in the viewer's process and its to close.</summary>
public sealed class RemoteSurface
{
    public uint Width { get; init; }

    public uint Height { get; init; }

    /// <summary>The surfaces' pixel format, as its value: <see cref="SurfaceFormat"/> is made from it.</summary>
    public Format Format { get; init; }

    public SharedHandleType HandleType { get; init; }

    public RemoteSurfaceBuffer[] Buffers { get; init; } = [];

    /// <summary>The ring as a <see cref="SharedSurfaceDescriptor"/> to import; its handles stay this ring's.</summary>
    public SharedSurfaceDescriptor ToDescriptor()
    {
        if (Buffers.Length == 0)
        {
            throw new InvalidOperationException("A remote surface has at least one buffer.");
        }

        var first = Describe(Buffers[0]);
        first.BufferCount = (uint)Buffers.Length;
        var others = new SharedSurfaceDescriptor[Buffers.Length - 1];
        for (var i = 1; i < Buffers.Length; i++)
        {
            others[i - 1] = Describe(Buffers[i]);
        }

        first.Others = others;
        return first;
    }

    /// <summary>Closes the handles of the viewer's process, once what imported them has: the import keeps its own.</summary>
    public void CloseHandles()
    {
        foreach (var buffer in Buffers)
        {
            Close(buffer.MemoryHandle);
            Close(buffer.ProduceSemaphoreHandle);
            Close(buffer.ConsumeSemaphoreHandle);
        }
    }

    private SharedSurfaceDescriptor Describe(RemoteSurfaceBuffer buffer) => new()
    {
        HandleType = HandleType,
        MemoryHandle = (IntPtr)buffer.MemoryHandle,
        AllocationSize = buffer.AllocationSize,
        Width = Width,
        Height = Height,
        Format = Format,
        ProduceSemaphoreHandle = (IntPtr)buffer.ProduceSemaphoreHandle,
        ConsumeSemaphoreHandle = (IntPtr)buffer.ConsumeSemaphoreHandle
    };

    private void Close(long handle)
    {
        if (handle == 0)
        {
            return;
        }

        if (HandleType == SharedHandleType.OpaqueFd)
        {
            PosixInterop.Close((int)handle);
        }
        else
        {
            Win32Interop.CloseHandle((IntPtr)handle);
        }
    }
}
