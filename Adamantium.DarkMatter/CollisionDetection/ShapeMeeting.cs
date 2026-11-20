namespace Adamantium.DarkMatter.CollisionDetection;

/// <summary>Where a cast shape meets another: the shares of its translation where it stops and where it meets, and the
/// other's outward normal and point at the stop.</summary>
internal struct ShapeMeeting
{
    public float Stop;
    public float Meet;
    public Vector3F Normal;
    public Vector3F Point;
}
