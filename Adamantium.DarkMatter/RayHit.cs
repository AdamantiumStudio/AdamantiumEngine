namespace Adamantium.DarkMatter;

/// <summary>Where a ray first met a body.</summary>
public readonly struct RayHit
{
    public RayHit(BodyHandle body, WorldPosition position, Vector3F normal, float distance)
    {
        Body = body;
        Position = position;
        Normal = normal;
        Distance = distance;
    }

    /// <summary>The body met.</summary>
    public BodyHandle Body { get; }

    /// <summary>The point met.</summary>
    public WorldPosition Position { get; }

    /// <summary>The body's outward normal there; against the ray for a ray starting inside.</summary>
    public Vector3F Normal { get; }

    /// <summary>How far along the ray, in meters.</summary>
    public float Distance { get; }
}
