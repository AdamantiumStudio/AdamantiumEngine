using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Adamantium.EffectsCompiler;
using Adamantium.Vulkan.Core;
using Adamantium.Vulkan.Core.Interop;

namespace Adamantium.Graphics.Effects;

/// <summary>The shaders of a pass on descriptor sets and their layout: the pass's resources pushed as one set through an
/// update template, a texture array of no length read from the <see cref="DescriptorSetManager"/>'s shared set.</summary>
internal sealed class SetsEffectPassShaders : EffectPassShaders
{
    /// <summary>The bytes one pushed descriptor takes in the array the template reads.</summary>
    internal static readonly int InfoSize =
        Math.Max(Unsafe.SizeOf<VkDescriptorImageInfo>(), Unsafe.SizeOf<VkDescriptorBufferInfo>());

    private DescriptorSetLayout pushLayout;

    internal SetsEffectPassShaders(GraphicsDevice device, EffectData data, EffectData.Pass pass, string effectName,
        string techniqueName, string passName)
        : base(device, data, pass, effectName, techniqueName, passName)
    {
    }

    /// <summary>The set the pass's resources are pushed as.</summary>
    public uint PushSet { get; private set; }

    /// <summary>The texture arrays of no length the pass reads, and the sets they are bound as.</summary>
    public (uint Set, DescriptorSetManager.TextureArray Array)[] TextureArrays { get; private set; } = [];

    /// <summary>Where the sets are bound: the graphics or the compute pipeline.</summary>
    public PipelineBindPoint BindPoint { get; private set; }

    /// <summary>The pipeline layout of the shaders.</summary>
    public PipelineLayout Layout { get; private set; }

    /// <summary>Writes the pushed set from an array of <see cref="InfoSize"/>-byte descriptors in the order the pass's
    /// data numbered them; null when the pass pushes none.</summary>
    public DescriptorUpdateTemplate Template { get; private set; }

    /// <inheritdoc />
    public override void Dispose()
    {
        base.Dispose();
        var device = graphicsDevice.LogicalDevice;
        if (Template != null)
        {
            device.DestroyDescriptorUpdateTemplate(Template);
        }

        if (Layout != null)
        {
            device.DestroyPipelineLayout(Layout);
        }

        if (pushLayout != null)
        {
            device.DestroyDescriptorSetLayout(pushLayout);
        }

        Template = null;
        Layout = null;
        pushLayout = null;
    }

    private protected override void CreateShaders()
    {
        var manager = (DescriptorSetManager)graphicsDevice.MainDevice.DescriptorManager;
        var stageMask = (ShaderStageFlagBits)0;
        foreach (var stage in stages)
        {
            stageMask |= stage;
        }

        List<(EffectData.PassResource Resource, DescriptorType Type)> pushed = [];
        var arrays = new SortedDictionary<uint, uint>();
        var pushSets = new SortedSet<uint>();
        foreach (var resource in Pass.Resources.OrderBy(resource => resource.Descriptor))
        {
            if (resource.Descriptor < 0)
            {
                if (arrays.TryGetValue(resource.Set, out var other))
                {
                    throw new NotSupportedException(
                        $"{passName}: on descriptor sets a texture array of no length needs a set of its own; set " +
                        $"{resource.Set} has two, at bindings {other} and {resource.Binding}");
                }

                arrays[resource.Set] = resource.Binding;
                continue;
            }

            pushSets.Add(resource.Set);
            pushed.Add((resource, GetDescriptorType(resource.Type)));
        }

        if (pushSets.Count > 1)
        {
            throw new NotSupportedException(
                $"{passName}: on descriptor sets a pass's resources must share one set; they use sets " +
                string.Join(", ", pushSets));
        }

        PushSet = pushSets.Count == 1 ? pushSets.Min : 0;
        if (arrays.ContainsKey(PushSet) && pushed.Count > 0)
        {
            throw new NotSupportedException(
                $"{passName}: on descriptor sets a texture array of no length needs a set of its own; it shares set " +
                $"{PushSet} with the pass's other resources");
        }

        BindPoint = (stageMask & ShaderStageFlagBits.ComputeBit) != 0 ? PipelineBindPoint.Compute : PipelineBindPoint.Graphics;
        CheckLimits(pushed);

        var bindings = new DescriptorSetLayoutBinding[pushed.Count];
        var entries = new DescriptorUpdateTemplateEntry[pushed.Count];
        var layoutKey = Mix(LayoutSeed, PushSet);
        for (var i = 0; i < pushed.Count; i++)
        {
            var (resource, type) = pushed[i];
            var count = Math.Max(resource.Count, 1);
            bindings[i] = new DescriptorSetLayoutBinding
            {
                Binding = resource.Binding, DescriptorType = type, DescriptorCount = count, StageFlags = stageMask
            };
            entries[i] = new DescriptorUpdateTemplateEntry
            {
                DstBinding = resource.Binding,
                DescriptorCount = count,
                DescriptorType = type,
                Offset = (nuint)(resource.Descriptor * InfoSize),
                Stride = (nuint)InfoSize
            };
            layoutKey = Mix(layoutKey, ((ulong)resource.Binding << 32) | (uint)type);
            layoutKey = Mix(layoutKey, ((ulong)count << 32) | (uint)stageMask);
        }

        foreach (var (set, binding) in arrays)
        {
            layoutKey = Mix(layoutKey, (1UL << 63) | ((ulong)set << 32) | binding);
        }

        var setCount = 0u;
        if (pushed.Count > 0)
        {
            setCount = PushSet + 1;
        }

        if (arrays.Count > 0)
        {
            setCount = Math.Max(setCount, arrays.Keys.Max() + 1);
        }

        var setLayouts = new DescriptorSetLayout[setCount];
        if (pushed.Count > 0)
        {
            var layoutInfo = new DescriptorSetLayoutCreateInfo
            {
                Flags = DescriptorSetLayoutCreateFlagBits.PushDescriptorBit,
                BindingCount = (uint)bindings.Length,
                PBindings = bindings
            };
            Check(graphicsDevice.LogicalDevice.CreateDescriptorSetLayout(layoutInfo, null, out pushLayout),
                "its descriptor set layout");
            setLayouts[PushSet] = pushLayout;
        }

        var textureArrays = new (uint, DescriptorSetManager.TextureArray)[arrays.Count];
        var arrayIndex = 0;
        foreach (var (set, binding) in arrays)
        {
            var array = manager.TextureArrayAt(binding);
            setLayouts[set] = array.Layout;
            textureArrays[arrayIndex++] = (set, array);
        }

        TextureArrays = textureArrays;
        for (var set = 0; set < setLayouts.Length; set++)
        {
            setLayouts[set] ??= manager.EmptyLayout;
        }

        Layout = graphicsDevice.LogicalDevice.CreatePipelineLayout(new PipelineLayoutCreateInfo
        {
            SetLayoutCount = setCount,
            PSetLayouts = setLayouts
        });

        if (Pass.DescriptorCount > 0)
        {
            var templateInfo = new DescriptorUpdateTemplateCreateInfo
            {
                DescriptorUpdateEntryCount = (uint)entries.Length,
                PDescriptorUpdateEntries = entries,
                TemplateType = DescriptorUpdateTemplateType.PushDescriptors,
                PipelineBindPoint = BindPoint,
                PipelineLayout = Layout,
                Set = PushSet
            };
            Check(graphicsDevice.LogicalDevice.CreateDescriptorUpdateTemplate(templateInfo, null, out var template),
                "its descriptor update template");
            Template = template;
        }

        for (var i = 0; i < stages.Length; i++)
        {
            var createInfo = NewCreateInfo(i);
            createInfo.SetLayoutCount = setCount;
            createInfo.PSetLayouts = setLayouts;
            CreateShader(i, createInfo, layoutKey);
        }
    }

    private void CheckLimits(List<(EffectData.PassResource Resource, DescriptorType Type)> pushed)
    {
        var adapter = graphicsDevice.Adapter;
        if (Pass.DescriptorCount > adapter.MaxPushDescriptors)
        {
            throw new NotSupportedException(
                $"{passName}: {Pass.DescriptorCount} descriptors, and this GPU pushes at most " +
                $"{adapter.MaxPushDescriptors} at once");
        }

        var range = adapter.AdapterProperties.Limits.MaxUniformBufferRange;
        foreach (var (resource, type) in pushed)
        {
            var size = type == DescriptorType.UniformBuffer ? GetConstantBufferSize(resource.Name) : 0;
            if (size > range)
            {
                throw new NotSupportedException(
                    $"{passName}: constant buffer {resource.Name} is {size} bytes, and this GPU reads at most {range} " +
                    "through one");
            }
        }
    }

    private ulong GetConstantBufferSize(string name)
    {
        foreach (var shader in effectData.Shaders)
        {
            foreach (var buffer in shader.ConstantBuffers)
            {
                if (buffer.Name == name && buffer.Size > 0)
                {
                    return buffer.Size;
                }
            }
        }

        return 0;
    }

    private static DescriptorType GetDescriptorType(EffectParameterType type) => type switch
    {
        EffectParameterType.UniformBuffer => DescriptorType.UniformBuffer,
        EffectParameterType.Sampler => DescriptorType.Sampler,
        EffectParameterType.StorageBuffer => DescriptorType.StorageBuffer,
        _ => DescriptorType.SampledImage
    };

    private static void Check(Result result, string what)
    {
        if (result != Result.Success)
        {
            throw new InvalidOperationException($"Could not create {what}: {result}");
        }
    }
}
