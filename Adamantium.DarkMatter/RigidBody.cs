using System;

namespace Adamantium.DarkMatter;

/// <summary>A rigid body of an <see cref="IPhysicsWorld"/>, as the handle the world gives on adding it. It knows its
/// world, and a removed body's handle never equals a later one.</summary>
public readonly struct RigidBody : IEquatable<RigidBody>
{
    /// <summary>The handle of the body a world keeps in <paramref name="index"/>, added as the
    /// <paramref name="generation"/>-th body there; for worlds to make.</summary>
    public RigidBody(IPhysicsWorld world, int index, int generation)
    {
        World = world;
        Index = index;
        Generation = generation;
    }

    /// <summary>The world that gave the handle.</summary>
    public IPhysicsWorld World { get; }

    /// <summary>Where the world keeps the body.</summary>
    public int Index { get; }

    /// <summary>How many bodies the world has kept there, this one included; zero for no body.</summary>
    public int Generation { get; }

    /// <summary>Whether a world gave this handle; a default one names no body.</summary>
    public bool IsValid => World != null && Generation != 0;

    public bool Equals(RigidBody other) =>
        ReferenceEquals(World, other.World) && Index == other.Index && Generation == other.Generation;

    public override bool Equals(object obj) => obj is RigidBody other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(World, Index, Generation);

    public static bool operator ==(RigidBody left, RigidBody right) => left.Equals(right);

    public static bool operator !=(RigidBody left, RigidBody right) => !left.Equals(right);

    public override string ToString() => $"RigidBody {Index}:{Generation}";
}
