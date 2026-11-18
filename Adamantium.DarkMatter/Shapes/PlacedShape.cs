namespace Adamantium.DarkMatter.Shapes;

/// <summary>A shape standing at a pose, as <see cref="Gjk"/> tests it.</summary>
internal readonly struct PlacedShape : IConvexShape<float>
{
    private readonly CollisionShape _shape;
    private readonly Pose _pose;

    public PlacedShape(CollisionShape shape, in Pose pose)
    {
        _shape = shape;
        _pose = pose;
    }

    public Vector3F Support(Vector3F direction) =>
        _pose.TransformPoint(_shape.GetSupport(_pose.InverseTransformDirection(direction)));
}
