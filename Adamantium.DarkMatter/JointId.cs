namespace Adamantium.DarkMatter;

/// <summary>A joint of a <see cref="PhysicsSimulation"/>: where the simulation keeps it and which joint there.</summary>
public readonly record struct JointId
{
    internal JointId(int index, int generation)
    {
        Index = index;
        Generation = generation;
    }

    /// <summary>No joint.</summary>
    public static JointId None => default;

    /// <summary>Whether this is no joint.</summary>
    public bool IsNone => Generation == 0;

    internal int Index { get; }

    internal int Generation { get; }

    public override string ToString() => IsNone ? "JointId None" : $"JointId {Index}:{Generation}";
}
