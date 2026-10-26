using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using Adamantium.Core;
using Adamantium.Graphics.Core;
using Adamantium.Vulkan.Core;

namespace Adamantium.Graphics;

/// <summary>Descriptor sets for a device without VK_EXT_descriptor_heap: the shared set of a texture array of no length,
/// and the samplers that passes push.</summary>
public class DescriptorSetManager : DisposableObject, IDescriptorManager
{
    /// <summary>Every stage a pass may have.</summary>
    internal const ShaderStageFlagBits EveryStage = ShaderStageFlagBits.AllGraphics | ShaderStageFlagBits.ComputeBit;

    private readonly IGraphicsDevice graphicsDevice;
    private readonly object _sync = new();
    private readonly ConcurrentDictionary<ITexture, uint> _textureIndices = new();
    private readonly Queue<uint> _freeIndices = new();
    private uint _nextIndex;
    private readonly ConcurrentDictionary<SamplerState, Sampler> _samplersByState = new();
    private readonly Dictionary<SamplerKey, Sampler> _samplers = new();
    private readonly Dictionary<uint, TextureArray> _arrays = new();
    private uint _capacity;
    private DescriptorSetLayout _emptyLayout;
    private ITexture _fallbackTexture;
    private Buffer _fallbackBuffer;
    private readonly ConditionalWeakTable<object, StrongBox<ulong>> _identities = new();
    private long _lastIdentity;

    public DescriptorSetManager(IGraphicsDevice graphicsDevice)
    {
        this.graphicsDevice = graphicsDevice;
        MaxTextureArrayCapacity = MaxCapacityOf(graphicsDevice.Adapter);
        _capacity = Math.Min(Math.Max(graphicsDevice.MainDevice.Settings.TextureArrayCapacity, 1u), MaxTextureArrayCapacity);
    }

    /// <summary>The most textures a texture array of no length can hold on this device: its update-after-bind limits
    /// for sampled images, less what a pass's pushed descriptors and color attachments may take of the stage's.</summary>
    public uint MaxTextureArrayCapacity { get; }

    /// <summary>How many textures the texture arrays hold now; doubled when full.</summary>
    public uint TextureArrayCapacity
    {
        get
        {
            lock (_sync)
            {
                return _capacity;
            }
        }
    }

    private static uint MaxCapacityOf(GraphicsAdapter adapter)
    {
        var limits = adapter.Vulkan12Properties;
        var pushed = adapter.MaxPushDescriptors;
        var attachments = adapter.AdapterProperties.Limits.MaxColorAttachments;
        var perStageImages = limits.MaxPerStageDescriptorUpdateAfterBindSampledImages;
        var perStageResources = limits.MaxPerStageUpdateAfterBindResources;
        return Math.Min(limits.MaxDescriptorSetUpdateAfterBindSampledImages,
            Math.Min(perStageImages > pushed ? perStageImages - pushed : 0,
                perStageResources > pushed + attachments ? perStageResources - pushed - attachments : 0));
    }

    private Device LogicalDevice => graphicsDevice.LogicalDevice;

    /// <summary>Nothing to bind up front: each pass binds the sets it reads as it is applied.</summary>
    public void BindTo(IGraphicsDevice device)
    {
    }

    public uint FallbackTextureIndex => TextureIndex(FallbackTexture);

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
            else
            {
                if (_nextIndex == _capacity)
                {
                    Grow();
                }

                index = _nextIndex++;
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

    /// <summary>The set of every indexed texture for an array declared at <paramref name="binding"/>.</summary>
    internal TextureArray TextureArrayAt(uint binding)
    {
        lock (_sync)
        {
            if (!_arrays.TryGetValue(binding, out var array))
            {
                array = CreateTextureArray(binding);
                _arrays.Add(binding, array);
            }

            return array;
        }
    }

    private TextureArray CreateTextureArray(uint binding)
    {
        if (MaxTextureArrayCapacity == 0)
        {
            throw new NotSupportedException(
                $"{graphicsDevice.Adapter.AdapterProperties.DeviceName} has no room for update-after-bind sampled images: " +
                "a texture array of no length cannot be bound on descriptor sets");
        }

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
                    DescriptorCount = MaxTextureArrayCapacity,
                    StageFlags = EveryStage
                }
            },
            PNext = new DescriptorSetLayoutBindingFlagsCreateInfo
            {
                BindingCount = 1,
                PBindingFlags = new[]
                {
                    DescriptorBindingFlagBits.UpdateAfterBindBit | DescriptorBindingFlagBits.PartiallyBoundBit |
                    DescriptorBindingFlagBits.UpdateUnusedWhilePendingBit |
                    DescriptorBindingFlagBits.VariableDescriptorCountBit
                }
            }
        };
        Check(LogicalDevice.CreateDescriptorSetLayout(layoutInfo, null, out var layout), "a texture array layout");

        var array = new TextureArray(binding, layout);
        var (pool, set) = Allocate(layout, binding, _capacity);
        array.Pool = pool;
        array.Set = set;
        return array;
    }

    private (DescriptorPool Pool, DescriptorSet Set) Allocate(DescriptorSetLayout layout, uint binding, uint capacity)
    {
        var pool = LogicalDevice.CreateDescriptorPool(new DescriptorPoolCreateInfo
        {
            Flags = DescriptorPoolCreateFlagBits.UpdateAfterBindBit,
            MaxSets = 1,
            PoolSizeCount = 1,
            PoolSizes = new[] { new DescriptorPoolSize { Type = DescriptorType.SampledImage, DescriptorCount = capacity } }
        });

        var allocation = new DescriptorSetAllocateInfo
        {
            DescriptorPool = pool,
            DescriptorSetCount = 1,
            PSetLayouts = new[] { layout },
            PNext = new DescriptorSetVariableDescriptorCountAllocateInfo
            {
                DescriptorSetCount = 1,
                PDescriptorCounts = new[] { capacity }
            }
        };
        Check(LogicalDevice.AllocateDescriptorSets(allocation, out var sets), "the texture array set");

        foreach (var (texture, index) in _textureIndices)
        {
            Write(sets[0], binding, index, texture);
        }

        return (pool, sets[0]);
    }

    private void Grow()
    {
        if (_capacity >= MaxTextureArrayCapacity)
        {
            throw new OutOfMemoryException(
                $"The texture array is full: {MaxTextureArrayCapacity} textures, as many as this device allows.");
        }

        _capacity = (uint)Math.Min((ulong)_capacity * 2, MaxTextureArrayCapacity);
        foreach (var array in _arrays.Values)
        {
            var (pool, set) = Allocate(array.Layout, array.Binding, _capacity);
            graphicsDevice.AddToDeferDisposeQueue(new RetiredPool(LogicalDevice, array.Pool));
            array.Pool = pool;
            array.Set = set;
            array.Generation++;
        }
    }

    private sealed class RetiredPool(Device device, DescriptorPool pool) : IDisposable
    {
        public void Dispose() => device.DestroyDescriptorPool(pool);
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

    /// <summary>A number no other resource ever gets: what a recording's fingerprint names a pushed resource by.</summary>
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

    /// <summary>A texture array's set: replaced by a bigger one when the array grows, so a pass reads
    /// <see cref="Set"/> as it is applied.</summary>
    internal sealed class TextureArray(uint binding, DescriptorSetLayout layout)
    {
        public uint Binding { get; } = binding;

        public DescriptorSetLayout Layout { get; } = layout;

        public DescriptorPool Pool { get; set; }

        public DescriptorSet Set { get; set; }

        /// <summary>How many times the set was replaced: what a recording's fingerprint names it by.</summary>
        public ulong Generation { get; set; }
    }
}
