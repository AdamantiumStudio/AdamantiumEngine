using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Adamantium.Core;
using Adamantium.EffectsCompiler;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.EffectsFramework;
using Adamantium.Vulkan.Core;
using Serilog;
using EffectTechnique = Adamantium.Graphics.Core.EffectsFramework.EffectTechnique;

namespace Adamantium.Graphics.Effects;

/// <summary>
/// Contains rendering state for drawing with an effect; updates constant buffers and directly sets all needed resources for each shader stage/>
/// </summary>
public sealed class EffectPass : DisposableObject, IEffectPass
{
    private const uint MaxItemsPerBuffer = 4096;

    /// <summary>
    ///   Gets the attributes associated with this pass.
    /// </summary>
    /// <value> The attributes. </value>
    public PropertyKeyCollection PropertiesKey { get; private set; }

    /// <summary>
    /// The parent effect of this pass.
    /// </summary>
    public readonly Effect Effect;

    private EffectData.Pass pass;
    private readonly IGraphicsDevice graphicsDevice;

    private readonly List<StageBlock> pipelineStages;

    internal EffectTechnique Technique;

    private List<PipelineShaderStageCreateInfo> shaderStages;

    private List<DescriptorSetLayoutBinding> layoutBindings;

    private readonly List<StageBlock> stages = new List<StageBlock>();

    // The push-data wrappers, made ONCE. Both are generated as CLASSES, not structs, so `new` here allocated two objects
    // on EVERY Apply - i.e. on every draw call, tens of thousands a second. They are marshaled to a pointer inside
    // PushDataEXT and nothing keeps a reference past the call, so one instance per pass can be refilled and re-sent.
    // Same lesson as the delegate this method already avoids (see WriteHeapOffsets): per-draw garbage in the draw path.
    private readonly HostAddressRangeConstEXT _pushRange = new();
    private readonly PushDataInfoEXT _pushDataInfo = new();

    // The transient per-frame constant-buffer arena lives on the render device (not the shared heap manager).
    private DynamicBufferPool CurrentBufferPool => ((GraphicsDevice)graphicsDevice).CurrentBufferPool;

    private uint appliesCounter = 0;
    private bool geometryStagePresent = false;
    public ReadOnlyCollection<PipelineShaderStageCreateInfo> ShaderStages => shaderStages.AsReadOnly();

    /// <summary>
    ///   Initializes a new instance of the <see cref="EffectPass" /> class.
    /// </summary>
    /// <param name="logger">The logger used to log errors.</param>
    /// <param name="effect"> The effect. </param>
    /// <param name="technique">The technique. </param>
    /// <param name="pass"> The pass. </param>
    /// <param name="name"> The name. </param>
    internal EffectPass(Logger logger, Effect effect, EffectTechnique technique, EffectData.Pass pass, string name)
        : base(name)
    {
        Technique = technique;
        this.pass = pass;
        Effect = effect;
        graphicsDevice = effect.GraphicsDevice;
        pipelineStages = new List<StageBlock>();

        shaderStages = new List<PipelineShaderStageCreateInfo>();
        layoutBindings = new List<DescriptorSetLayoutBinding>();
        PropertiesKey = PrepareProperties(logger, pass.Properties);
    }

    private void ClearLayoutBindings()
    {
        foreach (var binding in layoutBindings)
        {
            //binding?.Dispose();
        }

        layoutBindings.Clear();
    }

    /// <summary>Applies this pass: binds each stage's shader, uploads dirty constant buffers and binds its resources.</summary>
    public void Apply()
    {
       ApplyInternal();
    }

    private void ApplyInternal() => ApplyHeap();

    // === VK_EXT_descriptor_heap: CB via push-address; textures/samplers — indices into heaps ===
    // Which linker resource collection a heap-offset write pulls from - a plain kind so ApplyHeap's inner loop needs no
    // per-draw delegate/closure.
    private enum HeapResourceKind { ShaderResource, Sampler, Uav }

    /// <inheritdoc />
    public bool ResourcesBound { get; private set; } = true;

    private HashSet<EffectParameter> _reportedUnbound;

    private unsafe void ApplyHeap()
    {
        Effect.CurrentTechnique = Technique;
        graphicsDevice.CurrentEffectPass = this;
        ResourcesBound = true;
        stages.Clear();

        var resourceLinker = (EffectResourceLinker)Effect.ResourceLinker;

        // Allocate a stack buffer for the full push-data size of this pass
        byte* pushDataBytes = stackalloc byte[(int)totalPushDataSize];

        // Shared per-frame pool: sub-allocates CB data (a per-draw chunk), without a separate buffer per Apply.
        var dynamicPool = CurrentBufferPool;
        var device = (GraphicsDevice)graphicsDevice;
        var hashing = device.IsHashing;
        var swallowing = device.IsSwallowing;

        for (int stageIndex = 0; stageIndex < pipelineStages.Count; stageIndex++)
        {
            var stageBlock = pipelineStages[stageIndex];
            if (stageBlock == null || stageBlock.Index < 0) continue;

            // 1. CONSTANT BUFFERS (PushAddress):
            // sub-allocate a chunk in the pool, copy the CB data, and put its GPU address into push data.
            for (int i = 0; i < stageBlock.ConstantBufferLinks.Count; ++i)
            {
                var link = stageBlock.ConstantBufferLinks[i];

                ulong alignment = graphicsDevice.Adapter.AdapterProperties.Limits.MinUniformBufferOffsetAlignment;
                uint pushOffset = parameterPushOffsets[link.Parameter];
                if (hashing)
                {
                    var backing = link.ConstantBuffer.BackingBuffer;
                    device.Hash(pushOffset);
                    device.HashBytes(new ReadOnlySpan<byte>(backing.DataPointer.ToPointer(), (int)backing.Size));
                }

                *(ulong*)(pushDataBytes + pushOffset) = swallowing ? 0 : dynamicPool.Upload(link.ConstantBuffer, alignment);
            }

            // Write each bound resource's heap-slot index into push data; link.Parameter is already resolved, so there is
            // no per-draw lookup.
            WriteHeapOffsets(pushDataBytes, stageBlock.ShaderResourceViewSlotLinks, HeapResourceKind.ShaderResource);
            WriteHeapOffsets(pushDataBytes, stageBlock.SamplerStateSlotLinks, HeapResourceKind.Sampler);
            WriteHeapOffsets(pushDataBytes, stageBlock.UnorderedAccessViewSlotLinks, HeapResourceKind.Uav);

            stages.Add(stageBlock);

        }

        // 5. BIND SHADERS - unless this command buffer already has THIS pass's shaders on it. A run of draws of one
        // material re-bound the same handles per draw, and every bind is a marshaled call.
        if (!graphicsDevice.ShadersBoundFor(this))
        {
            BindAllStages();
            graphicsDevice.ShadersBound(this);
        }

        // 6. SEND PUSH DATA — strictly AFTER binding shaders (in the reference, push data goes
        // after bind pipeline). Otherwise binding a shader resets push data before the draw.
        if (totalPushDataSize > 0)
        {
            _pushRange.Address = (nuint)pushDataBytes;
            _pushRange.Size = totalPushDataSize;
            _pushDataInfo.Offset = 0;
            _pushDataInfo.Data = _pushRange;

            device.PushData(_pushDataInfo);
        }

        appliesCounter++;

        // Writes each bound resource's stable heap-slot index into push data for one resource list. The actual descriptor
        // (SampledImage / Sampler / StorageBuffer) already sits in that heap slot (allocated by the linker); here we only
        // publish the index. link.Parameter is the pre-resolved EffectParameter, so no per-draw name lookup.
        void WriteHeapOffsets(byte* push, List<SlotLink> links, HeapResourceKind kind)
        {
            for (int i = 0; i < links.Count; ++i)
            {
                var link = links[i];
                // resourceLinker is read from the enclosing scope directly (a struct closure - no heap alloc). Passing a
                // Func here allocated a delegate + closure PER Apply, i.e. per draw, which showed up as GC-driven frame
                // spikes (240 -> 60 fps) under many draws. A plain kind switch keeps the single loop without the garbage.
                IHeapResource[] resources = kind switch
                {
                    HeapResourceKind.ShaderResource => link.IsStorageBuffer
                        ? (IHeapResource[])resourceLinker.GetShaderResourceBuffers(link.ResourceParamDescription)
                        : resourceLinker.GetShaderResources(link.ResourceParamDescription),
                    HeapResourceKind.Sampler => resourceLinker.GetSamplers(link.ResourceParamDescription),
                    _ => resourceLinker.GetUAVs(link.ResourceParamDescription),
                };
                uint basePushOffset = parameterPushOffsets[link.Parameter];

                // Every element a texture or sampler parameter DECLARES. One never set has no entry at all, and its index
                // stayed 0 - heap slot 0, whatever holds it, or a texture freed since, which loses the device. Buffers
                // keep what they had: there is no buffer stand-in, and an image descriptor read as one is no better.
                var isBuffer = kind == HeapResourceKind.Uav || link.IsStorageBuffer;
                var wholeHeap = kind == HeapResourceKind.ShaderResource && !isBuffer && link.Parameter.ElementCount == 0;
                if (wholeHeap)
                {
                    *(uint*)(push + basePushOffset) = 0;
                    if (hashing)
                    {
                        device.Hash((ulong)basePushOffset << 32);
                    }

                    continue;
                }

                var declared = isBuffer ? resources.Length : Math.Max(resources.Length, (int)link.Parameter.ElementCount);
                for (int resIdx = 0; resIdx < declared; resIdx++)
                {
                    var resource = resIdx < resources.Length ? resources[resIdx] : null;
                    var slot = isBuffer ? resource?.GlobalHeapOffset ?? uint.MaxValue
                        : resource is { HasLiveSlot: true } ? resource.GlobalHeapOffset : uint.MaxValue;
                    // Nothing bound. An out-of-heap index would sample whatever the driver finds there - in practice
                    // another effect's live descriptor - so hand over the stand-in instead: the worst case becomes a
                    // red square (DEBUG) or a transparent one, both of which are honest, rather than someone else's
                    // texture. The pass is still marked, because a missing binding is a bug either way.
                    if (slot == uint.MaxValue)
                    {
                        ResourcesBound = false;
                        // Once per parameter of this pass: a resource left unset on purpose meets this on every draw.
                        if ((_reportedUnbound ??= []).Add(link.Parameter))
                            UnboundResource.ReportOnce(Effect.Name, link.Parameter?.Name);
                        var heap = ((GraphicsDevice)graphicsDevice).DescriptorHeapManager;
                        if (heap != null)
                            slot = kind == HeapResourceKind.Sampler ? heap.FallbackSamplerOffset : heap.FallbackTextureOffset;
                    }
                    *(uint*)(push + basePushOffset + (uint)(resIdx * 4)) = slot;
                    if (hashing) device.Hash(((ulong)(basePushOffset + (uint)(resIdx * 4)) << 32) | slot);
                }
            }
        }
    }

    // The stage list of a pass does not change once its shaders exist, so the arrays vkCmdBindShadersEXT wants are built
    // ONCE and re-used. A pass that also has to say "no geometry shader" carries that entry in the same arrays: one call
    // instead of one per stage plus one for the null.
    private ShaderStageFlagBits[] _bindStages;
    private ShaderEXT[] _bindShaders;

    private void BindAllStages()
    {
        if (_bindStages == null || _bindStages.Length != stages.Count + (geometryStagePresent ? 0 : 1))
        {
            var count = stages.Count + (geometryStagePresent ? 0 : 1);
            _bindStages = new ShaderStageFlagBits[count];
            _bindShaders = new ShaderEXT[count];
        }

        for (var i = 0; i < stages.Count; i++)
        {
            _bindStages[i] = stages[i].Stage;
            _bindShaders[i] = stages[i].ShaderObject;
        }

        if (!geometryStagePresent)
        {
            _bindStages[^1] = ShaderStageFlagBits.GeometryBit;
            _bindShaders[^1] = null;
        }

        ((GraphicsDevice)graphicsDevice).BindShaders(_bindStages, _bindShaders);
    }

    /// <summary>
    /// Un-Applies this pass to the device pipeline by unbinding all resources/views previously bound by this pass. This is not mandatory to call this method, unless you want to explicitly unbind
    /// resource views that were bound by this pass.
    /// </summary>
    /// <param name="fullUnApply">if set to <c>true</c> this will unbind all resources; otherwise <c>false</c> will unbind only ShaderResourceView and UnorderedAccessView. Default is false.</param>
    public void UnApply(bool fullUnApply = false)
    {
    }

    /// <inheritdoc />
    public void Reset(EffectTechnique technique, EffectData.Pass pass, Logger logger)
    {
        RetireStages();
        stages.Clear();
        shaderStages.Clear();
        ClearLayoutBindings();
        parameterPushOffsets.Clear();
        totalPushDataSize = 0;
        geometryStagePresent = false;
        Technique = technique;
        this.pass = pass;
        PropertiesKey = PrepareProperties(logger, pass.Properties);
        if (graphicsDevice.ShadersBoundFor(this))
        {
            graphicsDevice.ShadersBound(null);
        }
    }

    /// <summary>
    /// Initializes this pass.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <exception cref="System.InvalidOperationException"></exception>
    public void Initialize(Logger logger)
    {
        foreach (var link in pass.Pipeline)
        {
            if (link.IsImport)
            {
                throw new InvalidOperationException(
                    $"Unable to resolve imported shader [{link.ImportName}] for stage [{link.ShaderType}]");
            }

            var stageBlock = new StageBlock(graphicsDevice, link.ShaderType);
            pipelineStages.Add(stageBlock);

            stageBlock.Index = link.Index;
            stageBlock.EntryPoint = link.EntryPoint;
            stageBlock.CacheName = $"{Effect?.Name}.{Technique?.Name}.{Name}.{link.ShaderType}";

            InitStageBlock(stageBlock, logger);

            if (stageBlock.Stage == ShaderStageFlagBits.GeometryBit)
            {
                geometryStagePresent = true;
            }
        }
    }

    public void PrepareData()
    {
        ComputeSlotLinks();
        ComputePushDataLayout();   // creates shaders with heap mappings
    }

    private readonly Dictionary<EffectParameter, uint> parameterPushOffsets = new();
    private uint totalPushDataSize = 0;

    private void ComputePushDataLayout()
    {
        parameterPushOffsets.Clear();
        uint currentOffset = 0;

        // Constant buffers (PushAddress): reserve 8 bytes for each unique CB to hold its
        // 64-bit GPU address. ApplyInternal sub-allocates CB data from the shared pool and writes the address here.
        for (int stageIndex = 0; stageIndex < pipelineStages.Count; stageIndex++)
        {
            var stageBlock = pipelineStages[stageIndex];
            if (stageBlock == null || stageBlock.Index < 0) continue;

            foreach (var link in stageBlock.ConstantBufferLinks)
            {
                if (!parameterPushOffsets.ContainsKey(link.Parameter))
                {
                    currentOffset = (currentOffset + 7) & ~7U;
                    parameterPushOffsets[link.Parameter] = currentOffset;
                    currentOffset += 8;
                }
            }
        }

        for (int stageIndex = 0; stageIndex < pipelineStages.Count; stageIndex++)
        {
            var stageBlock = pipelineStages[stageIndex];
            if (stageBlock == null || stageBlock.Index < 0) continue;

            if (stageBlock.Parameters != null)
            {
                foreach (var param in stageBlock.Parameters)
                {
                    if (param.Parameter.ResourceType == EffectResourceType.ConstantBuffer) 
                        continue;
                    
                    if (!parameterPushOffsets.ContainsKey(param.Parameter))
                    {
                        currentOffset = (currentOffset + 3) & ~3U;
                        parameterPushOffsets[param.Parameter] = currentOffset;
                        // An array of no length still takes the slot its base is pushed in.
                        currentOffset += Math.Max(param.Parameter.ElementCount, 1) * 4;
                    }
                }
            }
        }

        totalPushDataSize = currentOffset;
        
        CreateShaderObjects();
    }

    private void CreateShaderObjects()
    {
        for (int i = 0; i < pipelineStages.Count; ++i)
        {
            var stage = pipelineStages[i];
            if (i > 0)
            {
                pipelineStages[i - 1].NextStage = stage.Stage;
            }
        }

        pipelineStages.ForEach(stage => stage.CreateShader(parameterPushOffsets));
    }

    /// <summary>
    /// Initializes the stage block.
    /// </summary>
    /// <param name="stageBlock">The stage block.</param>
    /// <param name="logger">The logger.</param>
    private void InitStageBlock(StageBlock stageBlock, Logger logger)
    {
        // If null shader, then skip init
        var shaderIndex = stageBlock.Index;
        if (shaderIndex < 0)
        {
            return;
        }

        // stageBlock.ShaderModule = Effect.Pool.GetOrCompileShader(
        //     stageBlock.Type,
        //     shaderIndex,
        //     out var errorProfile);

        stageBlock.ByteCode = Effect.Pool.GetShaderBytecode(stageBlock.Type, shaderIndex);

        // if (stageBlock.ShaderModule == null)
        // {
        //     logger.Error(
        //         "Unsupported shader profile [{0} / {1}] on current GraphicsDevice in (effect [{2}] Technique [{3}] Pass: [{4}])",
        //         stageBlock.Type,
        //         errorProfile,
        //         Effect.Name,
        //         Technique.Name,
        //         Name);
        //     return;
        // }

        var shaderStageInfo = new PipelineShaderStageCreateInfo();
        shaderStageInfo.Stage = EffectShaderTypeToShaderStage(stageBlock.Type);
        shaderStageInfo.Module = stageBlock.ShaderModule;
        shaderStageInfo.PName = stageBlock.EntryPoint;

        shaderStages.Add(shaderStageInfo);

        var shaderRaw = Effect.Pool.RegisteredShaders[shaderIndex];

        for (int i = 0; i < shaderRaw.ConstantBuffers.Count; i++)
        {
            var constantBufferRaw = shaderRaw.ConstantBuffers[i];

            // Constant buffers with a null size are skipped
            if (constantBufferRaw.Size == 0)
                continue;

            var constantBuffer = Effect.GetOrCreateConstantBuffer(constantBufferRaw);
            // IF constant buffer is null, it means that there is a conflict
            if (constantBuffer == null)
            {
                logger.Error(
                    "Constant buffer [{0}] cannot have multiple size or different content declaration inside the same effect pool",
                    constantBufferRaw.Name);
                continue;
            }

            CreateAndAddLayoutBinding(constantBuffer.Description.DescriptorSet, constantBuffer.Description.Slot, 1U,
                DescriptorType.UniformBuffer, EffectShaderTypeToShaderStage(stageBlock.Type));

            // Test if this constant buffer is not already part of the effect
            if (Effect.ConstantBuffers[constantBufferRaw.Name] == null)
            {
                // Add the declared constant buffer to the effect shader.
                Effect.ConstantBuffers.Add(constantBuffer);

                // Declare all parameter from constant buffer at the effect level.
                foreach (var parameter in constantBuffer.Parameters)
                {
                    var previousParameter = Effect.Parameters[parameter.Name];
                    if (previousParameter == null)
                    {
                        // Add an effect parameter linked to the appropriate constant buffer at the effect level.
                        Effect.DeclareValueParameter(
                            (EffectData.ValueTypeParameter)parameter.ParameterDescription,
                            constantBuffer);
                    }
                    else if (parameter.ParameterDescription != previousParameter.ParameterDescription ||
                             parameter.Buffer != previousParameter.Buffer)
                    {
                        // If registered parameters is different
                        logger.Error(
                            "Parameter [{0}] defined in Constant buffer [{0}] is already defined by another constant buffer with the definition [{2}]",
                            parameter,
                            constantBuffer.Name,
                            previousParameter);
                    }
                }
            }
        }

        stageBlock.ConstantBufferLinks.Clear();

        // Declare all resource parameters at the effect level.
        foreach (var parameterRaw in shaderRaw.ResourceParameters)
        {
            EffectParameter parameter;
            var previousParameter = Effect.Parameters[parameterRaw.Name];

            // Skip empty constant buffers.
            if (parameterRaw.Type == EffectParameterType.ConstantBuffer &&
                Effect.ConstantBuffers[parameterRaw.Name] == null)
            {
                continue;
            }

            if (previousParameter == null)
            {
                var paramType = EffectResourceTypeHelper.ConvertFromParameterType(parameterRaw.Type);
                parameter = Effect.DeclareResourceParameter(parameterRaw, paramType);
            }
            else
            {
                if (CompareResourceParameter(
                        parameterRaw,
                        (EffectData.ResourceParameter)previousParameter.ParameterDescription))
                {
                    // If registered parameters is different
                    logger.Error(
                        "Resource Parameter [{0}] is already defined with a different definition [{1}]",
                        parameterRaw,
                        previousParameter.ParameterDescription);
                }

                parameter = previousParameter;
            }

            CreateAndAddLayoutBinding(parameterRaw.DescriptorSet, parameterRaw.Slot, parameterRaw.Count,
                ConvertFromEffectParameterType(parameterRaw.Type),
                EffectShaderTypeToShaderStage(stageBlock.Type));

            // For constant buffers, we need to store explicit link
            if (parameter.ResourceType == EffectResourceType.ConstantBuffer)
            {
                stageBlock.ConstantBufferLinks.Add(new ConstantBufferLink(Effect.ConstantBuffers[parameter.Name], parameter));
            }

            stageBlock.Parameters ??= new List<ParameterBinding>(shaderRaw.ResourceParameters.Count);
            stageBlock.Parameters.Add(new ParameterBinding(parameter, parameterRaw.Slot, parameterRaw.DescriptorSet));
        }
    }

    private DescriptorSetLayoutBinding CreateAndAddLayoutBinding(uint descriptorSet, uint slot, uint descriptorCount,
        DescriptorType descriptorType, ShaderStageFlagBits stageFlags)
    {
        var binding = layoutBindings.FirstOrDefault(x => x.Binding == slot);

        if (binding != null)
        {
            binding.StageFlags |= stageFlags;
        }
        else
        {
            var resourceBinding = new DescriptorSetLayoutBinding();
            resourceBinding.Binding = slot;
            resourceBinding.DescriptorCount = descriptorCount;
            resourceBinding.DescriptorType = descriptorType;
            resourceBinding.StageFlags = stageFlags;

            layoutBindings.Add(resourceBinding);
            binding = resourceBinding;
        }

        return binding;
    }

    public static ShaderStageFlagBits EffectShaderTypeToShaderStage(EffectShaderType type)
    {
        switch (type)
        {
            case EffectShaderType.Vertex:
                return ShaderStageFlagBits.VertexBit;
            case EffectShaderType.Hull:
                return ShaderStageFlagBits.TessellationControlBit;
            case EffectShaderType.Domain:
                return ShaderStageFlagBits.TessellationEvaluationBit;
            case EffectShaderType.Geometry:
                return ShaderStageFlagBits.GeometryBit;
            case EffectShaderType.Fragment:
                return ShaderStageFlagBits.FragmentBit;
            case EffectShaderType.Compute:
                return ShaderStageFlagBits.ComputeBit;
            default:
                throw new ArgumentOutOfRangeException(
                    $"Effect type {type} currently has no equivalent for ShaderStageFlagBits");
        }
    }

    private DescriptorType ConvertFromEffectParameterType(EffectParameterType type)
    {
        switch (type)
        {
            case EffectParameterType.ConstantBuffer:
                return DescriptorType.UniformBuffer;
            case EffectParameterType.Sampler:
            case EffectParameterType.Sampler1D:
            case EffectParameterType.Sampler2D:
            case EffectParameterType.Sampler3D:
            case EffectParameterType.SamplerCube:
                return DescriptorType.Sampler;
            case EffectParameterType.Texture:
            case EffectParameterType.Texture1D:
            case EffectParameterType.Texture1DArray:
            case EffectParameterType.Texture2D:
            case EffectParameterType.Texture2DArray:
            case EffectParameterType.Texture2DMultisampled:
            case EffectParameterType.Texture2DMultisampledArray:
            case EffectParameterType.Texture3D:
            case EffectParameterType.TextureCube:
            case EffectParameterType.TextureCubeArray:
                return DescriptorType.SampledImage;
            case EffectParameterType.RWTexture1D:
            case EffectParameterType.RWTexture1DArray:
            case EffectParameterType.RWTexture2D:
            case EffectParameterType.RWTexture2DArray:
            case EffectParameterType.RWTexture3D:
                return DescriptorType.StorageTexelBuffer;
            case EffectParameterType.StorageBuffer:
                return DescriptorType.StorageBuffer;
            case EffectParameterType.StorageImage:
                return DescriptorType.StorageImage;
            default:
                throw new ArgumentOutOfRangeException(
                    $"Cannot convert parameter {type} to corresponding DescriptorType");
        }
    }

    internal void ComputeSlotLinks()
    {
        foreach (var stageBlockVar in pipelineStages)
        {
            var stageBlock = stageBlockVar;

            if (stageBlock?.Parameters == null)
                continue;

            PrepareSlotLinks(ref stageBlock);
        }
    }

    /// <summary>
    /// Optimizes the slot links.
    /// </summary>
    /// <param name="stageBlock">The stage block.</param>
    private void PrepareSlotLinks(ref StageBlock stageBlock)
    {
        foreach (var parameter in stageBlock.Parameters)
        {
            SlotLink link;
            var resourceType = parameter.Parameter.ResourceType;
            switch (resourceType)
            {
                case EffectResourceType.ShaderResourceView:
                    // A read-only StructuredBuffer reflects as an SRV too, but its resource is a STORAGE BUFFER, not a
                    // texture. Same list, tagged so the consumers branch (read-only storage-buffer descriptor + mask).
                    link = new SlotLink((uint)parameter.Parameter.SlotIndex, (uint)parameter.Parameter.DescriptorSet,
                        parameter.Parameter.ParameterDescription, parameter.Parameter,
                        isStorageBuffer: parameter.Parameter.ParameterDescription.Type == EffectParameterType.StorageBuffer);
                    stageBlock.ShaderResourceViewSlotLinks.Add(link);
                    break;
                case EffectResourceType.SamplerState:
                    link = new SlotLink((uint)parameter.Parameter.SlotIndex, (uint)parameter.Parameter.DescriptorSet,
                        parameter.Parameter.ParameterDescription, parameter.Parameter);
                    stageBlock.SamplerStateSlotLinks.Add(link);
                    break;
                case EffectResourceType.UnorderedAccessView:
                    link = new SlotLink((uint)parameter.Parameter.SlotIndex, (uint)parameter.Parameter.DescriptorSet,
                        parameter.Parameter.ParameterDescription, parameter.Parameter);
                    stageBlock.UnorderedAccessViewSlotLinks.Add(link);
                    break;
            }
        }
    }

    private bool CompareResourceParameter(EffectData.ResourceParameter left, EffectData.ResourceParameter right)
    {
        return left.Class != right.Class || left.Type != right.Type || left.Count != right.Count;
    }

    private PropertyKeyCollection PrepareProperties(Logger logger, EffectPropertyCollection properties)
    {
        var passProperties = new PropertyKeyCollection();

        foreach (var property in properties)
        {
            switch (property.Key)
            {
                //case EffectData.PropertyKeys.Blending:
                //    BlendState = graphicsDevice.BlendStates[(string)property.Value];
                //    if (BlendState == null)
                //        logger.Error("Unable to find registered BlendState [{0}]", (string)property.Value);
                //    break;
                //case EffectData.PropertyKeys.BlendingColor:
                //    BlendStateColor = (Color4)(Vector4F)property.Value;
                //    break;
                //case EffectData.PropertyKeys.BlendingSampleMask:
                //    BlendStateSampleMask = (uint)property.Value;
                //    break;

                //case EffectData.PropertyKeys.DepthStencil:
                //    DepthStencilState = graphicsDevice.DepthStencilStates[(string)property.Value];
                //    if (DepthStencilState == null)
                //        logger.Error("Unable to find registered DepthStencilState [{0}]", (string)property.Value);
                //    break;
                //case EffectData.PropertyKeys.DepthStencilReference:
                //    DepthStencilReference = (int)property.Value;
                //    break;

                //case EffectData.PropertyKeys.Rasterizer:
                //    RasterizerState = graphicsDevice.RasterizerStates[(string)property.Value];
                //    if (RasterizerState == null)
                //        logger.Error("Unable to find registered RasterizerState [{0}]", (string)property.Value);
                //    break;
                default:
                    passProperties[new PropertyKey(property.Key)] = property.Value;
                    break;
            }
        }

        return passProperties;
    }

    public override int GetHashCode()
    {
        int hashCode = 0;

        foreach (var stage in pipelineStages)
        {
            if (stage == null) continue;

            hashCode = stage.Type.GetHashCode();
            hashCode = (hashCode * 397) ^ stage.EntryPoint.GetHashCode();
        }

        return hashCode;
    }

    #region Nested type: PipelineBlock

    private struct ParameterBinding
    {
        public ParameterBinding(EffectParameter parameter, int slot, uint descriptorSet)
        {
            Parameter = parameter;
            Slot = slot;
            DescriptorSet = descriptorSet;
        }

        public readonly EffectParameter Parameter;

        public readonly int Slot;

        public readonly uint DescriptorSet;
    }

    protected override void Dispose(bool disposeManagedResources)
    {
        Log.Logger.Debug("Disposing EffectPass resources");
        ClearLayoutBindings();
        RetireStages();
        base.Dispose(disposeManagedResources);
    }

    // Frames in flight may still bind these shaders.
    private void RetireStages()
    {
        foreach (var stage in pipelineStages)
        {
            graphicsDevice.MainDevice.RetireResource(stage);
        }

        pipelineStages.Clear();
    }

    #endregion

    #region Nested type: SlotLink

    [StructLayout(LayoutKind.Sequential)]
    private struct SlotLink
    {
        public SlotLink(uint slotIndex, uint descriptorSet, EffectData.Parameter paramDescription, EffectParameter parameter,
            bool isStorageBuffer = false)
        {
            ResourceParamDescription = paramDescription;
            SlotIndex = slotIndex;
            DescriptorSet = descriptorSet;
            Parameter = parameter;
            IsStorageBuffer = isStorageBuffer;
        }

        public readonly EffectData.Parameter ResourceParamDescription;

        public readonly EffectParameter Parameter;

        public readonly uint SlotIndex;

        public readonly uint DescriptorSet;

        // An SRV slot that is a read-only StructuredBuffer (a storage-buffer resource) rather than a texture. Only
        // meaningful for links in ShaderResourceViewSlotLinks; lets the one SRV loop branch instead of a second list.
        public readonly bool IsStorageBuffer;
    }

    #endregion

    #region Nested type: StageBlock

    private class StageBlock : DisposableObject
    {
        public List<ParameterBinding> Parameters;
        public readonly List<SlotLink> SamplerStateSlotLinks;
        public readonly List<SlotLink> ShaderResourceViewSlotLinks;   // textures AND read-only StructuredBuffers (SlotLink.IsStorageBuffer)
        public readonly List<SlotLink> UnorderedAccessViewSlotLinks;

        public readonly ShaderStageFlagBits Stage;
        public ShaderStageFlagBits NextStage;

        public List<ConstantBufferLink> ConstantBufferLinks;
        public int Index;

        public ShaderModule ShaderModule;
        public ShaderEXT ShaderObject;

        public byte[] ByteCode;
        public string EntryPoint;
        /// <summary>effect.technique.pass.stage - names this shader's binary-cache file so the cache folder is readable.</summary>
        public string CacheName;
        public readonly EffectShaderType Type;
        public readonly IGraphicsDevice GraphicsDevice;

        public StageBlock(IGraphicsDevice device,
            EffectShaderType type)
        {
            GraphicsDevice = device;
            Type = type;
            Stage = EffectShaderTypeToShaderStage(type);
            SamplerStateSlotLinks = new List<SlotLink>();
            ShaderResourceViewSlotLinks = new List<SlotLink>();
            UnorderedAccessViewSlotLinks = new List<SlotLink>();
            ConstantBufferLinks = new List<ConstantBufferLink>();
        }

        public void CreateShader(Dictionary<EffectParameter, uint> pushOffsets)
        {
            var shaderCreateInfo = new ShaderCreateInfoEXT();
            shaderCreateInfo.Stage = Stage;
            shaderCreateInfo.NextStage = NextStage;
            shaderCreateInfo.CodeType = ShaderCodeTypeEXT.SpirvExt;
            shaderCreateInfo.CodeSize = (uint)ByteCode.Length;
            shaderCreateInfo.PCode = ByteCode;
            shaderCreateInfo.PName = EntryPoint;

            // No set layouts; classic (set,binding) are remapped to heap/push-address.
            shaderCreateInfo.Flags = ShaderCreateFlagBitsEXT.DescriptorHeapBitExt;
            shaderCreateInfo.PSetLayouts = null;
            shaderCreateInfo.SetLayoutCount = 0;

            int mappingCount = ConstantBufferLinks.Count +
                               ShaderResourceViewSlotLinks.Count +
                               SamplerStateSlotLinks.Count +
                               UnorderedAccessViewSlotLinks.Count;

            if (mappingCount > 0)
            {
                var mappings = new  DescriptorSetAndBindingMappingEXT[mappingCount];
                int mapIdx = 0;
                uint descriptorSize = (uint)GraphicsDevice.Adapter.DeviceHeapProperties.BufferDescriptorSize;

                // 1. Map the classic cbuffer register(b0) to PushAddress: the shader reads the CB
                // directly via the 64-bit address from push data (written by ApplyInternal at this same offset).
                foreach (var link in ConstantBufferLinks)
                {
                    mappings[mapIdx] = new DescriptorSetAndBindingMappingEXT
                    {
                        DescriptorSet = link.Parameter.DescriptorSet,
                        FirstBinding = (uint)link.Parameter.SlotIndex,
                        BindingCount = 1,
                        ResourceMask = SpirvResourceTypeFlagBitsEXT.UniformBufferBitExt,
                        Source = DescriptorMappingSourceEXT.PushAddressExt
                    };
                    mappings[mapIdx].SourceData = new DescriptorMappingSourceDataEXT();
                    mappings[mapIdx].SourceData.PushAddressOffset = pushOffsets[link.Parameter];
                    mapIdx++;
                }

                // 2. Map each SRV register(t#) to a resource-heap slot. A texture -> a sampled-image descriptor; a
                //    read-only StructuredBuffer (IsStorageBuffer) -> a read-only storage-buffer descriptor (its own
                //    resource mask + the buffer-descriptor stride, not the image one).
                foreach (var link in ShaderResourceViewSlotLinks)
                {
                    uint offsetInPushData = pushOffsets[link.Parameter];

                    mappings[mapIdx] = new DescriptorSetAndBindingMappingEXT
                    {
                        DescriptorSet = link.DescriptorSet,
                        FirstBinding = link.SlotIndex,
                        BindingCount = 1,
                        ResourceMask = link.IsStorageBuffer
                            ? SpirvResourceTypeFlagBitsEXT.ReadOnlyStorageBufferBitExt
                            : SpirvResourceTypeFlagBitsEXT.SampledImageBitExt | SpirvResourceTypeFlagBitsEXT.ReadOnlyImageBitExt,
                        Source = DescriptorMappingSourceEXT.HeapWithPushIndexExt
                    };
                    mappings[mapIdx].SourceData = new DescriptorMappingSourceDataEXT();
                    mappings[mapIdx].SourceData.PushIndex = new DescriptorMappingSourcePushIndexEXT
                    {
                        PushOffset = offsetInPushData,
                        HeapOffset = 0,
                        HeapIndexStride = 1,
                        HeapArrayStride = (uint)(link.IsStorageBuffer
                            ? GraphicsDevice.Adapter.DeviceHeapProperties.BufferDescriptorSize
                            : GraphicsDevice.Adapter.DeviceHeapProperties.ImageDescriptorSize)
                    };
                    mapIdx++;
                }

                // 3. Map the classic sampler register(s0) to an index in the sampler heap
                foreach (var link in SamplerStateSlotLinks)
                {
                    var effectParam = link.Parameter;
                    uint offsetInPushData = pushOffsets[effectParam];

                    mappings[mapIdx] = new DescriptorSetAndBindingMappingEXT
                    {
                        DescriptorSet = link.DescriptorSet,
                        FirstBinding = link.SlotIndex,
                        BindingCount = 1,
                        ResourceMask = SpirvResourceTypeFlagBitsEXT.SamplerBitExt,
                        Source = DescriptorMappingSourceEXT.HeapWithPushIndexExt
                    };
                    mappings[mapIdx].SourceData = new DescriptorMappingSourceDataEXT();
                    mappings[mapIdx].SourceData.PushIndex = new DescriptorMappingSourcePushIndexEXT
                    {
                        PushOffset = offsetInPushData,
                        HeapOffset = 0,
                        HeapIndexStride = 1,
                        HeapArrayStride = (uint)GraphicsDevice.Adapter.DeviceHeapProperties.SamplerDescriptorSize
                    };
                    mapIdx++;
                }

                // 4. Map the classic UAV register(u0) to an index in the resource heap
                foreach (var link in UnorderedAccessViewSlotLinks)
                {
                    var effectParam = link.Parameter;
                    uint offsetInPushData = pushOffsets[effectParam];
                    
                    mappings[mapIdx] = new DescriptorSetAndBindingMappingEXT
                    {
                        DescriptorSet = link.DescriptorSet,
                        FirstBinding = link.SlotIndex,
                        BindingCount = 1,
                        ResourceMask = SpirvResourceTypeFlagBitsEXT.ReadWriteImageBitExt |
                                       SpirvResourceTypeFlagBitsEXT.ReadWriteStorageBufferBitExt,
                        Source = DescriptorMappingSourceEXT.HeapWithPushIndexExt
                    };
    
                    mappings[mapIdx].SourceData = new DescriptorMappingSourceDataEXT();
                    mappings[mapIdx].SourceData.PushIndex = new DescriptorMappingSourcePushIndexEXT
                    {
                        PushOffset = offsetInPushData,
                        HeapOffset = 0,
                        HeapIndexStride = 1,
                        HeapArrayStride = (uint)GraphicsDevice.Adapter.DeviceHeapProperties.BufferDescriptorSize
                    };
                    mapIdx++;
                }
                
                Array.Sort(mappings, (a, b) => {
                    int setCmp = a.DescriptorSet.CompareTo(b.DescriptorSet);
                    if (setCmp != 0) return setCmp;
                    return a.FirstBinding.CompareTo(b.FirstBinding);
                });

                // Assemble the final mapping structure for pNext
                var mappingInfo = new ShaderDescriptorSetAndBindingMappingInfoEXT
                {
                    MappingCount = (uint)mappingCount,
                    PMappings = mappings
                };

                // Pass it into native shader creation via a pointer
                shaderCreateInfo.PNext = mappingInfo;
            }

            ShaderObject = GraphicsDevice.CreateShader(shaderCreateInfo, CacheName);
        }

        protected override void Dispose(bool disposeManagedResources)
        {
            if (disposeManagedResources)
            {
                GraphicsDevice.DestroyShader(ShaderObject);
                ShaderModule = null;
            }

            base.Dispose(disposeManagedResources);
        }
    }

    private struct ConstantBufferLink
    {
        public ConstantBufferLink(EffectConstantBuffer constantBuffer, EffectParameter parameter)
        {
            ConstantBuffer = constantBuffer;
            Parameter = parameter;
            ResourceIndex = 0;
        }

        public readonly EffectConstantBuffer ConstantBuffer;

        public readonly EffectParameter Parameter;

        public readonly uint ResourceIndex;
    }

    #endregion

    #region Nested Types

    public class DynamicBufferPool : IDisposable
    {
        private readonly IGraphicsDevice graphicsDevice;
        private readonly ulong pageSize;
        private readonly List<Buffer> pages = new();
    
        private int currentPageIndex = 0;
        private ulong currentOffset = 0;
        private long generation;
        private readonly ConditionalWeakTable<EffectConstantBuffer, UploadedConstants> uploads = new();

        public DynamicBufferPool(IGraphicsDevice device, ulong pageSize = 2 * 1024 * 1024) // 2 MB initial page; grows on demand per frame
        {
            graphicsDevice = device;
            this.pageSize = pageSize;
        
            // Create the first page immediately
            CreateNewPage();
        }

        public void Reset()
        {
            currentPageIndex = 0;
            currentOffset = 0;
            generation++;
        }

        /// <summary>The device address of a copy of <paramref name="buffer"/>'s data in this frame's pages: the copy made
        /// earlier in the frame while the data is still the same - a run of draws whose constants did not change, or a
        /// buffer every stage of a pass reads - else a new one.</summary>
        public unsafe ulong Upload(EffectConstantBuffer buffer, ulong alignment)
        {
            var backing = buffer.BackingBuffer;
            var data = new ReadOnlySpan<byte>(backing.DataPointer.ToPointer(), (int)backing.Size);
            if (uploads.TryGetValue(buffer, out var last) && last.Generation == generation && data.SequenceEqual(last.Data))
            {
                return last.Address;
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
            last.Address = page.GetDeviceAddress() + offset;
            return last.Address;
        }

        private sealed class UploadedConstants
        {
            public long Generation = -1;
            public ulong Address;
            public byte[] Data = [];
        }
        
        public (Buffer Page, ulong Offset) Allocate(ulong size, ulong alignment)
        {
            // Align the current offset to hardware requirements (e.g., to 16 or 256 bytes)
            ulong alignedOffset = (currentOffset + alignment - 1) & ~(alignment - 1);

            // If the data does not fit within the current page, create/take the next one
            if (alignedOffset + size > pageSize)
            {
                currentPageIndex++;
                if (currentPageIndex >= pages.Count)
                {
                    CreateNewPage();
                }
            
                // On a new page the allocation is guaranteed to start at zero
                alignedOffset = 0;
            }

            Buffer page = pages[currentPageIndex];

            // Advance the allocator pointer for the next Allocate call
            currentOffset = alignedOffset + size;

            // Return the page itself and the exact byte offset within it
            return (page, alignedOffset);
        }

        private void CreateNewPage()
        {
            // CRITICALLY IMPORTANT: the ShaderDeviceAddress flag is required for Vulkan to allow obtaining the buffer's GPU address
            var flags = BufferUsageFlags.UniformBuffer | BufferUsageFlags.ShaderDeviceAddress;
            // Constant-buffer pages: CPU writes them, GPU reads them each frame → CPU-to-GPU upload (BAR window).
            var memFlags = BufferMemoryUsage.UploadFromCpuToGpu;

            var buffer = Buffer.New(graphicsDevice, pageSize, flags, memFlags);
            pages.Add(buffer);
        }

        public void Dispose()
        {
            foreach (var page in pages) page.Dispose();
            pages.Clear();
        }
    }

    #endregion
}