using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using Adamantium.Core;
using Adamantium.EffectsCompiler;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.EffectsFramework;
using Adamantium.Vulkan.Core;
using Serilog;

namespace Adamantium.Graphics.Effects;

/// <summary>
/// Contains rendering state for drawing with an effect; updates constant buffers and directly sets all needed resources
/// for each shader stage. The shaders are the pass data's <see cref="EffectPassShaders"/>, which every effect of that data
/// shares; how the resources reach them - the descriptor heap or descriptor sets - is up to the implementation the device
/// chose.
/// </summary>
public abstract class EffectPass : DisposableObject, IEffectPass
{
    /// <summary>
    /// The parent effect of this pass.
    /// </summary>
    public readonly Effect Effect;

    internal EffectTechnique Technique;

    private protected readonly IGraphicsDevice graphicsDevice;
    private protected EffectData.Pass pass;
    private protected EffectPassShaders shaders;
    private protected BoundPassResource[] resources = [];
    private EffectPassShaders previousShaders;
    private HashSet<EffectParameter> _reportedUnbound;

    /// <summary>
    ///   Initializes a new instance of the <see cref="EffectPass" /> class.
    /// </summary>
    /// <param name="logger">The logger used to log errors.</param>
    /// <param name="effect"> The effect. </param>
    /// <param name="technique">The technique. </param>
    /// <param name="pass"> The pass. </param>
    /// <param name="name"> The name. </param>
    private protected EffectPass(Logger logger, Effect effect, EffectTechnique technique, EffectData.Pass pass, string name)
        : base(name)
    {
        Technique = technique;
        this.pass = pass;
        Effect = effect;
        graphicsDevice = effect.GraphicsDevice;
    }

    /// <inheritdoc />
    public bool ResourcesBound { get; private set; } = true;

    private protected DynamicBufferPool CurrentBufferPool => ((GraphicsDevice)graphicsDevice).CurrentBufferPool;

    /// <summary>Applies this pass: binds each stage's shader, uploads dirty constant buffers and binds its resources.
    /// </summary>
    /// <exception cref="NotSupportedException">The pass runs a stage the device has not enabled, or ray tracing.
    /// </exception>
    /// <exception cref="InvalidOperationException">The pass has no shaders: its effect could not be made.</exception>
    public void Apply()
    {
        if (shaders == null)
        {
            throw new InvalidOperationException($"{Effect.Name}.{Name} has no shaders: its effect could not be made.");
        }

        if (shaders.UnsupportedReason != null)
        {
            throw new NotSupportedException(shaders.UnsupportedReason);
        }

        Effect.CurrentTechnique = Technique;
        graphicsDevice.CurrentEffectPass = this;
        ResourcesBound = true;

        ApplyResources();
    }

    /// <summary>Takes a recompiled declaration of this same pass. The shaders it has are held until it takes the new
    /// ones, so a version that cannot be made goes back to them as they are.</summary>
    public void Reset(EffectTechnique technique, EffectData.Pass pass, Logger logger)
    {
        if (shaders != null)
        {
            ReleasePrevious();
            previousShaders = shaders;
            shaders = null;
        }

        resources = [];
        Technique = technique;
        this.pass = pass;
    }

    /// <summary>
    /// Initializes this pass: the constant buffers and the parameters its stages declare - those of a pass the device
    /// cannot run too, though <see cref="Apply"/> then says why.
    /// </summary>
    /// <param name="logger">The logger.</param>
    public void Initialize(Logger logger)
    {
        foreach (var link in pass.Stages)
        {
            var shader = Effect.Data.Shaders[link.Index];
            foreach (var buffer in shader.ConstantBuffers)
            {
                if (buffer.Size > 0)
                {
                    Effect.DeclareConstantBuffer(buffer, logger);
                }
            }

            foreach (var declared in shader.ResourceParameters)
            {
                if (declared.Type != EffectParameterType.UniformBuffer
                    || pass.Resources.Exists(resource => resource.Name == declared.Name))
                {
                    DeclareResource(declared, logger);
                }
            }
        }
    }

    /// <summary>Takes the shaders of the pass's data, and finds each resource the parameter of this effect that holds it.
    /// </summary>
    public void PrepareData()
    {
        shaders = GetShaderCache().Acquire(Effect.Data, pass, Effect.Name, Technique?.Name, Name);
        ReleasePrevious();

        var bound = new BoundPassResource[pass.Resources.Count];
        for (var i = 0; i < bound.Length; i++)
        {
            var resource = pass.Resources[i];
            bound[i] = new BoundPassResource(resource, Effect.Parameters[resource.Name],
                resource.Type == EffectParameterType.UniformBuffer ? Effect.ConstantBuffers[resource.Name] : null);
        }

        resources = bound;
    }

    private protected abstract void ApplyResources();

    private protected void ReportUnbound(EffectParameter parameter)
    {
        ResourcesBound = false;
        if ((_reportedUnbound ??= []).Add(parameter))
        {
            UnboundResource.ReportOnce(Effect.Name, parameter?.Name);
        }
    }

    private protected void BindShaders()
    {
        if (graphicsDevice.ShadersBoundFor(shaders))
        {
            return;
        }

        ((GraphicsDevice)graphicsDevice).BindShaders(shaders.BindStages, shaders.BindShaders, shaders.InputPatchSize);
        graphicsDevice.ShadersBound(shaders);
    }

    private void DeclareResource(EffectData.ResourceParameter declared, Logger logger)
    {
        var parameter = Effect.Parameters[declared.Name];
        if (parameter == null)
        {
            Effect.DeclareResourceParameter(declared);
        }
        else if (IsRedefined(declared, (EffectData.ResourceParameter)parameter.ParameterDescription))
        {
            logger.Error("{0}.{1}: {2} is declared differently by another stage.", Effect.Name, Name, declared.Name);
        }
    }

    /// <summary>The Vulkan stage an effect's <paramref name="stage"/> runs at.</summary>
    public static ShaderStageFlagBits GetShaderStage(EffectStage stage) => stage switch
    {
        EffectStage.Vertex => ShaderStageFlagBits.VertexBit,
        EffectStage.Hull => ShaderStageFlagBits.TessellationControlBit,
        EffectStage.Domain => ShaderStageFlagBits.TessellationEvaluationBit,
        EffectStage.Pixel => ShaderStageFlagBits.FragmentBit,
        EffectStage.Task => ShaderStageFlagBits.TaskBitExt,
        EffectStage.Mesh => ShaderStageFlagBits.MeshBitExt,
        EffectStage.Compute => ShaderStageFlagBits.ComputeBit,
        EffectStage.RayGeneration => ShaderStageFlagBits.RaygenBitKhr,
        EffectStage.Miss => ShaderStageFlagBits.MissBitKhr,
        EffectStage.Callable => ShaderStageFlagBits.CallableBitKhr,
        EffectStage.ClosestHit => ShaderStageFlagBits.ClosestHitBitKhr,
        EffectStage.AnyHit => ShaderStageFlagBits.AnyHitBitKhr,
        EffectStage.Intersection => ShaderStageFlagBits.IntersectionBitKhr,
        _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, null)
    };

    private static bool IsRedefined(EffectData.ResourceParameter left, EffectData.ResourceParameter right)
    {
        return left.Class != right.Class || left.Type != right.Type || left.Count != right.Count
               || left.Access != right.Access;
    }

    protected override void Dispose(bool disposeManagedResources)
    {
        Log.Logger.Debug("Disposing EffectPass resources");
        ReleaseShaders();
        base.Dispose(disposeManagedResources);
    }

    private EffectPassShaderCache GetShaderCache() =>
        (EffectPassShaderCache)graphicsDevice.MainDevice.EffectPassShaders;

    private void ReleaseShaders()
    {
        ReleasePrevious();
        if (shaders != null)
        {
            GetShaderCache().Release(shaders);
            shaders = null;
        }

        resources = [];
    }

    private void ReleasePrevious()
    {
        if (previousShaders != null)
        {
            GetShaderCache().Release(previousShaders);
            previousShaders = null;
        }
    }

    public class DynamicBufferPool : IDisposable
    {
        private const BufferUsageFlags PageUsage = BufferUsageFlags.UniformBuffer | BufferUsageFlags.ShaderDeviceAddress;

        private readonly IGraphicsDevice graphicsDevice;
        private readonly ulong pageSize;
        private readonly List<Buffer> pages = new();
        private readonly List<Buffer> takenOversized = [];
        private readonly List<Buffer> spareOversized = [];

        private int currentPageIndex = 0;
        private ulong currentOffset = 0;
        private long generation;
        private readonly ConditionalWeakTable<EffectConstantBuffer, UploadedConstants> uploads = new();

        public DynamicBufferPool(IGraphicsDevice device, ulong pageSize = 2 * 1024 * 1024)
        {
            graphicsDevice = device;
            this.pageSize = pageSize;
            pages.Add(NewPage());
        }

        public void Reset()
        {
            currentPageIndex = 0;
            currentOffset = 0;
            generation++;
            ReleaseUnusedOversized();
        }

        /// <summary>The device address of a copy of <paramref name="buffer"/>'s data in this frame's pages: the copy made
        /// earlier in the frame while the data is still the same - a run of draws whose constants did not change, or a
        /// buffer every stage of a pass reads - else a new one.</summary>
        public ulong Upload(EffectConstantBuffer buffer, ulong alignment) => UploadCopy(buffer, alignment).Address;

        /// <summary>As <see cref="Upload"/>, but where the copy is as a page and an offset in it - what a uniform-buffer
        /// descriptor names.</summary>
        public (Buffer Page, ulong Offset) UploadRange(EffectConstantBuffer buffer, ulong alignment)
        {
            var copy = UploadCopy(buffer, alignment);
            return (copy.Page, copy.Offset);
        }

        private unsafe UploadedConstants UploadCopy(EffectConstantBuffer buffer, ulong alignment)
        {
            var backing = buffer.BackingBuffer;
            var data = new ReadOnlySpan<byte>(backing.DataPointer.ToPointer(), (int)backing.Size);
            if (uploads.TryGetValue(buffer, out var last) && last.Generation == generation && data.SequenceEqual(last.Data))
            {
                return last;
            }

            var (page, offset) = Allocate(backing.Size, alignment);
            page.CopyFrom(backing, offset);
            if (last == null)
            {
                last = new UploadedConstants();
                uploads.Add(buffer, last);
            }

            if (last.Data.Length != data.Length)
            {
                last.Data = new byte[data.Length];
            }

            data.CopyTo(last.Data);
            last.Generation = generation;
            last.Page = page;
            last.Offset = offset;
            last.Address = page.GetDeviceAddress() + offset;
            return last;
        }

        private sealed class UploadedConstants
        {
            public long Generation = -1;
            public Buffer Page;
            public ulong Offset;
            public ulong Address;
            public byte[] Data = [];
        }

        /// <summary>Room for <paramref name="size"/> bytes in this frame's pages. More than a page holds gets a buffer of
        /// its own in system memory, a power of two in size: one the last use of this pool took, of that size or twice
        /// it, is taken again; one it left unused is disposed of once the frames in flight are done with it.</summary>
        public (Buffer Page, ulong Offset) Allocate(ulong size, ulong alignment)
        {
            if (size > pageSize)
            {
                return (TakeOversized(size), 0);
            }

            ulong alignedOffset = (currentOffset + alignment - 1) & ~(alignment - 1);
            if (alignedOffset + size > pageSize)
            {
                currentPageIndex++;
                if (currentPageIndex >= pages.Count)
                {
                    pages.Add(NewPage());
                }

                alignedOffset = 0;
            }

            Buffer page = pages[currentPageIndex];
            currentOffset = alignedOffset + size;
            return (page, alignedOffset);
        }

        private Buffer NewPage() => Buffer.New(graphicsDevice, pageSize, PageUsage, BufferMemoryUsage.UploadFromCpuToGpu);

        private Buffer TakeOversized(ulong size)
        {
            var rounded = BitOperations.RoundUpToPowerOf2(size);
            var spare = -1;
            for (var i = 0; i < spareOversized.Count; i++)
            {
                var room = spareOversized[i].TotalSize;
                if ((room == rounded || room == rounded * 2) && (spare < 0 || room < spareOversized[spare].TotalSize))
                {
                    spare = i;
                }
            }

            Buffer page;
            if (spare >= 0)
            {
                page = spareOversized[spare];
                spareOversized.RemoveAt(spare);
            }
            else
            {
                page = Buffer.New(graphicsDevice, rounded, PageUsage, BufferMemoryUsage.StreamFromCpuToGpu);
            }

            takenOversized.Add(page);
            return page;
        }

        private void ReleaseUnusedOversized()
        {
            foreach (var page in spareOversized)
            {
                graphicsDevice.AddToDeferDisposeQueue(page);
            }

            spareOversized.Clear();
            spareOversized.AddRange(takenOversized);
            takenOversized.Clear();
        }

        public void Dispose()
        {
            foreach (var page in pages) page.Dispose();
            pages.Clear();
            foreach (var page in spareOversized)
            {
                page.Dispose();
            }

            foreach (var page in takenOversized)
            {
                page.Dispose();
            }

            spareOversized.Clear();
            takenOversized.Clear();
        }
    }
}
