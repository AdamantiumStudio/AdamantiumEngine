using System;
using System.Runtime.InteropServices;
using Adamantium.Core;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Extensions;
using Adamantium.Imaging;
using Adamantium.Win32;
using Adamantium.Vulkan.Core;
using Adamantium.Vulkan.Windows;
using VulkanImage = Adamantium.Vulkan.Core.Image;

namespace Adamantium.Graphics;

/// <summary>A GPU surface shared across processes and APIs through an OS handle: <see cref="CreateExportable"/> exports one,
/// <see cref="Import"/> binds a producer's zero-copy. Owners dispose it explicitly.</summary>
public sealed unsafe class SharedSurface : Texture
{
    private static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    private Semaphore _produceSemaphore;   // signaled by the producer when a frame is ready; consumer waits on it
    private Semaphore _consumeSemaphore;   // signaled by the consumer after sampling; producer waits before reuse

    // The exporter owns the handles it created and must close them; the importer borrows the producer's.
    private bool _ownsHandles;
    // Transport for this surface's handles: POSIX fd vs Win32 NT handle. Chosen from the handle TYPE (the producer
    // decides), not the consumer's OS — an OpaqueFd producer can exist on Windows, a D3D/Win32 producer is an NT handle.
    private bool _useFd;
    private IntPtr _memoryHandle;
    private IntPtr _produceHandle;
    private IntPtr _consumeHandle;
    private ulong _latched;
    private ulong _consumeSignaled;
    private Texture _copy;
    private SharedSurface[] _others = [];

    public SharedSurfaceDescriptor Descriptor { get; private set; }

    /// <summary>Producer→consumer semaphore (frame ready). Wait on this before sampling the surface.</summary>
    public Semaphore ProduceSemaphore => _produceSemaphore;

    /// <summary>Consumer→producer semaphore (sampling done). Signal this after sampling so the producer can reuse.</summary>
    public Semaphore ConsumeSemaphore => _consumeSemaphore;

    /// <summary>Current value of the produce timeline (last frame the producer finished writing).</summary>
    public ulong ProduceValue => GetCounter(_produceSemaphore);

    /// <summary>Current value of the consume timeline (last frame the consumer finished latching).</summary>
    public ulong ConsumeValue => GetCounter(_consumeSemaphore);

    /// <summary>How many surfaces the producer rotates through: this one and <see cref="SurfaceAt"/> the others.</summary>
    public int BufferCount => 1 + _others.Length;

    /// <summary>One of the surfaces the producer rotates through; 0 is this one. Each has its own produce and consume
    /// timelines, numbered by the frames written to it.</summary>
    public SharedSurface SurfaceAt(int index) => index == 0 ? this : _others[index - 1];

    /// <summary>
    /// For the consumer: the newest frame the producer has finished in any of its surfaces, handed out once - 0 when
    /// nothing is new since the last call, whoever made it.
    /// </summary>
    public ulong LatchNewFrame() => LatchNewest(out _);

    private ulong LatchNewest(out SharedSurface source)
    {
        source = null;
        var newest = 0ul;
        for (var i = 0; i < BufferCount; i++)
        {
            var surface = SurfaceAt(i);
            var produced = surface.ProduceValue;
            if (produced > newest)
            {
                newest = produced;
                source = surface;
            }
        }

        if (newest <= _latched)
        {
            source = null;
            return 0;
        }

        _latched = newest;
        return newest;
    }

    /// <summary>For the producer: waits until the consumer has finished with frame <paramref name="value"/>, up to
    /// <paramref name="timeout"/>. False when it ran out first.</summary>
    public bool WaitConsumed(ulong value, TimeSpan timeout)
    {
        if (_consumeSemaphore == null || value == 0) return true;

        var wait = new SemaphoreWaitInfo
        {
            SemaphoreCount = 1,
            PSemaphores = new[] { _consumeSemaphore },
            PValues = new[] { value }
        };
        return GraphicsDevice.LogicalDevice.WaitSemaphores(wait, (ulong)timeout.Ticks * 100) == Result.Success;
    }

    /// <summary>The consumer's copy of the last frame latched by <see cref="LatchCopy"/>, sampled in place of the surface
    /// so the producer may write its next frame meanwhile; <c>null</c> before the first latch.</summary>
    public ITexture LatchedCopy => _copy;

    /// <summary>
    /// For the consumer, outside a render pass: when the producer has finished a frame newer than <see cref="LatchedCopy"/>
    /// in any of its surfaces, copies the newest into that texture on <paramref name="device"/>, submitted on its own ahead
    /// of the frame. The copy waits for the frame on its surface's produce timeline; once it is done every surface is given
    /// back on its consume timeline - the one copied, and the others holding older frames nobody will read - so the
    /// producer writes again right after it, not once the consumer's whole frame has run. One import per surface: a second
    /// one would copy a frame the first released to the producer. Each call asks for the next frame through
    /// <see cref="SharedSurfaceDescriptor.Demand"/>.
    /// </summary>
    public void LatchCopy(IGraphicsDevice device)
    {
        Descriptor?.Demand.Raise();
        var frame = LatchNewest(out var source);
        if (frame == 0)
        {
            return;
        }

        var description = BuildDescription(Width, Height, Description.Format);
        description.Usage = ImageUsageFlagBits.SampledBit | ImageUsageFlagBits.TransferDstBit;
        _copy ??= New(device, description, "SharedSurface(copy)");

        var released = new System.Collections.Generic.List<(Semaphore, ulong)>(BufferCount);
        for (var i = 0; i < BufferCount; i++)
        {
            var surface = SurfaceAt(i);
            var produced = ReferenceEquals(surface, source) ? frame : surface.ProduceValue;
            var through = produced <= frame ? produced : 0;
            if (through > surface._consumeSignaled && surface._consumeSemaphore != null)
            {
                surface._consumeSignaled = through;
                released.Add((surface._consumeSemaphore, through));
            }
        }

        var copy = _copy;
        device.SubmitAside(commandBuffer => source.RecordCopy(device, commandBuffer, copy),
            source._produceSemaphore, PipelineStageFlagBits2.AllTransferBit, frame, released);
    }

    // Both images rest in ShaderReadOnly: the producer leaves the surface there, and the copy is only ever sampled.
    private void RecordCopy(IGraphicsDevice device, CommandBuffer commandBuffer, ITexture copy)
    {
        device.InsertImageMemoryBarrier(commandBuffer, this,
            AccessFlagBits2.None, AccessFlagBits2.TransferReadBit,
            ImageLayout.ShaderReadOnlyOptimal, ImageLayout.TransferSrcOptimal,
            PipelineStageFlagBits2.AllTransferBit, PipelineStageFlagBits2.AllTransferBit);
        device.InsertImageMemoryBarrier(commandBuffer, copy,
            AccessFlagBits2.ShaderReadBit, AccessFlagBits2.TransferWriteBit,
            ImageLayout.ShaderReadOnlyOptimal, ImageLayout.TransferDstOptimal,
            PipelineStageFlagBits2.FragmentShaderBit, PipelineStageFlagBits2.AllTransferBit);

        var region = new ImageCopy
        {
            Extent = new Extent3D { Width = Width, Height = Height, Depth = 1 },
            SrcOffset = new Offset3D(),
            DstOffset = new Offset3D(),
            SrcSubresource = new ImageSubresourceLayers { AspectMask = ImageAspectFlagBits.ColorBit, LayerCount = 1 },
            DstSubresource = new ImageSubresourceLayers { AspectMask = ImageAspectFlagBits.ColorBit, LayerCount = 1 }
        };
        commandBuffer.CopyImage(this.GetImage(), ImageLayout.TransferSrcOptimal,
            copy.GetImage(), ImageLayout.TransferDstOptimal, 1, region);

        device.InsertImageMemoryBarrier(commandBuffer, this,
            AccessFlagBits2.TransferReadBit, AccessFlagBits2.None,
            ImageLayout.TransferSrcOptimal, ImageLayout.ShaderReadOnlyOptimal,
            PipelineStageFlagBits2.AllTransferBit, PipelineStageFlagBits2.AllTransferBit);
        device.InsertImageMemoryBarrier(commandBuffer, copy,
            AccessFlagBits2.TransferWriteBit, AccessFlagBits2.ShaderReadBit,
            ImageLayout.TransferDstOptimal, ImageLayout.ShaderReadOnlyOptimal,
            PipelineStageFlagBits2.AllTransferBit, PipelineStageFlagBits2.FragmentShaderBit);
    }

    private ulong GetCounter(Semaphore semaphore)
    {
        if (semaphore == null) return 0;
        GraphicsDevice.LogicalDevice.GetSemaphoreCounterValue(semaphore, out var value);
        return value;
    }

    private SharedSurface(IGraphicsDevice device, TextureDescription description, object imagePNext,
        Func<VulkanImage, object> allocPNextFactory, string name)
        : base(device, description, imagePNext, allocPNextFactory, name)
    {
    }

    /// <summary>
    /// Allocates an exportable surface: a dedicated image whose memory and semaphores can be shared with another
    /// process/API. The returned <see cref="Descriptor"/> carries the OS handles to hand off to the consumer.
    /// <paramref name="handleType"/> defaults to the platform's opaque type (Win32 handle / POSIX fd). With
    /// <paramref name="bufferCount"/> above one the producer rotates through that many surfaces, writing one while the
    /// consumer copies another (see <see cref="SurfaceAt"/>); the descriptor carries the others in
    /// <see cref="SharedSurfaceDescriptor.Others"/>.
    /// </summary>
    public static SharedSurface CreateExportable(IGraphicsDevice device, uint width, uint height, SurfaceFormat format,
        SharedHandleType? handleType = null, string name = "SharedSurface(export)", uint bufferCount = 1)
    {
        var surface = CreateExportableSingle(device, width, height, format, handleType, name);
        if (bufferCount <= 1) return surface;

        surface._others = new SharedSurface[bufferCount - 1];
        var others = new SharedSurfaceDescriptor[bufferCount - 1];
        for (var i = 0; i < others.Length; i++)
        {
            surface._others[i] = CreateExportableSingle(device, width, height, format, handleType, name);
            others[i] = surface._others[i].Descriptor;
        }

        surface.Descriptor.Others = others;
        surface.Descriptor.BufferCount = bufferCount;
        return surface;
    }

    private static SharedSurface CreateExportableSingle(IGraphicsDevice device, uint width, uint height, SurfaceFormat format,
        SharedHandleType? handleType, string name)
    {
        var resolvedType = handleType ?? DefaultHandleType;
        var memHandleType = ToMemoryHandleType(resolvedType);
        var description = BuildDescription(width, height, format);

        var externalInfo = new ExternalMemoryImageCreateInfo { HandleTypes = memHandleType };
        // Exporting an image's memory requires a dedicated allocation that names the image.
        Func<VulkanImage, object> allocFactory = image => new ExportMemoryAllocateInfo
        {
            HandleTypes = memHandleType,
            PNext = new MemoryDedicatedAllocateInfo { Image = image }
        };

        var surface = new SharedSurface(device, description, externalInfo, allocFactory, name);
        surface._useFd = IsFdHandle(resolvedType);
        // TODO: producer/consumer ownership transfer (VK_QUEUE_FAMILY_EXTERNAL) on the shared queue.
        surface.TransitionImageLayout(description.DesiredImageLayout);

        var logical = surface.GraphicsDevice.LogicalDevice;
        surface._memoryHandle = surface.ExportMemoryHandle(memHandleType);
        logical.GetImageMemoryRequirements(surface.VulkanImage, out var req);

        surface._produceHandle = surface.CreateExportableSemaphore(out surface._produceSemaphore);
        surface._consumeHandle = surface.CreateExportableSemaphore(out surface._consumeSemaphore);
        surface._ownsHandles = true;

        surface.Descriptor = new SharedSurfaceDescriptor
        {
            HandleType = resolvedType,
            MemoryHandle = surface._memoryHandle,
            AllocationSize = req.Size,
            Width = width,
            Height = height,
            Format = format,
            ProduceSemaphoreHandle = surface._produceHandle,
            ConsumeSemaphoreHandle = surface._consumeHandle,
            BufferCount = 1
        };
        return surface;
    }

    /// <summary>
    /// Imports an externally produced surface zero-copy: allocates a local image bound to the producer's shared
    /// memory and imports its produce/consume semaphores. The image samples the producer's frames directly.
    /// </summary>
    public static SharedSurface Import(IGraphicsDevice device, SharedSurfaceDescriptor descriptor,
        string name = "SharedSurface(import)")
    {
        var surface = ImportSingle(device, descriptor, name);
        if (descriptor.Others is { Length: > 0 } others)
        {
            surface._others = new SharedSurface[others.Length];
            for (var i = 0; i < others.Length; i++)
            {
                surface._others[i] = ImportSingle(device, others[i], name);
            }
        }

        return surface;
    }

    private static SharedSurface ImportSingle(IGraphicsDevice device, SharedSurfaceDescriptor descriptor, string name)
    {
        var memHandleType = ToMemoryHandleType(descriptor.HandleType);
        var useFd = IsFdHandle(descriptor.HandleType);
        var description = BuildDescription(descriptor.Width, descriptor.Height, descriptor.Format);

        var externalInfo = new ExternalMemoryImageCreateInfo { HandleTypes = memHandleType };
        // Import the producer's memory in place of a normal allocation; still a dedicated allocation for the image.
        // The transport (fd vs Win32 handle) follows the producer's handle type, not our OS.
        Func<VulkanImage, object> allocFactory = image => useFd
            ? (object)new ImportMemoryFdInfoKHR
            {
                HandleType = memHandleType,
                Fd = (int)descriptor.MemoryHandle,
                PNext = new MemoryDedicatedAllocateInfo { Image = image }
            }
            : new ImportMemoryWin32HandleInfoKHR
            {
                HandleType = memHandleType,
                Handle = (nuint)descriptor.MemoryHandle,
                PNext = new MemoryDedicatedAllocateInfo { Image = image }
            };

        var surface = new SharedSurface(device, description, externalInfo, allocFactory, name);
        surface._useFd = useFd;
        surface.AcquireFromProducer();

        if (descriptor.ProduceSemaphoreHandle != IntPtr.Zero)
        {
            surface._produceSemaphore = surface.ImportSemaphore(descriptor.ProduceSemaphoreHandle);
        }
        if (descriptor.ConsumeSemaphoreHandle != IntPtr.Zero)
        {
            surface._consumeSemaphore = surface.ImportSemaphore(descriptor.ConsumeSemaphoreHandle);
        }

        surface._ownsHandles = false; // the producer owns and closes the handles
        surface.Descriptor = descriptor;
        return surface;
    }

    // VK_QUEUE_FAMILY_EXTERNAL: the producer owns the memory as it hands it over.
    private const uint QueueFamilyExternal = ~1u;

    // Taken over from the producer in the layout it keeps the surface in, not moved out of Undefined: the producer may
    // already have written a frame, and a transition from Undefined may discard it.
    private void AcquireFromProducer()
    {
        var device = GraphicsDevice;
        var family = device.MainDevice.QueueFamilyContainer.GetFamilyInfo(QueueFlagBits.GraphicsBit).FamilyIndex;
        var barrier = new ImageMemoryBarrier2
        {
            SrcStageMask = PipelineStageFlagBits2.TopOfPipeBit,
            DstStageMask = PipelineStageFlagBits2.AllTransferBit | PipelineStageFlagBits2.FragmentShaderBit,
            SrcAccessMask = AccessFlagBits2.None,
            DstAccessMask = AccessFlagBits2.TransferReadBit | AccessFlagBits2.ShaderReadBit,
            OldLayout = ImageLayout.ShaderReadOnlyOptimal,
            NewLayout = ImageLayout.ShaderReadOnlyOptimal,
            SrcQueueFamilyIndex = QueueFamilyExternal,
            DstQueueFamilyIndex = family,
            Image = this.GetImage(),
            SubresourceRange = new ImageSubresourceRange
            {
                AspectMask = ImageAspectFlagBits.ColorBit,
                LevelCount = 1,
                LayerCount = 1
            }
        };

        var commandBuffer = device.BeginSingleTimeCommand();
        commandBuffer.PipelineBarrier2(new DependencyInfo
        {
            ImageMemoryBarrierCount = 1,
            PImageMemoryBarriers = new[] { barrier }
        });
        device.EndSingleTimeCommand(commandBuffer);
        ImageLayout = ImageLayout.ShaderReadOnlyOptimal;
    }

    private IntPtr ExportMemoryHandle(ExternalMemoryHandleTypeFlagBits memHandleType)
    {
        var logical = GraphicsDevice.LogicalDevice;
        Result res;
        if (!_useFd)
        {
            var getInfo = new MemoryGetWin32HandleInfoKHR { Memory = ImageMemory, HandleType = memHandleType };
            res = logical.GetMemoryWin32HandleKHR(getInfo, out var handle);
            if (res != Result.Success) { Dispose(); throw new Exception($"failed to export memory win32 handle: {res}"); }
            return (nint)handle;
        }
        var getFdInfo = new MemoryGetFdInfoKHR { Memory = ImageMemory, HandleType = memHandleType };
        res = logical.GetMemoryFdKHR(getFdInfo, out var fd);
        if (res != Result.Success) { Dispose(); throw new Exception($"failed to export memory fd: {res}"); }
        return (nint)fd;
    }

    private IntPtr CreateExportableSemaphore(out Semaphore semaphore)
    {
        var logical = GraphicsDevice.LogicalDevice;
        var semHandleType = _useFd
            ? ExternalSemaphoreHandleTypeFlagBits.OpaqueFdBit
            : ExternalSemaphoreHandleTypeFlagBits.OpaqueWin32Bit;

        var info = new SemaphoreCreateInfo
        {
            // Timeline (monotonic) so producer/consumer can poll progress and order across queues without binary
            // ping-pong deadlocks. Exportable like a binary one (verified working + importable on this GPU).
            PNext = new ExportSemaphoreCreateInfo
            {
                HandleTypes = semHandleType,
                PNext = new SemaphoreTypeCreateInfo { SemaphoreType = SemaphoreType.Timeline, InitialValue = 0 }
            }
        };
        semaphore = logical.CreateSemaphore(info);

        Result res;
        if (!_useFd)
        {
            var getInfo = new SemaphoreGetWin32HandleInfoKHR { Semaphore = semaphore, HandleType = semHandleType };
            res = logical.GetSemaphoreWin32HandleKHR(getInfo, out var handle);
            if (res != Result.Success) throw new Exception($"failed to export semaphore win32 handle: {res}");
            return (nint)handle;
        }
        var getFdInfo = new SemaphoreGetFdInfoKHR { Semaphore = semaphore, HandleType = semHandleType };
        res = logical.GetSemaphoreFdKHR(getFdInfo, out var fd);
        if (res != Result.Success) throw new Exception($"failed to export semaphore fd: {res}");
        return (nint)fd;
    }

    private Semaphore ImportSemaphore(IntPtr handle)
    {
        var logical = GraphicsDevice.LogicalDevice;
        // Created as timeline before importing the producer's timeline payload.
        var semaphore = logical.CreateSemaphore(new SemaphoreCreateInfo
        {
            PNext = new SemaphoreTypeCreateInfo { SemaphoreType = SemaphoreType.Timeline, InitialValue = 0 }
        });
        Result res;
        if (!_useFd)
        {
            res = logical.ImportSemaphoreWin32HandleKHR(new ImportSemaphoreWin32HandleInfoKHR
            {
                Semaphore = semaphore,
                HandleType = ExternalSemaphoreHandleTypeFlagBits.OpaqueWin32Bit,
                Handle = (nuint)handle
            });
        }
        else
        {
            res = logical.ImportSemaphoreFdKHR(new ImportSemaphoreFdInfoKHR
            {
                Semaphore = semaphore,
                HandleType = ExternalSemaphoreHandleTypeFlagBits.OpaqueFdBit,
                Fd = (int)handle
            });
        }
        if (res != Result.Success)
        {
            logical.DestroySemaphore(semaphore);
            throw new Exception($"failed to import semaphore handle: {res}");
        }
        return semaphore;
    }

    private static SharedHandleType DefaultHandleType => IsWindows ? SharedHandleType.OpaqueWin32 : SharedHandleType.OpaqueFd;

    // Only the opaque-fd type uses the POSIX fd transport; Win32/D3D11/D3D12 are all NT-handle (Win32) transports.
    private static bool IsFdHandle(SharedHandleType type) => type == SharedHandleType.OpaqueFd;

    private static ExternalMemoryHandleTypeFlagBits ToMemoryHandleType(SharedHandleType type) => type switch
    {
        SharedHandleType.OpaqueWin32 => ExternalMemoryHandleTypeFlagBits.OpaqueWin32Bit,
        SharedHandleType.OpaqueFd => ExternalMemoryHandleTypeFlagBits.OpaqueFdBit,
        SharedHandleType.D3D11Texture => ExternalMemoryHandleTypeFlagBits.D3d11TextureBit,
        SharedHandleType.D3D12Resource => ExternalMemoryHandleTypeFlagBits.D3d12ResourceBit,
        _ => ExternalMemoryHandleTypeFlagBits.OpaqueWin32Bit
    };

    private static TextureDescription BuildDescription(uint width, uint height, SurfaceFormat format) => new()
    {
        Dimension = TextureDimension.Texture2D,
        ImageType = ImageType._2d,
        Width = width,
        Height = height,
        Depth = 1,
        ArrayLayers = 1,
        MipLevels = 1,
        Format = format,
        Samples = MSAALevel.None,
        // Producer copies its resolved frame INTO this surface (TransferDst, then transitions it to ShaderReadOnly);
        // consumer copies it out (TransferSrc, see LatchCopy) or samples it. Producer and consumer are different queues
        // of the SAME graphics family, so a semaphore (the Produce/Consume timeline) is enough to order the layout
        // transition + write vs the read (no queue-family ownership transfer needed).
        Usage = ImageUsageFlagBits.SampledBit | ImageUsageFlagBits.TransferDstBit | ImageUsageFlagBits.TransferSrcBit,
        SharingMode = SharingMode.Exclusive,
        ImageTiling = ImageTiling.Optimal,
        InitialLayout = ImageLayout.Undefined,
        DesiredImageLayout = ImageLayout.ShaderReadOnlyOptimal,
        ImageAspect = ImageAspectFlagBits.ColorBit
    };

    private void CloseSharedHandle(IntPtr handle)
    {
        if (handle == IntPtr.Zero) return;
        // POSIX fd vs Win32 NT handle, per this surface's transport. The unused platform's library is never resolved.
        if (_useFd) PosixInterop.Close((int)handle);
        else Win32Interop.CloseHandle(handle);
    }

    protected override void Dispose(bool disposeManaged)
    {
        foreach (var other in _others)
        {
            other.Dispose();
        }

        _others = [];
        _copy?.Dispose();
        _copy = null;
        var logical = GraphicsDevice.LogicalDevice;
        if (_produceSemaphore != null) { logical.DestroySemaphore(_produceSemaphore); _produceSemaphore = null; }
        if (_consumeSemaphore != null) { logical.DestroySemaphore(_consumeSemaphore); _consumeSemaphore = null; }

        // Close the handles we exported; the importer borrows the producer's and must not close them. Null each
        // out after closing so a second pass can never double-close (the zero check would otherwise re-enter).
        if (_ownsHandles)
        {
            CloseSharedHandle(_memoryHandle); _memoryHandle = IntPtr.Zero;
            CloseSharedHandle(_produceHandle); _produceHandle = IntPtr.Zero;
            CloseSharedHandle(_consumeHandle); _consumeHandle = IntPtr.Zero;
        }

        // Frees the image, view and (imported or exported) memory.
        base.Dispose(disposeManaged);
    }
}
