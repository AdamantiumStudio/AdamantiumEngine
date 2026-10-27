using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Adamantium.Core;
using Adamantium.EffectsCompiler;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.EffectsFramework;
using Adamantium.Vulkan.Core;
using Serilog;

namespace Adamantium.Graphics.Effects;

/// <summary>
/// Contains rendering state for drawing with an effect; updates constant buffers and directly sets all needed resources
/// for each shader stage. How the resources reach the shaders - the descriptor heap or descriptor sets - is up to the
/// implementation the device chose.
/// </summary>
public abstract class EffectPass : DisposableObject, IEffectPass
{
    /// <summary>
    /// The parent effect of this pass.
    /// </summary>
    public readonly Effect Effect;

    private EffectData.Pass pass;
    private protected readonly IGraphicsDevice graphicsDevice;

    private protected readonly List<StageBlock> pipelineStages;

    internal EffectTechnique Technique;

    private protected readonly List<StageBlock> stages = new List<StageBlock>();

    private protected DynamicBufferPool CurrentBufferPool => ((GraphicsDevice)graphicsDevice).CurrentBufferPool;

    private NotSupportedException unsupported;

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
        pipelineStages = new List<StageBlock>();
    }

    /// <summary>Applies this pass: binds each stage's shader, uploads dirty constant buffers and binds its resources.
    /// </summary>
    /// <exception cref="NotSupportedException">The pass runs a stage the device has not enabled, or ray tracing.
    /// </exception>
    public void Apply()
    {
        if (unsupported != null)
        {
            throw unsupported;
        }

        Effect.CurrentTechnique = Technique;
        graphicsDevice.CurrentEffectPass = this;
        ResourcesBound = true;
        stages.Clear();
        stages.AddRange(pipelineStages);

        ApplyResources();
    }

    private protected abstract void ApplyResources();

    /// <inheritdoc />
    public bool ResourcesBound { get; private set; } = true;

    private HashSet<EffectParameter> _reportedUnbound;

    private protected void ReportUnbound(EffectParameter parameter)
    {
        ResourcesBound = false;
        if ((_reportedUnbound ??= []).Add(parameter))
            UnboundResource.ReportOnce(Effect.Name, parameter?.Name);
    }

    private ShaderStageFlagBits[] _bindStages;
    private ShaderEXT[] _bindShaders;

    private protected void BindShaders()
    {
        if (graphicsDevice.ShadersBoundFor(this)) return;

        BindAllStages();
        graphicsDevice.ShadersBound(this);
    }

    private void BindAllStages()
    {
        var present = (ShaderStageFlagBits)0;
        var inputPatchSize = 0u;
        foreach (var stage in stages)
        {
            present |= stage.Stage;
            if (stage.Type == EffectStage.Hull)
            {
                inputPatchSize = stage.Shader.InputPatchSize;
            }
        }

        var empty = GetEmptyStages(present);
        var count = stages.Count + BitOperations.PopCount((uint)empty);
        if (_bindStages == null || _bindStages.Length != count)
        {
            _bindStages = new ShaderStageFlagBits[count];
            _bindShaders = new ShaderEXT[count];
        }

        for (var i = 0; i < stages.Count; i++)
        {
            _bindStages[i] = stages[i].Stage;
            _bindShaders[i] = stages[i].ShaderObject;
        }

        var next = stages.Count;
        for (var bit = 1u; bit != 0 && next < count; bit <<= 1)
        {
            if (((uint)empty & bit) != 0)
            {
                _bindStages[next] = (ShaderStageFlagBits)bit;
                _bindShaders[next] = null;
                next++;
            }
        }

        ((GraphicsDevice)graphicsDevice).BindShaders(_bindStages, _bindShaders, inputPatchSize);
    }

    /// <summary>The stages a pass of <paramref name="present"/> stages binds empty: a draw takes a shader, or none, at
    /// every graphics stage the device has enabled - the pixel stage always, hull and domain with tessellation, task
    /// and mesh with mesh shaders, the vertex stage under a mesh shader. A compute pass binds only its own.</summary>
    private ShaderStageFlagBits GetEmptyStages(ShaderStageFlagBits present)
    {
        if ((present & ShaderStageFlagBits.ComputeBit) != 0)
        {
            return 0;
        }

        var device = graphicsDevice.MainDevice;
        var empty = ShaderStageFlagBits.VertexBit | ShaderStageFlagBits.FragmentBit;
        if (device.IsTessellationEnabled)
        {
            empty |= ShaderStageFlagBits.TessellationControlBit | ShaderStageFlagBits.TessellationEvaluationBit;
        }

        if (device.IsMeshShaderEnabled)
        {
            empty |= ShaderStageFlagBits.MeshBitExt;
        }

        if (device.IsTaskShaderEnabled)
        {
            empty |= ShaderStageFlagBits.TaskBitExt;
        }

        return empty & ~present;
    }

    /// <inheritdoc />
    public void Reset(EffectTechnique technique, EffectData.Pass pass, Logger logger)
    {
        RetireStages();
        ReleaseResourceLayout();
        stages.Clear();
        Technique = technique;
        this.pass = pass;
        if (graphicsDevice.ShadersBoundFor(this))
        {
            graphicsDevice.ShadersBound(null);
        }
    }

    /// <summary>
    /// Initializes this pass: its stages in pipeline order, whatever order the file names them in, and the parameters
    /// they declare - those of a pass the device cannot run too, though it keeps no stage and <see cref="Apply"/> says
    /// why.
    /// </summary>
    /// <param name="logger">The logger.</param>
    public void Initialize(Logger logger)
    {
        unsupported = pass.HitGroups.Count > 0 ? CreateRayTracingError() : null;
        foreach (var link in pass.Stages.OrderBy(link => GetPipelineOrder(link.Stage)))
        {
            var shader = Effect.Data.Shaders[link.Index];
            unsupported ??= FindMissingFeature(link.Stage) ?? FindUnboundResource(shader);
            var stageBlock = new StageBlock(graphicsDevice, link.Stage, shader);
            pipelineStages.Add(stageBlock);

            stageBlock.CacheName = $"{Effect?.Name}.{Technique?.Name}.{Name}.{link.Stage}";

            DeclareStage(stageBlock, logger);
        }

        if (unsupported != null)
        {
            pipelineStages.Clear();
        }
    }

    private static int GetPipelineOrder(EffectStage stage) => stage switch
    {
        EffectStage.Vertex => 0,
        EffectStage.Hull => 1,
        EffectStage.Domain => 2,
        EffectStage.Task => 3,
        EffectStage.Mesh => 4,
        EffectStage.Pixel => 5,
        _ => 6
    };

    private NotSupportedException FindMissingFeature(EffectStage stage)
    {
        var device = graphicsDevice.MainDevice;
        return stage switch
        {
            EffectStage.Hull or EffectStage.Domain when !device.IsTessellationEnabled =>
                CreateMissingFeatureError($"{stage} shaders", "the tessellationShader feature"),
            EffectStage.Mesh when !device.IsMeshShaderEnabled =>
                CreateMissingFeatureError("mesh shaders", "VK_EXT_mesh_shader and its meshShader feature"),
            EffectStage.Task when !device.IsTaskShaderEnabled =>
                CreateMissingFeatureError("task shaders", "VK_EXT_mesh_shader and its taskShader feature"),
            EffectStage.RayGeneration or EffectStage.Miss or EffectStage.Callable or EffectStage.ClosestHit
                or EffectStage.AnyHit or EffectStage.Intersection => CreateRayTracingError(),
            _ => null
        };
    }

    private NotSupportedException FindUnboundResource(EffectData.Shader shader)
    {
        foreach (var resource in shader.ResourceParameters)
        {
            if (!IsBoundByEffects(resource.Type))
            {
                return new NotSupportedException($"{Effect.Name}.{Name}: '{resource.Name}' is a {resource.Type}; " +
                                                 "effects bind uniform and storage buffers, sampled images and samplers.");
            }
        }

        return null;
    }

    private static bool IsBoundByEffects(EffectParameterType type) => type is EffectParameterType.UniformBuffer
        or EffectParameterType.StorageBuffer or EffectParameterType.Sampler or EffectParameterType.SampledImage1D
        or EffectParameterType.SampledImage2D or EffectParameterType.SampledImage3D or EffectParameterType.SampledImageCube;

    private NotSupportedException CreateMissingFeatureError(string shaders, string feature) =>
        new($"{Effect.Name}.{Name}: {shaders} need {feature}, which " +
            $"{graphicsDevice.Adapter.AdapterProperties.DeviceName} does not have.");

    private NotSupportedException CreateRayTracingError() =>
        new($"{Effect.Name}.{Name}: ray-tracing shaders run in a ray-tracing pipeline, which effects do not create yet.");

    public void PrepareData()
    {
        for (int i = 1; i < pipelineStages.Count; ++i)
        {
            pipelineStages[i - 1].NextStage = pipelineStages[i].Stage;
        }

        var hasTaskShader = pipelineStages.Exists(stage => stage.Type == EffectStage.Task);
        foreach (var stage in pipelineStages)
        {
            stage.HasTaskShader = hasTaskShader;
        }

        CreateShaders();
    }

    private protected const ulong LayoutSeed = 0xCBF29CE484222325UL;

    private protected static ulong Mix(ulong hash, ulong value)
    {
        var mixed = (hash ^ value) * 0x9E3779B97F4A7C15UL;
        return mixed ^ (mixed >> 31);
    }

    private protected abstract void CreateShaders();

    private protected abstract void ReleaseResourceLayout();

    private void DeclareStage(StageBlock stage, Logger logger)
    {
        foreach (var buffer in stage.Shader.ConstantBuffers)
        {
            if (buffer.Size > 0)
            {
                Effect.DeclareConstantBuffer(buffer, logger);
            }
        }

        foreach (var declared in stage.Shader.ResourceParameters)
        {
            if (declared.Type == EffectParameterType.UniformBuffer)
            {
                if (Effect.ConstantBuffers[declared.Name] is { } buffer)
                {
                    stage.ConstantBufferLinks.Add(new ConstantBufferLink(buffer, DeclareResource(declared, logger)));
                }

                continue;
            }

            var parameter = DeclareResource(declared, logger);
            GetResourcesOf(stage, parameter.ParameterType)?.Add(parameter);
        }
    }

    private EffectParameter DeclareResource(EffectData.ResourceParameter declared, Logger logger)
    {
        var parameter = Effect.Parameters[declared.Name];
        if (parameter == null)
        {
            return Effect.DeclareResourceParameter(declared);
        }

        if (IsRedefined(declared, (EffectData.ResourceParameter)parameter.ParameterDescription))
        {
            logger.Error("{0}.{1}: {2} is declared differently by another stage.", Effect.Name, Name, declared.Name);
        }

        return parameter;
    }

    private static List<EffectParameter> GetResourcesOf(StageBlock stage, EffectParameterType type) => type switch
    {
        EffectParameterType.Sampler => stage.Samplers,
        EffectParameterType.SampledImage1D or EffectParameterType.SampledImage2D or EffectParameterType.SampledImage3D
            or EffectParameterType.SampledImageCube => stage.SampledImages,
        EffectParameterType.StorageBuffer => stage.StorageBuffers,
        _ => null
    };

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
        RetireStages();
        ReleaseResourceLayout();
        base.Dispose(disposeManagedResources);
    }

    private void RetireStages()
    {
        foreach (var stage in pipelineStages)
        {
            graphicsDevice.MainDevice.RetireResource(stage);
        }

        pipelineStages.Clear();
    }

    private protected class StageBlock : DisposableObject
    {
        public readonly List<ConstantBufferLink> ConstantBufferLinks = [];
        public readonly List<EffectParameter> Samplers = [];
        public readonly List<EffectParameter> SampledImages = [];
        public readonly List<EffectParameter> StorageBuffers = [];

        public readonly ShaderStageFlagBits Stage;
        public ShaderStageFlagBits NextStage;
        public bool HasTaskShader;

        public ShaderEXT ShaderObject;

        public readonly EffectData.Shader Shader;
        public string CacheName;
        public readonly EffectStage Type;
        public readonly IGraphicsDevice GraphicsDevice;

        public StageBlock(IGraphicsDevice device, EffectStage type, EffectData.Shader shader)
        {
            GraphicsDevice = device;
            Type = type;
            Shader = shader;
            Stage = GetShaderStage(type);
        }

        public ShaderCreateInfoEXT NewCreateInfo() => new()
        {
            Flags = Type == EffectStage.Mesh && !HasTaskShader ? ShaderCreateFlagBitsEXT.NoTaskShaderBitExt : 0,
            Stage = Stage,
            NextStage = NextStage,
            CodeType = ShaderCodeTypeEXT.SpirvExt,
            CodeSize = (uint)Shader.Bytecode.Length,
            PCode = Shader.Bytecode,
            PName = Shader.EntryPoint
        };

        public void CreateShader(ShaderCreateInfoEXT createInfo, ulong layout)
        {
            ShaderObject = ((GraphicsDevice)GraphicsDevice).CreateShader(createInfo, CacheName, layout);
        }

        protected override void Dispose(bool disposeManagedResources)
        {
            if (disposeManagedResources)
            {
                GraphicsDevice.DestroyShader(ShaderObject);
            }

            base.Dispose(disposeManagedResources);
        }
    }

    private protected readonly record struct ConstantBufferLink(EffectConstantBuffer ConstantBuffer,
        EffectParameter Parameter);

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
