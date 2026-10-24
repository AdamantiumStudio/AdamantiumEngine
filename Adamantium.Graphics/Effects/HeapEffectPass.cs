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
    // The push-data wrappers, made ONCE. Both are generated as CLASSES, not structs, so `new` here allocated two objects
    // on EVERY Apply - i.e. on every draw call, tens of thousands a second. They are marshaled to a pointer inside
    // PushDataEXT and nothing keeps a reference past the call, so one instance per pass can be refilled and re-sent.
    // Same lesson as the delegate this method already avoids (see WriteHeapOffsets): per-draw garbage in the draw path.
    private readonly HostAddressRangeConstEXT _pushRange = new();
    private readonly PushDataInfoEXT _pushDataInfo = new();

    private readonly Dictionary<EffectParameter, uint> parameterPushOffsets = new();
    private uint totalPushDataSize = 0;

    internal HeapEffectPass(Logger logger, Effect effect, EffectTechnique technique, EffectData.Pass pass, string name)
        : base(logger, effect, technique, pass, name)
    {
    }

    // Which linker resource collection a heap-offset write pulls from - a plain kind so ApplyResources' inner loop needs no
    // per-draw delegate/closure.
    private enum HeapResourceKind { ShaderResource, Sampler, Uav }

    // === VK_EXT_descriptor_heap: CB via push-address; textures/samplers — indices into heaps ===
    private protected override unsafe void ApplyResources()
    {
        var resourceLinker = (EffectResourceLinker)Effect.ResourceLinker;

        // Allocate a stack buffer for the full push-data size of this pass
        byte* pushDataBytes = stackalloc byte[(int)totalPushDataSize];

        // Shared per-frame pool: sub-allocates CB data (a per-draw chunk), without a separate buffer per Apply.
        var dynamicPool = CurrentBufferPool;
        var device = (GraphicsDevice)graphicsDevice;
        var hashing = device.IsHashing;
        var swallowing = device.IsSwallowing;

        for (int stageIndex = 0; stageIndex < stages.Count; stageIndex++)
        {
            var stageBlock = stages[stageIndex];

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
        }

        BindShaders();

        // SEND PUSH DATA — strictly AFTER binding shaders (in the reference, push data goes after bind pipeline).
        // Otherwise binding a shader resets push data before the draw.
        if (totalPushDataSize > 0)
        {
            _pushRange.Address = (nuint)pushDataBytes;
            _pushRange.Size = totalPushDataSize;
            _pushDataInfo.Offset = 0;
            _pushDataInfo.Data = _pushRange;

            device.PushData(_pushDataInfo);
        }

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

        // Constant buffers (PushAddress): reserve 8 bytes for each unique CB to hold its
        // 64-bit GPU address. ApplyResources sub-allocates CB data from the shared pool and writes the address here.
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

    // The stage's shader with each classic (set, binding) remapped to push data: a constant buffer to its address there,
    // the rest to a heap index there. No set layouts. The mappings - where in push data each one reads, which the other
    // stages' resources move - are the layout the binary cache keys by.
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

        // 1. Map the classic cbuffer register(b0) to PushAddress: the shader reads the CB
        // directly via the 64-bit address from push data (written by ApplyResources at this same offset).
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

        // 2. Map each SRV register(t#) to a resource-heap slot. A texture -> a sampled-image descriptor; a
        //    read-only StructuredBuffer (IsStorageBuffer) -> a read-only storage-buffer descriptor (its own
        //    resource mask + the buffer-descriptor stride, not the image one).
        foreach (var link in stage.ShaderResourceViewSlotLinks)
        {
            mappings[mapIdx] = PushIndexed(link,
                link.IsStorageBuffer
                    ? SpirvResourceTypeFlagBitsEXT.ReadOnlyStorageBufferBitExt
                    : SpirvResourceTypeFlagBitsEXT.SampledImageBitExt | SpirvResourceTypeFlagBitsEXT.ReadOnlyImageBitExt,
                (uint)(link.IsStorageBuffer ? heapProperties.BufferDescriptorSize : heapProperties.ImageDescriptorSize));
            mapIdx++;
        }

        // 3. Map the classic sampler register(s0) to an index in the sampler heap
        foreach (var link in stage.SamplerStateSlotLinks)
        {
            mappings[mapIdx] = PushIndexed(link, SpirvResourceTypeFlagBitsEXT.SamplerBitExt,
                (uint)heapProperties.SamplerDescriptorSize);
            mapIdx++;
        }

        // 4. Map the classic UAV register(u0) to an index in the resource heap
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

        // Pass it into native shader creation via a pointer
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
