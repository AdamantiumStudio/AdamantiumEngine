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

    [TestCase(CameraProjectionType.Perspective)]
    [TestCase(CameraProjectionType.Ortho)]
    public void AnIsometricView_TurnsDownACubesDiagonal_AboutThePointItLooksAt_InEitherProjection(CameraProjectionType projection)
    {
        var camera = FreeCamera();
        camera.ProjectionType = projection;
        Step(camera, 1000);
        var pivot = camera.WorldPosition + camera.Forward * camera.FocusDistance;

        camera.IsIsometric = true;
        Step(camera, 1000);
        Step(camera, 16);

        var forward = camera.Forward;
        Assert.Multiple(() =>
        {
            Assert.That(Math.Abs(forward.X), Is.EqualTo(1 / Math.Sqrt(3)).Within(1e-4));
            Assert.That(forward.Y, Is.EqualTo(1 / Math.Sqrt(3)).Within(1e-4), "down, as up is -Y");
            Assert.That(Math.Abs(forward.Z), Is.EqualTo(1 / Math.Sqrt(3)).Within(1e-4));
            Assert.That(camera.Up.Y, Is.GreaterThan(0), "upright, its view's up along +Y as the orientation cube's views");
            Assert.That((camera.WorldPosition + camera.Forward * camera.FocusDistance - pivot).Length(), Is.LessThan(1e-3));
            Assert.That(camera.ProjectionMatrix, Is.EqualTo(camera.ProjectionOf(projection)), "the projection stays as it was");
        });
    }

    [Test]
    public void LeavingAnIsometricView_TurnsBackToWhereTheCameraLookedBefore()
    {
        var camera = FreeCamera();
        var before = camera.Rotation;

        camera.IsIsometric = true;
        Step(camera, 1000);
        camera.IsIsometric = false;
        Step(camera, 1000);
        Step(camera, 16);

        Assert.That(Math.Abs(QuaternionF.Dot(camera.Rotation, before)), Is.GreaterThan(0.9999f));
    }

    [Test]
    public void GoneRoundWhileIsometric_TheCameraStillTurnsBackWhenItLeaves()
    {
        var camera = FreeCamera();
        var before = camera.Rotation;
        camera.IsIsometric = true;
        Step(camera, 1000);
        camera.RotateRelativeXY(0, 30);
        Step(camera, 16);

        camera.IsIsometric = false;
        Step(camera, 1000);

        Assert.That(Math.Abs(QuaternionF.Dot(camera.Rotation, before)), Is.GreaterThan(0.9999f));
    }

    [Test]
    public void TurnedByOthersWhileIsometric_TheCameraKeepsThatTurnWhenItLeaves()
    {
        var camera = FreeCamera();
        camera.IsIsometric = true;
        Step(camera, 1000);
        camera.Rotation = QuaternionF.Multiply(camera.Rotation, QuaternionF.RotationAxis(Vector3F.UnitX, 0.3f));
        var turned = camera.Rotation;

        camera.IsIsometric = false;
        Step(camera, 1000);

        Assert.That(Math.Abs(QuaternionF.Dot(camera.Rotation, turned)), Is.GreaterThan(0.9999f));
    }

    // Turned this way and that, a free camera keeps its horizon level, as an editor's does: turns about its own axes
    // gathered a roll.
    [Test]
    public void AFreeCameraTurnedEveryWay_KeepsItsHorizonLevel()
    {
        var camera = FreeCamera();
        camera.Rotation = QuaternionF.Identity;
        Step(camera, 16);

        for (var i = 0; i < 200; i++)
        {
            camera.RotateRelativeXY(i % 2 == 0 ? 7 : -5, i % 3 == 0 ? 11 : -4);
            camera.RotateRight(i % 5 == 0 ? 3 : -2);
            camera.RotateUp(6);
        }

        Step(camera, 16);
        Assert.That(Math.Abs(camera.Right.Y), Is.LessThan(1e-3), "its right stays level");
    }

    [Test]
    public void AFreeCameraTurnedUp_StopsShortOfStraightUp()
    {
        var camera = FreeCamera();
        camera.Rotation = QuaternionF.Identity;
        Step(camera, 16);

        for (var i = 0; i < 100; i++)
        {
            camera.RotateRelativeXY(5, 0);
        }

        Step(camera, 16);
        Assert.Multiple(() =>
        {
            Assert.That(Math.Abs(camera.Forward.Y), Is.LessThan(1).And.GreaterThan(0.99));
            Assert.That(Math.Abs(camera.Right.Y), Is.LessThan(1e-3), "and never rolls over");
        });
    }

    // Its host turned - by the rotation tool, while seen through the camera - a free orbit stays where it is and keeps
    // looking at the host: it swung round with the host while its view stayed, and the tool chased the swing.
    [Test]
    public void AFreeOrbit_StaysPut_WhenItsHostTurns()
    {
        var camera = FreeCamera();
        var host = new Entity(null, "Host");
        host.Transform.Position = new Vector3(0, 0, 10);
        var world = new EntityWorld(new AdamantiumDependencyContainer(), new Satellites());
        world.EntityManager.AddEntity(host);
        world.EntityManager.AddEntity(camera.Owner);
        world.EntityManager.SetOwner(camera.Owner, host);
        camera.HostChangeTime = 0;
        camera.SetThirdPersonCamera(host, Vector3F.Zero, Graphics.Core.CameraType.ThirdPersonFree, distanceToObject: 8);
        Step(camera, 16);
        Step(camera, 16);
        var standing = camera.WorldPosition;
        var looking = camera.Forward;

        host.Transform.Rotation = QuaternionF.RotationAxis(Vector3F.UnitY, 0.8f);
        Step(camera, 16);

        Assert.Multiple(() =>
        {
            Assert.That((camera.WorldPosition - standing).Length(), Is.LessThan(1e-3), "it stays where it is");
            Assert.That(Vector3.Dot(camera.Forward, looking), Is.GreaterThan(0.9999), "looking the same way");
        });
    }

    // Turned past the camera's own update within a frame - by a tool - the host would carry the orbit along until the
    // next frame; put back at once, nothing is drawn with it swung.
    [Test]
    public void AHostTurnedAfterTheCameraUpdated_ReorbitPutsItBackAtOnce()
    {
        var camera = FreeCamera();
        var host = new Entity(null, "Host");
        host.Transform.Position = new Vector3(0, 0, 10);
        var world = new EntityWorld(new AdamantiumDependencyContainer(), new Satellites());
        world.EntityManager.AddEntity(host);
        world.EntityManager.AddEntity(camera.Owner);
        world.EntityManager.SetOwner(camera.Owner, host);
        camera.HostChangeTime = 0;
        camera.SetThirdPersonCamera(host, Vector3F.Zero, Graphics.Core.CameraType.ThirdPersonFree, distanceToObject: 8);
        Step(camera, 16);
        Step(camera, 16);
        var standing = camera.WorldPosition;

        host.Transform.Rotation = QuaternionF.RotationAxis(Vector3F.UnitY, 0.8f);
        var swung = camera.WorldPosition;
        camera.Reorbit();

        Assert.Multiple(() =>
        {
            Assert.That((swung - standing).Length(), Is.GreaterThan(1), "the turn carried it along");
            Assert.That((camera.WorldPosition - standing).Length(), Is.LessThan(1e-3), "and it is back at once");
        });
    }

    [Test]
    public void ALockedOrbit_ReorbitedAfterItsHostTurned_LooksAtItsHostAtOnce()
    {
        var camera = FreeCamera();
        var host = new Entity(null, "Host");
        host.Transform.Position = new Vector3(0, 0, 10);
        var world = new EntityWorld(new AdamantiumDependencyContainer(), new Satellites());
        world.EntityManager.AddEntity(host);
        world.EntityManager.AddEntity(camera.Owner);
        world.EntityManager.SetOwner(camera.Owner, host);
        camera.HostChangeTime = 0;
        camera.SetThirdPersonCamera(host, Vector3F.Zero, Graphics.Core.CameraType.ThirdPersonLocked, distanceToObject: 8);
        Step(camera, 16);
        Step(camera, 16);

        host.Transform.Rotation = QuaternionF.RotationAxis(Vector3F.UnitY, 0.9f);
        camera.Reorbit();

        var toHost = Vector3.Normalize(host.GetCenterAbsolute() - camera.WorldPosition);
        Assert.That(Vector3.Dot(camera.Forward, toHost), Is.GreaterThan(0.999));
    }

    // A face of the orientation cube turns the camera there and it stays: the entity it rides kept its old turn through
    // the animation, and the camera took that back the moment it was free again.
    [Test]
    public void TurnedByTheOrientationCube_TheCameraStaysWhereItWasTurned()
    {
        var camera = FreeCamera();
        var target = QuaternionF.RotationYawPitchRoll(1.0f, 0.3f, 0);

        camera.RotateAroundSelectedObject(target, 200);
        Step(camera, 100);
        Step(camera, 150);
        Step(camera, 16);
        Step(camera, 16);

        Assert.That(Math.Abs(QuaternionF.Dot(camera.Rotation, target)), Is.GreaterThan(0.9999f));
    }

    // Moved along its view, a free camera keeps looking at the same point: going flat and back then leaves it where it
    // stood; a focus left behind sent it far away, and made the flat view much smaller or larger than the perspective.
    [Test]
    public void MovedForward_ThenFlatAndBack_TheCameraStaysWhereItStood()
    {
        var camera = FreeCamera();
        camera.TranslateForward(5);
        Step(camera, 16);
        var stood = camera.WorldPosition;

        camera.ProjectionType = CameraProjectionType.Ortho;
        Step(camera, 1000);
        camera.ProjectionType = CameraProjectionType.Perspective;
        Step(camera, 1000);

        Assert.Multiple(() =>
        {
            Assert.That(camera.FocusDistance, Is.EqualTo(Focus - 5).Within(1e-3));
            Assert.That((camera.WorldPosition - stood).Length(), Is.LessThan(1e-3));
        });
    }

    [TestCase(4f)]
    [TestCase(7f)]
    [TestCase(-6f)]
    public void AFreeCameraTurnedUpOrDownInBigSteps_NeverGoesOverTheTop(float step)
    {
        var camera = FreeCamera();
        camera.Rotation = QuaternionF.Identity;
        Step(camera, 16);

        for (var i = 0; i < 60; i++)
        {
            camera.RotateRelativeXY(step, 0);
            Step(camera, 16);
            Assert.That(camera.Up.Y, Is.GreaterThanOrEqualTo(0), $"upright after step {i}");
        }

        Assert.That(Math.Abs(camera.Forward.Y), Is.GreaterThan(0.99));
    }

    [Test]
    public void LookingStraightDown_AFreeCameraTurnsAwayUpright_EitherWay()
    {
        foreach (var step in new[] { 5f, -5f })
        {
            var camera = FreeCamera();
            camera.Rotation = QuaternionF.RotationAxis(Vector3F.UnitX, MathHelper.DegreesToRadians(90));
            Step(camera, 16);

            camera.RotateRelativeXY(step, 0);
            Step(camera, 16);

            Assert.That(camera.Up.Y, Is.GreaterThanOrEqualTo(0), $"upright after {step}");
        }
    }

    [Test]
    public void AnIsometricCamera_TurnedRoundTwiceInAFrame_KeepsGoingRoundTheSamePoint()
    {
        var camera = FreeCamera();
        camera.IsIsometric = true;
        Step(camera, 1000);
        Step(camera, 16);
        var pivot = camera.WorldPosition + camera.Forward * camera.FocusDistance;

        for (var i = 0; i < 60; i++)
        {
            camera.RotateUp(2);
            camera.RotateRight(2);
            Step(camera, 16);
        }

        Assert.That((camera.WorldPosition + camera.Forward * camera.FocusDistance - pivot).Length(), Is.LessThan(1e-2));
    }

    [Test]
    public void TurnedAwayByOthersWhileIsometric_TheCameraIsNoLongerIsometric()
    {
        var camera = FreeCamera();
        camera.IsIsometric = true;
        Step(camera, 1000);

        camera.Rotation = QuaternionF.Identity;
        Step(camera, 16);

        Assert.That(camera.IsIsometric, Is.False);
    }

    [Test]
    public void IsometricWhileOnAHost_TheCameraTurnsOnceItIsFree()
    {
        var camera = FreeCamera();
        var host = new Entity(null, "Host");
        var world = new EntityWorld(new AdamantiumDependencyContainer(), new Satellites());
        world.EntityManager.AddEntity(host);
        world.EntityManager.AddEntity(camera.Owner);
        world.EntityManager.SetOwner(camera.Owner, host);
        camera.HostChangeTime = 0;
        camera.SetFirstPersonCamera(Vector3.Zero, QuaternionF.Identity, 0);
        Step(camera, 16);
        camera.IsIsometric = true;
        Step(camera, 1000);

        world.EntityManager.RemoveOwner(camera.Owner);
        world.EntityManager.AddEntity(camera.Owner);
        camera.SetFreeCamera();
        Step(camera, 1000);
        Step(camera, 16);

        Assert.That(camera.Forward.Y, Is.EqualTo(1 / Math.Sqrt(3)).Within(1e-3));
    }

    [Test]
    public void AnIsometricCamera_TurnsOnlyRoundThePointItLooksAt_ItsAngleDownKept()
    {
        var camera = FreeCamera();
        camera.IsIsometric = true;
        Step(camera, 1000);
        Step(camera, 16);
        var pivot = camera.WorldPosition + camera.Forward * camera.FocusDistance;
        var down = camera.Forward.Y;

        camera.RotateRelativeXY(20, 35);
        camera.RotateForward(15);
        Step(camera, 16);

        Assert.Multiple(() =>
        {
            Assert.That(camera.Forward.Y, Is.EqualTo(down).Within(1e-4), "its angle down kept");
            Assert.That(Math.Abs(camera.Right.Y), Is.LessThan(1e-3), "no roll");
            Assert.That((camera.WorldPosition + camera.Forward * camera.FocusDistance - pivot).Length(), Is.LessThan(1e-3));
        });
    }

    [Test]
    public void ChangingTheProjectionWhileIsometric_KeepsTheIsometricAngle()
    {
        var camera = FreeCamera();
        camera.IsIsometric = true;
        Step(camera, 1000);
        var isometric = camera.Rotation;

        camera.ProjectionType = CameraProjectionType.Ortho;
        Step(camera, 1000);

        Assert.That(Math.Abs(QuaternionF.Dot(camera.Rotation, isometric)), Is.GreaterThan(0.9999f));
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

        camera.ProjectionType = CameraProjectionType.Perspective;
        Step(camera, 16);

        Assert.That(camera.ProjectionMatrix.M44, Is.LessThan(midway).And.GreaterThan(0.05f));
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
