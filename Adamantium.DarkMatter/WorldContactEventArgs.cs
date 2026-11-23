namespace Adamantium.DarkMatter;

/// <summary>What the world tells of a touch of two parts begun or ended: by its own handles, the parts by their index
/// in their bodies.</summary>
internal readonly struct WorldContactEventArgs
{
    public BodyHandle First { get; init; }
    public BodyHandle Second { get; init; }
    public int FirstPart { get; init; }
    public int SecondPart { get; init; }
    public ContactChange Change { get; init; }
    public bool IsTrigger { get; init; }

    /// <summary>The surface of each part's material; null for none.</summary>
    public string FirstSurface { get; init; }
    public string SecondSurface { get; init; }

    /// <summary>Where they first touched, the normal from the first to the second, and how fast they closed; none of
    /// these for an end.</summary>
    public WorldPosition Point { get; init; }
    public Vector3F Normal { get; init; }
    public float ApproachSpeed { get; init; }
}
