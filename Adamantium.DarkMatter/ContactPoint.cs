namespace Adamantium.DarkMatter;

/// <summary>A point where two bodies touched in the last step, or were about to, as instruments show it.</summary>
public readonly struct ContactPoint
{
    public ContactPoint(RigidBody first, RigidBody second, Vector3F position, Vector3F normal, float separation,
        float normalImpulse)
    {
        First = first;
        Second = second;
        Position = position;
        Normal = normal;
        Separation = separation;
        NormalImpulse = normalImpulse;
    }

    /// <summary>The body the normal leaves.</summary>
    public RigidBody First { get; }

    /// <summary>The body the normal points at.</summary>
    public RigidBody Second { get; }

    /// <summary>Where the point is, midway between the two surfaces.</summary>
    public Vector3F Position { get; }

    /// <summary>The direction from the first body to the second.</summary>
    public Vector3F Normal { get; }

    /// <summary>The gap between the surfaces along the normal, in meters; negative while they overlap.</summary>
    public float Separation { get; }

    /// <summary>The push along the normal the point gave in the last substep, in newton seconds.</summary>
    public float NormalImpulse { get; }
}
