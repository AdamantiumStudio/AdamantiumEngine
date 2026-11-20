using Adamantium.DarkMatter.Shapes;

namespace Adamantium.DarkMatter.CollisionDetection;

/// <summary>A shape's core standing at a pose, as GJK and EPA meet it, in the world; its turn is made a matrix once,
/// not worked out of the quaternion for every support point.</summary>
internal readonly struct PlacedCore
{
    private readonly CollisionShape _shape;
    private readonly Vector3F _position;
    private readonly Matrix3x3F _rotation;

    public PlacedCore(CollisionShape shape, in Pose pose)
    {
        _shape = shape;
        _position = pose.Position;
        _rotation = Matrix3x3.CreateFromQuaternion(pose.Rotation);
    }

    private PlacedCore(CollisionShape shape, Vector3F position, in Matrix3x3F rotation)
    {
        _shape = shape;
        _position = position;
        _rotation = rotation;
    }

    public Vector3F Center => _position;

    /// <summary>The same core, turned the same, standing at <paramref name="position"/>.</summary>
    public PlacedCore MovedTo(Vector3F position) => new(_shape, position, _rotation);

    public Vector3F Support(Vector3F direction)
    {
        // Into the shape's axes by the turn's transpose, and its point back out by the turn.
        var local = new Vector3F(Vector3F.Dot(direction, _rotation.Row1), Vector3F.Dot(direction, _rotation.Row2),
            Vector3F.Dot(direction, _rotation.Row3));
        var point = _shape.GetCoreSupport(local);
        return _position + point.X * _rotation.Row1 + point.Y * _rotation.Row2 + point.Z * _rotation.Row3;
    }

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
