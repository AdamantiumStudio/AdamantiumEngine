using System;
using Adamantium.Core;
using Adamantium.Core.DependencyInjection;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.ECS.Components.Extensions;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.ECSTests;

/// <summary>A change of projection flows from the old into the new keeping the plane the camera looks at the same
/// size; a flat view zooms by its scale, and an isometric one turns to the isometric angle about the point it looks
/// at. A camera hung on another entity travels there.</summary>
[TestFixture]
public class CameraProjectionChangeTests
{
    private const double Focus = 12;

    private static Camera FreeCamera()
    {
        var camera = new Camera(60, 800, 600, 0.1f, 1000f) { FocusDistance = Focus };
        new Entity(null, "Camera").AddComponent(camera);
        camera.Update(new AppTime());
        camera.Owner.Transform.Position = new Vector3(3, -2, 1);
        camera.Rotation = QuaternionF.RotationYawPitchRoll(0.4f, 0.2f, 0);
        camera.Update(new AppTime());
        return camera;
    }

    private static void Step(Camera camera, double milliseconds) =>
        camera.Update(new AppTime { FrameTime = milliseconds / 1000 });

    // A point on the plane at the focus distance, off the view's center, where the camera shows it.
    private static Vector3F OnFocusPlane(Camera camera) =>
        (Vector3F)(camera.Forward * camera.FocusDistance + camera.Right * 2 + camera.Up * 1.5);

    private static Vector2F Shown(Camera camera, Vector3F inRender)
    {
        var clip = Vector4F.Transform(new Vector4F(inRender, 1), camera.ViewMatrix * camera.ProjectionMatrix);
        return new Vector2F(clip.X / clip.W, clip.Y / clip.W);
    }

    [Test]
    public void GoingFlat_ShowsTheFocusPlaneAsLarge_AllTheWay()
    {
        var camera = FreeCamera();
        var point = OnFocusPlane(camera);
        var before = Shown(camera, point);

        camera.ProjectionType = CameraProjectionType.Ortho;
        Step(camera, 100);
        var midway = Shown(camera, point);
        var blend = camera.ProjectionMatrix.M44;
        Step(camera, 1000);
        var after = Shown(camera, point);

        Assert.Multiple(() =>
        {
            Assert.That(blend, Is.GreaterThan(0.05f).And.LessThan(0.95f), "between the two");
            Assert.That(Vector2F.Distance(midway, before), Is.LessThan(1e-3f), "while it flows");
            Assert.That(Vector2F.Distance(after, before), Is.LessThan(1e-3f), "when it is flat");
            Assert.That(camera.ProjectionMatrix, Is.EqualTo(camera.OrthoProjection));
        });
    }

    [Test]
    public void ComingBackFromAZoomedFlatView_StepsToWhereThePlaneIsAsLarge()
    {
        var camera = FreeCamera();
        camera.ProjectionTransitionTime = 0;
        camera.ProjectionType = CameraProjectionType.Ortho;
        Step(camera, 16);
        camera.Zoom(3);
        Step(camera, 16);
        var point = OnFocusPlane(camera);
        var flat = Shown(camera, point);
        var pivot = camera.WorldPosition + camera.Forward * camera.FocusDistance;

        camera.ProjectionType = CameraProjectionType.Perspective;
        Step(camera, 16);

        Assert.Multiple(() =>
        {
            Assert.That(camera.FocusDistance, Is.LessThan(Focus), "zoomed in, it comes nearer");
            var stillThere = camera.WorldPosition + camera.Forward * camera.FocusDistance;
            Assert.That((stillThere - pivot).Length(), Is.LessThan(1e-3));
            Assert.That(Vector2F.Distance(Shown(camera, OnFocusPlane(camera)), flat), Is.LessThan(1e-3f));
        });
    }

    [Test]
    public void AFlatView_ZoomsByItsScaleAlongXAndYOnly()
    {
        var camera = FreeCamera();
        camera.ProjectionTransitionTime = 0;
        camera.ProjectionType = CameraProjectionType.Ortho;
        Step(camera, 16);
        var scale = camera.OrthoScale;
        var standing = camera.WorldPosition;
        var depthScale = camera.OrthoProjection.M33;

        camera.Zoom(2);
        Step(camera, 16);

        Assert.Multiple(() =>
        {
            Assert.That(camera.OrthoScale.X, Is.EqualTo(scale.X * camera.ZoomStep * camera.ZoomStep).Within(1e-3));
            Assert.That(camera.OrthoScale.Y, Is.EqualTo(scale.Y * camera.ZoomStep * camera.ZoomStep).Within(1e-3));
            Assert.That(camera.WorldPosition, Is.EqualTo(standing), "it zooms, not moves");
            Assert.That(camera.OrthoProjection.M33, Is.EqualTo(depthScale), "depth is not scaled");
        });
    }

    [Test]
    public void AnIsometricView_TurnsDownACubesDiagonal_AboutThePointItLooksAt()
    {
        var camera = FreeCamera();
        var pivot = camera.WorldPosition + camera.Forward * Focus;

        camera.ProjectionType = CameraProjectionType.Isometric;
        Step(camera, 1000);
        Step(camera, 16);

        var forward = camera.Forward;
        Assert.Multiple(() =>
        {
            Assert.That(Math.Abs(forward.X), Is.EqualTo(1 / Math.Sqrt(3)).Within(1e-4));
            Assert.That(forward.Y, Is.EqualTo(1 / Math.Sqrt(3)).Within(1e-4), "down, as up is -Y");
            Assert.That(Math.Abs(forward.Z), Is.EqualTo(1 / Math.Sqrt(3)).Within(1e-4));
            Assert.That(camera.Up.Y, Is.GreaterThan(0), "upright, its view's up along +Y as the orientation cube's views");
            Assert.That((camera.WorldPosition + camera.Forward * Focus - pivot).Length(), Is.LessThan(1e-3));
            Assert.That(camera.ProjectionMatrix, Is.EqualTo(camera.OrthoProjection));
        });
    }

    [Test]
    public void LeavingAnIsometricView_TurnsBackToWhereTheCameraLookedBefore()
    {
        var camera = FreeCamera();
        var before = camera.Rotation;

        camera.ProjectionType = CameraProjectionType.Isometric;
        Step(camera, 1000);
        camera.ProjectionType = CameraProjectionType.Ortho;
        Step(camera, 1000);
        Step(camera, 16);

        Assert.That(Math.Abs(QuaternionF.Dot(camera.Rotation, before)), Is.GreaterThan(0.9999f));
    }

    [Test]
    public void TurnedWhileIsometric_TheCameraKeepsItsTurnWhenItLeaves()
    {
        var camera = FreeCamera();
        camera.ProjectionType = CameraProjectionType.Isometric;
        Step(camera, 1000);
        camera.RotateRelativeXY(0, 30);
        Step(camera, 16);
        var turned = camera.Rotation;

        camera.ProjectionType = CameraProjectionType.Perspective;
        Step(camera, 1000);

        Assert.That(Math.Abs(QuaternionF.Dot(camera.Rotation, turned)), Is.GreaterThan(0.9999f));
    }

    [Test]
    public void HungOnAnotherEntity_TheCameraTravelsThereFromWhereItStood()
    {
        var camera = FreeCamera();
        var stood = camera.WorldPosition;
        var subject = new Entity(null, "Subject");
        subject.Transform.Position = new Vector3(40, -3, 25);

        var world = new EntityWorld(new AdamantiumDependencyContainer(), new Satellites());
        world.EntityManager.AddEntity(subject);
        world.EntityManager.AddEntity(camera.Owner);
        world.EntityManager.SetOwner(camera.Owner, subject);
        camera.SetThirdPersonCamera(subject, Vector3F.Zero, Graphics.Core.CameraType.ThirdPersonFree, distanceToObject: 8);
        Step(camera, 1);
        var setOff = camera.WorldPosition;
        Step(camera, camera.HostChangeTime / 2.0);
        var midway = camera.WorldPosition;
        Step(camera, camera.HostChangeTime);
        var there = camera.Owner.Transform.WorldPosition;

        Assert.Multiple(() =>
        {
            Assert.That((setOff - stood).Length(), Is.LessThan(0.1), "it sets off from where it stood");
            Assert.That((midway - stood).Length(), Is.GreaterThan(1).And.LessThan((there - stood).Length() - 1), "on its way");
            Assert.That((camera.WorldPosition - there).Length(), Is.LessThan(1e-6), "and arrives");
        });
    }

    [Test]
    public void UntilItsNextFrame_TheCameraDrawsWithTheProjectionItShows()
    {
        var camera = FreeCamera();

        camera.ProjectionType = CameraProjectionType.Ortho;

        Assert.That(camera.ProjectionMatrix, Is.EqualTo(camera.PerspectiveProjection));
    }

    [Test]
    public void ChangedAgainMidway_TheProjectionFlowsOnWithoutAJump()
    {
        var camera = FreeCamera();
        camera.ProjectionType = CameraProjectionType.Ortho;
        Step(camera, 100);
        var midway = camera.ProjectionMatrix.M44;

        camera.ProjectionType = CameraProjectionType.Isometric;
        Step(camera, 16);

        Assert.That(camera.ProjectionMatrix.M44, Is.GreaterThan(midway).And.LessThan(0.95f));
    }

    [Test]
    public void GoingFlat_RoundAScaledSubject_KeepsItAsLarge()
    {
        var camera = FreeCamera();
        var subject = new Entity(null, "Subject");
        subject.Transform.Position = new Vector3(10, -2, 30);
        subject.Transform.ScaleFactor = new Vector3F(0.01f);
        var world = new EntityWorld(new AdamantiumDependencyContainer(), new Satellites());
        world.EntityManager.AddEntity(subject);
        world.EntityManager.AddEntity(camera.Owner);
        world.EntityManager.SetOwner(camera.Owner, subject);
        camera.HostChangeTime = 0;
        camera.ProjectionTransitionTime = 0;
        camera.SetThirdPersonCamera(subject, Vector3F.Zero, Graphics.Core.CameraType.ThirdPersonFree, distanceToObject: 600);
        Step(camera, 16);
        var point = (Vector3F)(subject.GetCenterAbsolute() - camera.WorldPosition + camera.Right * 0.5);
        var before = Shown(camera, point);

        camera.ProjectionType = CameraProjectionType.Ortho;
        Step(camera, 16);

        Assert.That(Vector2F.Distance(Shown(camera, point), before), Is.LessThan(1e-3f));
    }

    [Test]
    public void TurningAFreeCamerasEntity_TurnsItsView_AndItsViewTurnsTheEntity()
    {
        var camera = FreeCamera();
        var turn = QuaternionF.RotationYawPitchRoll(1.1f, -0.3f, 0);

        camera.Owner.Transform.Rotation = turn;
        Step(camera, 16);
        var viewAfterTool = camera.Rotation;
        camera.RotateRelativeXY(0, 20);
        Step(camera, 16);

        Assert.Multiple(() =>
        {
            Assert.That(QuaternionF.Dot(viewAfterTool, QuaternionF.Conjugate(turn)), Is.GreaterThan(0.9999f).Or.LessThan(-0.9999f));
            Assert.That(QuaternionF.Dot(camera.Owner.Transform.Rotation, QuaternionF.Conjugate(camera.Rotation)),
                Is.GreaterThan(0.9999f).Or.LessThan(-0.9999f));
        });
    }

    // Turned a little each frame, as the mouse turns it, the camera goes on turning: its entity follows every step.
    [Test]
    public void AFreeCameraTurnedFrameByFrame_GoesOnTurning()
    {
        var camera = FreeCamera();
        camera.Rotation = QuaternionF.Identity;
        Step(camera, 16);

        for (int i = 0; i < 300; i++)
        {
            camera.RotateRelativeXY(0.02f, 0.05f);
            Step(camera, 16);
        }

        var expected = QuaternionF.Identity;
        for (int i = 0; i < 300; i++)
        {
            expected = QuaternionF.Multiply(QuaternionF.RotationAxis(Vector3F.UnitX, MathHelper.DegreesToRadians(0.02f)), expected);
            expected = QuaternionF.Multiply(QuaternionF.RotationAxis(Vector3F.UnitY, MathHelper.DegreesToRadians(0.05f)), expected);
        }

        Assert.Multiple(() =>
        {
            Assert.That(Math.Abs(QuaternionF.Dot(camera.Rotation, expected)), Is.GreaterThan(0.9999f));
            Assert.That(camera.Owner.Transform.Rotation, Is.EqualTo(QuaternionF.Conjugate(camera.Rotation)));
        });
    }

    [Test]
    public void AFirstPersonCamera_LooksWhereItsHostFaces()
    {
        var camera = FreeCamera();
        var host = new Entity(null, "Host");
        host.Transform.Rotation = QuaternionF.RotationAxis(Vector3F.Up, MathF.PI / 2);
        var world = new EntityWorld(new AdamantiumDependencyContainer(), new Satellites());
        world.EntityManager.AddEntity(host);
        world.EntityManager.AddEntity(camera.Owner);
        world.EntityManager.SetOwner(camera.Owner, host);
        camera.HostChangeTime = 0;

        camera.SetFirstPersonCamera(Vector3.Zero, QuaternionF.Identity, 0);
        Step(camera, 16);

        var faces = Matrix4x4F.RotationQuaternion(host.Transform.Rotation).Forward;
        Assert.That(Vector3F.Dot((Vector3F)camera.Forward, faces), Is.GreaterThan(0.9999f));
    }

    [Test]
    public void AProjectionSetBeforeTheFirstFrame_IsTakenAsItIs()
    {
        var camera = new Camera(60, 800, 600, 0.1f, 1000f) { ProjectionType = CameraProjectionType.Ortho };
        new Entity(null, "Camera").AddComponent(camera);

        camera.Update(new AppTime { FrameTime = 0.016 });

        Assert.That(camera.ProjectionMatrix, Is.EqualTo(camera.OrthoProjection));
    }
}
