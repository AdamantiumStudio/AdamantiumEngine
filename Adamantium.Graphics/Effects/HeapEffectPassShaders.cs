using System;
using System.Collections.Generic;
using Adamantium.EffectsCompiler;
using Adamantium.Vulkan.Core;

namespace Adamantium.Graphics.Effects;

/// <summary>The shaders of a pass on the descriptor heap (VK_EXT_descriptor_heap): each reads a constant buffer's address,
/// and the heap index of a texture, sampler or buffer, where the pass's data placed it in the push data.</summary>
internal sealed class HeapEffectPassShaders : EffectPassShaders
{
    internal HeapEffectPassShaders(GraphicsDevice device, EffectData data, EffectData.Pass pass, string effectName,
        string techniqueName, string passName)
        : base(device, data, pass, effectName, techniqueName, passName)
    {
    }

    private protected override void CreateShaders()
    {
        for (var i = 0; i < stages.Length; i++)
        {
            var createInfo = MappedCreateInfo(i, out var layout);
            CreateShader(i, createInfo, layout);
        }
    }

    private ShaderCreateInfoEXT MappedCreateInfo(int stage, out ulong layout)
    {
        var shaderCreateInfo = NewCreateInfo(stage);
        shaderCreateInfo.Flags |= ShaderCreateFlagBitsEXT.DescriptorHeapBitExt;
        layout = LayoutSeed;

        var heapProperties = graphicsDevice.Adapter.DeviceHeapProperties;
        List<DescriptorSetAndBindingMappingEXT> mappings = [];
        foreach (var declared in effectData.Shaders[Pass.Stages[stage].Index].ResourceParameters)
        {
            var resource = Pass.Resources.Find(placed => placed.Name == declared.Name);
            if (resource == null)
            {
                continue;
            }

            mappings.Add(declared.Type switch
            {
                EffectParameterType.UniformBuffer => PushAddress(resource),
                EffectParameterType.Sampler => PushIndexed(resource, SpirvResourceTypeFlagBitsEXT.SamplerBitExt,
                    (uint)heapProperties.SamplerDescriptorSize),
                EffectParameterType.StorageBuffer => PushIndexed(resource,
                    declared.Access == EffectResourceAccess.ReadOnly
                        ? SpirvResourceTypeFlagBitsEXT.ReadOnlyStorageBufferBitExt
                        : SpirvResourceTypeFlagBitsEXT.ReadWriteStorageBufferBitExt,
                    (uint)heapProperties.BufferDescriptorSize),
                _ => PushIndexed(resource,
                    SpirvResourceTypeFlagBitsEXT.SampledImageBitExt | SpirvResourceTypeFlagBitsEXT.ReadOnlyImageBitExt,
                    (uint)heapProperties.ImageDescriptorSize)
            });
        }

        if (mappings.Count == 0)
        {
            return shaderCreateInfo;
        }

        mappings.Sort((a, b) =>
        {
            var setCmp = a.DescriptorSet.CompareTo(b.DescriptorSet);
            return setCmp != 0 ? setCmp : a.FirstBinding.CompareTo(b.FirstBinding);
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
            MappingCount = (uint)mappings.Count,
            PMappings = mappings.ToArray()
        };
        return shaderCreateInfo;
    }

    private static DescriptorSetAndBindingMappingEXT PushAddress(EffectData.PassResource resource)
    {
        var mapping = new DescriptorSetAndBindingMappingEXT
        {
            DescriptorSet = resource.Set,
            FirstBinding = resource.Binding,
            BindingCount = 1,
            ResourceMask = SpirvResourceTypeFlagBitsEXT.UniformBufferBitExt,
            Source = DescriptorMappingSourceEXT.PushAddressExt
        };
        mapping.SourceData = new DescriptorMappingSourceDataEXT();
        mapping.SourceData.PushAddressOffset = resource.PushOffset;
        return mapping;
    }

    private static DescriptorSetAndBindingMappingEXT PushIndexed(EffectData.PassResource resource,
        SpirvResourceTypeFlagBitsEXT resourceMask, uint heapArrayStride)
    {
        var mapping = new DescriptorSetAndBindingMappingEXT
        {
            DescriptorSet = resource.Set,
            FirstBinding = resource.Binding,
            BindingCount = 1,
            ResourceMask = resourceMask,
            Source = DescriptorMappingSourceEXT.HeapWithPushIndexExt
        };
        mapping.SourceData = new DescriptorMappingSourceDataEXT();
        mapping.SourceData.PushIndex = new DescriptorMappingSourcePushIndexEXT
        {
            PushOffset = resource.PushOffset,
            HeapOffset = 0,
            HeapIndexStride = 1,
            HeapArrayStride = heapArrayStride
        };
        return mapping;
    }
}
