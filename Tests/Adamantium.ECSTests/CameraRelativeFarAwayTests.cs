using Adamantium.ECS;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.ECSTests;

/// <summary>Far from the origin an entity is drawn where it stands against the camera: its place is taken from the
/// camera's in double, and only the small way between them goes to float.</summary>
[TestFixture]
public class CameraRelativeFarAwayTests
{
    // Within a tenth of a millimeter of the true way from the camera.
    private const float Tolerance = 1e-4f;

    [TestCase(1e6)]
    [TestCase(1e9)]
    public void ARootNearTheCamera_IsDrawnWhereItStandsAgainstIt(double offset)
    {
        var scene = new PickScene();
        var far = new WorldPosition(offset, offset * 0.5, -offset);
        scene.Camera.Owner.Transform.Position = far;
        var box = new Entity(null, "Box");
        box.Transform.Position = far + new Vector3<double>(0.03, 0.01, 5);

        scene.Place(box);

        var metadata = box.Transform.GetMetadata(scene.Camera);
        var expected = new Vector3F(0.03f, 0.01f, 5);
        Assert.Multiple(() =>
        {
            Assert.That((metadata.RelativePosition - expected).Length(), Is.LessThan(Tolerance), "relative position");
            Assert.That((metadata.WorldMatrixF.TranslationVector - expected).Length(), Is.LessThan(Tolerance),
                "render matrix");
            Assert.That((box.Transform.WorldPosition - box.Transform.Position).Length(), Is.LessThan(1e-6),
                "world position");
        });
    }

    // A child turned with its parent, both far: the parent's place in double carries into the child's.
    [TestCase(1e6)]
    [TestCase(1e9)]
    public void AChildOfAFarParent_IsDrawnWhereItStandsAgainstTheCamera(double offset)
    {
        var scene = new PickScene();
        var far = new WorldPosition(offset, offset * 0.5, -offset);
        scene.Camera.Owner.Transform.Position = far;
        var parent = new Entity(null, "Parent");
        parent.Transform.Position = far + new Vector3<double>(0.02, 0, 3);
        parent.Transform.Rotation = Quaternion.CreateFromAxisAngle<float>(Vector3F.UnitY, float.DegreesToRadians(90));
        var child = new Entity(parent, "Child");
        child.Transform.Position = new WorldPosition(1, 0.04, 0);

        scene.Place(parent);

        var turned = Vector3F.Transform(new Vector3F(1, 0.04f, 0), parent.Transform.Rotation);
        var expected = new Vector3F(0.02f, 0, 3) + turned;
        var metadata = child.Transform.GetMetadata(scene.Camera);
        Assert.Multiple(() =>
        {
            Assert.That((metadata.RelativePosition - expected).Length(), Is.LessThan(Tolerance), "relative position");
            Assert.That((metadata.WorldMatrixF.TranslationVector - expected).Length(), Is.LessThan(Tolerance),
                "render matrix");
            Assert.That((child.Transform.WorldPosition - far - expected.As<double>()).Length(), Is.LessThan(1e-5),
                "world position");
        });
    }
}
