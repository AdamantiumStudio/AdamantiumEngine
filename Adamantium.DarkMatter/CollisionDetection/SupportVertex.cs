namespace Adamantium.DarkMatter.CollisionDetection;

/// <summary>A point of the difference of two cores and the points of each it is made of: the first's minus the
/// second's. The difference is kept in double, so a small gap beside a huge face is still measured.</summary>
internal struct SupportVertex
{
    public Vector3<double> Point;
    public Vector3F OnFirst;
    public Vector3F OnSecond;
}
