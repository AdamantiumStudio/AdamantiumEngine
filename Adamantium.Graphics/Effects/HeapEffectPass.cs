using System;
using Adamantium.Core;
using Adamantium.EffectsCompiler;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.EffectsFramework;
using Adamantium.Vulkan.Core;
using EffectTechnique = Adamantium.Graphics.Core.EffectsFramework.EffectTechnique;

namespace Adamantium.Graphics.Effects;

/// <summary>A pass on the descriptor heap (VK_EXT_descriptor_heap): constant buffers by their address, textures, samplers
/// and buffers by their heap slot - both written to push data on every Apply, where the pass's data placed them.</summary>
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
        var size = Data.PushDataSize;
        byte* push = stackalloc byte[(int)size];
        var device = (GraphicsDevice)graphicsDevice;
        var hashing = device.IsHashing;
        var swallowing = device.IsSwallowing;

        foreach (var bound in Resources)
        {
            var resource = bound.Resource;
            var offset = resource.PushOffset;
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
                    WriteHeapIndices(bound.Parameter, resourceLinker.GetSamplers(bound.Parameter.ParameterDescription),
                        offset, resource.Count, isBuffer: false, isSampler: true);
                    break;
                case EffectParameterType.StorageBuffer:
                    WriteHeapIndices(bound.Parameter,
                        resourceLinker.GetStorageBuffers(bound.Parameter.ParameterDescription), offset, resource.Count,
                        isBuffer: true, isSampler: false);
                    break;
                case var _ when resource.Count == 0:
                    *(uint*)(push + offset) = 0;
                    if (hashing)
                    {
                        device.Hash((ulong)offset << 32);
                    }

                    break;
                default:
                    WriteHeapIndices(bound.Parameter,
                        resourceLinker.GetSampledImages(bound.Parameter.ParameterDescription), offset, resource.Count,
                        isBuffer: false, isSampler: false);
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

        void WriteHeapIndices(EffectParameter parameter, IHeapResource[] resources, uint offset, uint count,
            bool isBuffer, bool isSampler)
        {
            var room = (int)Math.Max(count, 1);
            var elements = Math.Min(isBuffer ? resources.Length : Math.Max(resources.Length, (int)count), room);
            for (var element = 0; element < elements; element++)
            {
                var resource = element < resources.Length ? resources[element] : null;
                var slot = isBuffer ? resource?.GlobalHeapOffset ?? uint.MaxValue
                    : resource is { HasLiveSlot: true } ? resource.GlobalHeapOffset : uint.MaxValue;
                if (slot == uint.MaxValue)
                {
                    ReportUnbound(parameter);
                    if (device.DescriptorManager is IDescriptorHeapManager heap)
                        slot = isSampler ? heap.FallbackSamplerOffset : heap.FallbackTextureOffset;
                }

                var at = offset + (uint)(element * 4);
                *(uint*)(push + at) = slot;
                if (hashing) device.Hash(((ulong)at << 32) | slot);
            }
        }
    }
}
