using System;

namespace Adamantium.DarkMatter;

/// <summary>A rigid body of a <see cref="PhysicsWorld"/>, as the handle the world gives on adding it. A removed body's
/// handle never equals a later one.</summary>
public readonly struct RigidBody : IEquatable<RigidBody>
{
    internal RigidBody(int index, int generation)
    {
        Index = index;
        Generation = generation;
    }

    internal int Index { get; }

    internal int Generation { get; }

    /// <summary>Whether a world gave this handle; a default one names no body.</summary>
    public bool IsValid => Generation != 0;

    public bool Equals(RigidBody other) => Index == other.Index && Generation == other.Generation;

    public override bool Equals(object obj) => obj is RigidBody other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Index, Generation);

    public static bool operator ==(RigidBody left, RigidBody right) => left.Equals(right);

    public static bool operator !=(RigidBody left, RigidBody right) => !left.Equals(right);

    public override string ToString() => $"RigidBody {Index}:{Generation}";
}
