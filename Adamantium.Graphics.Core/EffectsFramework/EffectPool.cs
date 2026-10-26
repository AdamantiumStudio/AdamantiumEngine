using System;
using System.Collections.Generic;
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
/// This class is responsible to store all EffectData, create shareable constant buffers between effects and reuse shader EffectData instances.
/// </remarks>
public sealed class EffectPool : DisposableObject
{
    public readonly List<EffectData.Shader> RegisteredShaders;
    private readonly List<ShaderModule>[] compiledShadersGroup;
    private readonly IGraphicsDevice graphicsDevice;
    private readonly AdamantiumCollection<Effect> effects;

    private readonly Dictionary<string, Dictionary<EffectConstantBufferKey, EffectConstantBuffer>> mapNameToConstantBuffer;
    private readonly Dictionary<EffectData, EffectData.Effect> registered;
    private readonly object syncObj = new object();


    private EffectPool(IGraphicsDevice device, string name = null)
        : base(name)
    {
        RegisteredShaders = new List<EffectData.Shader>();
        mapNameToConstantBuffer = new Dictionary<string, Dictionary<EffectConstantBufferKey, EffectConstantBuffer>>();
        compiledShadersGroup = new List<ShaderModule>[(int)EffectShaderType.Compute + 1];
        for (int i = 0; i < compiledShadersGroup.Length; i++)
        {
            compiledShadersGroup[i] = new List<ShaderModule>(256);
        }

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

                foreach (var compiledShaders in compiledShadersGroup)
                {
                    for (int i = compiledShaders.Count; i < RegisteredShaders.Count; i++)
                    {
                        compiledShaders.Add(null);
                    }
                }
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

    public ShaderModule GetOrCompileShader(EffectShaderType shaderType, int index, out string profileError)
    {
        ShaderModule shader = null;
        profileError = null;
        lock (syncObj)
        {
            shader = compiledShadersGroup[(int)shaderType][index];
        }
        return shader;
    }
        
    public byte[] GetShaderBytecode(EffectShaderType shaderType, int index)
    {
        var bytecode = RegisteredShaders[index].Bytecode;
        return bytecode;
    }

    internal EffectConstantBuffer GetOrCreateConstantBuffer(EffectData.ConstantBuffer bufferRaw)
    {
        lock (mapNameToConstantBuffer)
        {
            if (!mapNameToConstantBuffer.TryGetValue(bufferRaw.Name, out var bufferSet))
            {
                bufferSet = new Dictionary<EffectConstantBufferKey, EffectConstantBuffer>();
                mapNameToConstantBuffer[bufferRaw.Name] = bufferSet;
            }

            var bufferKey = new EffectConstantBufferKey(bufferRaw);
            if (!bufferSet.TryGetValue(bufferKey, out var buffer))
            {
                buffer = new EffectConstantBuffer(bufferRaw);
                bufferSet[bufferKey] = ToDispose(buffer);
            }

            return buffer;
        }
    }

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

        for (var index = 0; index < compiledShadersGroup.Length; index++)
        {
            var group = compiledShadersGroup[index];
            Log.Logger.Debug($"Disposing shader modules in group [{index}]");
            group.Clear();
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
            ShareConstantBuffers = effect.ShareConstantBuffers,
            Techniques = new List<EffectData.Technique>(effect.Techniques.Count)
        };

        Logger logger = null;

        foreach (var techniqueOriginal in effect.Techniques)
        {
            var technique = techniqueOriginal.Clone();
            effectRuntime.Techniques.Add(technique);

            foreach (var pass in technique.Passes)
            {
                foreach (var shaderLink in pass.Pipeline)
                {
                    if (shaderLink == null) continue;

                    if (shaderLink.IsImport)
                    {
                        var index = FindShaderByName(shaderLink.ImportName);
                        if (index >= 0)
                        {
                            shaderLink.ImportName = null;
                            shaderLink.Index = index;
                        }
                        else
                        {
                            if (logger == null)
                            {
                                logger = new Logger();
                            }

                            logger.Error("Cannot find shader import by name [{0}]", shaderLink.ImportName);
                        }
                    }
                    else if (!shaderLink.IsNullShader)
                    {
                        var shader = source.Shaders[shaderLink.Index];

                        var shaderIndex = FindSimilarShader(shader);

                        if (shaderIndex >= 0)
                        {
                            var previousShader = RegisteredShaders[shaderIndex];

                            if (shader.Name != null)
                            {
                                if (previousShader.Name == null)
                                {
                                    previousShader.Name = shader.Name;
                                }
                                else if (shader.Name != previousShader.Name)
                                {
                                    if (logger == null)
                                    {
                                        logger = new Logger();
                                    }
                                    logger.Error("Cannot merge shader [{0}] into this instance, as there is already a global shader with a different name [{1}]", shader.Name, previousShader.Name);
                                }
                            }

                            shaderLink.Index = shaderIndex;
                        }
                        else
                        {
                            shaderLink.Index = RegisteredShaders.Count;
                            RegisteredShaders.Add(shader);
                        }
                    }
                }
            }
        }

        if (logger != null && logger.HasErrors)
            throw new InvalidOperationException(string.Join("\r\n", logger.Messages));

        return effectRuntime;
    }

    private int FindSimilarShader(EffectData.Shader shader)
    {
        for (int i = 0; i < RegisteredShaders.Count; i++)
        {
            if (RegisteredShaders[i].IsSimilar(shader))
                return i;
        }
        return -1;
    }

    private int FindShaderByName(string name)
    {
        for (int i = 0; i < RegisteredShaders.Count; i++)
        {
            if (RegisteredShaders[i].Name == name)
                return i;
        }
        return -1;

    }

    private void OnEffectAdded(EffectPoolEventArgs e)
    {
        EffectAdded?.Invoke(this, e);
    }

    private void OnEffectRemoved(EffectPoolEventArgs e)
    {
        EffectRemoved?.Invoke(this, e);
    }
}