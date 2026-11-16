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

    // A trackball rolls the horizon; Level stands the camera upright again, flowing into it: halfway it is partly there,
    // at the end level, looking the same way and standing where it stood.
    [Test]
    public void Level_FlowsARolledCameraUpright_LookingTheSameWay_WhereItStands()
    {
        var camera = Rolled();
        var rolled = Matrix4x4.CreateFromQuaternion<float>(camera.Rotation);
        var position = camera.Owner.Transform.Position;

        camera.Level();
        Frames(camera, camera.ProjectionTransitionTime / 2.0);
        var halfway = Matrix4x4.CreateFromQuaternion<float>(camera.Rotation).M21;
        Frames(camera, camera.ProjectionTransitionTime);

        var level = Matrix4x4.CreateFromQuaternion<float>(camera.Rotation);
        var forwardBefore = new Vector3F(rolled.M13, rolled.M23, rolled.M33);
        var forwardAfter = new Vector3F(level.M13, level.M23, level.M33);
        Assert.Multiple(() =>
        {
            Assert.That(System.Math.Abs(halfway), Is.LessThan(System.Math.Abs(rolled.M21)).And.GreaterThan(1e-3f), "on the way");
            Assert.That(level.M21, Is.EqualTo(0).Within(1e-4), "the sideways axis level");
            Assert.That(level.M22, Is.GreaterThan(0), "upright");
            Assert.That(Vector3F.Dot(forwardBefore, forwardAfter), Is.GreaterThan(0.9999f), "looking the same way");
            Assert.That((camera.Owner.Transform.Position - position).Length(), Is.LessThan(1e-9), "where it stood");
        });
    }

    // A turntable orbit right after the switch goes on with the leveling, not against it: it ends level all the same,
    // the orbit's turn kept.
    [Test]
    public void ATurntableOrbitWhileItLevels_EndsLevelAllTheSame()
    {
        var camera = Rolled();
        camera.Level();
        var started = Matrix4x4.CreateFromQuaternion<float>(camera.Rotation);

        for (var i = 0; i < 30; i++)
        {
            camera.Orbit(Pivot, 0, 2, CameraOrbitStyle.Turntable);
            camera.Update(new AppTime { FrameTime = 1.0 / 60 });
        }

        var level = Matrix4x4.CreateFromQuaternion<float>(camera.Rotation);
        var startedForward = new Vector3F(started.M13, started.M23, started.M33);
        var forward = new Vector3F(level.M13, level.M23, level.M33);
        Assert.Multiple(() =>
        {
            Assert.That(level.M21, Is.EqualTo(0).Within(1e-4), "level");
            Assert.That(Vector3F.Dot(startedForward, forward), Is.LessThan(0.99f), "and the orbit turned it");
        });
    }

    // Isometric switched on while it levels: the view turns isometric as ever, and the leveling gives way.
    [Test]
    public void IsometricWhileItLevels_TurnsIsometricAsEver()
    {
        var camera = Rolled();
        camera.Level();
        Frames(camera, camera.ProjectionTransitionTime / 3.0);

        camera.IsIsometric = true;
        Frames(camera, camera.ProjectionTransitionTime * 3);

        var view = Matrix4x4.CreateFromQuaternion<float>(camera.Rotation);
        var forward = new Vector3F(view.M13, view.M23, view.M33);
        Assert.Multiple(() =>
        {
            Assert.That(camera.IsIsometric, Is.True);
            Assert.That(System.Math.Abs(forward.Y), Is.EqualTo(1 / System.MathF.Sqrt(3)).Within(1e-3f), "down a cube's diagonal");
        });
    }

    // A trackball drag, or a rotation set from elsewhere - a view put back, an undo - is meant too: either ends it.
    [TestCase(true)]
    [TestCase(false)]
    public void ATrackballOrARotationSetFromElsewhere_EndsTheLeveling(bool trackball)
    {
        var camera = Rolled();
        camera.Level();
        Frames(camera, camera.ProjectionTransitionTime / 3.0);
        Assume.That(System.Math.Abs(Matrix4x4.CreateFromQuaternion<float>(camera.Rotation).M21), Is.GreaterThan(1e-3f));

        if (trackball)
        {
            camera.Orbit(Pivot, 6, 9, CameraOrbitStyle.Trackball);
        }
        else
        {
            camera.Rotation = Quaternion.CreateFromAxisAngle<float>(Vector3F.UnitZ, 0.4f);
        }

        var meant = camera.Rotation;
        Frames(camera, camera.ProjectionTransitionTime);

        Assert.That(System.Math.Abs(QuaternionF.Dot(camera.Rotation, meant)), Is.GreaterThan(0.99999f));
    }

    // Leveled while isometric, the camera keeps the way it will turn back to level: out of isometric, the horizon is
    // level.
    [Test]
    public void LeveledWhileIsometric_TurnsBackLevel()
    {
        var camera = Rolled();
        camera.IsIsometric = true;
        Frames(camera, camera.ProjectionTransitionTime * 2);

        camera.Level();
        camera.IsIsometric = false;
        Frames(camera, camera.ProjectionTransitionTime * 2);

        Assert.That(Matrix4x4.CreateFromQuaternion<float>(camera.Rotation).M21, Is.EqualTo(0).Within(1e-4f));
    }

    // Leveled as the view turns back out of isometric: it waits the turn out, then levels.
    [Test]
    public void LeveledAsTheViewTurnsOutOfIsometric_LevelsAfter()
    {
        var camera = Rolled();
        camera.IsIsometric = true;
        Frames(camera, camera.ProjectionTransitionTime * 2);
        camera.IsIsometric = false;
        camera.Update(new AppTime { FrameTime = 1.0 / 60 });

        camera.Level();
        Frames(camera, camera.ProjectionTransitionTime * 3);

        Assert.That(Matrix4x4.CreateFromQuaternion<float>(camera.Rotation).M21, Is.EqualTo(0).Within(1e-4f));
    }

    // A trackball took it over the top: upside down, its sideways axis level. Level stands it upright all the same.
    [Test]
    public void Level_StandsACameraOverTheTopUpright()
    {
        var camera = NewCamera();
        camera.Orbit(Pivot, 120, 0, CameraOrbitStyle.Trackball);
        Assume.That(Matrix4x4.CreateFromQuaternion<float>(camera.Rotation).M22, Is.LessThan(0), "upside down");

        camera.Level();
        Frames(camera, camera.ProjectionTransitionTime * 2);

        var view = Matrix4x4.CreateFromQuaternion<float>(camera.Rotation);
        Assert.Multiple(() =>
        {
            Assert.That(view.M22, Is.GreaterThan(0), "upright");
            Assert.That(view.M21, Is.EqualTo(0).Within(1e-4f), "level");
        });
    }

    // A trackball drag with the camera looking level, where it barely changes the roll, still ends the leveling.
    [Test]
    public void ATrackballLookingLevel_EndsTheLeveling()
    {
        var camera = NewCamera();
        camera.RotateForward(25);
        camera.Update(new AppTime { FrameTime = 1.0 / 60 });
        camera.Level();
        Frames(camera, camera.ProjectionTransitionTime / 3.0);

        camera.Orbit(Pivot, 0, 0.5f, CameraOrbitStyle.Trackball);
        var meant = camera.Rotation;
        Frames(camera, camera.ProjectionTransitionTime);

        Assert.That(System.Math.Abs(QuaternionF.Dot(camera.Rotation, meant)), Is.GreaterThan(0.99999f));
    }

    // Leveled while the orientation gizmo turns the camera to a rolled view: it waits the turn out, then levels.
    [Test]
    public void LeveledDuringAGizmoTurn_LevelsAfter()
    {
        var camera = NewCamera();
        camera.RotateAroundSelectedObject(Quaternion.CreateFromAxisAngle<float>(Vector3F.UnitZ, 0.5f), 200);

        camera.Level();
        Frames(camera, 200 + camera.ProjectionTransitionTime * 2);

        Assert.That(Matrix4x4.CreateFromQuaternion<float>(camera.Rotation).M21, Is.EqualTo(0).Within(1e-4f));
    }

    // A roll by hand is meant: the leveling stops where it got to and leaves the roll as the hand made it.
    [Test]
    public void ARollByHand_EndsTheLeveling()
    {
        var camera = Rolled();
        camera.Level();
        Frames(camera, camera.ProjectionTransitionTime / 3.0);
        var partly = Matrix4x4.CreateFromQuaternion<float>(camera.Rotation).M21;
        Assume.That(System.Math.Abs(partly), Is.GreaterThan(1e-3f), "it was leveling");

        camera.RotateForward(10);
        var rolled = camera.Rotation;
        Frames(camera, camera.ProjectionTransitionTime);

        Assert.That(System.Math.Abs(QuaternionF.Dot(camera.Rotation, rolled)), Is.GreaterThan(0.99999f));
    }

    private static Camera Rolled()
    {
        var camera = NewCamera();
        camera.Orbit(Pivot, 40, 0, CameraOrbitStyle.Trackball);
        camera.Orbit(Pivot, 0, 50, CameraOrbitStyle.Trackball);
        Assume.That(System.Math.Abs(Matrix4x4.CreateFromQuaternion<float>(camera.Rotation).M21), Is.GreaterThan(0.05f),
            "the trackball rolled it");
        return camera;
    }

    private static void Frames(Camera camera, double milliseconds)
    {
        for (var elapsed = 0.0; elapsed < milliseconds; elapsed += 1000.0 / 60)
        {
            camera.Update(new AppTime { FrameTime = 1.0 / 60 });
        }
    }

    // An isometric view stays isometric under a trackball: it turns only about the vertical, the pivot kept.
    [Test]
    public void AnIsometricView_StaysIsometric_UnderATrackball()
    {
        var camera = NewCamera();
        camera.IsIsometric = true;
        for (var i = 0; i < 60; i++)
        {
            camera.Update(new AppTime { FrameTime = 0.05 });
        }

        var before = InView(camera, Pivot);
        camera.Orbit(Pivot, 30, 20, CameraOrbitStyle.Trackball);
        camera.Update(new AppTime { FrameTime = 0.05 });

        var after = InView(camera, Pivot);
        Assert.Multiple(() =>
        {
            Assert.That(camera.IsIsometric, Is.True);
            Assert.That(after.X, Is.EqualTo(before.X).Within(1e-3));
            Assert.That(after.Y, Is.EqualTo(before.Y).Within(1e-3));
        });
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
