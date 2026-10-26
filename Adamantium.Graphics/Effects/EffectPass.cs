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
/// Contains rendering state for drawing with an effect; updates constant buffers and directly sets all needed resources
/// for each shader stage. How the resources reach the shaders - the descriptor heap or descriptor sets - is up to the
/// implementation the device chose.
/// </summary>
public abstract class EffectPass : DisposableObject, IEffectPass
{
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
    private protected readonly IGraphicsDevice graphicsDevice;

    private protected readonly List<StageBlock> pipelineStages;

    internal EffectTechnique Technique;

    private List<PipelineShaderStageCreateInfo> shaderStages;

    private List<DescriptorSetLayoutBinding> layoutBindings;

    private protected readonly List<StageBlock> stages = new List<StageBlock>();

    private protected DynamicBufferPool CurrentBufferPool => ((GraphicsDevice)graphicsDevice).CurrentBufferPool;

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
    private protected EffectPass(Logger logger, Effect effect, EffectTechnique technique, EffectData.Pass pass, string name)
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
        layoutBindings.Clear();
    }

    /// <summary>Applies this pass: binds each stage's shader, uploads dirty constant buffers and binds its resources.</summary>
    public void Apply()
    {
        Effect.CurrentTechnique = Technique;
        graphicsDevice.CurrentEffectPass = this;
        ResourcesBound = true;
        stages.Clear();
        for (int stageIndex = 0; stageIndex < pipelineStages.Count; stageIndex++)
        {
            var stageBlock = pipelineStages[stageIndex];
            if (stageBlock == null || stageBlock.Index < 0) continue;

            stages.Add(stageBlock);
        }

        ApplyResources();
        appliesCounter++;
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
        ReleaseResourceLayout();
        stages.Clear();
        shaderStages.Clear();
        ClearLayoutBindings();
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
        for (int i = 1; i < pipelineStages.Count; ++i)
        {
            pipelineStages[i - 1].NextStage = pipelineStages[i].Stage;
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

    private void InitStageBlock(StageBlock stageBlock, Logger logger)
    {
        var shaderIndex = stageBlock.Index;
        if (shaderIndex < 0)
        {
            return;
        }

        stageBlock.ByteCode = Effect.Pool.GetShaderBytecode(stageBlock.Type, shaderIndex);

        var shaderStageInfo = new PipelineShaderStageCreateInfo();
        shaderStageInfo.Stage = EffectShaderTypeToShaderStage(stageBlock.Type);
        shaderStageInfo.Module = stageBlock.ShaderModule;
        shaderStageInfo.PName = stageBlock.EntryPoint;

        shaderStages.Add(shaderStageInfo);

        var shaderRaw = Effect.Pool.RegisteredShaders[shaderIndex];

        for (int i = 0; i < shaderRaw.ConstantBuffers.Count; i++)
        {
            var constantBufferRaw = shaderRaw.ConstantBuffers[i];

            if (constantBufferRaw.Size == 0)
                continue;

            var constantBuffer = Effect.GetOrCreateConstantBuffer(constantBufferRaw);
            if (constantBuffer == null)
            {
                logger.Error(
                    "Constant buffer [{0}] cannot have multiple size or different content declaration inside the same effect pool",
                    constantBufferRaw.Name);
                continue;
            }

            CreateAndAddLayoutBinding(constantBuffer.Description.DescriptorSet, constantBuffer.Description.Slot, 1U,
                DescriptorType.UniformBuffer, EffectShaderTypeToShaderStage(stageBlock.Type));

            if (Effect.ConstantBuffers[constantBufferRaw.Name] == null)
            {
                Effect.ConstantBuffers.Add(constantBuffer);

                foreach (var parameter in constantBuffer.Parameters)
                {
                    var previousParameter = Effect.Parameters[parameter.Name];
                    if (previousParameter == null)
                    {
                        Effect.DeclareValueParameter(
                            (EffectData.ValueTypeParameter)parameter.ParameterDescription,
                            constantBuffer);
                    }
                    else if (parameter.ParameterDescription != previousParameter.ParameterDescription ||
                             parameter.Buffer != previousParameter.Buffer)
                    {
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

        foreach (var parameterRaw in shaderRaw.ResourceParameters)
        {
            EffectParameter parameter;
            var previousParameter = Effect.Parameters[parameterRaw.Name];

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

    private void PrepareSlotLinks(ref StageBlock stageBlock)
    {
        foreach (var parameter in stageBlock.Parameters)
        {
            SlotLink link;
            var resourceType = parameter.Parameter.ResourceType;
            switch (resourceType)
            {
                case EffectResourceType.ShaderResourceView:
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

    private protected struct ParameterBinding
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

    #endregion

    #region Nested type: SlotLink

    [StructLayout(LayoutKind.Sequential)]
    private protected struct SlotLink
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

        public readonly bool IsStorageBuffer;
    }

    #endregion

    #region Nested type: StageBlock

    private protected class StageBlock : DisposableObject
    {
        public List<ParameterBinding> Parameters;
        public readonly List<SlotLink> SamplerStateSlotLinks;
        public readonly List<SlotLink> ShaderResourceViewSlotLinks;
        public readonly List<SlotLink> UnorderedAccessViewSlotLinks;

        public readonly ShaderStageFlagBits Stage;
        public ShaderStageFlagBits NextStage;

        public List<ConstantBufferLink> ConstantBufferLinks;
        public int Index;

        public ShaderModule ShaderModule;
        public ShaderEXT ShaderObject;

        public byte[] ByteCode;
        public string EntryPoint;
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

        public ShaderCreateInfoEXT NewCreateInfo() => new()
        {
            Stage = Stage,
            NextStage = NextStage,
            CodeType = ShaderCodeTypeEXT.SpirvExt,
            CodeSize = (uint)ByteCode.Length,
            PCode = ByteCode,
            PName = EntryPoint
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
                ShaderModule = null;
            }

            base.Dispose(disposeManagedResources);
        }
    }

    private protected struct ConstantBufferLink
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
        private readonly List<Buffer> oversized = [];

        private int currentPageIndex = 0;
        private ulong currentOffset = 0;
        private long generation;
        private readonly ConditionalWeakTable<EffectConstantBuffer, UploadedConstants> uploads = new();

        public DynamicBufferPool(IGraphicsDevice device, ulong pageSize = 2 * 1024 * 1024)
        {
            graphicsDevice = device;
            this.pageSize = pageSize;
            pages.Add(NewPage(pageSize));
        }

        public void Reset()
        {
            currentPageIndex = 0;
            currentOffset = 0;
            generation++;
            ReleaseOversized();
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
        /// its own, freed when the pool is reset.</summary>
        public (Buffer Page, ulong Offset) Allocate(ulong size, ulong alignment)
        {
            if (size > pageSize)
            {
                var own = NewPage(size);
                oversized.Add(own);
                return (own, 0);
            }

            ulong alignedOffset = (currentOffset + alignment - 1) & ~(alignment - 1);
            if (alignedOffset + size > pageSize)
            {
                currentPageIndex++;
                if (currentPageIndex >= pages.Count)
                {
                    pages.Add(NewPage(pageSize));
                }

                alignedOffset = 0;
            }

            Buffer page = pages[currentPageIndex];
            currentOffset = alignedOffset + size;
            return (page, alignedOffset);
        }

        private Buffer NewPage(ulong size)
        {
            var flags = BufferUsageFlags.UniformBuffer | BufferUsageFlags.ShaderDeviceAddress;
            var memFlags = BufferMemoryUsage.UploadFromCpuToGpu;

            return Buffer.New(graphicsDevice, size, flags, memFlags);
        }

        private void ReleaseOversized()
        {
            foreach (var page in oversized)
            {
                graphicsDevice.AddToDeferDisposeQueue(page);
            }

            oversized.Clear();
        }

        public void Dispose()
        {
            foreach (var page in pages) page.Dispose();
            pages.Clear();
            foreach (var page in oversized)
            {
                page.Dispose();
            }

            oversized.Clear();
        }
    }

    #endregion
}
