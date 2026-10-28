using System;
using Adamantium.Core;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.ECSTests;

[TestFixture]
public class CameraProjectionTests
{
    [Test]
    public void Portrait_SpansItsFovAlongTheTallSide()
    {
        var camera = new Camera(60, 600, 800, 0.1f, 1000f);
        new Entity(null, "Camera").AddComponent(camera);
        camera.Update(new AppTime());

        var expected = 1f / (float)Math.Tan(float.DegreesToRadians(30));
        Assert.That(camera.ProjectionMatrix.M22, Is.EqualTo(expected).Within(1e-4f));
    }

    // An inspector edits a camera long after it was made: the projection follows, as does what reads the planes.
    [Test]
    public void AFieldOfViewOrAPlaneChangedLater_RebuildsTheProjection()
    {
        var camera = new Camera(60, 600, 800, 0.1f, 1000f);
        new Entity(null, "Camera").AddComponent(camera);
        camera.Update(new AppTime());
        var before = camera.ProjectionMatrix;

        camera.Fov = 90;
        camera.Update(new AppTime());

        var expected = 1f / (float)Math.Tan(float.DegreesToRadians(45));
        Assert.That(camera.ProjectionMatrix.M22, Is.EqualTo(expected).Within(1e-4f));

        camera.ZFar = 10f;
        camera.Update(new AppTime());
        Assert.That(camera.ProjectionMatrix.M33, Is.Not.EqualTo(before.M33).Within(1e-6f), "the far plane moved");
    }
}
