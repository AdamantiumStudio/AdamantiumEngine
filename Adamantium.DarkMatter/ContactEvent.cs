namespace Adamantium.DarkMatter;

/// <summary>Two parts of two bodies began or stopped touching: a trigger entered or left among them.</summary>
public readonly struct ContactEvent
{
    internal ContactEvent(BodyId first, BodyId second, in WorldContactEvent told)
    {
        First = first;
        Second = second;
        FirstPart = told.FirstPart;
        SecondPart = told.SecondPart;
        Change = told.Change;
        IsTrigger = told.IsTrigger;
        Point = told.Point;
        Normal = told.Normal;
        ApproachSpeed = told.ApproachSpeed;
    }

    public BodyId First { get; }
    public BodyId Second { get; }

    /// <summary>Each body's part that touched, by its place among the body's parts; 0 for a body of one shape.</summary>
    public int FirstPart { get; }
    public int SecondPart { get; }

    public ContactChange Change { get; }

    /// <summary>Whether one of the parts is a trigger: it was entered or left, and pushed nothing.</summary>
    public bool IsTrigger { get; }

    /// <summary>Where they first touched; none for an end.</summary>
    public WorldPosition Point { get; }

    /// <summary>The normal of their touch as it began, pointing from the first to the second; none for an end.
    /// </summary>
    public Vector3F Normal { get; }

    /// <summary>How hard they met: how fast they closed along the normal as they began to touch, in meters per second -
    /// a sound's loudness, a hit's damage; zero for a trigger and for an end.</summary>
    public float ApproachSpeed { get; }

    /// <summary>Whether <paramref name="body"/> is one of the two, and which is the other.</summary>
    public bool Involves(BodyId body, out BodyId other)
    {
        other = body == First ? Second : body == Second ? First : default;
        return body == First || body == Second;
    }
}
