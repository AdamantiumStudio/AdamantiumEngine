using Adamantium.DarkMatter.Shapes;

namespace Adamantium.DarkMatter.CollisionDetection;

/// <summary>A shape's core standing at a pose, as GJK and EPA meet it, in the world.</summary>
internal readonly struct PlacedCore
{
    private readonly CollisionShape _shape;
    private readonly Pose _pose;

    public PlacedCore(CollisionShape shape, in Pose pose)
    {
        _shape = shape;
        _pose = pose;
    }

    public Vector3F Center => _pose.Position;

    public Vector3F Support(Vector3F direction) =>
        _pose.TransformPoint(_shape.GetCoreSupport(_pose.InverseTransformDirection(direction)));

    /// <summary>The point of the difference of <paramref name="first"/> and <paramref name="second"/> farthest along
    /// <paramref name="direction"/>.</summary>
    public static SupportVertex SupportOfDifference(in PlacedCore first, in PlacedCore second, Vector3<double> direction)
    {
        var along = direction.As<float>();
        var onFirst = first.Support(along);
        var onSecond = second.Support(-along);
        return new SupportVertex { Point = onFirst.As<double>() - onSecond.As<double>(), OnFirst = onFirst, OnSecond = onSecond };
    }
}
