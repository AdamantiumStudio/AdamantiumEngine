using System;
using Adamantium.Core;
using Adamantium.EffectsCompiler;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.EffectsFramework;
using Adamantium.Vulkan.Core;
using EffectTechnique = Adamantium.Graphics.Core.EffectsFramework.EffectTechnique;

namespace Adamantium.Graphics.Effects;

/// <summary>A pass on the descriptor heap (VK_EXT_descriptor_heap): constant buffers by their address, textures, samplers
/// and buffers by their heap slot - both written to push data on every Apply, where the pass's data placed them. A slot
/// nobody bound reads the heap's stand-in of its kind.</summary>
internal sealed class HeapEffectPass : EffectPass
{
    private readonly HostAddressRangeConstEXT _pushRange = new();
    private readonly PushDataInfoEXT _pushDataInfo = new();

    internal HeapEffectPass(Logger logger, Effect effect, EffectTechnique technique, EffectData.Pass pass, string name)
        : base(logger, effect, technique, pass, name)
    {
    }

    private protected override unsafe void ApplyResources()
    {
        var resourceLinker = (EffectResourceLinker)Effect.ResourceLinker;
        var size = pass.PushDataSize;
        byte* push = stackalloc byte[(int)size];
        var device = (GraphicsDevice)graphicsDevice;
        var heap = (IDescriptorHeapManager)device.DescriptorManager;
        var hashing = device.IsHashing;
        var swallowing = device.IsSwallowing;

        foreach (var bound in resources)
        {
            var resource = bound.Resource;
            var offset = resource.PushOffset;
            var description = bound.Parameter?.ParameterDescription;
            switch (resource.Type)
            {
                case EffectParameterType.UniformBuffer:
                {
                    if (hashing)
                    {
                        var backing = bound.ConstantBuffer.BackingBuffer;
                        device.Hash(offset);
                        device.HashBytes(new ReadOnlySpan<byte>(backing.DataPointer.ToPointer(), (int)backing.Size));
                    }

                    var alignment = graphicsDevice.Adapter.AdapterProperties.Limits.MinUniformBufferOffsetAlignment;
                    *(ulong*)(push + offset) = swallowing ? 0 : CurrentBufferPool.Upload(bound.ConstantBuffer, alignment);
                    break;
                }
                case EffectParameterType.Sampler:
                    WriteHeapSlots(bound.Parameter, resourceLinker.GetSamplers(description), resource);
                    break;
                case EffectParameterType.StorageBuffer:
                    WriteHeapSlots(bound.Parameter, resourceLinker.GetStorageBuffers(description), resource);
                    break;
                case var _ when resource.Count == 0:
                    *(uint*)(push + offset) = 0;
                    if (hashing)
                    {
                        device.Hash((ulong)offset << 32);
                    }

                    break;
                default:
                    WriteHeapSlots(bound.Parameter, resourceLinker.GetSampledImages(description), resource);
                    break;
            }
        }

        BindShaders();

        if (size > 0)
        {
            _pushRange.Address = (nuint)push;
            _pushRange.Size = size;
            _pushDataInfo.Offset = 0;
            _pushDataInfo.Data = _pushRange;

            device.PushData(_pushDataInfo);
        }

        void WriteHeapSlots(EffectParameter parameter, IHeapResource[] bound, EffectData.PassResource placed)
        {
            var elements = Math.Max(placed.Count, 1);
            for (var element = 0; element < elements; element++)
            {
                var resource = element < bound.Length ? bound[element] : null;
                uint slot;
                if (resource is { HasLiveSlot: true })
                {
                    slot = resource.GlobalHeapOffset;
                }
                else
                {
                    ReportUnbound(parameter);
                    slot = placed.Type switch
                    {
                        EffectParameterType.Sampler => heap.FallbackSamplerOffset,
                        EffectParameterType.StorageBuffer => heap.FallbackBufferOffset,
                        _ => heap.FallbackTextureOffset
                    };
                }

                var at = placed.PushOffset + (uint)(element * 4);
                *(uint*)(push + at) = slot;
                if (hashing)
                {
                    device.Hash(((ulong)at << 32) | slot);
                }
            }
        }
    }
}
