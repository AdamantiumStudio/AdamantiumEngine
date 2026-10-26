using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Adamantium.Core;
using Adamantium.EffectsCompiler;

namespace Adamantium.Graphics.Core.EffectsFramework;

/// <summary>
/// Effect class. Representing a wrapper over the shaders to make work with shaders easier
/// </summary>
public class Effect : GraphicsResource
{
    /// <summary>
    /// Occurs when the effect is being initialized (after a recompilation at runtime for example)
    /// </summary>
    public event EventHandler<EventArgs> Initialized;

    /// <summary>
    /// Occurs after <see cref="Reload"/> has put a recompiled version of this effect in place.
    /// </summary>
    public event EventHandler<EventArgs> Reloaded;

    public delegate IEffectPass OnApplyDelegate(IEffectPass pass);

    private Dictionary<EffectConstantBufferKey, EffectConstantBuffer> effectConstantBuffersCache;
    private Dictionary<string, EffectParameter> reusableParameters;
    private Dictionary<string, EffectTechnique> reusableTechniques;
    private Dictionary<(string Technique, string Pass), IEffectPass> reusablePasses;

    public IEffectResourceLinker ResourceLinker { get; private set; }

    /// <summary>
    /// Gets a collection of constant buffers that are defined for this effect.
    /// </summary>
    public EffectConstantBufferCollection ConstantBuffers { get; private set; }

    /// <summary>
    /// Gets a collection of parameters that are defined for this effect.
    /// </summary>
    public EffectParameterCollection Parameters { get; private set; }

    /// <summary>
    /// Gets a collection of techniques that are defined for this effect.
    /// </summary>
    public EffectTechniqueCollection Techniques { get; private set; }

    /// <summary>
    /// Gets the data associated to this effect.
    /// </summary> 
    public EffectData.Effect RawEffectData { get; private set; }
        
    /// <summary>
    /// Set to <c>true</c> to force all constant shaders to be shared between other effects within a common <see cref="EffectPool"/>. Default is <c>false</c>.
    /// </summary>
    /// <remarks>
    /// This value can also be set in the TKFX file directly by setting ShareConstantBuffers = <c>true</c>; in a pass.
    /// </remarks>
    protected internal bool ShareConstantBuffers;

    /// <summary>
    /// Initializes a new instance of the <see cref="Effect"/> class with the specified bytecode effect. See remarks.
    /// </summary>
    /// <param name="device">The device.</param>
    /// <param name="bytecode">The bytecode to add to <see cref="IGraphicsDevice.DefaultEffectPool"/>. This bytecode must contain only one effect.</param>
    /// <exception cref="ArgumentException">If the bytecode doesn't contain a single effect.</exception>
    /// <remarks>
    /// The effect bytecode must contain only a single effect and will be registered into the <see cref="IGraphicsDevice.DefaultEffectPool"/>.
    /// </remarks>
    public Effect(IGraphicsDevice device, byte[] bytecode)
        : this(device, EffectData.Load(bytecode))
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Effect" /> class with the specified bytecode effect. See remarks.
    /// </summary>
    /// <param name="device">The device.</param>
    /// <param name="effectData">The bytecode to add to the Effect Pool. This bytecode must contain only one effect.</param>
    /// <param name="effectPool">The effect pool used to register the bytecode. Default is <see cref="IGraphicsDevice.DefaultEffectPool"/>.</param>
    /// <exception cref="ArgumentException">If the bytecode doesn't contain a single effect.</exception>
    /// <remarks>The effect bytecode must contain only a single effect and will be registered into the <see cref="IGraphicsDevice.DefaultEffectPool"/>.</remarks>
    public Effect(IGraphicsDevice device, EffectData effectData, EffectPool effectPool = null) : base(device)
    {
        CreateInstanceFrom(device, effectData, effectPool);
    }

    /// <summary>
    /// Gets the pool this effect attached to.
    /// </summary>
    /// <value> The pool. </value>
    public EffectPool Pool { get; private set; }

    /// <summary>
    /// Occurs when the on apply is applied on a pass.
    /// </summary>
    /// <remarks>
    /// This external hook provides a way to pre-configure a pipeline when a pass is applied.
    /// Subclass of this class can override the method <see cref="OnApply"/>.
    /// </remarks>
    public event OnApplyDelegate OnApplyCallback;

    /// <summary>
    /// Gets or sets the current technique. By default, it is set to the first available technique in this effect.
    /// </summary>
    /// <value>The current technique.</value>
    public EffectTechnique CurrentTechnique { get; set; }

    /// <summary>
    /// The effect is supporting dynamic compilation.
    /// </summary>
    public bool IsSupportingDynamicCompilation { get; private set; }

    internal void CreateInstanceFrom(IGraphicsDevice device, EffectData effectData, EffectPool effectPool)
    {
        ConstantBuffers = new EffectConstantBufferCollection();
        Parameters = new EffectParameterCollection();
        Techniques = new EffectTechniqueCollection();

        Pool = effectPool ?? device.DefaultEffectPool;

        Name = effectData.Description.Name;

        var t = Stopwatch.StartNew();
        var effect = Pool.RegisterBytecode(effectData);

        InitializeFrom(effect, null);

        t.Stop();
        Debug.WriteLine($"Effect initialized in {t.ElapsedMilliseconds} ms");
        Pool.AddEffect(this);

        if (IsSupportingDynamicCompilation)
        {
            device.MainDevice?.ShaderHotReload.Track(this);
        }
    }

    /// <summary>
    /// Binds the specified effect data to this instance.
    /// </summary>
    /// <param name="effectDataArg">The effect data arg.</param>
    /// <param name="cloneFromEffect">The clone from effect.</param>
    /// <exception cref="System.InvalidOperationException">If no techniques found in this effect.</exception>
    /// <exception cref="System.ArgumentException">If unable to find effect [effectName] from the EffectPool.</exception>
    internal void InitializeFrom(EffectData.Effect effectDataArg, Effect cloneFromEffect)
    {
        DisposeCollector?.DisposeAndClear();
        ConstantBuffers.Clear();
        Parameters.Clear();
        Techniques.Clear();
        effectConstantBuffersCache?.Clear();

        Build(effectDataArg, cloneFromEffect);
    }

    private void Build(EffectData.Effect effectDataArg, Effect cloneFromEffect)
    {
        RawEffectData = effectDataArg;
        ResourceLinker = GraphicsDevice.CreateEffectResourceLinker();

        IsSupportingDynamicCompilation = RawEffectData.Arguments != null;
        ShareConstantBuffers = RawEffectData.ShareConstantBuffers;

        if (!ShareConstantBuffers)
            effectConstantBuffersCache ??= new Dictionary<EffectConstantBufferKey, EffectConstantBuffer>();

        var logger = new Logger();
        int techniqueIndex = 0;
        int totalPassCount = 0;

        foreach (var techniqueRaw in RawEffectData.Techniques)
        {
            var name = techniqueRaw.Name;
            if (string.IsNullOrEmpty(name))
                name = $"${techniqueIndex++}";

            var technique = TechniqueFor(name);
            Techniques.Add(technique);

            int passIndex = 0;
            foreach (var passRaw in techniqueRaw.Passes)
            {
                name = passRaw.Name;
                if (string.IsNullOrEmpty(name))
                    name = $"${passIndex++}";

                var pass = PassFor(logger, technique, passRaw, name);

                pass.Initialize(logger);
                
                technique.Passes.Add(pass);
            }

            totalPassCount += technique.Passes.Count;
        }

        if (totalPassCount == 0)
            throw new InvalidOperationException("No passes found in this effect.");

        if (logger.HasErrors)
            throw new InvalidOperationException(string.Join("\n", logger.Messages));

        ResourceLinker.Initialize();

        Parameters.Items.Sort((left, right) =>
        {
            var comparison = left.IsValueType != right.IsValueType ? left.IsValueType ? -1 : 1 : 0;

            if (comparison == 0)
            {
                comparison = ((int) left.ResourceType).CompareTo((int) right.ResourceType);

                if (comparison == 0)
                {
                    comparison = left.Offset.CompareTo(right.Offset);
                }
            }

            return comparison;
        });

        uint resourceIndex = 0;
        foreach (var parameter in Parameters)
        {
            if (!parameter.IsValueType)
            {
                parameter.Offset = (int)resourceIndex;
                resourceIndex += parameter.ElementCount;
            }

            parameter.SetDefaultValue();

            if (parameter.ResourceType == EffectResourceType.ConstantBuffer)
                parameter.SetResource(ConstantBuffers[parameter.Name]);
        }

        foreach (var technique in Techniques)
        {
            foreach (var pass in technique.Passes)
            {
                pass.PrepareData();
            }
        }

        CurrentTechnique = Techniques[0];

        if (cloneFromEffect != null)
        {
            for (int i = 0; i < ConstantBuffers.Count; i++)
            {
                cloneFromEffect.ConstantBuffers[i].CopyTo(ConstantBuffers[i]);
            }

            foreach (var boundResource in cloneFromEffect.ResourceLinker.GetBoundResources())
            {
                if (boundResource.Value is EffectConstantBuffer)
                    continue;

                ResourceLinker.AddBoundResource(boundResource.Key, boundResource.Value);
            }

            Pool.AddEffect(this);
        }

        Initialize();

        OnInitialized();
    }

    protected virtual void Initialize()
    {
    }

    /// <summary>Puts a recompiled version in place, keeping the technique, pass and parameter objects and the values of
    /// unchanged parameters. Call it between frames.</summary>
    /// <returns>Null when in place, otherwise why it was refused: a version that drops a technique or a pass.</returns>
    public string Reload(EffectData effectData)
    {
        var effect = Pool.RegisterBytecode(effectData);
        var dropped = FirstDroppedPass(effect);
        if (dropped != null)
        {
            return $"{Name}: the new version has no pass {dropped}; rebuild the application to take it.";
        }

        var carried = CarryValues();
        var previousParameters = Parameters.Items.ToList();
        var previousBuffers = ConstantBuffers.Items.ToList();

        reusableParameters = new Dictionary<string, EffectParameter>();
        foreach (var parameter in previousParameters)
        {
            reusableParameters[parameter.Name] = parameter;
        }

        reusableTechniques = new Dictionary<string, EffectTechnique>();
        reusablePasses = new Dictionary<(string Technique, string Pass), IEffectPass>();
        foreach (var technique in Techniques)
        {
            reusableTechniques[technique.Name] = technique;
            foreach (var pass in technique.Passes)
            {
                reusablePasses[(technique.Name, pass.Name)] = pass;
            }
        }

        try
        {
            ConstantBuffers.Clear();
            Parameters.Clear();
            Techniques.Clear();
            Build(effect, null);
        }
        finally
        {
            reusableParameters = null;
            reusableTechniques = null;
            reusablePasses = null;
        }

        RestoreValues(carried);
        RetireStaleBuffers(previousParameters, previousBuffers);
        Reloaded?.Invoke(this, EventArgs.Empty);
        return null;
    }

    /// <summary>
    /// The effect-level parameter a constant buffer of a pass declares: a new one, or - while <see cref="Reload"/>
    /// runs - the one the previous version had under that name, pointed at the new declaration.
    /// </summary>
    public EffectParameter DeclareValueParameter(EffectData.ValueTypeParameter description, EffectConstantBuffer buffer)
    {
        var parameter = Reused(description.Name, isValueType: true);
        if (parameter == null)
        {
            parameter = new EffectParameter(description, buffer);
        }
        else
        {
            parameter.BindValue(description, buffer);
        }

        return Parameters.Add(parameter);
    }

    /// <summary>
    /// The effect-level parameter a resource of a pass declares, taking the next slots of <see cref="ResourceLinker"/>:
    /// a new one, or - while <see cref="Reload"/> runs - the one the previous version had under that name.
    /// </summary>
    public EffectParameter DeclareResourceParameter(EffectData.ResourceParameter description, EffectResourceType resourceType)
    {
        var offset = ResourceLinker.Count;
        var parameter = Reused(description.Name, isValueType: false);
        if (parameter == null)
        {
            parameter = new EffectParameter(description, resourceType, offset, ResourceLinker);
        }
        else
        {
            parameter.BindResource(description, resourceType, offset, ResourceLinker);
        }

        ResourceLinker.Count += description.Count;
        return Parameters.Add(parameter);
    }

    private EffectParameter Reused(string name, bool isValueType)
    {
        if (reusableParameters == null
            || !reusableParameters.TryGetValue(name, out var parameter)
            || parameter.IsValueType != isValueType)
        {
            return null;
        }

        reusableParameters.Remove(name);
        return parameter;
    }

    private EffectTechnique TechniqueFor(string name)
    {
        if (reusableTechniques != null && reusableTechniques.Remove(name, out var technique))
        {
            technique.Passes.Clear();
            return technique;
        }

        return new EffectTechnique(name);
    }

    private IEffectPass PassFor(Logger logger, EffectTechnique technique, EffectData.Pass passRaw, string name)
    {
        if (reusablePasses != null && reusablePasses.Remove((technique.Name, name), out var pass))
        {
            pass.Reset(technique, passRaw, logger);
            return pass;
        }

        return ToDispose(GraphicsDevice.CreateEffectPass(logger, this, technique, passRaw, name));
    }

    private string FirstDroppedPass(EffectData.Effect effect)
    {
        var offered = new HashSet<(string, string)>();
        int techniqueIndex = 0;
        foreach (var technique in effect.Techniques)
        {
            var techniqueName = string.IsNullOrEmpty(technique.Name) ? $"${techniqueIndex++}" : technique.Name;
            int passIndex = 0;
            foreach (var pass in technique.Passes)
            {
                offered.Add((techniqueName, string.IsNullOrEmpty(pass.Name) ? $"${passIndex++}" : pass.Name));
            }
        }

        foreach (var technique in Techniques)
        {
            foreach (var pass in technique.Passes)
            {
                if (!offered.Contains((technique.Name, pass.Name)))
                {
                    return $"{technique.Name}.{pass.Name}";
                }
            }
        }

        return null;
    }

    private List<(EffectData.Parameter Description, byte[] Bytes, object[] Resources)> CarryValues()
    {
        var carried = new List<(EffectData.Parameter Description, byte[] Bytes, object[] Resources)>();
        foreach (var parameter in Parameters)
        {
            if (parameter.IsValueType)
            {
                carried.Add((parameter.ParameterDescription,
                    parameter.Buffer.BackingBuffer.GetRange<byte>(parameter.Offset, parameter.Size), null));
            }
            else if (parameter.ResourceType != EffectResourceType.ConstantBuffer)
            {
                var resources = ResourceLinker.GetBoundValues(parameter.ParameterDescription);
                if (resources != null)
                {
                    carried.Add((parameter.ParameterDescription, null, resources));
                }
            }
        }

        return carried;
    }

    private void RestoreValues(List<(EffectData.Parameter Description, byte[] Bytes, object[] Resources)> carried)
    {
        foreach (var (description, bytes, resources) in carried)
        {
            var parameter = Parameters[description.Name];
            if (parameter == null || !Unchanged(description, parameter.ParameterDescription))
            {
                continue;
            }

            if (bytes != null)
            {
                parameter.Buffer.BackingBuffer.Set(parameter.Offset, bytes);
            }
            else
            {
                ResourceLinker.SetResource<object>((EffectData.ResourceParameter)parameter.ParameterDescription,
                    parameter.ResourceType, resources);
            }
        }
    }

    private static bool Unchanged(EffectData.Parameter previous, EffectData.Parameter current)
    {
        if (previous is EffectData.ValueTypeParameter before && current is EffectData.ValueTypeParameter after)
        {
            return before.Class == after.Class && before.Type == after.Type && before.RowCount == after.RowCount
                   && before.ColumnCount == after.ColumnCount && before.Count == after.Count && before.Size == after.Size;
        }

        if (previous is EffectData.ResourceParameter was && current is EffectData.ResourceParameter now)
        {
            return was.Class == now.Class && was.Type == now.Type && was.Count == now.Count;
        }

        return false;
    }

    private void RetireStaleBuffers(List<EffectParameter> previousParameters, List<EffectConstantBuffer> previousBuffers)
    {
        if (ShareConstantBuffers)
        {
            return;
        }

        var kept = new HashSet<EffectConstantBuffer>(ReferenceEqualityComparer.Instance);
        foreach (var buffer in ConstantBuffers)
        {
            kept.Add(buffer);
        }

        foreach (var parameter in previousParameters)
        {
            if (parameter.Buffer != null && !ReferenceEquals(Parameters[parameter.Name], parameter))
            {
                kept.Add(parameter.Buffer);
            }
        }

        foreach (var (key, buffer) in effectConstantBuffersCache.ToList())
        {
            if (!kept.Contains(buffer))
            {
                effectConstantBuffersCache.Remove(key);
            }
        }

        foreach (var buffer in previousBuffers)
        {
            if (!kept.Contains(buffer))
            {
                DisposeCollector.Remove(buffer);
                GraphicsDevice.MainDevice.RetireResource(buffer);
            }
        }
    }

    /// <summary>
    /// Clones this instance.
    /// </summary>
    /// <returns>A new instance of this Effect.</returns>
    public virtual Effect Clone()
    {
        var effect = (Effect)MemberwiseClone();
        effect.DisposeCollector = new DisposeCollector();
        effect.ConstantBuffers = new EffectConstantBufferCollection();
        effect.Parameters = new EffectParameterCollection();
        effect.Techniques = new EffectTechniqueCollection();
        effect.effectConstantBuffersCache = null;

        effect.InitializeFrom(effect.RawEffectData, this);

        return effect;
    }

    protected internal virtual IEffectPass OnApply(IEffectPass pass)
    {
        var handler = OnApplyCallback;
        if (handler != null) pass = handler(pass);

        return pass;
    }

    /// <summary>
    /// Disposes of object resources.
    /// </summary>
    /// <param name="disposeManagedResources">If true, managed resources should be
    /// disposed of in addition to unmanaged resources.</param>
    protected override void Dispose(bool disposeManagedResources)
    {
        Pool.RemoveEffect(this);

        base.Dispose(disposeManagedResources);
    }

    public EffectConstantBuffer GetOrCreateConstantBuffer(EffectData.ConstantBuffer bufferRaw)
    {
        EffectConstantBuffer constantBuffer;
        if (ShareConstantBuffers)
        {
            constantBuffer = Pool.GetOrCreateConstantBuffer(bufferRaw);
        }
        else
        {
            var bufferKey = new EffectConstantBufferKey(bufferRaw);
            if (!effectConstantBuffersCache.TryGetValue(bufferKey, out constantBuffer))
            {
                constantBuffer = new EffectConstantBuffer(bufferRaw);
                effectConstantBuffersCache.Add(bufferKey, constantBuffer);
                ToDispose(constantBuffer);
            }
        }

        return constantBuffer;
    }

    /// <summary>
    /// Initialized event handler
    /// </summary>
    protected virtual void OnInitialized()
    {
        EventHandler<EventArgs> handler = Initialized;
        handler?.Invoke(this, EventArgs.Empty);
    }

    public static Effect Load(string path, IGraphicsDevice device)
    {
        var data = EffectData.Load(path);
        return new Effect(device, data);
    }

    public static Effect Load(Stream stream, IGraphicsDevice device)
    {
        var data = EffectData.Load(stream);
        return new Effect(device, data);
    }

    public static Effect Load(byte[] data, IGraphicsDevice device)
    {
        var effectData = EffectData.Load(data);
        return new Effect(device, effectData);
    }

    public static Effect CompileFromFile(string path, IGraphicsDevice device)
    {
        var result = EffectCompiler.CompileFromFile(path);
        if (result.HasErrors) return null;

        return new Effect(device, result.EffectData);
    }
}