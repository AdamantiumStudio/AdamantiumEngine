using System;
using System.Collections.Generic;
using Adamantium.DarkMatter.Shapes;
using NUnit.Framework;

namespace Adamantium.DarkMatter.Tests;

[TestFixture]
public class QueryTests
{
    [Test]
    public void Ray_MeetsTheNearestBody()
    {
        var scene = new SceneBuilder();
        var far = scene.AddBox(new Vector3F(10, 0, 0), new Vector3F(0.5f));
        var near = scene.AddBox(new Vector3F(5, 0, 0), new Vector3F(0.5f));

        var met = scene.World.RayCast(new Ray<float>(Vector3F.Zero, new Vector3F(2, 0, 0)), 100, out var hit);

        Assert.That(met, Is.True);
        Assert.That(hit.Body, Is.EqualTo(near));
        Assert.That(hit.Body, Is.Not.EqualTo(far));
        Assert.That(hit.Distance, Is.EqualTo(4.5f).Within(1e-4f));
        Assert.That(hit.Normal.X, Is.EqualTo(-1).Within(1e-4f));
    }

    [Test]
    public void Ray_StopsAtItsLength()
    {
        var scene = new SceneBuilder();
        scene.AddBox(new Vector3F(5, 0, 0), new Vector3F(0.5f));

        Assert.That(scene.World.RayCast(new Ray<float>(Vector3F.Zero, Vector3F.UnitX), 4, out _), Is.False);
    }

    [Test]
    public void Ray_MeetsTurnedShapes()
    {
        var scene = new SceneBuilder();
        var lying = Quaternion.CreateFromAxisAngle(Vector3F.UnitZ, MathF.PI / 2);
        var capsule = scene.AddCapsule(new Vector3F(0, 0, 5), 1, 0.5f, lying);
        var ball = scene.AddSphere(new Vector3F(0, 0, -5), 0.5f);

        Assert.That(scene.World.RayCast(new Ray<float>(new Vector3F(0.9f, 3, 5), -Vector3F.UnitY), 10, out var onCapsule),
            Is.True);
        Assert.That(onCapsule.Body, Is.EqualTo(capsule));
        Assert.That(onCapsule.Distance, Is.EqualTo(2.5f).Within(1e-4f));

        Assert.That(scene.World.RayCast(new Ray<float>(new Vector3F(3, 0, 5), -Vector3F.UnitX), 10, out var onEnd),
            Is.True);
        Assert.That(onEnd.Distance, Is.EqualTo(1.5f).Within(1e-4f));
        Assert.That(onEnd.Normal.X, Is.EqualTo(1).Within(1e-4f));

        Assert.That(scene.World.RayCast(new Ray<float>(new Vector3F(0, 0, -10), Vector3F.UnitZ), 10, out var onBall),
            Is.True);
        Assert.That(onBall.Body, Is.EqualTo(ball));
        Assert.That(onBall.Distance, Is.EqualTo(4.5f).Within(1e-4f));
    }

    [Test]
    public void EndlessRay_PassingACapsule_MeetsNothing()
    {
        var scene = new SceneBuilder();
        scene.AddCapsule(Vector3F.Zero, 1, 0.5f);

        var met = scene.World.RayCast(new Ray<float>(new Vector3F(0.45f, 1.45f, -5), Vector3F.UnitZ),
            float.PositiveInfinity, out _);

        Assert.That(met, Is.False);
    }

    [Test]
    public void Overlap_FindsTheBodiesTheShapeTouches()
    {
        var scene = new SceneBuilder();
        var touched = scene.AddBox(new Vector3F(1, 0, 0), new Vector3F(0.5f));
        scene.AddBox(new Vector3F(3, 0, 0), new Vector3F(0.5f));

        var found = new List<BodyHandle>();
        scene.World.Overlap(new SphereShape(0.6f), new Pose(Vector3F.Zero), found);

        Assert.That(found, Has.Count.EqualTo(1));
        Assert.That(found[0], Is.EqualTo(touched));
    }
}
