using Adamantium.Engine.Tools;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.EngineTests;

[TestFixture]
public class PlacementToolTests
{
    [Test]
    public void ARayLookingDown_MeetsTheGroundUnderIt_FarFromTheWorldsOrigin()
    {
        var eye = new Vector3(1e7, -10, 2e7);
        var ray = new Ray(Vector3F.Zero, Vector3F.Normalize(new Vector3F(1, 1, 0)));

        Assert.That(PlacementTool.OnGround(ray, eye, 0, 1000, out var point), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(point.X, Is.EqualTo(1e7 + 10).Within(1e-3));
            Assert.That(point.Y, Is.EqualTo(0).Within(1e-6));
            Assert.That(point.Z, Is.EqualTo(2e7).Within(1e-3));
        });
    }

    [Test]
    public void ARaisedGround_IsMetAtItsHeight()
    {
        var ray = new Ray(Vector3F.Zero, Vector3F.UnitY);

        Assert.That(PlacementTool.OnGround(ray, new Vector3(0, -10, 0), -4, 1000, out var point), Is.True);
        Assert.That(point.Y, Is.EqualTo(-4).Within(1e-6));
    }

    [TestCase(0, -1, 0)]
    [TestCase(1, 0, 0)]
    public void ARayAwayFromOrAlongTheGround_MeetsNothing(float x, float y, float z)
    {
        var ray = new Ray(Vector3F.Zero, new Vector3F(x, y, z));

        Assert.That(PlacementTool.OnGround(ray, new Vector3(0, -10, 0), 0, 1000, out _), Is.False);
    }

    [Test]
    public void GroundPastTheReach_IsNotMet()
    {
        var ray = new Ray(Vector3F.Zero, Vector3F.Normalize(new Vector3F(100, 1, 0)));

        Assert.That(PlacementTool.OnGround(ray, new Vector3(0, -10, 0), 0, 500, out _), Is.False);
    }

    // A flat view casts its rays from far behind the camera: the reach counts across the ground, not along the ray.
    [Test]
    public void ARayFromFarBehindTheEye_MeetsTheGroundNearIt()
    {
        var direction = Vector3F.Normalize(new Vector3F(0, 1, 1));
        var ray = new Ray(direction * -10000, direction);

        Assert.That(PlacementTool.OnGround(ray, new Vector3(0, -10, 0), 0, 500, out var point), Is.True);
        Assert.That(point.Z, Is.EqualTo(10).Within(1e-2));
    }

    // Looking along the ground, or up, the pointer meets it far off or never: the object goes onto the ground under
    // what the pointer shows at the camera's focus depth.
    [TestCase(0f)]
    [TestCase(-0.5f)]
    public void ARayAlongTheGroundOrAbove_GoesOntoTheGroundUnderTheFocus(float up)
    {
        var camera = new Adamantium.ECS.Components.Camera(60, 800, 600, 0.1f, 1000f) { FocusDistance = 12 };
        new Adamantium.ECS.Entity(null, "Camera").AddComponent(camera);
        camera.Update(new Adamantium.Core.AppTime());
        var ray = new Ray(Vector3F.Zero, Vector3F.Normalize(new Vector3F(0.2f, up, 1)));

        var point = PlacementTool.UnderFocus(ray, camera, 0, camera.FocusDistance);

        Assert.Multiple(() =>
        {
            Assert.That(point.Y, Is.EqualTo(0), "on the ground");
            Assert.That(point.Z, Is.EqualTo(12).Within(1e-3), "at the focus depth along the view");
            Assert.That(point.X, Is.EqualTo(2.4).Within(1e-3), "under the pointer");
        });
    }

    // A flat view's ray starts far behind the eye: the depth still counts from the eye.
    [Test]
    public void AFlatViewsRay_GoesOntoTheGroundAtTheDepthFromTheEye()
    {
        var camera = new Adamantium.ECS.Components.Camera(60, 800, 600, 0.1f, 1000f);
        new Adamantium.ECS.Entity(null, "Camera").AddComponent(camera);
        camera.Owner.Transform.Position = new Vector3(4, -3, 1);
        camera.Update(new Adamantium.Core.AppTime());
        var ray = new Ray(new Vector3F(0.5f, 0, -10000), Vector3F.UnitZ);

        var point = PlacementTool.UnderFocus(ray, camera, 0, 12);

        Assert.That(point, Is.EqualTo(new Vector3(4.5, 0, 13)).Using<Vector3>((a, b) => (a - b).Length() < 1e-2));
    }

    // Where the ground under the pointer is as deep as the depth asked, the two points are one: no jump between them.
    [Test]
    public void TheGroundAtTheDepth_IsTheVeryPointBeyondIt()
    {
        var camera = new Adamantium.ECS.Components.Camera(60, 800, 600, 0.1f, 1000f);
        new Adamantium.ECS.Entity(null, "Camera").AddComponent(camera);
        camera.Owner.Transform.Position = new Vector3(0, -3, 0);
        camera.Update(new Adamantium.Core.AppTime());
        var ray = new Ray(Vector3F.Zero, Vector3F.Normalize(new Vector3F(0, 3, 20)));

        PlacementTool.OnGround(ray, camera.WorldPosition, 0, 500, out var onGround);
        var beyond = PlacementTool.UnderFocus(ray, camera, 0, 20);

        Assert.That((onGround - beyond).Length(), Is.LessThan(1e-3));
    }

    [Test]
    public void AnObject_RestsOnThePointByItsLowestPoint_UpBeingMinusY()
    {
        var position = PlacementTool.RestOn(new Vector3(3, 0, 4), 0.5f);

        Assert.That(position, Is.EqualTo(new Vector3(3, -0.5, 4)));
    }
}
