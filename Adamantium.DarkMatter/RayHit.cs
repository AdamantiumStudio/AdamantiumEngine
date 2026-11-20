namespace Adamantium.DarkMatter;

/// <summary>Where a ray met a body of a <see cref="PhysicsSimulation"/>.</summary>
public readonly struct RayHit
{
    public RayHit(BodyId body, int part, WorldPosition point, Vector3F normal, float distance)
    {
        Body = body;
        Part = part;
        Point = point;
        Normal = normal;
        Distance = distance;
    }

    /// <summary>The body met.</summary>
    public BodyId Body { get; }

    /// <summary>Which of its parts: its place among the body's parts, 0 for a body of one shape.</summary>
    public int Part { get; }

    /// <summary>The point met.</summary>
    public WorldPosition Point { get; }

    /// <summary>The body's outward normal there; against the ray for a ray starting inside.</summary>
    public Vector3F Normal { get; }

    /// <summary>How far along the ray, in meters.</summary>
    public float Distance { get; }
}
