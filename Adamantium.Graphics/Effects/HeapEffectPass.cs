using System;
using System.Collections.Generic;
using Adamantium.Core;
using Adamantium.EffectsCompiler;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.EffectsFramework;
using Adamantium.Vulkan.Core;
using EffectTechnique = Adamantium.Graphics.Core.EffectsFramework.EffectTechnique;

namespace Adamantium.Graphics.Effects;

/// <summary>A pass on the descriptor heap (VK_EXT_descriptor_heap): constant buffers by their address, textures, samplers
/// and buffers by their heap slot - both written to push data on every Apply.</summary>
internal sealed class HeapEffectPass : EffectPass
{
    private readonly HostAddressRangeConstEXT _pushRange = new();
    private readonly PushDataInfoEXT _pushDataInfo = new();

    private readonly Dictionary<EffectParameter, uint> parameterPushOffsets = new();
    private uint totalPushDataSize = 0;

    internal HeapEffectPass(Logger logger, Effect effect, EffectTechnique technique, EffectData.Pass pass, string name)
        : base(logger, effect, technique, pass, name)
    {
    }

    private enum HeapResourceKind { ShaderResource, Sampler, Uav }

    private protected override unsafe void ApplyResources()
    {
        var resourceLinker = (EffectResourceLinker)Effect.ResourceLinker;
        byte* pushDataBytes = stackalloc byte[(int)totalPushDataSize];
        var dynamicPool = CurrentBufferPool;
        var device = (GraphicsDevice)graphicsDevice;
        var hashing = device.IsHashing;
        var swallowing = device.IsSwallowing;

        for (int stageIndex = 0; stageIndex < stages.Count; stageIndex++)
        {
            var stageBlock = stages[stageIndex];
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

            WriteHeapOffsets(pushDataBytes, stageBlock.ShaderResourceViewSlotLinks, HeapResourceKind.ShaderResource);
            WriteHeapOffsets(pushDataBytes, stageBlock.SamplerStateSlotLinks, HeapResourceKind.Sampler);
            WriteHeapOffsets(pushDataBytes, stageBlock.UnorderedAccessViewSlotLinks, HeapResourceKind.Uav);
        }

        BindShaders();

        if (totalPushDataSize > 0)
        {
            _pushRange.Address = (nuint)pushDataBytes;
            _pushRange.Size = totalPushDataSize;
            _pushDataInfo.Offset = 0;
            _pushDataInfo.Data = _pushRange;

            device.PushData(_pushDataInfo);
        }

        void WriteHeapOffsets(byte* push, List<SlotLink> links, HeapResourceKind kind)
        {
            for (int i = 0; i < links.Count; ++i)
            {
                var link = links[i];
                IHeapResource[] resources = kind switch
                {
                    HeapResourceKind.ShaderResource => link.IsStorageBuffer
                        ? (IHeapResource[])resourceLinker.GetShaderResourceBuffers(link.ResourceParamDescription)
                        : resourceLinker.GetShaderResources(link.ResourceParamDescription),
                    HeapResourceKind.Sampler => resourceLinker.GetSamplers(link.ResourceParamDescription),
                    _ => resourceLinker.GetUAVs(link.ResourceParamDescription),
                };
                uint basePushOffset = parameterPushOffsets[link.Parameter];

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
                    if (slot == uint.MaxValue)
                    {
                        ReportUnbound(link.Parameter);
                        if (device.DescriptorManager is IDescriptorHeapManager heap)
                            slot = kind == HeapResourceKind.Sampler ? heap.FallbackSamplerOffset : heap.FallbackTextureOffset;
                    }
                    *(uint*)(push + basePushOffset + (uint)(resIdx * 4)) = slot;
                    if (hashing) device.Hash(((ulong)(basePushOffset + (uint)(resIdx * 4)) << 32) | slot);
                }
            }
        }
    }

    private protected override void CreateShaders()
    {
        parameterPushOffsets.Clear();
        uint currentOffset = 0;

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
                        currentOffset += Math.Max(param.Parameter.ElementCount, 1) * 4;
                    }
                }
            }
        }

        totalPushDataSize = currentOffset;

        foreach (var stage in pipelineStages)
        {
            var createInfo = MappedCreateInfo(stage, out var layout);
            stage.CreateShader(createInfo, layout);
        }
    }

    private protected override void ReleaseResourceLayout()
    {
        parameterPushOffsets.Clear();
        totalPushDataSize = 0;
    }

    private ShaderCreateInfoEXT MappedCreateInfo(StageBlock stage, out ulong layout)
    {
        var shaderCreateInfo = stage.NewCreateInfo();
        shaderCreateInfo.Flags = ShaderCreateFlagBitsEXT.DescriptorHeapBitExt;
        layout = LayoutSeed;

        int mappingCount = stage.ConstantBufferLinks.Count +
                           stage.ShaderResourceViewSlotLinks.Count +
                           stage.SamplerStateSlotLinks.Count +
                           stage.UnorderedAccessViewSlotLinks.Count;

        if (mappingCount == 0)
        {
            return shaderCreateInfo;
        }

        var heapProperties = graphicsDevice.Adapter.DeviceHeapProperties;
        var mappings = new DescriptorSetAndBindingMappingEXT[mappingCount];
        int mapIdx = 0;

        foreach (var link in stage.ConstantBufferLinks)
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
            mappings[mapIdx].SourceData.PushAddressOffset = parameterPushOffsets[link.Parameter];
            mapIdx++;
        }

        foreach (var link in stage.ShaderResourceViewSlotLinks)
        {
            mappings[mapIdx] = PushIndexed(link,
                link.IsStorageBuffer
                    ? SpirvResourceTypeFlagBitsEXT.ReadOnlyStorageBufferBitExt
                    : SpirvResourceTypeFlagBitsEXT.SampledImageBitExt | SpirvResourceTypeFlagBitsEXT.ReadOnlyImageBitExt,
                (uint)(link.IsStorageBuffer ? heapProperties.BufferDescriptorSize : heapProperties.ImageDescriptorSize));
            mapIdx++;
        }

        foreach (var link in stage.SamplerStateSlotLinks)
        {
            mappings[mapIdx] = PushIndexed(link, SpirvResourceTypeFlagBitsEXT.SamplerBitExt,
                (uint)heapProperties.SamplerDescriptorSize);
            mapIdx++;
        }

        foreach (var link in stage.UnorderedAccessViewSlotLinks)
        {
            mappings[mapIdx] = PushIndexed(link,
                SpirvResourceTypeFlagBitsEXT.ReadWriteImageBitExt | SpirvResourceTypeFlagBitsEXT.ReadWriteStorageBufferBitExt,
                (uint)heapProperties.BufferDescriptorSize);
            mapIdx++;
        }

        Array.Sort(mappings, (a, b) =>
        {
            int setCmp = a.DescriptorSet.CompareTo(b.DescriptorSet);
            if (setCmp != 0) return setCmp;
            return a.FirstBinding.CompareTo(b.FirstBinding);
        });

        foreach (var mapping in mappings)
        {
            layout = Mix(layout, ((ulong)mapping.DescriptorSet << 32) | mapping.FirstBinding);
            layout = Mix(layout, ((ulong)mapping.ResourceMask << 32) | (uint)mapping.Source);
            layout = mapping.Source == DescriptorMappingSourceEXT.PushAddressExt
                ? Mix(layout, mapping.SourceData.PushAddressOffset)
                : Mix(layout, ((ulong)mapping.SourceData.PushIndex.PushOffset << 32) |
                              mapping.SourceData.PushIndex.HeapArrayStride);
        }

        shaderCreateInfo.PNext = new ShaderDescriptorSetAndBindingMappingInfoEXT
        {
            MappingCount = (uint)mappingCount,
            PMappings = mappings
        };
        return shaderCreateInfo;
    }

    private DescriptorSetAndBindingMappingEXT PushIndexed(SlotLink link, SpirvResourceTypeFlagBitsEXT resourceMask,
        uint heapArrayStride)
    {
        var mapping = new DescriptorSetAndBindingMappingEXT
        {
            DescriptorSet = link.DescriptorSet,
            FirstBinding = link.SlotIndex,
            BindingCount = 1,
            ResourceMask = resourceMask,
            Source = DescriptorMappingSourceEXT.HeapWithPushIndexExt
        };
        mapping.SourceData = new DescriptorMappingSourceDataEXT();
        mapping.SourceData.PushIndex = new DescriptorMappingSourcePushIndexEXT
        {
            PushOffset = parameterPushOffsets[link.Parameter],
            HeapOffset = 0,
            HeapIndexStride = 1,
            HeapArrayStride = heapArrayStride
        };
        return mapping;
    }
}
