using Adamantium.ECS.Components.Extensions;
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
        var eye = new Vector3<double>(1e7, -10, 2e7);
        var ray = new Ray<float>(Vector3F.Zero, Vector3F.Normalize(new Vector3F(1, 1, 0)));

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
        var ray = new Ray<float>(Vector3F.Zero, Vector3F.UnitY);

        Assert.That(PlacementTool.OnGround(ray, new Vector3<double>(0, -10, 0), -4, 1000, out var point), Is.True);
        Assert.That(point.Y, Is.EqualTo(-4).Within(1e-6));
    }

    [TestCase(0, -1, 0)]
    [TestCase(1, 0, 0)]
    public void ARayAwayFromOrAlongTheGround_MeetsNothing(float x, float y, float z)
    {
        var ray = new Ray<float>(Vector3F.Zero, new Vector3F(x, y, z));

        Assert.That(PlacementTool.OnGround(ray, new Vector3<double>(0, -10, 0), 0, 1000, out _), Is.False);
    }

    [Test]
    public void GroundPastTheReach_IsNotMet()
    {
        var ray = new Ray<float>(Vector3F.Zero, Vector3F.Normalize(new Vector3F(100, 1, 0)));

        Assert.That(PlacementTool.OnGround(ray, new Vector3<double>(0, -10, 0), 0, 500, out _), Is.False);
    }

    // A flat view casts its rays from far behind the camera: the reach counts across the ground, not along the ray.
    [Test]
    public void ARayFromFarBehindTheEye_MeetsTheGroundNearIt()
    {
        var direction = Vector3F.Normalize(new Vector3F(0, 1, 1));
        var ray = new Ray<float>(direction * -10000, direction);

        Assert.That(PlacementTool.OnGround(ray, new Vector3<double>(0, -10, 0), 0, 500, out var point), Is.True);
        Assert.That(point.Z, Is.EqualTo(10).Within(1e-2));
    }

    // With no surface under the pointer - along the ground, above it - the ghost hangs on the pointer at the depth asked,
    // along the view.
    [TestCase(0f)]
    [TestCase(-0.5f)]
    public void ARayMeetingNoSurface_HangsAtTheDepthAheadOfTheEye(float up)
    {
        var camera = new Adamantium.ECS.Components.Camera(60, 800, 600, 0.1f, 1000f);
        new Adamantium.ECS.Entity(null, "Camera").AddComponent(camera);
        camera.Update(new Adamantium.Core.AppTime());
        var ray = new Ray<float>(Vector3F.Zero, Vector3F.Normalize(new Vector3F(0.2f, up, 1)));

        var point = PlacementTool.Along(ray, camera, 12);

        Assert.Multiple(() =>
        {
            Assert.That(point.Z, Is.EqualTo(12).Within(1e-3), "at the depth along the view");
            Assert.That(point.X, Is.EqualTo(2.4).Within(1e-3), "under the pointer");
            Assert.That(point.Y, Is.EqualTo(up * 12).Within(1e-3), "on the pointer's ray, not dropped to the ground");
        });
    }

    // A flat view's ray starts far behind the eye: the depth still counts from the eye.
    [Test]
    public void AFlatViewsRay_HangsAtTheDepthFromTheEye()
    {
        var camera = new Adamantium.ECS.Components.Camera(60, 800, 600, 0.1f, 1000f);
        new Adamantium.ECS.Entity(null, "Camera").AddComponent(camera);
        camera.Owner.Transform.Position = new Vector3<double>(4, -3, 1);
        camera.Update(new Adamantium.Core.AppTime());
        var ray = new Ray<float>(new Vector3F(0.5f, 0, -10000), Vector3F.UnitZ);

        var point = PlacementTool.Along(ray, camera, 12);

        Assert.That(point, Is.EqualTo(new Vector3<double>(4.5, -3, 13)).Using<Vector3<double>>((a, b) => (a - b).Length() < 1e-2));
    }

    // A view along the ground - a flat side view, an eye on it - meets it ahead nowhere: the object goes onto the
    // ground under what the pointer shows at the depth asked.
    [TestCase(0f)]
    [TestCase(-0.5f)]
    public void AViewAlongTheGround_PutsTheObjectOnTheGroundUnderThePointer(float up)
    {
        var camera = new Adamantium.ECS.Components.Camera(60, 800, 600, 0.1f, 1000f);
        new Adamantium.ECS.Entity(null, "Camera").AddComponent(camera);
        camera.Update(new Adamantium.Core.AppTime());
        var ray = new Ray<float>(Vector3F.Zero, Vector3F.Normalize(new Vector3F(0.2f, up, 1)));

        var point = PlacementTool.UnderFocus(ray, camera, 0, 12);

        Assert.Multiple(() =>
        {
            Assert.That(point.Y, Is.EqualTo(0), "on the ground");
            Assert.That(point.Z, Is.EqualTo(12).Within(1e-3), "at the depth along the view");
            Assert.That(point.X, Is.EqualTo(2.4).Within(1e-3), "under the pointer");
        });
    }

    // The ghost hangs at the depth it last touched a surface, so leaving the ground for the sky above it, it stays
    // where it was under the pointer: the two points are one.
    [Test]
    public void TheGroundAtADepth_IsWhereTheGhostHangsAtIt()
    {
        var camera = new Adamantium.ECS.Components.Camera(60, 800, 600, 0.1f, 1000f);
        new Adamantium.ECS.Entity(null, "Camera").AddComponent(camera);
        camera.Owner.Transform.Position = new Vector3<double>(0, -3, 0);
        camera.Update(new Adamantium.Core.AppTime());
        var ray = new Ray<float>(Vector3F.Zero, Vector3F.Normalize(new Vector3F(0, 3, 20)));

        PlacementTool.OnGround(ray, camera.WorldPosition, 0, 500, out var onGround);
        var hanging = PlacementTool.Along(ray, camera, 20);

        Assert.That((onGround - hanging).Length(), Is.LessThan(1e-3));
    }

    // Brought in over the sky, the ghost hangs as deep as the ground the pointer first meets going down the view: as
    // small as it will be on landing there, not at the camera's focus right in front of the eye.
    [Test]
    public void OverTheSky_TheGroundFirstMetBelowIsFarOff()
    {
        var camera = RaisedCamera();
        var sky = new Vector2F(400, 10);

        var depth = PlacementTool.FirstGroundBelow(camera, sky, 0, 500);

        Assert.Multiple(() =>
        {
            Assert.That(depth, Is.Not.Null);
            Assert.That(depth, Is.GreaterThan(camera.FocusDistance * 10), "far off, not at the focus");
            Assert.That(depth, Is.LessThanOrEqualTo(500.5), "no further than the ground reaches");
        });
    }

    // Over the ground already, the first ground met is the ground under the pointer: no jump as the ghost lands.
    [Test]
    public void OverTheGround_TheGroundFirstMetBelowIsUnderThePointer()
    {
        var camera = RaisedCamera();
        var pixel = new Vector2F(400, 500);
        PlacementTool.OnGround(PickRay.FromCamera(camera, pixel).Ray, camera.WorldPosition, 0, 500, out var under);

        var depth = PlacementTool.FirstGroundBelow(camera, pixel, 0, 500);

        Assert.That(depth, Is.EqualTo(Vector3<double>.Dot(under - camera.WorldPosition, camera.Forward.As<double>())).Within(1e-3));
    }

    private static Adamantium.ECS.Components.Camera RaisedCamera()
    {
        var camera = new Adamantium.ECS.Components.Camera(60, 800, 600, 0.1f, 1000f) { FocusDistance = 2 };
        new Adamantium.ECS.Entity(null, "Camera").AddComponent(camera);
        camera.Owner.Transform.Position = new Vector3<double>(0, -3, 0);
        camera.Update(new Adamantium.Core.AppTime());
        return camera;
    }

    [Test]
    public void AnObject_RestsOnThePointByItsLowestPoint_UpBeingMinusY()
    {
        var position = PlacementTool.RestOn(new Vector3<double>(3, 0, 4), 0.5f);

        Assert.That(position, Is.EqualTo(new Vector3<double>(3, -0.5, 4)));
    }
}
