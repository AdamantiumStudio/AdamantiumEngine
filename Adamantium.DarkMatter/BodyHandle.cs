using System;

namespace Adamantium.DarkMatter;

/// <summary>A handle to a body in an <see cref="IPhysicsWorld"/>, given when the body is added. It knows its world,
/// and a removed body's handle never equals a later one.</summary>
public readonly struct BodyHandle : IEquatable<BodyHandle>
{
    /// <summary>The handle of the body a world keeps in <paramref name="index"/>, added as the
    /// <paramref name="generation"/>-th body there; for worlds to make.</summary>
    public BodyHandle(IPhysicsWorld world, int index, int generation)
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

    /// <summary>Whether the handle is not a default one; whether its body is still there, its world's
    /// <see cref="IPhysicsWorld.Contains"/> says.</summary>
    public bool IsValid => World != null && Generation != 0;

    public bool Equals(BodyHandle other) =>
        ReferenceEquals(World, other.World) && Index == other.Index && Generation == other.Generation;

    public override bool Equals(object obj) => obj is BodyHandle other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(World, Index, Generation);

    public static bool operator ==(BodyHandle left, BodyHandle right) => left.Equals(right);

    public static bool operator !=(BodyHandle left, BodyHandle right) => !left.Equals(right);

    public override string ToString() => $"BodyHandle {Index}:{Generation}";
}
