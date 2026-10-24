using System.Collections.Generic;
using Adamantium.Core;
using Adamantium.EffectsCompiler;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.EffectsFramework;
using Adamantium.Vulkan.Core;
using VulkanBuffer = Adamantium.Vulkan.Core.Buffer;

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

    // Bound to nothing: what SetResource(null) means.
    internal void Clear()
    {
        Resource = null;
        GlobalHeapOffset = uint.MaxValue;
    }
}

internal class EffectResourceLinker : IEffectResourceLinker
{
    /// <summary>
    /// Real object resources, as they were set on the parameter.
    /// </summary>
    public Dictionary<EffectData.Parameter, EffectConstantBuffer> ConstantBuffers;

    /// <summary>
    /// Total number of resources.
    /// </summary>
    public int Count { get; set; }

    // public Dictionary<EffectData.Parameter, Sampler[]> SamplerStates;
    public Dictionary<EffectData.Parameter, ResourceInfo<SamplerState>[]> SamplerStates;
    public Dictionary<EffectData.Parameter, ResourceInfo<Texture>[]> ShaderResourceViews;
    // Read-only StructuredBuffers: they reflect as a ShaderResourceView (D3D_SVT_STRUCTURED_BUFFER), but their RESOURCE
    // is a storage buffer, not a Texture - so they live here, separate from the texture SRVs. In Vulkan a read-only and a
    // read-write structured buffer are the SAME DescriptorType.StorageBuffer (SRV vs UAV is a D3D-ism); the read-only-ness
    // is a shader decoration, so binding is identical to a UAV buffer, just without shader writes.
    public Dictionary<EffectData.Parameter, ResourceInfo<Buffer>[]> ShaderResourceBuffers;
    public Dictionary<EffectData.Parameter, ResourceInfo<Buffer>[]> UnorderedAccessViews;
    public Dictionary<EffectData.Parameter, object> BoundResources;

    private static ResourceInfo<SamplerState>[] EmptySamplers = [];
    private static ResourceInfo<Texture>[] EmptyResourceViews = [];
    private static ResourceInfo<Buffer>[] EmptyShaderResourceBuffers = [];
    private static ResourceInfo<Buffer>[] EmptyUAVs = [];

    /// <summary>The value of <see cref="ResourceInfo{T}.GlobalHeapOffset"/> where a path keeps no slots.</summary>
    protected const uint NoSlot = uint.MaxValue;

    // Where a bound resource's descriptor waits for the draws that read it: a heap slot. Descriptor sets keep none - a
    // pass writes the descriptors as it is applied.
    protected virtual uint SlotOf(SamplerState sampler) => NoSlot;

    protected virtual uint SlotOf(Texture texture) => NoSlot;

    protected virtual uint SlotOf(Buffer buffer) => NoSlot;

    /// <summary>
    /// Initializes this instance.
    /// </summary>
    public void Initialize()
    {
        ConstantBuffers = new Dictionary<EffectData.Parameter, EffectConstantBuffer>();

        SamplerStates = new Dictionary<EffectData.Parameter, ResourceInfo<SamplerState>[]>();
        ShaderResourceViews = new Dictionary<EffectData.Parameter, ResourceInfo<Texture>[]>();
        ShaderResourceBuffers = new Dictionary<EffectData.Parameter, ResourceInfo<Buffer>[]>();
        UnorderedAccessViews = new Dictionary<EffectData.Parameter, ResourceInfo<Buffer>[]>();
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

    public ResourceInfo<Texture>[] GetShaderResources(EffectData.Parameter resourceName)
    {
        return ShaderResourceViews.GetValueOrDefault(resourceName, EmptyResourceViews);
    }

    public ResourceInfo<SamplerState>[] GetSamplers(EffectData.Parameter resourceName)
    {
        return SamplerStates.GetValueOrDefault(resourceName, EmptySamplers);
    }

    public ResourceInfo<Buffer>[] GetUAVs(EffectData.Parameter resourceName)
    {
        return UnorderedAccessViews.GetValueOrDefault(resourceName, EmptyUAVs);
    }

    public ResourceInfo<Buffer>[] GetShaderResourceBuffers(EffectData.Parameter resourceName)
    {
        return ShaderResourceBuffers.GetValueOrDefault(resourceName, EmptyShaderResourceBuffers);
    }

    public void SetResource(EffectData.ResourceParameter resourceName, EffectResourceType type, VulkanBuffer view)
    {
        ResolveResource(resourceName, type, view, 0);
    }

    public void SetResource<T>(EffectData.ResourceParameter paramDescription, EffectResourceType type, T value)
    {
        ResolveResource(paramDescription, type, value, 0);
    }

    public void SetResource<T>(EffectData.ResourceParameter resourceName, EffectResourceType type,
        params T[] valueArray) where T : class
    {
        for (int i = 0; i < valueArray.Length; ++i)
        {
            ResolveResource(resourceName, type, valueArray[i], i);
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
        if (SamplerStates.TryGetValue(resource, out var samplers))
        {
            return ResourcesOf(samplers);
        }

        if (ShaderResourceViews.TryGetValue(resource, out var textures))
        {
            return ResourcesOf(textures);
        }

        if (ShaderResourceBuffers.TryGetValue(resource, out var buffers))
        {
            return ResourcesOf(buffers);
        }

        return UnorderedAccessViews.TryGetValue(resource, out var views) ? ResourcesOf(views) : null;
    }

    private static object[] ResourcesOf<T>(ResourceInfo<T>[] infos) where T : class
    {
        var resources = new object[infos.Length];
        for (int i = 0; i < infos.Length; i++)
        {
            resources[i] = infos[i]?.Resource;
        }

        return resources;
    }

    public void SetResource(EffectData.ResourceParameter resourceName, EffectResourceType type,
        VulkanBuffer[] valueArray, int[] uavInitialCount)
    {
        for (int i = 0; i < valueArray.Length; ++i)
        {
            ResolveResource(resourceName, type, valueArray[i], i);
        }
    }

    private void ResolveResource(EffectData.Parameter resourceName, EffectResourceType type, object value, int index)
    {
        switch (type)
        {
            case EffectResourceType.ConstantBuffer:
                ProcessConstantBuffer(resourceName, value);
                break;
            case EffectResourceType.SamplerState:
            case EffectResourceType.ShaderResourceView:
            case EffectResourceType.UnorderedAccessView:
                ProcessReferenceResources((EffectData.ResourceParameter)resourceName, type, value, index);
                break;
        }
    }

    private void ProcessConstantBuffer(EffectData.Parameter resourceName, object value)
    {
        var constantBuffer = value as EffectConstantBuffer;
        if (ConstantBuffers.ContainsKey(resourceName))
        {
            ConstantBuffers[resourceName] = constantBuffer;
            BoundResources[resourceName] = constantBuffer;
        }
        else
        {
            ConstantBuffers.Add(resourceName, constantBuffer);
            BoundResources.Add(resourceName, constantBuffer);
        }
    }

    private void ProcessReferenceResources(EffectData.ResourceParameter parameter, EffectResourceType type,
        object value, int index)
    {
        if (index >= parameter.Count)
        {
            return;
        }

        switch (type)
        {
            case EffectResourceType.SamplerState:
            {
                if (!SamplerStates.TryGetValue(parameter, out var states))
                {
                    states = new ResourceInfo<SamplerState>[parameter.Count];
                    SamplerStates.Add(parameter, states);
                    BoundResources.Add(parameter, states);
                }

                SamplerState state = null;
                if (value is SamplerState samplerState)
                {
                    state = samplerState;
                }

                if (states[index] == null)
                {
                    states[index] = new ResourceInfo<SamplerState>();
                }

                // Each sampler gets its OWN stable heap slot instead of sharing one per-parameter slot, which made the
                // last-bound sampler apply to every draw.
                if (state != null)
                {
                    states[index].Resource = state;
                    states[index].GlobalHeapOffset = SlotOf(state);
                }
                else
                {
                    states[index].Clear();
                }
            }
                break;
            case EffectResourceType.ShaderResourceView:
            {
                // A read-only StructuredBuffer also reflects as a ShaderResourceView, but its resource is a storage
                // BUFFER, not a texture. Route it to its own collection + bind it like a UAV buffer (its OWN heap slot,
                // a StorageBuffer descriptor) - in Vulkan a read-only structured buffer IS a StorageBuffer descriptor.
                if (value is Buffer srvBuffer)
                {
                    if (!ShaderResourceBuffers.TryGetValue(parameter, out var srvBufs))
                    {
                        srvBufs = new ResourceInfo<Buffer>[parameter.Count];
                        ShaderResourceBuffers.Add(parameter, srvBufs);
                        BoundResources[parameter] = srvBufs;
                    }

                    srvBufs[index] ??= new ResourceInfo<Buffer>();
                    srvBufs[index].Resource = srvBuffer;
                    srvBufs[index].GlobalHeapOffset = SlotOf(srvBuffer);
                    break;
                }

                if (value == null && ShaderResourceBuffers.TryGetValue(parameter, out var bound))
                {
                    bound[index]?.Clear();
                    break;
                }

                if (!ShaderResourceViews.TryGetValue(parameter, out var views))
                {
                    views = new ResourceInfo<Texture>[parameter.Count];
                    ShaderResourceViews.Add(parameter, views);
                    BoundResources.Add(parameter, views);
                }

                if (views[index] == null)
                {
                    views[index] = new ResourceInfo<Texture>();
                }

                if (value is Texture texture)
                {
                    views[index].Resource = texture;

                    // This texture's OWN stable heap slot (bindless). The offset used to be allocated per parameter, so
                    // every texture bound to ShaderTexture shared ONE slot and the last-written one showed up on every
                    // draw. The slot belongs to the texture itself.
                    views[index].GlobalHeapOffset = SlotOf(texture);
                }
                else
                {
                    // Setting null used to leave the last texture's slot in place, which then went on being sampled
                    // after that texture was gone.
                    views[index].Clear();
                }
            }
                break;
            case EffectResourceType.UnorderedAccessView:
            {
                if (!UnorderedAccessViews.TryGetValue(parameter, out var uavs))
                {
                    uavs = new ResourceInfo<Buffer>[parameter.Count];
                    UnorderedAccessViews.Add(parameter, uavs);
                    BoundResources.Add(parameter, uavs);
                }

                if (value is Buffer buffer)
                {
                    if (uavs[index] == null)
                    {
                        uavs[index] = new ResourceInfo<Buffer>();
                    }
                    
                    uavs[index].Resource = buffer;

                    // This buffer's OWN stable heap slot - same per-resource rule as textures and samplers.
                    uavs[index].GlobalHeapOffset = SlotOf(buffer);
                }
                else
                {
                    uavs[index]?.Clear();
                }
            }
                break;
        }
    }
}