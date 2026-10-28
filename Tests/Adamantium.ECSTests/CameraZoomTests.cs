using System;
using Adamantium.Core;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.Graphics.Core;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.ECSTests;

[TestFixture]
public class CameraZoomTests
{
    [Test]
    public void AnOrbitingCamera_ClosesTheDistanceByAStepANotch_WhateverTheFrameRate()
    {
        var camera = NewCamera();
        camera.SetThirdPersonCamera(new Entity(null, "Subject"), Vector3F.Zero, CameraType.ThirdPersonFree, distanceToObject: 20);

        camera.Zoom(1);
        Assert.That(camera.Radius, Is.EqualTo(20 / camera.ZoomStep).Within(1e-9));

        camera.Zoom(-1);
        Assert.That(camera.Radius, Is.EqualTo(20).Within(1e-9));
    }

    [Test]
    public void AnOrbitingCamera_StopsShortOfItsSubject()
    {
        var camera = NewCamera();
        camera.SetThirdPersonCamera(new Entity(null, "Subject"), Vector3F.Zero, CameraType.ThirdPersonLocked, distanceToObject: 20);

        camera.Zoom(1000);

        Assert.That(camera.Radius, Is.EqualTo(camera.ZNear * 2).Within(1e-6));
    }

    // Even at any distance: a notch makes what it looks at ZoomStep times larger, near or far.
    [TestCase(10.0)]
    [TestCase(400.0)]
    public void AFreeCamera_ComesNearerThePointItLooksAt_ByZoomStepANotch(double focus)
    {
        var camera = NewCamera();
        camera.FocusDistance = focus;
        var start = camera.Owner.Transform.Position;

        camera.Zoom(2);

        var moved = camera.Owner.Transform.Position - start;
        Assert.Multiple(() =>
        {
            Assert.That(focus / camera.FocusDistance, Is.EqualTo(camera.ZoomStep * camera.ZoomStep).Within(1e-9));
            Assert.That(moved.Length(), Is.EqualTo(focus - camera.FocusDistance).Within(1e-6));
            Assert.That(Vector3<double>.Dot(Vector3<double>.Normalize(moved), camera.Forward), Is.GreaterThan(0.9999));
        });
    }

    // Driven past the point it looked at, the camera pushes that point on ahead: the wheel, a share of the way there,
    // stopped moving it once the point was at its feet.
    [Test]
    public void AFreeCameraPastItsFocus_StillZoomsWithTheWheel()
    {
        var camera = NewCamera();
        camera.FocusDistance = 12;
        camera.TranslateForward(20);
        var start = camera.Owner.Transform.Position;

        camera.Zoom(3);

        Assert.Multiple(() =>
        {
            Assert.That(camera.FocusDistance, Is.GreaterThanOrEqualTo(1));
            Assert.That((camera.Owner.Transform.Position - start).Length(), Is.GreaterThan(0.3));
        });
    }

    [Test]
    public void AFreeCameraBackingAway_KeepsItsFocusWithinItsFarPlane()
    {
        var camera = NewCamera();

        camera.TranslateForward(-100000);

        Assert.That(camera.FocusDistance, Is.LessThanOrEqualTo(camera.ZFar / 2));
    }

    [Test]
    public void AFlatFreeCamera_ZoomsFar_ItsFocusKept()
    {
        var camera = NewCamera();
        camera.ProjectionTransitionTime = 0;
        camera.ProjectionType = CameraProjectionType.Ortho;
        camera.Update(new AppTime());
        var scale = camera.OrthoScale.X;
        var focus = camera.FocusDistance;

        camera.Zoom(60);
        camera.TranslateForward(50);

        Assert.Multiple(() =>
        {
            Assert.That(camera.OrthoScale.X / scale, Is.GreaterThan(Math.Pow(camera.ZoomStep, 60) * 0.999));
            Assert.That(camera.FocusDistance, Is.EqualTo(focus));
        });
    }

    [Test]
    public void AFlatFreeCamera_ZoomsAsMuchAsAPerspectiveOne()
    {
        var camera = NewCamera();
        camera.ProjectionTransitionTime = 0;
        camera.ProjectionType = CameraProjectionType.Ortho;
        camera.Update(new AppTime());
        var scale = camera.OrthoScale.X;
        var start = camera.Owner.Transform.Position;

        camera.Zoom(3);

        Assert.Multiple(() =>
        {
            Assert.That(camera.OrthoScale.X / scale, Is.EqualTo(Math.Pow(camera.ZoomStep, 3)).Within(1e-4));
            Assert.That(camera.Owner.Transform.Position, Is.EqualTo(start), "a flat view zooms, it does not move");
        });
    }

    private static Camera NewCamera()
    {
        var camera = new Camera(60, 800, 600, 0.1f, 10000f);
        new Entity(null, "Camera").AddComponent(camera);
        camera.Update(new AppTime());
        return camera;
    }
}
