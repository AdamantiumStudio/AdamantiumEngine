using System;
using System.Collections.Generic;

namespace Adamantium.DarkMatter;

/// <summary>A simulation's 64 layers by name, and which of them collide: every layer with every other until told. A
/// change takes with the next step - its pairs found or let go of then.</summary>
public sealed class PhysicsLayers
{
    /// <summary>How many layers there are.</summary>
    public const int Count = 64;

    private readonly PhysicsSimulation _simulation;
    private readonly string[] _names = new string[Count];
    private readonly Dictionary<string, Layer> _layersByName = new(StringComparer.Ordinal);
    // As asked; the core's own goes with the next step.
    private readonly LayerCollisions _collisions = new();
    private int _defined;

    internal PhysicsLayers(PhysicsSimulation simulation)
    {
        _simulation = simulation;
        Define("Default");
    }

    /// <summary>Layer 0, every body's unless told.</summary>
    public Layer Default => new(0);

    /// <summary>The layer named <paramref name="name"/>; there is none until defined.</summary>
    public Layer this[string name]
    {
        get
        {
            _simulation.ThrowIfDisposed();
            ArgumentNullException.ThrowIfNull(name);
            return _layersByName.TryGetValue(name, out var layer)
                ? layer
                : throw new KeyNotFoundException($"No layer is named \"{name}\".");
        }
    }

    /// <summary>Names the next layer not yet named <paramref name="name"/> and gives it.</summary>
    public Layer Define(string name)
    {
        _simulation.ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (_layersByName.ContainsKey(name))
            throw new ArgumentException($"A layer is named \"{name}\" already.", nameof(name));

        if (_defined == Count)
            throw new InvalidOperationException($"All {Count} layers are named.");

        var layer = new Layer(_defined++);
        _names[layer.Index] = name;
        _layersByName.Add(name, layer);
        return layer;
    }

    /// <summary>What <paramref name="layer"/> is named.</summary>
    public string NameOf(Layer layer)
    {
        RequireDefined(layer, nameof(layer));
        return _names[layer.Index];
    }

    /// <summary>Whether bodies on <paramref name="first"/> collide with bodies on <paramref name="second"/>.</summary>
    public bool Collide(Layer first, Layer second)
    {
        RequireDefined(first, nameof(first));
        RequireDefined(second, nameof(second));
        return _collisions.Collide(first.Index, second.Index);
    }

    /// <summary>Makes bodies on <paramref name="first"/> and on <paramref name="second"/> collide, or pass through each
    /// other, from the next step.</summary>
    public void SetCollides(Layer first, Layer second, bool collides)
    {
        RequireDefined(first, nameof(first));
        RequireDefined(second, nameof(second));
        if (_collisions.Set(first.Index, second.Index, collides))
            _simulation.AskLayersCollide(first, second, collides);
    }

    /// <summary>Throws for a layer this simulation has not named. A layer is only its index: another simulation's of
    /// an index named here is taken as this one's.</summary>
    internal void RequireDefined(Layer layer, string parameterName)
    {
        _simulation.ThrowIfDisposed();
        if (_names[layer.Index] == null)
            throw new ArgumentException($"Layer {layer.Index} is not defined.", parameterName);
    }
}
