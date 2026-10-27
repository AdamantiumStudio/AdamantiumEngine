using System;
using System.Collections.Generic;
using System.Linq;
using Adamantium.Core;
using Adamantium.Core.Collections;
using Adamantium.EffectsCompiler;
using Adamantium.Vulkan.Core;
using Serilog;

namespace Adamantium.Graphics.Core.EffectsFramework;

/// <summary>
/// This class manages a pool of <see cref="Effect"/>.
/// </summary>
/// <remarks>
/// This class is responsible to store all EffectData and reuse shader EffectData instances.
/// </remarks>
public sealed class EffectPool : DisposableObject
{
    public readonly List<EffectData.Shader> RegisteredShaders;
    private readonly IGraphicsDevice graphicsDevice;
    private readonly AdamantiumCollection<Effect> effects;

    private readonly Dictionary<EffectData, EffectData.Effect> registered;
    private readonly object syncObj = new object();


    private EffectPool(IGraphicsDevice device, string name = null)
        : base(name)
    {
        RegisteredShaders = new List<EffectData.Shader>();
        registered = new Dictionary<EffectData, EffectData.Effect>();
        effects = new AdamantiumCollection<Effect>();
        graphicsDevice = device;
        graphicsDevice.AddEffectPool(this);
    }

    /// <summary>
    /// Registers a EffectData to this pool.
    /// </summary>
    /// <param name="data">The effect data to register.</param>
    /// <returns>The effect description.</returns>
    public EffectData.Effect RegisterBytecode(EffectData data)
    {
        lock (syncObj)
        {
            EffectData.Effect effect;

            if (!registered.TryGetValue(data, out effect))
            {
                effect = RegisterInternal(data);
                registered.Add(data, effect);
            }

            return effect;
        }
    }

    /// <summary>
    /// Readonly collection of registered effects
    /// </summary>
    public IReadOnlyCollection<Effect> RegisteredEffects
    {
        get
        {
            lock (syncObj)
            {
                return effects.AsReadOnly();
            }
        }
    }

    /// <summary>
    /// Called when Effect added
    /// </summary>
    public event EventHandler<EffectPoolEventArgs> EffectAdded;

    /// <summary>
    /// Called when Effect removed
    /// </summary>
    public event EventHandler<EffectPoolEventArgs> EffectRemoved;

    internal void AddEffect(Effect effect)
    {
        lock (effects)
        {
            effects.Add(effect);
        }
        OnEffectAdded(new EffectPoolEventArgs(effect));
    }

    internal void RemoveEffect(Effect effect)
    {
        lock (effects)
        {
            effects.Remove(effect);
        }
        OnEffectRemoved(new EffectPoolEventArgs(effect));
    }

    /// <summary>The SPIR-V of the shader at <paramref name="index"/> in <see cref="RegisteredShaders"/>.</summary>
    public byte[] GetShaderBytecode(int index) => RegisteredShaders[index].Bytecode;

    /// <summary>
    /// Creates a new effect pool from a specified list of <see cref="EffectData" />.
    /// </summary>
    /// <param name="device">The device.</param>
    /// <returns>An instance of <see cref="EffectPool" />.</returns>
    public static EffectPool New(IGraphicsDevice device)
    {
        return new EffectPool(device);
    }

    /// <summary>
    /// Creates a new named effect pool from a specified list of <see cref="EffectData" />.
    /// </summary>
    /// <param name="device">The device.</param>
    /// <param name="name">The name of this effect pool.</param>
    /// <returns>An instance of <see cref="EffectPool" />.</returns>
    public static EffectPool New(IGraphicsDevice device, string name)
    {
        return new EffectPool(device);
    }

    /// <summary>
    /// Returns a string that represents the current object.
    /// </summary>
    /// <returns>
    /// A string that represents the current object.
    /// </returns>
    /// <filterpriority>2</filterpriority>
    public override string ToString() => $"EffectPool [{Name}]";

    protected override void Dispose(bool disposeManagedResources)
    {
        Log.Logger.Debug("Start disposing EffectsPool");
        lock (effects)
        {
            foreach (var effect in effects)
            {
                effect.Dispose();
            }

            effects.Clear();
        }

        graphicsDevice.RemoveEffectPool(this);
        base.Dispose(disposeManagedResources);
    }

    private EffectData.Effect RegisterInternal(EffectData source)
    {
        var effect = source.Description;

        var effectRuntime = new EffectData.Effect()
        {
            Name = effect.Name,
            Arguments = effect.Arguments,
            Techniques = new List<EffectData.Technique>(effect.Techniques.Count)
        };

        foreach (var techniqueOriginal in effect.Techniques)
        {
            var technique = techniqueOriginal.Clone();
            effectRuntime.Techniques.Add(technique);
            foreach (var pass in technique.Passes)
            {
                foreach (var link in pass.Stages.Concat(pass.HitGroups.SelectMany(group => group.Shaders)))
                {
                    link.Index = Register(source.Shaders[link.Index]);
                }
            }
        }

        return effectRuntime;
    }

    private int Register(EffectData.Shader shader)
    {
        for (var i = 0; i < RegisteredShaders.Count; i++)
        {
            if (IsSameShader(RegisteredShaders[i], shader))
            {
                return i;
            }
        }

        RegisteredShaders.Add(shader);
        return RegisteredShaders.Count - 1;
    }

    private static bool IsSameShader(EffectData.Shader left, EffectData.Shader right) =>
        ReferenceEquals(left, right)
        || (left.Stage == right.Stage && left.EntryPoint == right.EntryPoint && left.Level == right.Level
            && Utilities.Compare(left.Bytecode, right.Bytecode)
            && Utilities.Compare(left.ConstantBuffers, right.ConstantBuffers)
            && Utilities.Compare(left.ResourceParameters, right.ResourceParameters));

    private void OnEffectAdded(EffectPoolEventArgs e)
    {
        EffectAdded?.Invoke(this, e);
    }

    private void OnEffectRemoved(EffectPoolEventArgs e)
    {
        EffectRemoved?.Invoke(this, e);
    }
}