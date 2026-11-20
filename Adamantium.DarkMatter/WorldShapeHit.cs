namespace Adamantium.DarkMatter;

/// <summary>Where a moving shape first met a body of an <see cref="IPhysicsWorld"/>.</summary>
public readonly struct WorldShapeHit
{
    public WorldShapeHit(BodyHandle body, int part, WorldPosition position, Vector3F normal, float distance)
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

    /// <summary>The point of the body met.</summary>
    public WorldPosition Position { get; }

    /// <summary>The body's outward normal there, toward the moving shape.</summary>
    public Vector3F Normal { get; }

    /// <summary>How far the shape moves before it stops by the body, in meters.</summary>
    public float Distance { get; }
}
