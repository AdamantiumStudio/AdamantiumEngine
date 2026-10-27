using System;
using System.Collections.Generic;
using Adamantium.Core;
using Adamantium.EffectsCompiler;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.EffectsFramework;
using Adamantium.Vulkan.Core;

namespace Adamantium.Graphics.Effects;

// Non-generic view of a bound resource's heap slot, so the per-type resource arrays (textures / buffers / samplers) can
// be handled uniformly when only the heap offset matters (the ApplyHeap push-offset write).
internal interface IHeapResource
{
    uint GlobalHeapOffset { get; }

    // A slot to read from: set, and its resource not disposed since - a disposed one's slot is retired or reused.
    bool HasLiveSlot { get; }
}

internal class ResourceInfo<T> : IHeapResource where T : class
{
    private T resource;

    public ResourceInfo()
    {
    }

    public ResourceInfo(T resource)
    {
        Resource = resource;
    }

    public T Resource
    {
        get => resource;
        set
        {
            if (resource != value)
            {
                IsDirty = true;
            }

            resource = value;
        }
    }

    public int SlotIndex { get; set; }

    public int DescriptorSet { get; set; }

    public bool IsDirty { get; internal set; }

    public uint GlobalHeapOffset { get; internal set; } = uint.MaxValue;

    public bool HasLiveSlot =>
        GlobalHeapOffset != uint.MaxValue && resource is not DisposableObject { IsDisposed: true };

    /// <summary>The sampler a bound <see cref="SamplerState"/> stands for, on descriptor sets.</summary>
    public Sampler Sampler { get; internal set; }

    /// <summary>Bound to nothing: what SetResource(null) means.</summary>
    internal void Clear()
    {
        Resource = null;
        GlobalHeapOffset = uint.MaxValue;
        Sampler = null;
    }
}

/// <summary>What an effect's parameters are bound to, by the kind of descriptor each is: uniform buffers, samplers,
/// sampled images and storage buffers - read only or read and written alike, as Vulkan binds them. The heap and
/// descriptor sets each prepare a bound resource for the draws that read it in their own way.</summary>
internal abstract class EffectResourceLinker : IEffectResourceLinker
{
    private static readonly ResourceInfo<SamplerState>[] NoSamplers = [];
    private static readonly ResourceInfo<Texture>[] NoSampledImages = [];
    private static readonly ResourceInfo<Buffer>[] NoStorageBuffers = [];

    /// <summary>The constant buffers bound to uniform-buffer parameters.</summary>
    public Dictionary<EffectData.Parameter, EffectConstantBuffer> ConstantBuffers;

    /// <summary>Total number of resources.</summary>
    public int Count { get; set; }

    public Dictionary<EffectData.Parameter, ResourceInfo<SamplerState>[]> Samplers;

    public Dictionary<EffectData.Parameter, ResourceInfo<Texture>[]> SampledImages;

    public Dictionary<EffectData.Parameter, ResourceInfo<Buffer>[]> StorageBuffers;

    public Dictionary<EffectData.Parameter, object> BoundResources;

    protected abstract void Bound(ResourceInfo<SamplerState> sampler);

    protected abstract void Bound(ResourceInfo<Texture> texture);

    protected abstract void Bound(ResourceInfo<Buffer> buffer);

    /// <summary>
    /// Initializes this instance.
    /// </summary>
    public void Initialize()
    {
        ConstantBuffers = new Dictionary<EffectData.Parameter, EffectConstantBuffer>();
        Samplers = new Dictionary<EffectData.Parameter, ResourceInfo<SamplerState>[]>();
        SampledImages = new Dictionary<EffectData.Parameter, ResourceInfo<Texture>[]>();
        StorageBuffers = new Dictionary<EffectData.Parameter, ResourceInfo<Buffer>[]>();
        BoundResources = new Dictionary<EffectData.Parameter, object>();
    }

    public T GetResource<T>(EffectData.Parameter resourceName) where T : class
    {
        BoundResources.TryGetValue(resourceName, out var res);
        return (T)res;
    }

    public T[] GetResources<T>(EffectData.Parameter resourceName) where T : class
    {
        BoundResources.TryGetValue((EffectData.ResourceParameter)resourceName, out var res);
        return (T[])res;
    }

    public ResourceInfo<Texture>[] GetSampledImages(EffectData.Parameter resourceName) =>
        SampledImages.GetValueOrDefault(resourceName, NoSampledImages);

    public ResourceInfo<SamplerState>[] GetSamplers(EffectData.Parameter resourceName) =>
        Samplers.GetValueOrDefault(resourceName, NoSamplers);

    public ResourceInfo<Buffer>[] GetStorageBuffers(EffectData.Parameter resourceName) =>
        StorageBuffers.GetValueOrDefault(resourceName, NoStorageBuffers);

    public void SetResource<T>(EffectData.ResourceParameter paramDescription, T value)
    {
        Resolve(paramDescription, value, 0);
    }

    public void SetResource<T>(EffectData.ResourceParameter resourceName, params T[] valueArray) where T : class
    {
        SetResource(resourceName, 0, valueArray);
    }

    public void SetResource<T>(EffectData.ResourceParameter parameter, int index, params T[] values) where T : class
    {
        for (var i = 0; i < values.Length; ++i)
        {
            Resolve(parameter, values[i], index + i);
        }
    }

    public Dictionary<EffectData.Parameter, object> GetBoundResources()
    {
        return BoundResources;
    }

    public void AddBoundResource(EffectData.Parameter resourceName, object value)
    {
        BoundResources[resourceName] = value;
    }

    public object[] GetBoundValues(EffectData.Parameter resource)
    {
        if (Samplers.TryGetValue(resource, out var samplers))
        {
            return GetResourcesOf(samplers);
        }

        if (SampledImages.TryGetValue(resource, out var images))
        {
            return GetResourcesOf(images);
        }

        return StorageBuffers.TryGetValue(resource, out var buffers) ? GetResourcesOf(buffers) : null;
    }

    private static object[] GetResourcesOf<T>(ResourceInfo<T>[] infos) where T : class
    {
        var resources = new object[infos.Length];
        for (var i = 0; i < infos.Length; i++)
        {
            resources[i] = infos[i]?.Resource;
        }

        return resources;
    }

    private void Resolve(EffectData.ResourceParameter parameter, object value, int index)
    {
        switch (parameter.Type)
        {
            case EffectParameterType.UniformBuffer:
                BindConstantBuffer(parameter, value);
                break;
            case EffectParameterType.Sampler:
                Bind(parameter, Samplers, value as SamplerState, index, Bound);
                break;
            case EffectParameterType.SampledImage1D:
            case EffectParameterType.SampledImage2D:
            case EffectParameterType.SampledImage3D:
            case EffectParameterType.SampledImageCube:
                Bind(parameter, SampledImages, value as Texture, index, Bound);
                break;
            case EffectParameterType.StorageBuffer:
                Bind(parameter, StorageBuffers, value as Buffer, index, Bound);
                break;
            default:
                throw new NotSupportedException(
                    $"'{parameter.Name}' is a {parameter.Type}; effects bind uniform buffers, samplers, sampled images and storage buffers.");
        }
    }

    private void BindConstantBuffer(EffectData.Parameter parameter, object value)
    {
        var constantBuffer = value as EffectConstantBuffer;
        ConstantBuffers[parameter] = constantBuffer;
        BoundResources[parameter] = constantBuffer;
    }

    private void Bind<T>(EffectData.ResourceParameter parameter, Dictionary<EffectData.Parameter, ResourceInfo<T>[]> bound,
        T value, int index, Action<ResourceInfo<T>> prepare) where T : class
    {
        if (index >= parameter.Count)
        {
            return;
        }

        if (!bound.TryGetValue(parameter, out var infos))
        {
            infos = new ResourceInfo<T>[parameter.Count];
            bound.Add(parameter, infos);
            BoundResources[parameter] = infos;
        }

        infos[index] ??= new ResourceInfo<T>();
        if (value == null)
        {
            infos[index].Clear();
            return;
        }

        infos[index].Resource = value;
        prepare(infos[index]);
    }
}
