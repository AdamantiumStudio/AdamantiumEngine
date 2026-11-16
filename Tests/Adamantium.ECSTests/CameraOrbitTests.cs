using Adamantium.Core;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.ECSTests;

/// <summary><see cref="Camera.Orbit"/>: a free camera turned about a point keeps that point where it was on its screen;
/// a turntable keeps the horizon level and stops short of the poles, a trackball goes over them.</summary>
[TestFixture]
public class CameraOrbitTests
{
    private static readonly Vector3<double> Pivot = new(3, -1, 4);

    [TestCase(CameraOrbitStyle.Turntable)]
    [TestCase(CameraOrbitStyle.Trackball)]
    public void ThePivot_StaysWhereItIsOnTheScreen_AtTheSameDistance(CameraOrbitStyle style)
    {
        var camera = NewCamera();
        var before = InView(camera, Pivot);

        camera.Orbit(Pivot, 25, -40, style);

        var after = InView(camera, Pivot);
        Assert.Multiple(() =>
        {
            Assert.That(after.X, Is.EqualTo(before.X).Within(1e-4));
            Assert.That(after.Y, Is.EqualTo(before.Y).Within(1e-4));
            Assert.That(after.Z, Is.EqualTo(before.Z).Within(1e-4));
            Assert.That(camera.Owner.Transform.Position, Is.Not.EqualTo(new Vector3<double>(0, 0, -10)), "the camera went round");
        });
    }

    [Test]
    public void ATurntable_KeepsTheHorizonLevel_AndStopsShortOfThePole()
    {
        var camera = NewCamera();

        for (var i = 0; i < 20; i++)
        {
            camera.Orbit(Pivot, 15, 7, CameraOrbitStyle.Turntable);
        }

        var view = Matrix4x4.CreateFromQuaternion<float>(camera.Rotation);
        Assert.Multiple(() =>
        {
            Assert.That(view.M21, Is.EqualTo(0).Within(1e-4), "the camera's sideways axis stays level");
            Assert.That(System.Math.Abs(view.M23), Is.LessThan(1f), "never looking straight down");
            Assert.That(view.M22, Is.GreaterThanOrEqualTo(0), "never upside down");
        });
    }

    [Test]
    public void ATrackball_GoesOverThePole()
    {
        var camera = NewCamera();

        camera.Orbit(Pivot, 120, 0, CameraOrbitStyle.Trackball);

        var view = Matrix4x4.CreateFromQuaternion<float>(camera.Rotation);
        Assert.That(view.M22, Is.LessThan(0), "past the top, the camera's up turned over");
    }

    [Test]
    public void ACameraRidingOnAHost_DoesNotOrbit()
    {
        var camera = NewCamera(new Entity(null, "Host"));
        var rotation = camera.Rotation;
        var position = camera.Owner.Transform.Position;

        camera.Orbit(Pivot, 25, -40, CameraOrbitStyle.Turntable);

        Assert.Multiple(() =>
        {
            Assert.That(camera.Rotation, Is.EqualTo(rotation));
            Assert.That(camera.Owner.Transform.Position, Is.EqualTo(position));
        });
    }

    // The pivot from the camera, in the axes of its view: where it is on the screen and how deep.
    private static Vector3F InView(Camera camera, Vector3<double> point) =>
        Vector3F.TransformNormal((point - camera.Owner.Transform.Position).As<float>(),
            Matrix4x4.CreateFromQuaternion<float>(camera.Rotation));

    private static Camera NewCamera(Entity host = null)
    {
        var camera = new Camera(60, 800, 600, 0.1f, 10000f);
        var entity = new Entity(host, "Camera");
        entity.AddComponent(camera);
        entity.Transform.Position = new Vector3<double>(0, 0, -10);
        camera.Update(new AppTime());
        return camera;
    }
}
