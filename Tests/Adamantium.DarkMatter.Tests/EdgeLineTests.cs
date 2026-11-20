using System;
using System.Collections.Generic;
using Adamantium.DarkMatter.CollisionDetection;
using Adamantium.DarkMatter.Shapes;
using NUnit.Framework;

namespace Adamantium.DarkMatter.Tests;

/// <summary>A log or a capsule lying along an edge of a box or a hull touches along a line - at both ends of where
/// the two overlap - not at one point it could turn about.</summary>
[TestFixture]
public class EdgeLineTests
{
    private const float Speculative = 0.02f;
    private static readonly float Ridge = MathF.Sqrt(2) * 0.5f;
    private readonly ManifoldBuffer _manifold = new();

    [Test]
    public void LogAlongABoxsEdge_TouchesAlongIt()
    {
        AssertLine(Log(), new BoxCollisionShape(0.5f, 0.5f, 1), 0.3f, 2 * (0.8f - CollisionShape.DefaultConvexRadius));
    }

    [Test]
    public void LogAlongAHullsEdge_TouchesAlongIt()
    {
        AssertLine(Log(), SharpBlock(), 0.3f, 2 * (0.8f - CollisionShape.DefaultConvexRadius));
    }

    [Test]
    public void CapsuleAlongAHullsEdge_TouchesAlongIt()
    {
        AssertLine(new CapsuleCollisionShape(0.8f, 0.3f), SharpBlock(), 0.3f, 1.6f);
    }

    [Test]
    public void CapsuleAlongABoxsEdge_TouchesAlongIt()
    {
        AssertLine(new CapsuleCollisionShape(0.8f, 0.3f), new BoxCollisionShape(0.5f, 0.5f, 1), 0.3f, 1.6f);
    }

    // A pole longer than the edge, hanging past both its ends and a hair aslope, still lies along it: two points, at
    // the edge's ends - on a box, met by its own code, as on a hull.
    [Test]
    public void PoleLongerThanTheEdgeAndAslope_TouchesAtTheEdgesEnds([Values(true, false)] bool onABox)
    {
        // A third of a degree: the edge's middle stands six millimeters below its higher end.
        var aslope = Quaternion.CreateFromAxisAngle(Vector3F.UnitX, MathF.PI / 2 - 0.006f);
        CollisionShape block = onABox ? new BoxCollisionShape(0.5f, 0.5f, 1) : SharpBlock();
        var found = NarrowPhase.Collide(new CapsuleCollisionShape(1.5f, 0.3f),
            new LocalPose(new Vector3F(0, Ridge + 0.3f - 0.01f, 0), aslope), block, new LocalPose(Vector3F.Zero, Turn()),
            Speculative, _manifold);

        Assert.That(found, Is.True);
        Assert.That(_manifold.Count, Is.EqualTo(2));
        Assert.That(MathF.Abs(_manifold.Positions[0].Z - _manifold.Positions[1].Z), Is.EqualTo(2).Within(0.01f));
    }

    // Turned five degrees about the up axis, a long log crosses the ridge rather than lying along it: its overlap's
    // ends stand aside from the edge and above it, so one point where they cross - no false depth a meter away.
    [Test]
    public void LongLogCrossingTheRidgeAslant_TouchesAtOnePoint([Values(true, false)] bool onABox)
    {
        var aslant = Quaternion.CreateFromAxisAngle(Vector3F.UnitY, 5 * MathF.PI / 180)
                     * Quaternion.CreateFromAxisAngle(Vector3F.UnitX, MathF.PI / 2);
        CollisionShape block = onABox ? new BoxCollisionShape(0.5f, 0.5f, 1) : SharpBlock();
        var found = NarrowPhase.Collide(new CapsuleCollisionShape(1.5f, 0.3f),
            new LocalPose(new Vector3F(0, Ridge + 0.3f - 0.01f, 0), aslant), block, new LocalPose(Vector3F.Zero, Turn()),
            Speculative, _manifold);

        Assert.That(found, Is.True);
        Assert.That(_manifold.Count, Is.EqualTo(1));
        Assert.That(_manifold.Separations[0], Is.EqualTo(-0.01f).Within(2e-3f));
    }

    // Long capsules lying on each other a degree apart: their ends stand a couple of centimeters aside, which their
    // roundness makes a fraction of a millimeter - still a line, as logs rest stacked.
    [Test]
    public void LongCapsulesADegreeApart_TouchAlongALine()
    {
        var lying = Quaternion.CreateFromAxisAngle(Vector3F.UnitX, MathF.PI / 2);
        var turned = Quaternion.CreateFromAxisAngle(Vector3F.UnitY, MathF.PI / 180) * lying;
        var found = NarrowPhase.Collide(new CapsuleCollisionShape(1, 0.3f), new LocalPose(new Vector3F(0, 0.59f, 0), turned),
            new CapsuleCollisionShape(1, 0.3f), new LocalPose(Vector3F.Zero, lying), Speculative, _manifold);

        Assert.That(found, Is.True);
        Assert.That(_manifold.Count, Is.EqualTo(2));
    }

    // The block first, the log second: the same line, the normal from the block up to the log.
    [Test]
    public void BoxFirstAndLogSecond_TouchAlongTheEdgeAlike()
    {
        var alongZ = Quaternion.CreateFromAxisAngle(Vector3F.UnitX, MathF.PI / 2);
        var found = NarrowPhase.Collide(new BoxCollisionShape(0.5f, 0.5f, 1), new LocalPose(Vector3F.Zero, Turn()), Log(),
            new LocalPose(new Vector3F(0, Ridge + 0.3f - 0.01f, 0), alongZ), Speculative, _manifold);

        Assert.That(found, Is.True);
        Assert.That((_manifold.Normal - Vector3F.UnitY).Length(), Is.LessThan(1e-3f), $"normal {_manifold.Normal}");
        Assert.That(_manifold.Count, Is.EqualTo(2));
        Assert.That(_manifold.Separations[1], Is.EqualTo(-0.01f).Within(1e-3f));
    }

    // Beside the top face's border, the nearest a hair past it: the normal leans little from the face's, the face
    // cannot clip the capsule, and the edge still gives the line.
    [Test]
    public void CapsuleJustPastAFacesBorder_TouchesAlongTheEdge()
    {
        var alongZ = Quaternion.CreateFromAxisAngle(Vector3F.UnitX, MathF.PI / 2);
        var found = NarrowPhase.Collide(new CapsuleCollisionShape(0.8f, 0.3f), new LocalPose(new Vector3F(0.55f, 0.79f, 0),
            alongZ), new BoxCollisionShape(0.5f, 0.5f, 1), LocalPose.Identity, Speculative, _manifold);

        Assert.That(found, Is.True);
        Assert.That(_manifold.Count, Is.EqualTo(2));
        Assert.That(_manifold.Separations[0], Is.EqualTo(_manifold.Separations[1]).Within(1e-4f));
    }

    // A capsule touching a cylinder's round rim, though it runs with a side of the polygon the end is drawn as: one
    // point where it touches the circle, not two along a chord.
    [Test]
    public void CapsuleAlongACylindersRim_TouchesAtOnePoint()
    {
        var cylinder = new CylinderCollisionShape(0.5f, 2);
        var corner = MathF.PI * 2 * 4.5f / CylinderCollisionShape.EndCorners;
        var outward = Vector3F.Normalize(new Vector3F(MathF.Cos(corner), 1, -MathF.Sin(corner)));
        var rim = new Vector3F(MathF.Cos(corner), 0, -MathF.Sin(corner)) * (2 - CollisionShape.DefaultConvexRadius)
                  + Vector3F.UnitY * (0.5f - CollisionShape.DefaultConvexRadius);
        var tangent = new Vector3F(-MathF.Sin(corner), 0, -MathF.Cos(corner));
        var turn = Quaternion.CreateFromAxisAngle(Vector3F.Normalize(Vector3F.Cross(Vector3F.UnitY, tangent)),
            MathF.Acos(Vector3F.Dot(Vector3F.UnitY, tangent)));
        var capsule = new CapsuleCollisionShape(0.4f, 0.3f);

        var found = NarrowPhase.Collide(capsule,
            new LocalPose(rim + outward * (CollisionShape.DefaultConvexRadius + 0.3f - 0.005f), turn), cylinder,
            LocalPose.Identity, Speculative, _manifold);

        Assert.That(found, Is.True);
        Assert.That(_manifold.Count, Is.EqualTo(1));
        Assert.That(_manifold.Separations[0], Is.EqualTo(-0.005f).Within(1e-3f));
    }

    // A log lying across the edge, not along it, still touches at the one point where they cross.
    [Test]
    public void LogAcrossABoxsEdge_TouchesAtOnePoint()
    {
        var across = Quaternion.CreateFromAxisAngle(Vector3F.UnitZ, MathF.PI / 2);
        var found = NarrowPhase.Collide(Log(), new LocalPose(new Vector3F(0, Ridge + 0.3f - 0.01f, 0), across),
            new BoxCollisionShape(0.5f, 0.5f, 1), new LocalPose(Vector3F.Zero, Turn()), Speculative, _manifold);

        Assert.That(found, Is.True);
        Assert.That(_manifold.Count, Is.EqualTo(1));
        Assert.That(_manifold.Separations[0], Is.EqualTo(-0.01f).Within(1e-3f));
    }

    // Lying on its side along the ridge of a block turned edge up, the shape sinks a centimeter: two points at the
    // ends of the overlap, as deep, the normal straight down.
    private void AssertLine(CollisionShape lying, CollisionShape block, float radius, float overlap)
    {
        var alongZ = Quaternion.CreateFromAxisAngle(Vector3F.UnitX, MathF.PI / 2);
        var found = NarrowPhase.Collide(lying, new LocalPose(new Vector3F(0, Ridge + radius - 0.01f, 0), alongZ), block,
            new LocalPose(Vector3F.Zero, Turn()), Speculative, _manifold);

        Assert.That(found, Is.True);
        Assert.That((_manifold.Normal + Vector3F.UnitY).Length(), Is.LessThan(1e-3f), $"normal {_manifold.Normal}");
        Assert.That(_manifold.Count, Is.EqualTo(2));
        Assert.That(_manifold.Separations[0], Is.EqualTo(-0.01f).Within(1e-3f));
        Assert.That(_manifold.Separations[1], Is.EqualTo(-0.01f).Within(1e-3f));
        Assert.That(MathF.Abs(_manifold.Positions[0].Z - _manifold.Positions[1].Z), Is.EqualTo(overlap).Within(1e-3f));
    }

    private static CylinderCollisionShape Log() => new(0.8f, 0.3f);

    // A block a meter across and two long, turned about its length so an edge stands up.
    private static QuaternionF Turn() => Quaternion.CreateFromAxisAngle(Vector3F.UnitZ, MathF.PI / 4);

    private static ConvexHullCollisionShape SharpBlock()
    {
        var corners = new List<Vector3F>();
        foreach (var x in new[] { -0.5f, 0.5f })
        foreach (var y in new[] { -0.5f, 0.5f })
        foreach (var z in new[] { -1f, 1f })
        {
            corners.Add(new Vector3F(x, y, z));
        }

        return new ConvexHullCollisionShape(corners, 0);
    }
}
