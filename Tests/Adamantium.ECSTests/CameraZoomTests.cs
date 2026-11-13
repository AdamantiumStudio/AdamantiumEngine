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

    // The wheel over a point zooms toward it, as a canvas does: the point stays under the pointer, and comes nearer by
    // ZoomStep a notch, as the middle does with Zoom.
    [Test]
    public void AFreeCamera_ZoomingAtAPixel_KeepsWhatIsUnderIt_AndComesNearerIt()
    {
        var camera = NewCamera();
        var pixel = new Vector2F(620, 140);
        var point = PointUnder(camera, pixel, depth: 40);
        var distance = (point - camera.WorldPosition).Length();

        camera.ZoomAt(2, pixel, 40);
        camera.Update(new AppTime());

        Assert.Multiple(() =>
        {
            Assert.That(Vector2F.Distance(PixelOf(camera, point), pixel), Is.LessThan(0.01f), "still under the pixel");
            Assert.That(distance / (point - camera.WorldPosition).Length(),
                Is.EqualTo(camera.ZoomStep * camera.ZoomStep).Within(1e-6));
        });
    }

    [Test]
    public void AFlatView_ZoomingAtAPixel_KeepsWhatIsUnderIt_AndScalesByZoomStepANotch()
    {
        var camera = NewCamera();
        camera.ProjectionTransitionTime = 0;
        camera.ProjectionType = CameraProjectionType.Ortho;
        camera.Update(new AppTime());
        var scale = camera.OrthoScale;
        var pixel = new Vector2F(130, 470);
        var point = PointUnder(camera, pixel, depth: 25);

        camera.ZoomAt(3, pixel, 25);
        camera.Update(new AppTime());

        Assert.Multiple(() =>
        {
            Assert.That(Vector2F.Distance(PixelOf(camera, point), pixel), Is.LessThan(0.01f), "still under the pixel");
            Assert.That(camera.OrthoScale.X / scale.X, Is.EqualTo(Math.Pow(camera.ZoomStep, 3)).Within(1e-4));
        });
    }

    // The point the ray through the pixel meets at that depth ahead.
    private static Vector3<double> PointUnder(Camera camera, Vector2F pixel, double depth)
    {
        var ray = Adamantium.ECS.Components.Extensions.PickRay.FromCamera(camera, pixel).Ray;
        var direction = Vector3<double>.Normalize(ray.Direction.As<double>());
        var origin = ray.Position.As<double>();
        var along = (depth - Vector3<double>.Dot(origin, camera.Forward)) / Vector3<double>.Dot(direction, camera.Forward);
        return camera.WorldPosition + origin + direction * along;
    }

    private static Vector2F PixelOf(Camera camera, Vector3<double> point)
    {
        var inRender = (point - camera.WorldPosition).As<float>();
        var pixel = Vector3F.Project(inRender, 0, 0, camera.Width, camera.Height, 0, 1,
            camera.ViewMatrix * camera.ProjectionMatrix);
        return new Vector2F(pixel.X, pixel.Y);
    }

    private static Camera NewCamera()
    {
        var camera = new Camera(60, 800, 600, 0.1f, 10000f);
        new Entity(null, "Camera").AddComponent(camera);
        camera.Update(new AppTime());
        return camera;
    }
}
