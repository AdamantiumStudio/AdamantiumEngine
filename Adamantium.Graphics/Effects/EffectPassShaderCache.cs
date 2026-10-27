using System.Collections.Generic;
using Adamantium.EffectsCompiler;
using Adamantium.Graphics.Core;

namespace Adamantium.Graphics.Effects;

/// <summary>
/// The shaders of effect passes on a logical device, one <see cref="EffectPassShaders"/> for a pass's data: the effects
/// of that data on every render device hold the same, and the last to let go of them retires them.
/// </summary>
internal sealed class EffectPassShaderCache : IEffectPassShaderCache
{
    private readonly object sync = new();
    private readonly Dictionary<EffectData.Pass, EffectPassShaders> made = new(ReferenceEqualityComparer.Instance);
    private readonly GraphicsDevice device;
    private readonly DescriptorModel model;

    /// <summary>A cache that makes shaders on <paramref name="device"/>, for the descriptor <paramref name="model"/> it
    /// binds them with.</summary>
    internal EffectPassShaderCache(GraphicsDevice device, DescriptorModel model)
    {
        this.device = device;
        this.model = model;
    }

    /// <inheritdoc />
    public int Count
    {
        get
        {
            lock (sync)
            {
                return made.Count;
            }
        }
    }

    /// <summary>The shaders of <paramref name="pass"/> of <paramref name="data"/>, made the first time they are asked for;
    /// each call is answered by one <see cref="Release"/>.</summary>
    /// <exception cref="System.NotSupportedException">The pass's resources do not fit the descriptor model or the
    /// device's limits.</exception>
    public EffectPassShaders Acquire(EffectData data, EffectData.Pass pass, string effectName, string techniqueName,
        string passName)
    {
        lock (sync)
        {
            if (!made.TryGetValue(pass, out var shaders))
            {
                shaders = model == DescriptorModel.Sets
                    ? new SetsEffectPassShaders(device, data, pass, effectName, techniqueName, passName)
                    : new HeapEffectPassShaders(device, data, pass, effectName, techniqueName, passName);
                shaders.Create();
                made.Add(pass, shaders);
            }

            shaders.References++;
            return shaders;
        }
    }

    /// <summary>Lets go of shaders <see cref="Acquire"/> gave; the last release retires them.</summary>
    public void Release(EffectPassShaders shaders)
    {
        lock (sync)
        {
            if (--shaders.References > 0)
            {
                return;
            }

            made.Remove(shaders.Pass);
        }

        device.MainDevice.RetireResource(shaders);
    }

    /// <summary>Destroys the shaders no effect let go of.</summary>
    public void Dispose()
    {
        lock (sync)
        {
            foreach (var shaders in made.Values)
            {
                shaders.Dispose();
            }

            made.Clear();
        }
    }
}
