namespace Adamantium.DarkMatter;

/// <summary>A shape cast under way: how far it moves, the share of that at which it meets the body met nearest yet,
/// and that meeting.</summary>
internal struct ShapeCast
{
    /// <summary>Where the cast starts: its parts and meetings are worked out in float from here.</summary>
    public WorldPosition Origin;
    public Vector3F Translation;
    public float Nearest;
    public bool Found;

    /// <summary>The world's own index of the body met nearest yet.</summary>
    public int Body;

    /// <summary>The meeting nearest yet, its part told by the world's own slot of it until the cast is done.</summary>
    public WorldShapeHit Hit;

    /// <summary>The world's own index of the body the cast passes by; none for -1.</summary>
    public int Except;

    /// <summary>The parts it meets.</summary>
    public PartFilter Parts;
}
