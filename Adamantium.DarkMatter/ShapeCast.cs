namespace Adamantium.DarkMatter;

/// <summary>A shape cast under way: how far it moves, the share of that at which it meets the body met nearest yet,
/// and that meeting.</summary>
internal struct ShapeCast
{
    public Vector3F Translation;
    public float Nearest;
    public bool Found;

    /// <summary>The world's own index of the body met nearest yet.</summary>
    public int Body;
    public ShapeHit Hit;
}
