using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Adamantium.Core;
using Adamantium.EffectsCompiler;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.EffectsFramework;
using Adamantium.Vulkan.Core;
using Adamantium.Vulkan.Core.Interop;
using EffectTechnique = Adamantium.Graphics.Core.EffectsFramework.EffectTechnique;

namespace Adamantium.Graphics.Effects;

/// <summary>A pass on descriptor sets: its resources pushed on every Apply, a texture array of no length read from the
/// <see cref="DescriptorSetManager"/>'s shared set.</summary>
internal sealed class SetsEffectPass : EffectPass
{
    private static readonly int InfoSize =
        Math.Max(Unsafe.SizeOf<VkDescriptorImageInfo>(), Unsafe.SizeOf<VkDescriptorBufferInfo>());

    private const ulong WholeSize = ulong.MaxValue;

    private enum Kind { ConstantBuffer, Texture, Sampler, Buffer }

    private readonly record struct Pushed(Kind Kind, EffectParameter Parameter, EffectConstantBuffer ConstantBuffer,
        EffectData.Parameter Description, uint Count, int FirstDescriptor);

    private Pushed[] _pushed = [];
    private int _descriptorCount;
    private uint _pushSet;
    private (uint Set, DescriptorSetManager.TextureArray Array)[] _textureArrays = [];
    private PipelineBindPoint _bindPoint;
    private PipelineLayout _layout;
    private DescriptorSetLayout _pushLayout;
    private DescriptorUpdateTemplate _template;

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
        byte* infos = stackalloc byte[_descriptorCount * InfoSize];

        for (int i = 0; i < _pushed.Length; i++)
        {
            var pushed = _pushed[i];
            var info = infos + pushed.FirstDescriptor * InfoSize;
            switch (pushed.Kind)
            {
                case Kind.ConstantBuffer:
                {
                    var backing = pushed.ConstantBuffer.BackingBuffer;
                    if (hashing)
                    {
                        device.Hash((ulong)i);
                        device.HashBytes(new ReadOnlySpan<byte>(backing.DataPointer.ToPointer(), (int)backing.Size));
                    }

                    if (swallowing) break;

                    var alignment = graphicsDevice.Adapter.AdapterProperties.Limits.MinUniformBufferOffsetAlignment;
                    var (page, offset) = CurrentBufferPool.UploadRange(pushed.ConstantBuffer, alignment);
                    *(VkDescriptorBufferInfo*)info = new VkDescriptorBufferInfo
                    {
                        buffer = page.GetBuffer(), offset = offset, range = backing.Size
                    };
                    break;
                }
                case Kind.Texture:
                {
                    var bound = linker.GetSampledImages(pushed.Description);
                    for (int element = 0; element < pushed.Count; element++)
                    {
                        ITexture texture = element < bound.Length ? bound[element]?.Resource : null;
                        if (texture == null || texture is DisposableObject { IsDisposed: true })
                        {
                            ReportUnbound(pushed.Parameter);
                            texture = manager.FallbackTexture;
                        }

                        var view = texture.GetImageView();
                        if (hashing) device.Hash(manager.IdentityOf(texture));
                        ((VkDescriptorImageInfo*)(info + element * InfoSize))[0] = new VkDescriptorImageInfo
                        {
                            imageView = view, imageLayout = texture.ImageLayout
                        };
                    }

                    break;
                }
                case Kind.Sampler:
                {
                    var bound = linker.GetSamplers(pushed.Description);
                    for (int element = 0; element < pushed.Count; element++)
                    {
                        var sampler = element < bound.Length ? bound[element]?.Sampler : null;
                        if (sampler == null)
                        {
                            ReportUnbound(pushed.Parameter);
                            sampler = manager.FallbackSampler;
                        }

                        if (hashing) device.Hash((ulong)sampler.NativePointer);
                        ((VkDescriptorImageInfo*)(info + element * InfoSize))[0] = new VkDescriptorImageInfo
                        {
                            sampler = sampler
                        };
                    }

                    break;
                }
                case Kind.Buffer:
                {
                    var bound = linker.GetStorageBuffers(pushed.Description);
                    for (int element = 0; element < pushed.Count; element++)
                    {
                        var buffer = element < bound.Length ? bound[element]?.Resource : null;
                        if (buffer == null || buffer.IsDisposed)
                        {
                            ReportUnbound(pushed.Parameter);
                            buffer = manager.FallbackBuffer;
                        }

                        var handle = buffer.GetBuffer();
                        if (hashing) device.Hash(manager.IdentityOf(buffer));
                        ((VkDescriptorBufferInfo*)(info + element * InfoSize))[0] = new VkDescriptorBufferInfo
                        {
                            buffer = handle, offset = 0, range = WholeSize
                        };
                    }

                    break;
                }
            }
        }

        if (hashing)
        {
            for (int i = 0; i < _textureArrays.Length; i++)
            {
                device.Hash(_textureArrays[i].Array.Generation);
            }
        }

        BindShaders();
        if (swallowing) return;

        var commands = graphicsDevice.CurrentCommandBuffer;
        if (_descriptorCount > 0)
        {
            commands.PushDescriptorSetWithTemplate(_template, _layout, _pushSet, (nuint)infos);
        }

        for (int i = 0; i < _textureArrays.Length; i++)
        {
            commands.BindDescriptorSets(_bindPoint, _layout, _textureArrays[i].Set, 1, _textureArrays[i].Array.Set, 0,
                ReadOnlySpan<uint>.Empty);
        }
    }

    private protected override void CreateShaders()
    {
        var manager = Manager;
        var pushedBindings = new SortedDictionary<uint, (DescriptorType Type, Pushed Pushed)>();
        var arrays = new SortedDictionary<uint, uint>();
        var pushSets = new HashSet<uint>();
        var stageMask = (ShaderStageFlagBits)0;

        foreach (var stage in pipelineStages)
        {
            stageMask |= stage.Stage;
            foreach (var link in stage.ConstantBufferLinks)
            {
                Push(link.Parameter.Set, link.Parameter.Binding, DescriptorType.UniformBuffer,
                    new Pushed(Kind.ConstantBuffer, link.Parameter, link.ConstantBuffer, null, 1, 0));
            }

            foreach (var parameter in stage.SampledImages)
            {
                if (parameter.ElementCount == 0)
                {
                    if (arrays.TryGetValue(parameter.Set, out var other) && other != parameter.Binding)
                    {
                        throw new NotSupportedException(
                            $"{Effect.Name}.{Name}: on descriptor sets a texture array of no length needs a set of its " +
                            $"own; set {parameter.Set} has two, at bindings {other} and {parameter.Binding}");
                    }

                    arrays[parameter.Set] = parameter.Binding;
                }
                else
                {
                    Push(parameter.Set, parameter.Binding, DescriptorType.SampledImage, Of(Kind.Texture, parameter));
                }
            }

            foreach (var parameter in stage.Samplers)
            {
                Push(parameter.Set, parameter.Binding, DescriptorType.Sampler, Of(Kind.Sampler, parameter));
            }

            foreach (var parameter in stage.StorageBuffers)
            {
                Push(parameter.Set, parameter.Binding, DescriptorType.StorageBuffer, Of(Kind.Buffer, parameter));
            }
        }

        if (pushSets.Count > 1)
        {
            throw new NotSupportedException(
                $"{Effect.Name}.{Name}: on descriptor sets a pass's resources must share one set; they use sets " +
                string.Join(", ", pushSets.Order()));
        }

        _pushSet = pushSets.Count == 1 ? pushSets.First() : 0;
        if (arrays.ContainsKey(_pushSet) && pushedBindings.Count > 0)
        {
            throw new NotSupportedException(
                $"{Effect.Name}.{Name}: on descriptor sets a texture array of no length needs a set of its own; it shares " +
                $"set {_pushSet} with the pass's other resources");
        }

        _bindPoint = (stageMask & ShaderStageFlagBits.ComputeBit) != 0 ? PipelineBindPoint.Compute : PipelineBindPoint.Graphics;

        var bindings = new List<DescriptorSetLayoutBinding>();
        var entries = new List<DescriptorUpdateTemplateEntry>();
        var pushed = new List<Pushed>();
        var descriptor = 0;
        foreach (var (binding, (type, value)) in pushedBindings)
        {
            bindings.Add(new DescriptorSetLayoutBinding
            {
                Binding = binding, DescriptorType = type, DescriptorCount = value.Count, StageFlags = stageMask
            });
            entries.Add(new DescriptorUpdateTemplateEntry
            {
                DstBinding = binding,
                DescriptorCount = value.Count,
                DescriptorType = type,
                Offset = (nuint)(descriptor * InfoSize),
                Stride = (nuint)InfoSize
            });
            pushed.Add(value with { FirstDescriptor = descriptor });
            descriptor += (int)value.Count;
        }

        _pushed = pushed.ToArray();
        _descriptorCount = descriptor;

        var limits = graphicsDevice.Adapter.AdapterProperties.Limits;
        if (_descriptorCount > graphicsDevice.Adapter.MaxPushDescriptors)
        {
            throw new NotSupportedException(
                $"{Effect.Name}.{Name}: {_descriptorCount} descriptors, and this GPU pushes at most " +
                $"{graphicsDevice.Adapter.MaxPushDescriptors} at once");
        }

        foreach (var value in _pushed)
        {
            if (value.Kind == Kind.ConstantBuffer && value.ConstantBuffer.BackingBuffer.Size > limits.MaxUniformBufferRange)
            {
                throw new NotSupportedException(
                    $"{Effect.Name}.{Name}: constant buffer {value.Parameter.Name} is {value.ConstantBuffer.BackingBuffer.Size} " +
                    $"bytes, and this GPU reads at most {limits.MaxUniformBufferRange} through one");
            }
        }

        var layoutKey = Mix(LayoutSeed, _pushSet);
        foreach (var (binding, (type, value)) in pushedBindings)
        {
            layoutKey = Mix(layoutKey, ((ulong)binding << 32) | (uint)type);
            layoutKey = Mix(layoutKey, ((ulong)value.Count << 32) | (uint)stageMask);
        }

        foreach (var (set, binding) in arrays)
        {
            layoutKey = Mix(layoutKey, (1UL << 63) | ((ulong)set << 32) | binding);
        }

        var setCount = 0u;
        if (pushedBindings.Count > 0) setCount = _pushSet + 1;
        if (arrays.Count > 0) setCount = Math.Max(setCount, arrays.Keys.Max() + 1);

        var setLayouts = new DescriptorSetLayout[setCount];
        if (pushedBindings.Count > 0)
        {
            var layoutInfo = new DescriptorSetLayoutCreateInfo
            {
                Flags = DescriptorSetLayoutCreateFlagBits.PushDescriptorBit,
                BindingCount = (uint)bindings.Count,
                PBindings = bindings.ToArray()
            };
            Check(graphicsDevice.LogicalDevice.CreateDescriptorSetLayout(layoutInfo, null, out _pushLayout),
                "its descriptor set layout");
            setLayouts[_pushSet] = _pushLayout;
        }

        _textureArrays = new (uint, DescriptorSetManager.TextureArray)[arrays.Count];
        var arrayIndex = 0;
        foreach (var (set, binding) in arrays)
        {
            var array = manager.TextureArrayAt(binding);
            setLayouts[set] = array.Layout;
            _textureArrays[arrayIndex++] = (set, array);
        }

        for (var set = 0; set < setLayouts.Length; set++)
        {
            setLayouts[set] ??= manager.EmptyLayout;
        }

        _layout = graphicsDevice.LogicalDevice.CreatePipelineLayout(new PipelineLayoutCreateInfo
        {
            SetLayoutCount = setCount,
            PSetLayouts = setLayouts
        });

        if (_descriptorCount > 0)
        {
            var templateInfo = new DescriptorUpdateTemplateCreateInfo
            {
                DescriptorUpdateEntryCount = (uint)entries.Count,
                PDescriptorUpdateEntries = entries.ToArray(),
                TemplateType = DescriptorUpdateTemplateType.PushDescriptors,
                PipelineBindPoint = _bindPoint,
                PipelineLayout = _layout,
                Set = _pushSet
            };
            Check(graphicsDevice.LogicalDevice.CreateDescriptorUpdateTemplate(templateInfo, null, out _template),
                "its descriptor update template");
        }

        foreach (var stage in pipelineStages)
        {
            var createInfo = stage.NewCreateInfo();
            createInfo.SetLayoutCount = setCount;
            createInfo.PSetLayouts = setLayouts;
            stage.CreateShader(createInfo, layoutKey);
        }

        void Push(uint set, uint binding, DescriptorType type, Pushed value)
        {
            pushSets.Add(set);
            if (pushedBindings.TryGetValue(binding, out var existing))
            {
                if (existing.Type != type || existing.Pushed.Parameter != value.Parameter)
                {
                    throw new NotSupportedException(
                        $"{Effect.Name}.{Name}: binding {binding} of set {set} is both " +
                        $"{existing.Pushed.Parameter?.Name} ({existing.Type}) and {value.Parameter?.Name} ({type})");
                }

                return;
            }

            pushedBindings.Add(binding, (type, value));
        }

        Pushed Of(Kind kind, EffectParameter parameter) =>
            new(kind, parameter, null, parameter.ParameterDescription, Math.Max(parameter.ElementCount, 1), 0);
    }

    private static void Check(Result result, string what)
    {
        if (result != Result.Success)
        {
            throw new InvalidOperationException($"Could not create {what}: {result}");
        }
    }

    private protected override void ReleaseResourceLayout()
    {
        if (_layout != null || _pushLayout != null || _template != null)
        {
            graphicsDevice.MainDevice.RetireResource(new RetiredLayout(graphicsDevice.LogicalDevice, _layout, _pushLayout,
                _template));
        }

        _layout = null;
        _pushLayout = null;
        _template = null;
        _pushed = [];
        _descriptorCount = 0;
        _textureArrays = [];
    }

    private sealed class RetiredLayout(Device device, PipelineLayout layout, DescriptorSetLayout pushLayout,
        DescriptorUpdateTemplate template) : IDisposable
    {
        public void Dispose()
        {
            if (template != null) device.DestroyDescriptorUpdateTemplate(template);
            if (layout != null) device.DestroyPipelineLayout(layout);
            if (pushLayout != null) device.DestroyDescriptorSetLayout(pushLayout);
        }
    }
}
