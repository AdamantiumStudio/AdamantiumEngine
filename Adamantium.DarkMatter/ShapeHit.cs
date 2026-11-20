namespace Adamantium.DarkMatter;

/// <summary>Where a moving shape first met a body.</summary>
public readonly struct ShapeHit
{
    public ShapeHit(BodyHandle body, Vector3<double> position, Vector3F normal, float distance)
    {
        Body = body;
        Position = position;
        Normal = normal;
        Distance = distance;
    }

    /// <summary>The body met.</summary>
    public BodyHandle Body { get; }

    /// <summary>The point of the body met.</summary>
    public Vector3<double> Position { get; }

    /// <summary>The body's outward normal there, toward the moving shape.</summary>
    public Vector3F Normal { get; }

    /// <summary>How far the shape moves before it stops by the body, in meters.</summary>
    public float Distance { get; }
}
