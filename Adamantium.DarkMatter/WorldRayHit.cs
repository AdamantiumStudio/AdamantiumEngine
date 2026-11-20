namespace Adamantium.DarkMatter;

/// <summary>Where a ray first met a body of an <see cref="IPhysicsWorld"/>.</summary>
internal readonly struct WorldRayHit
{
    public WorldRayHit(BodyHandle body, int part, WorldPosition position, Vector3F normal, float distance)
    {
        Body = body;
        Part = part;
        Position = position;
        Normal = normal;
        Distance = distance;
    }

    /// <summary>The body met.</summary>
    public BodyHandle Body { get; }

    /// <summary>Which of the body's parts was met: its place among a compound's parts, 0 for a body of one shape.
    /// </summary>
    public int Part { get; }

    /// <summary>The point met.</summary>
    public WorldPosition Position { get; }

    /// <summary>The body's outward normal there; against the ray for a ray starting inside.</summary>
    public Vector3F Normal { get; }

    /// <summary>How far along the ray, in meters.</summary>
    public float Distance { get; }
}
