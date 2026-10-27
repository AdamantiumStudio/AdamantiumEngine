using System;
using Adamantium.Core;
using Adamantium.EffectsCompiler;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.EffectsFramework;
using Adamantium.Vulkan.Core;
using Adamantium.Vulkan.Core.Interop;
using EffectTechnique = Adamantium.Graphics.Core.EffectsFramework.EffectTechnique;

namespace Adamantium.Graphics.Effects;

/// <summary>A pass on descriptor sets: its resources pushed on every Apply in the order the pass's data numbered them, a
/// texture array of no length read from the <see cref="DescriptorSetManager"/>'s shared set.</summary>
internal sealed class SetsEffectPass : EffectPass
{
    private const ulong WholeSize = ulong.MaxValue;

    internal SetsEffectPass(Logger logger, Effect effect, EffectTechnique technique, EffectData.Pass pass, string name)
        : base(logger, effect, technique, pass, name)
    {
    }

    private DescriptorSetManager Manager => (DescriptorSetManager)((GraphicsDevice)graphicsDevice).DescriptorManager;

    private protected override unsafe void ApplyResources()
    {
        var device = (GraphicsDevice)graphicsDevice;
        var hashing = device.IsHashing;
        var swallowing = device.IsSwallowing;
        var linker = (EffectResourceLinker)Effect.ResourceLinker;
        var manager = Manager;
        var shaders = (SetsEffectPassShaders)Shaders;
        var infoSize = SetsEffectPassShaders.InfoSize;
        var descriptorCount = (int)Data.DescriptorCount;
        byte* infos = stackalloc byte[descriptorCount * infoSize];

        foreach (var bound in Resources)
        {
            var resource = bound.Resource;
            if (resource.Descriptor < 0)
            {
                continue;
            }

            var info = infos + resource.Descriptor * infoSize;
            var count = Math.Max(resource.Count, 1);
            switch (resource.Type)
            {
                case EffectParameterType.UniformBuffer:
                {
                    var backing = bound.ConstantBuffer.BackingBuffer;
                    if (hashing)
                    {
                        device.Hash((ulong)resource.Descriptor);
                        device.HashBytes(new ReadOnlySpan<byte>(backing.DataPointer.ToPointer(), (int)backing.Size));
                    }

                    if (swallowing) break;

                    var alignment = graphicsDevice.Adapter.AdapterProperties.Limits.MinUniformBufferOffsetAlignment;
                    var (page, offset) = CurrentBufferPool.UploadRange(bound.ConstantBuffer, alignment);
                    *(VkDescriptorBufferInfo*)info = new VkDescriptorBufferInfo
                    {
                        buffer = page.GetBuffer(), offset = offset, range = backing.Size
                    };
                    break;
                }
                case EffectParameterType.Sampler:
                {
                    var samplers = linker.GetSamplers(bound.Parameter.ParameterDescription);
                    for (int element = 0; element < count; element++)
                    {
                        var sampler = element < samplers.Length ? samplers[element]?.Sampler : null;
                        if (sampler == null)
                        {
                            ReportUnbound(bound.Parameter);
                            sampler = manager.FallbackSampler;
                        }

                        if (hashing) device.Hash((ulong)sampler.NativePointer);
                        ((VkDescriptorImageInfo*)(info + element * infoSize))[0] = new VkDescriptorImageInfo
                        {
                            sampler = sampler
                        };
                    }

                    break;
                }
                case EffectParameterType.StorageBuffer:
                {
                    var buffers = linker.GetStorageBuffers(bound.Parameter.ParameterDescription);
                    for (int element = 0; element < count; element++)
                    {
                        var buffer = element < buffers.Length ? buffers[element]?.Resource : null;
                        if (buffer == null || buffer.IsDisposed)
                        {
                            ReportUnbound(bound.Parameter);
                            buffer = manager.FallbackBuffer;
                        }

                        var handle = buffer.GetBuffer();
                        if (hashing) device.Hash(manager.IdentityOf(buffer));
                        ((VkDescriptorBufferInfo*)(info + element * infoSize))[0] = new VkDescriptorBufferInfo
                        {
                            buffer = handle, offset = 0, range = WholeSize
                        };
                    }

                    break;
                }
                default:
                {
                    var images = linker.GetSampledImages(bound.Parameter.ParameterDescription);
                    for (int element = 0; element < count; element++)
                    {
                        ITexture texture = element < images.Length ? images[element]?.Resource : null;
                        if (texture == null || texture is DisposableObject { IsDisposed: true })
                        {
                            ReportUnbound(bound.Parameter);
                            texture = manager.FallbackTexture;
                        }

                        var view = texture.GetImageView();
                        if (hashing) device.Hash(manager.IdentityOf(texture));
                        ((VkDescriptorImageInfo*)(info + element * infoSize))[0] = new VkDescriptorImageInfo
                        {
                            imageView = view, imageLayout = texture.ImageLayout
                        };
                    }

                    break;
                }
            }
        }

        var textureArrays = shaders.TextureArrays;
        if (hashing)
        {
            for (int i = 0; i < textureArrays.Length; i++)
            {
                device.Hash(textureArrays[i].Array.Generation);
            }
        }

        BindShaders();
        if (swallowing) return;

        var commands = graphicsDevice.CurrentCommandBuffer;
        if (descriptorCount > 0)
        {
            commands.PushDescriptorSetWithTemplate(shaders.Template, shaders.Layout, shaders.PushSet, (nuint)infos);
        }

        for (int i = 0; i < textureArrays.Length; i++)
        {
            commands.BindDescriptorSets(shaders.BindPoint, shaders.Layout, textureArrays[i].Set, 1,
                textureArrays[i].Array.Set, 0, ReadOnlySpan<uint>.Empty);
        }
    }
}
