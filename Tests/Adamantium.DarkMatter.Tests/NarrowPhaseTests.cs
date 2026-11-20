using System;
using Adamantium.DarkMatter.CollisionDetection;
using Adamantium.DarkMatter.Shapes;
using NUnit.Framework;

namespace Adamantium.DarkMatter.Tests;

[TestFixture]
public class NarrowPhaseTests
{
    private const float Speculative = 0.02f;
    private readonly ManifoldBuffer _manifold = new();

    [Test]
    public void Spheres_TouchAlongTheLineOfCenters()
    {
        var found = Collide(new SphereCollisionShape(1), new LocalPose(Vector3F.Zero), new SphereCollisionShape(0.5f),
            new LocalPose(new Vector3F(1.4f, 0, 0)));

        Assert.That(found, Is.True);
        AssertNormal(Vector3F.UnitX);
        Assert.That(_manifold.Count, Is.EqualTo(1));
        Assert.That(_manifold.Separations[0], Is.EqualTo(-0.1f).Within(1e-5f));
        Assert.That(_manifold.Positions[0].X, Is.EqualTo(0.95f).Within(1e-5f));
    }

    [Test]
    public void Spheres_FartherThanSpeculative_DoNotTouch()
    {
        var found = Collide(new SphereCollisionShape(1), new LocalPose(Vector3F.Zero), new SphereCollisionShape(1),
            new LocalPose(new Vector3F(2.1f, 0, 0)));

        Assert.That(found, Is.False);
    }

    [Test]
    public void SphereOverBoxFace_TouchesTheFace()
    {
        var found = Collide(new SphereCollisionShape(0.5f), new LocalPose(new Vector3F(0.2f, 1.45f, 0)),
            new BoxCollisionShape(1, 1, 1), LocalPose.Identity);

        Assert.That(found, Is.True);
        AssertNormal(-Vector3F.UnitY);
        Assert.That(_manifold.Separations[0], Is.EqualTo(-0.05f).Within(1e-5f));
    }

    [Test]
    public void SphereCenterInsideBox_LeavesThroughTheNearestFace()
    {
        var found = Collide(new SphereCollisionShape(0.25f), new LocalPose(new Vector3F(0, 0, 0.9f)),
            new BoxCollisionShape(1, 1, 1), LocalPose.Identity);

        Assert.That(found, Is.True);
        AssertNormal(-Vector3F.UnitZ);
        Assert.That(_manifold.Separations[0], Is.EqualTo(-0.35f).Within(1e-5f));
    }

    [Test]
    public void BoxThenSphere_FlipsTheNormal()
    {
        var found = Collide(new BoxCollisionShape(1, 1, 1), LocalPose.Identity, new SphereCollisionShape(0.5f),
            new LocalPose(new Vector3F(0, 1.45f, 0)));

        Assert.That(found, Is.True);
        AssertNormal(Vector3F.UnitY);
    }

    [Test]
    public void ParallelCapsules_TouchAtTwoPoints()
    {
        var lying = Quaternion.CreateFromAxisAngle(Vector3F.UnitZ, MathF.PI / 2);
        var found = Collide(new CapsuleCollisionShape(1, 0.25f), new LocalPose(Vector3F.Zero, lying),
            new CapsuleCollisionShape(1, 0.25f), new LocalPose(new Vector3F(0.5f, 0.45f, 0), lying));

        Assert.That(found, Is.True);
        Assert.That(_manifold.Count, Is.EqualTo(2));
        AssertNormal(Vector3F.UnitY);
        Assert.That(_manifold.Separations[0], Is.EqualTo(-0.05f).Within(1e-4f));
        Assert.That(_manifold.Separations[1], Is.EqualTo(-0.05f).Within(1e-4f));
    }

    [Test]
    public void CrossedCapsules_TouchAtOnePoint()
    {
        var alongX = Quaternion.CreateFromAxisAngle(Vector3F.UnitZ, MathF.PI / 2);
        var alongZ = Quaternion.CreateFromAxisAngle(Vector3F.UnitX, MathF.PI / 2);
        var found = Collide(new CapsuleCollisionShape(1, 0.25f), new LocalPose(Vector3F.Zero, alongX),
            new CapsuleCollisionShape(1, 0.25f), new LocalPose(new Vector3F(0, 0.45f, 0), alongZ));

        Assert.That(found, Is.True);
        Assert.That(_manifold.Count, Is.EqualTo(1));
        AssertNormal(Vector3F.UnitY);
    }

    [Test]
    public void CapsuleLyingOnBox_TouchesAtTwoPoints()
    {
        var lying = Quaternion.CreateFromAxisAngle(Vector3F.UnitZ, MathF.PI / 2);
        var found = Collide(new CapsuleCollisionShape(0.5f, 0.25f), new LocalPose(new Vector3F(0, 1.24f, 0), lying),
            new BoxCollisionShape(2, 1, 2), LocalPose.Identity);

        Assert.That(found, Is.True);
        Assert.That(_manifold.Count, Is.EqualTo(2));
        AssertNormal(-Vector3F.UnitY);
        Assert.That(_manifold.Separations[0], Is.EqualTo(-0.01f).Within(1e-4f));
    }

    [Test]
    public void CapsuleCoreSunkInBox_IsPushedOutThroughTheTop()
    {
        var found = Collide(new CapsuleCollisionShape(0.5f, 0.25f), new LocalPose(new Vector3F(0, 1.2f, 0)),
            new BoxCollisionShape(2, 1, 2), LocalPose.Identity);

        Assert.That(found, Is.True);
        AssertNormal(-Vector3F.UnitY);
        Assert.That(_manifold.Separations[0], Is.EqualTo(-0.55f).Within(1e-4f));
    }

    [Test]
    public void StandingCapsuleEndOnBox_TouchesAtOnePoint()
    {
        var found = Collide(new CapsuleCollisionShape(0.5f, 0.25f), new LocalPose(new Vector3F(0, 1.74f, 0)),
            new BoxCollisionShape(2, 1, 2), LocalPose.Identity);

        Assert.That(found, Is.True);
        Assert.That(_manifold.Count, Is.EqualTo(1));
        AssertNormal(-Vector3F.UnitY);
        Assert.That(_manifold.Separations[0], Is.EqualTo(-0.01f).Within(1e-4f));
    }

    [Test]
    public void BoxOnBox_TouchesAtFourCorners()
    {
        var found = Collide(new BoxCollisionShape(0.5f, 0.5f, 0.5f), new LocalPose(new Vector3F(0, 1.49f, 0)),
            new BoxCollisionShape(1, 1, 1), LocalPose.Identity);

        Assert.That(found, Is.True);
        Assert.That(_manifold.Count, Is.EqualTo(4));
        AssertNormal(-Vector3F.UnitY);
        for (var index = 0; index < 4; index++)
        {
            Assert.That(_manifold.Separations[index], Is.EqualTo(-0.01f).Within(1e-4f));
        }
    }

    [Test]
    public void BoxHoveringOverSlab_TouchesAtFourCornersAhead()
    {
        var found = NarrowPhase.Collide(new BoxCollisionShape(5, 0.025f, 5), new LocalPose(new Vector3F(0, -0.025f, 0)),
            new BoxCollisionShape(0.25f, 0.25f, 0.25f), new LocalPose(new Vector3F(0, 0.311f, 0)), 0.35f, _manifold);

        Assert.That(found, Is.True);
        Assert.That(_manifold.Count, Is.EqualTo(4));
        AssertNormal(Vector3F.UnitY);
        Assert.That(_manifold.Separations[0], Is.EqualTo(0.061f).Within(1e-4f));
    }

    [Test]
    public void TurnedBoxOnBox_ClipsToAnOctagon()
    {
        var turned = Quaternion.CreateFromAxisAngle(Vector3F.UnitY, MathF.PI / 4);
        var found = Collide(new BoxCollisionShape(0.5f, 0.5f, 0.5f), new LocalPose(new Vector3F(0, 0.99f, 0), turned),
            new BoxCollisionShape(0.5f, 0.5f, 0.5f), LocalPose.Identity);

        Assert.That(found, Is.True);
        Assert.That(_manifold.Count, Is.EqualTo(8));

        _manifold.ReduceTo(4);
        Assert.That(_manifold.Count, Is.EqualTo(4));
    }

    [Test]
    public void CrossedBoxEdges_TouchAtOnePoint()
    {
        var edgeUp = Quaternion.CreateFromAxisAngle(Vector3F.UnitZ, MathF.PI / 4);
        var edgeDown = Quaternion.CreateFromAxisAngle(Vector3F.UnitX, MathF.PI / 4);
        var reach = MathF.Sqrt(2) * 0.5f;
        var found = Collide(new BoxCollisionShape(0.5f, 0.5f, 0.5f), new LocalPose(Vector3F.Zero, edgeUp),
            new BoxCollisionShape(0.5f, 0.5f, 0.5f), new LocalPose(new Vector3F(0, 2 * reach - 0.01f, 0), edgeDown));

        Assert.That(found, Is.True);
        Assert.That(_manifold.Count, Is.EqualTo(1));
        AssertNormal(Vector3F.UnitY);
        Assert.That(_manifold.Separations[0], Is.EqualTo(-0.01f).Within(1e-4f));
    }

    private bool Collide(CollisionShape first, LocalPose firstPose, CollisionShape second, LocalPose secondPose) =>
        NarrowPhase.Collide(first, firstPose, second, secondPose, Speculative, _manifold);

    private void AssertNormal(Vector3F expected)
    {
        Assert.That(Vector3F.Dot(_manifold.Normal, expected), Is.GreaterThan(0.9999f),
            $"normal {_manifold.Normal}, expected {expected}");
    }
}
