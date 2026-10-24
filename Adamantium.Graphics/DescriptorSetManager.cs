using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using Adamantium.Core;
using Adamantium.Graphics.Core;
using Adamantium.Vulkan.Core;

namespace Adamantium.Graphics;

/// <summary>Descriptor sets, for a device without VK_EXT_descriptor_heap. A pass pushes its own descriptors on every
/// draw; a texture array of no length reads one set shared by every pass, holding each texture that asked for an index
/// at that index - the same index the heap path gives it. Also owns the samplers pushed descriptors name, one per
/// description.</summary>
public class DescriptorSetManager : DisposableObject, IDescriptorManager
{
    /// <summary>How many textures the shared array holds at most.</summary>
    public const uint ArrayCapacity = 16384;

    // Every stage a pass may have. ShaderStageFlagBits.All of the bindings is 7, not VK_SHADER_STAGE_ALL.
    internal const ShaderStageFlagBits EveryStage = ShaderStageFlagBits.VertexBit |
                                                    ShaderStageFlagBits.TessellationControlBit |
                                                    ShaderStageFlagBits.TessellationEvaluationBit |
                                                    ShaderStageFlagBits.GeometryBit |
                                                    ShaderStageFlagBits.FragmentBit |
                                                    ShaderStageFlagBits.ComputeBit;

    private readonly IGraphicsDevice graphicsDevice;
    private readonly object _sync = new();
    private readonly ConcurrentDictionary<ITexture, uint> _textureIndices = new();
    private readonly Queue<uint> _freeIndices = new();
    private uint _nextIndex;
    private readonly ConcurrentDictionary<SamplerState, Sampler> _samplersByState = new();
    private readonly Dictionary<SamplerKey, Sampler> _samplers = new();
    private readonly Dictionary<uint, TextureArraySet> _arrays = new();
    private DescriptorSetLayout _emptyLayout;
    private ITexture _fallbackTexture;
    private Buffer _fallbackBuffer;

    public DescriptorSetManager(IGraphicsDevice graphicsDevice)
    {
        this.graphicsDevice = graphicsDevice;
    }

    private Device LogicalDevice => graphicsDevice.LogicalDevice;

    /// <summary>Nothing to bind up front: each pass binds the sets it reads as it is applied.</summary>
    public void BindTo(IGraphicsDevice device)
    {
    }

    public uint TextureIndex(ITexture texture)
    {
        if (_textureIndices.TryGetValue(texture, out var found)) return found;

        lock (_sync)
        {
            if (_textureIndices.TryGetValue(texture, out var existing)) return existing;

            uint index;
            if (_freeIndices.Count > 0)
            {
                index = _freeIndices.Dequeue();
            }
            else if (_nextIndex < ArrayCapacity)
            {
                index = _nextIndex++;
            }
            else
            {
                throw new OutOfMemoryException($"The texture array is full: {ArrayCapacity} textures.");
            }

            foreach (var array in _arrays.Values)
            {
                Write(array.Set, array.Binding, index, texture);
            }

            _textureIndices[texture] = index;
            if (texture is DisposableObject owned)
            {
                owned.Disposing += (_, _) => Release(texture);
            }

            return index;
        }
    }

    // The index goes back to the free list once the frames in flight are done with it.
    private void Release(ITexture texture)
    {
        lock (_sync)
        {
            if (!_textureIndices.TryRemove(texture, out var index)) return;
            graphicsDevice.AddToDeferDisposeQueue(new RetiredIndex(this, index));
        }
    }

    private sealed class RetiredIndex(DescriptorSetManager manager, uint index) : IDisposable
    {
        public void Dispose()
        {
            lock (manager._sync)
            {
                manager._freeIndices.Enqueue(index);
            }
        }
    }

    /// <summary>The set holding every texture that has an index, for a shader that declares its array at
    /// <paramref name="binding"/> - alone in its set - and the layout that set has.</summary>
    internal (DescriptorSetLayout Layout, DescriptorSet Set) TextureArray(uint binding)
    {
        lock (_sync)
        {
            if (!_arrays.TryGetValue(binding, out var array))
            {
                array = CreateTextureArray(binding);
                _arrays.Add(binding, array);
            }

            return (array.Layout, array.Set);
        }
    }

    private TextureArraySet CreateTextureArray(uint binding)
    {
        var layoutInfo = new DescriptorSetLayoutCreateInfo
        {
            Flags = DescriptorSetLayoutCreateFlagBits.UpdateAfterBindPoolBit,
            BindingCount = 1,
            PBindings = new[]
            {
                new DescriptorSetLayoutBinding
                {
                    Binding = binding,
                    DescriptorType = DescriptorType.SampledImage,
                    DescriptorCount = ArrayCapacity,
                    StageFlags = EveryStage
                }
            },
            PNext = new DescriptorSetLayoutBindingFlagsCreateInfo
            {
                BindingCount = 1,
                PBindingFlags = new[]
                {
                    DescriptorBindingFlagBits.UpdateAfterBindBit | DescriptorBindingFlagBits.PartiallyBoundBit |
                    DescriptorBindingFlagBits.UpdateUnusedWhilePendingBit
                }
            }
        };
        Check(LogicalDevice.CreateDescriptorSetLayout(layoutInfo, null, out var layout), "a texture array layout");

        var pool = LogicalDevice.CreateDescriptorPool(new DescriptorPoolCreateInfo
        {
            Flags = DescriptorPoolCreateFlagBits.UpdateAfterBindBit,
            MaxSets = 1,
            PoolSizeCount = 1,
            PoolSizes = new[] { new DescriptorPoolSize { Type = DescriptorType.SampledImage, DescriptorCount = ArrayCapacity } }
        });

        var allocation = new DescriptorSetAllocateInfo
        {
            DescriptorPool = pool,
            DescriptorSetCount = 1,
            PSetLayouts = new[] { layout }
        };
        Check(LogicalDevice.AllocateDescriptorSets(allocation, out var sets), "the texture array set");

        foreach (var (texture, index) in _textureIndices)
        {
            Write(sets[0], binding, index, texture);
        }

        return new TextureArraySet(binding, layout, pool, sets[0]);
    }

    private void Write(DescriptorSet set, uint binding, uint index, ITexture texture)
    {
        var write = new WriteDescriptorSet
        {
            DstSet = set,
            DstBinding = binding,
            DstArrayElement = index,
            DescriptorCount = 1,
            DescriptorType = DescriptorType.SampledImage,
            PImageInfo = new[]
            {
                new DescriptorImageInfo { ImageView = texture.GetImageView(), ImageLayout = texture.ImageLayout }
            }
        };
        LogicalDevice.UpdateDescriptorSets(1, write, 0, out _);
    }

    private readonly ConditionalWeakTable<object, StrongBox<ulong>> _identities = new();
    private long _lastIdentity;

    /// <summary>A number of <paramref name="resource"/>'s own that no other resource ever gets: what a recording's
    /// fingerprint names a pushed texture or buffer by. A handle will not do - a resource made after another is gone may
    /// be given the same one, and a recording that pushed the old one's descriptor would be taken for current.</summary>
    internal ulong IdentityOf(object resource) =>
        _identities.TryGetValue(resource, out var identity)
            ? identity.Value
            : _identities.GetValue(resource, NewIdentity).Value;

    private StrongBox<ulong> NewIdentity(object resource) => new((ulong)Interlocked.Increment(ref _lastIdentity));

    /// <summary>A layout of no bindings, for a set number a pass skips.</summary>
    internal DescriptorSetLayout EmptyLayout
    {
        get
        {
            lock (_sync)
            {
                if (_emptyLayout == null)
                {
                    Check(LogicalDevice.CreateDescriptorSetLayout(new DescriptorSetLayoutCreateInfo(), null, out _emptyLayout),
                        "an empty set layout");
                }

                return _emptyLayout;
            }
        }
    }

    /// <summary>The sampler <paramref name="state"/> describes; equal descriptions share one.</summary>
    internal Sampler SamplerOf(SamplerState state)
    {
        if (_samplersByState.TryGetValue(state, out var found)) return found;

        lock (_sync)
        {
            if (_samplersByState.TryGetValue(state, out var existing)) return existing;

            var key = SamplerKey.Of(state.Info);
            if (!_samplers.TryGetValue(key, out var sampler))
            {
                Check(LogicalDevice.CreateSampler(state.Info, null, out sampler), "a sampler");
                _samplers.Add(key, sampler);
            }

            _samplersByState[state] = sampler;
            return sampler;
        }
    }

    /// <summary>What a shader samples for a texture parameter nobody bound.</summary>
    internal ITexture FallbackTexture
    {
        get
        {
            lock (_sync)
            {
                return _fallbackTexture ??= Graphics.FallbackTexture.Create(graphicsDevice);
            }
        }
    }

    internal Sampler FallbackSampler => SamplerOf(SamplerStates.LinearClampToEdge);

    /// <summary>What a shader reads for a buffer parameter nobody bound: zeros, never a buffer in use.</summary>
    internal Buffer FallbackBuffer
    {
        get
        {
            lock (_sync)
            {
                if (_fallbackBuffer == null)
                {
                    _fallbackBuffer = Buffer.New(graphicsDevice, 16,
                        BufferUsageFlags.StorageBuffer | BufferUsageFlags.TransferDst);
                    _fallbackBuffer.SetData(new byte[16]);
                }

                return _fallbackBuffer;
            }
        }
    }

    private static void Check(Result result, string what)
    {
        if (result != Result.Success)
        {
            throw new InvalidOperationException($"Could not create {what}: {result}");
        }
    }

    protected override void Dispose(bool disposeManagedResources)
    {
        foreach (var array in _arrays.Values)
        {
            LogicalDevice.DestroyDescriptorPool(array.Pool);
            LogicalDevice.DestroyDescriptorSetLayout(array.Layout);
        }

        _arrays.Clear();
        foreach (var sampler in _samplers.Values)
        {
            LogicalDevice.DestroySampler(sampler);
        }

        _samplers.Clear();
        _samplersByState.Clear();
        if (_emptyLayout != null)
        {
            LogicalDevice.DestroyDescriptorSetLayout(_emptyLayout);
            _emptyLayout = null;
        }

        (_fallbackTexture as IDisposable)?.Dispose();
        _fallbackTexture = null;
        _fallbackBuffer?.Dispose();
        _fallbackBuffer = null;
        base.Dispose(disposeManagedResources);
    }

    private sealed record TextureArraySet(uint Binding, DescriptorSetLayout Layout, DescriptorPool Pool, DescriptorSet Set);
}
