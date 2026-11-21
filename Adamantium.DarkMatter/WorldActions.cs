namespace Adamantium.DarkMatter;

/// <summary>The world of a <see cref="PhysicsSimulation"/> as a chain: each action is the simulation's own, asked at
/// once, in order, and gives the chain back for the next.</summary>
public readonly ref struct WorldActions
{
    private readonly PhysicsSimulation _simulation;

    internal WorldActions(PhysicsSimulation simulation) => _simulation = simulation;

    public WorldActions SetGravity(Vector3F gravity)
    {
        _simulation.Gravity = gravity;
        return this;
    }

    public WorldActions SetCollides(Layer first, Layer second, bool collides)
    {
        _simulation.Layers.SetCollides(first, second, collides);
        return this;
    }

    /// <summary>Wakes every body asleep whose center of mass is within <paramref name="area"/>.</summary>
    public WorldActions WakeUp(in BoundingBox<double> area)
    {
        _simulation.WakeUp(area);
        return this;
    }

    /// <summary>An explosion: see <see cref="PhysicsSimulation.ApplyRadialImpulse"/>.</summary>
    public WorldActions ApplyRadialImpulse(WorldPosition center, float radius, float strength)
    {
        _simulation.ApplyRadialImpulse(center, radius, strength);
        return this;
    }

    public WorldActions Step()
    {
        _simulation.Step();
        return this;
    }

    public WorldActions Start()
    {
        _simulation.Start();
        return this;
    }

    public WorldActions Pause()
    {
        _simulation.Pause();
        return this;
    }

    public WorldActions Stop()
    {
        _simulation.Stop();
        return this;
    }
}
