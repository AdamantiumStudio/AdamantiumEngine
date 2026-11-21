using Adamantium.DarkMatter.Shapes;

namespace Adamantium.DarkMatter.Dynamics;

/// <summary>One shape of a body in the broad phase: a simple body has one, a compound one per part.</summary>
internal struct PartState
{
    public int Body;
    public CollisionShape Shape;

    /// <summary>Where the shape stands against the body's center of mass, in the body's own axes.</summary>
    public LocalPose Pose;

    /// <summary>Whether the shape's center is the body's center of mass, so it spins in place.</summary>
    public bool IsCentered;

    /// <summary>Whether the shape stands as its body does, centered and not turned: a body of one shape.</summary>
    public bool HasBodyPose;

    /// <summary>The material the part's contacts meet by: its own, or its body's.</summary>
    public PhysicsMaterial Material;

    /// <summary>Whether <see cref="Material"/> is the part's own, kept when the body's changes.</summary>
    public bool HasOwnMaterial;

    /// <summary>The index of the layer it collides on: its own, or its body's.</summary>
    public int Layer;

    /// <summary>Whether <see cref="Layer"/> is the part's own, kept when the body's changes.</summary>
    public bool HasOwnLayer;
    public int Proxy;
    public int Next;
}
