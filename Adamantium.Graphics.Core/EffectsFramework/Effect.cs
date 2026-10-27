using System;
using System.Collections.Generic;
using System.Linq;
using Adamantium.Core;
using Adamantium.EffectsCompiler;

namespace Adamantium.Graphics.Core.EffectsFramework;

/// <summary>
/// A compiled effect made ready on one device: its techniques and passes, the parameters its shaders declare and the
/// constant buffers those values live in. Every pass of the effect shares the parameters and buffers declared alike. The
/// main device's <see cref="ShaderHotReload"/> holds it among the live effects until it is disposed.
/// </summary>
public class Effect : GraphicsResource
{
    private readonly Dictionary<EffectData.ConstantBuffer, EffectConstantBuffer> buffersByDeclaration = [];
    private Dictionary<string, EffectParameter> reusableParameters;
    private Dictionary<string, EffectTechnique> reusableTechniques;
    private Dictionary<(string Technique, string Pass), IEffectPass> reusablePasses;
    private List<IEffectPass> madePasses;

    /// <summary>An effect of the saved <see cref="EffectData"/> in <paramref name="bytecode"/>.</summary>
    public Effect(IGraphicsDevice device, byte[] bytecode)
        : this(device, EffectData.Load(bytecode))
    {
    }

    /// <summary>An effect of <paramref name="data"/> on <paramref name="device"/>.</summary>
    /// <exception cref="InvalidOperationException">The effect has no pass, or a pass could not be made.</exception>
    public Effect(IGraphicsDevice device, EffectData data) : base(device)
    {
        Name = data.Description.Name;
        try
        {
            Build(data);
        }
        catch
        {
            Dispose();
            throw;
        }

        device.MainDevice?.ShaderHotReload.Add(this);
    }

    /// <summary>The compiled effect this one runs: its techniques and passes, and the shaders they link.</summary>
    public EffectData Data { get; private set; }

    /// <summary>The techniques of the effect, in the order it declares them.</summary>
    public EffectCollection<EffectTechnique> Techniques { get; } = new();

    /// <summary>The parameters of the effect: the values first, then the resources.</summary>
    public EffectCollection<EffectParameter> Parameters { get; } = new();

    /// <summary>The constant buffers the values live in.</summary>
    public EffectCollection<EffectConstantBuffer> ConstantBuffers { get; } = new();

    /// <summary>Where the resources set on the parameters are kept until a pass binds them.</summary>
    public IEffectResourceLinker ResourceLinker { get; private set; }

    /// <summary>The technique a pass of which was applied last; the first one until then.</summary>
    public EffectTechnique CurrentTechnique { get; set; }

    /// <summary>Whether the effect carries the source it was compiled from, so it can be compiled again.</summary>
    public bool IsSupportingDynamicCompilation { get; private set; }

    /// <summary>Compiles the effect at <paramref name="path"/> for <paramref name="device"/>; null when it does not
    /// compile.</summary>
    public static Effect CompileFromFile(string path, IGraphicsDevice device)
    {
        var result = EffectCompiler.CompileFromFile(path);
        return result.HasErrors ? null : new Effect(device, result.EffectData);
    }

    /// <summary>Puts a recompiled version in place, keeping the technique, pass and parameter objects and the values of
    /// unchanged parameters. Call it between frames.</summary>
    /// <returns>Null when in place, otherwise why it was refused: a version that drops a technique or a pass, or one
    /// that could not be made - the running version then stays, with its values.</returns>
    public string Reload(EffectData effectData)
    {
        var dropped = FindDroppedPass(effectData.Description);
        if (dropped != null)
        {
            return $"{Name}: the new version has no pass {dropped}; rebuild the application to take it.";
        }

        var running = Data;
        var carried = CarryValues();
        var parameters = Parameters.ToList();
        var techniques = Techniques.ToList();
        var passes = new List<(string Technique, IEffectPass Pass)>();
        foreach (var technique in techniques)
        {
            foreach (var pass in technique.Passes)
            {
                passes.Add((technique.Name, pass));
            }
        }

        string refused = null;
        madePasses = [];
        try
        {
            BuildReusing(effectData, parameters, techniques, passes);
        }
        catch (Exception exception)
        {
            BuildReusing(running, parameters, techniques, passes);
            refused = $"{Name}: the new version could not be made, so the running one stays: {exception.Message}";
        }

        RestoreValues(carried);
        RetireStale(parameters);
        madePasses = null;
        return refused;
    }

    /// <summary>A new effect of the same data on the same device, with this one's values and bound resources.</summary>
    public virtual Effect Clone()
    {
        var effect = new Effect(GraphicsDevice, Data);
        effect.CopyValuesFrom(this);
        return effect;
    }

    /// <summary>Takes the values and the bound resources of <paramref name="source"/>, an effect of the same data.</summary>
    protected void CopyValuesFrom(Effect source)
    {
        for (var i = 0; i < ConstantBuffers.Count; i++)
        {
            source.ConstantBuffers[i].CopyTo(ConstantBuffers[i]);
        }

        foreach (var parameter in source.Parameters)
        {
            if (parameter.IsValueType || parameter.ParameterType == EffectParameterType.UniformBuffer)
            {
                continue;
            }

            var bound = source.ResourceLinker.GetBoundValues(parameter.ParameterDescription);
            if (bound != null && Parameters[parameter.Name] is { } own)
            {
                ResourceLinker.SetResource<object>((EffectData.ResourceParameter)own.ParameterDescription, bound);
            }
        }
    }

    /// <summary>The constant buffer a pass declares - the one the effect already has for that name, or a new one - and,
    /// the first time the effect meets a buffer of that name, the values it holds. A buffer declared differently by
    /// two shaders, or a value one buffer declares that another declared differently, is an error in
    /// <paramref name="logger"/>.</summary>
    public EffectConstantBuffer DeclareConstantBuffer(EffectData.ConstantBuffer description, Logger logger)
    {
        if (!buffersByDeclaration.TryGetValue(description, out var buffer))
        {
            buffer = ToDispose(new EffectConstantBuffer(description));
            buffersByDeclaration.Add(description, buffer);
        }

        if (ConstantBuffers[description.Name] is { } named)
        {
            if (!ReferenceEquals(named, buffer))
            {
                logger.Error("{0}: constant buffer {1} is declared differently by two shaders.", Name, description.Name);
            }

            return named;
        }

        ConstantBuffers.Add(buffer);
        foreach (var value in description.Parameters)
        {
            var declared = Parameters[value.Name];
            if (declared == null)
            {
                DeclareValueParameter(value, buffer);
            }
            else if (declared.ParameterDescription != value || declared.Buffer != buffer)
            {
                logger.Error("{0}: {1} of constant buffer {2} is declared differently in {3}.", Name, value.Name,
                    description.Name, declared.Buffer?.Name);
            }
        }

        return buffer;
    }

    private EffectParameter DeclareValueParameter(EffectData.ValueTypeParameter description, EffectConstantBuffer buffer)
    {
        var parameter = TakeReusable(description.Name, isValueType: true);
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
    public EffectParameter DeclareResourceParameter(EffectData.ResourceParameter description)
    {
        var offset = ResourceLinker.Count;
        var parameter = TakeReusable(description.Name, isValueType: false);
        if (parameter == null)
        {
            parameter = new EffectParameter(description, offset, ResourceLinker);
        }
        else
        {
            parameter.BindResource(description, offset, ResourceLinker);
        }

        ResourceLinker.Count += (int)description.Count;
        return Parameters.Add(parameter);
    }

    protected override void Dispose(bool disposeManagedResources)
    {
        GraphicsDevice.MainDevice?.ShaderHotReload.Remove(this);
        base.Dispose(disposeManagedResources);
    }

    private void BuildReusing(EffectData data, List<EffectParameter> parameters, List<EffectTechnique> techniques,
        List<(string Technique, IEffectPass Pass)> passes)
    {
        reusableParameters = parameters.ToDictionary(parameter => parameter.Name);
        reusableTechniques = techniques.ToDictionary(technique => technique.Name);
        reusablePasses = [];
        foreach (var (technique, pass) in passes)
        {
            reusablePasses[(technique, pass.Name)] = pass;
        }

        try
        {
            Build(data);
        }
        finally
        {
            reusableParameters = null;
            reusableTechniques = null;
            reusablePasses = null;
        }
    }

    private void Build(EffectData data)
    {
        Data = data;
        IsSupportingDynamicCompilation = data.Description.Arguments != null;
        ConstantBuffers.Clear();
        Parameters.Clear();
        Techniques.Clear();
        ResourceLinker = GraphicsDevice.CreateEffectResourceLinker();

        var logger = new Logger();
        BuildTechniques(data.Description, logger);
        if (logger.HasErrors)
        {
            throw new InvalidOperationException(string.Join("\n", logger.Messages));
        }

        ResourceLinker.Initialize();
        PlaceParameters();
        foreach (var technique in Techniques)
        {
            foreach (var pass in technique.Passes)
            {
                pass.PrepareData();
            }
        }

        CurrentTechnique = Techniques[0];
    }

    private void BuildTechniques(EffectData.Effect description, Logger logger)
    {
        var unnamedTechniques = 0;
        var passCount = 0;
        foreach (var techniqueData in description.Techniques)
        {
            var technique = TakeTechnique(GetNameOrIndex(techniqueData.Name, ref unnamedTechniques));
            Techniques.Add(technique);

            var unnamedPasses = 0;
            foreach (var passData in techniqueData.Passes)
            {
                var pass = TakePass(logger, technique, passData, GetNameOrIndex(passData.Name, ref unnamedPasses));
                pass.Initialize(logger);
                technique.Passes.Add(pass);
                passCount++;
            }
        }

        if (passCount == 0)
        {
            throw new InvalidOperationException($"{Name} has no pass.");
        }
    }

    private void PlaceParameters()
    {
        Parameters.Sort((left, right) =>
        {
            if (left.IsValueType != right.IsValueType)
            {
                return left.IsValueType ? -1 : 1;
            }

            var byType = ((int)left.ParameterType).CompareTo((int)right.ParameterType);
            return byType != 0 ? byType : left.Offset.CompareTo(right.Offset);
        });

        var slot = 0u;
        foreach (var parameter in Parameters)
        {
            if (!parameter.IsValueType)
            {
                parameter.Offset = (int)slot;
                slot += parameter.ElementCount;
            }

            if (parameter.ParameterType == EffectParameterType.UniformBuffer)
            {
                parameter.SetResource(ConstantBuffers[parameter.Name]);
            }
        }
    }

    private static string GetNameOrIndex(string name, ref int unnamed) =>
        string.IsNullOrEmpty(name) ? $"${unnamed++}" : name;

    private EffectParameter TakeReusable(string name, bool isValueType)
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

    private EffectTechnique TakeTechnique(string name)
    {
        if (reusableTechniques != null && reusableTechniques.Remove(name, out var technique))
        {
            technique.Passes.Clear();
            return technique;
        }

        return new EffectTechnique(name);
    }

    private IEffectPass TakePass(Logger logger, EffectTechnique technique, EffectData.Pass passData, string name)
    {
        if (reusablePasses != null && reusablePasses.Remove((technique.Name, name), out var pass))
        {
            pass.Reset(technique, passData, logger);
            return pass;
        }

        var made = ToDispose(GraphicsDevice.CreateEffectPass(logger, this, technique, passData, name));
        madePasses?.Add(made);
        return made;
    }

    private string FindDroppedPass(EffectData.Effect description)
    {
        var offered = new HashSet<(string, string)>();
        var unnamedTechniques = 0;
        foreach (var technique in description.Techniques)
        {
            var techniqueName = GetNameOrIndex(technique.Name, ref unnamedTechniques);
            var unnamedPasses = 0;
            foreach (var pass in technique.Passes)
            {
                offered.Add((techniqueName, GetNameOrIndex(pass.Name, ref unnamedPasses)));
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
            else if (parameter.ParameterType != EffectParameterType.UniformBuffer)
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
            if (parameter == null || !IsUnchanged(description, parameter.ParameterDescription))
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
                    resources);
            }
        }
    }

    private static bool IsUnchanged(EffectData.Parameter previous, EffectData.Parameter current)
    {
        if (previous is EffectData.ValueTypeParameter before && current is EffectData.ValueTypeParameter after)
        {
            return before.Class == after.Class && before.Type == after.Type && before.RowCount == after.RowCount
                   && before.ColumnCount == after.ColumnCount && before.Count == after.Count && before.Size == after.Size;
        }

        if (previous is EffectData.ResourceParameter was && current is EffectData.ResourceParameter now)
        {
            return was.Class == now.Class && was.Type == now.Type && was.Count == now.Count && was.Access == now.Access;
        }

        return false;
    }

    private void RetireStale(List<EffectParameter> previousParameters)
    {
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

        foreach (var (description, buffer) in buffersByDeclaration.ToList())
        {
            if (!kept.Contains(buffer))
            {
                buffersByDeclaration.Remove(description);
                RemoveToDispose(buffer);
                GraphicsDevice.MainDevice.RetireResource(buffer);
            }
        }

        foreach (var pass in madePasses)
        {
            if (!Techniques.Any(technique => technique.Passes.Contains(pass.Name)
                                             && ReferenceEquals(technique.Passes[pass.Name], pass)))
            {
                RemoveToDispose(pass);
                GraphicsDevice.MainDevice.RetireResource(pass);
            }
        }
    }
}
