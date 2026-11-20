namespace Adamantium.DarkMatter;

/// <summary>A body of a <see cref="PhysicsSimulation"/>: where the simulation keeps it and which body there, so a removed
/// body's id is told apart from the next one's. A plain value; every action on the body is the simulation's.</summary>
public readonly record struct BodyId
{
    internal BodyId(int index, int generation)
    {
        Index = index;
        Generation = generation;
    }

    /// <summary>No body: for a joint, the world.</summary>
    public static BodyId None => default;

    /// <summary>Whether this is no body.</summary>
    public bool IsNone => Generation == 0;

    internal int Index { get; }

    internal int Generation { get; }

    public override string ToString() => IsNone ? "BodyId None" : $"BodyId {Index}:{Generation}";
}
