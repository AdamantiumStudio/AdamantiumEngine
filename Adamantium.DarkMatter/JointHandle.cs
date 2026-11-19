using System;

namespace Adamantium.DarkMatter;

/// <summary>A handle to a joint in an <see cref="IPhysicsWorld"/>, given when the joint is added. A removed joint's
/// handle never equals a later one.</summary>
public readonly struct JointHandle : IEquatable<JointHandle>
{
    /// <summary>The handle of the joint a world keeps in <paramref name="index"/>, added as the
    /// <paramref name="generation"/>-th joint there; for worlds to make.</summary>
    public JointHandle(IPhysicsWorld world, int index, int generation)
    {
        World = world;
        Index = index;
        Generation = generation;
    }

    public IPhysicsWorld World { get; }

    public int Index { get; }

    public int Generation { get; }

    /// <summary>Whether the handle is not a default one; whether its joint is still there, its world says.</summary>
    public bool IsValid => World != null && Generation != 0;

    public bool Equals(JointHandle other) =>
        ReferenceEquals(World, other.World) && Index == other.Index && Generation == other.Generation;

    public override bool Equals(object obj) => obj is JointHandle other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(World, Index, Generation);

    public static bool operator ==(JointHandle left, JointHandle right) => left.Equals(right);

    public static bool operator !=(JointHandle left, JointHandle right) => !left.Equals(right);

    public override string ToString() => $"JointHandle {Index}:{Generation}";
}
