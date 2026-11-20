using System;
using Adamantium.DarkMatter.Shapes;
using NUnit.Framework;

namespace Adamantium.DarkMatter.Tests;

/// <summary>A shape moved without turning, met by the first body in its way: where, how far, and which way that
/// body's surface faces.</summary>
[TestFixture]
public class ShapeCastTests
{
    // The gap a cast leaves short of what it meets, and a little more.
    private const float Near = 0.002f;

    [Test]
    public void ABall_MeetsTheNearestBody_LeavingAHair()
    {
        var scene = new SceneBuilder();
        var far = scene.AddBox(new Vector3F(10, 0, 0), new Vector3F(0.5f));
        var near = scene.AddBox(new Vector3F(5, 0, 0), new Vector3F(0.5f));

        var met = scene.World.CastShape(new SphereCollisionShape(0.5f), Pose.Identity, new Vector3F(20, 0, 0),
            out var hit);

        Assert.Multiple(() =>
        {
            Assert.That(met, Is.True);
            Assert.That(hit.Body, Is.EqualTo(near).And.Not.EqualTo(far));
            Assert.That(hit.Distance, Is.EqualTo(4).Within(Near).And.LessThan(4));
            Assert.That(hit.Normal.X, Is.EqualTo(-1).Within(1e-4f));
            Assert.That(hit.Position.X, Is.EqualTo(4.5f).Within(Near));
        });
    }

    [Test]
    public void ACast_StopsAtItsTranslation()
    {
        var scene = new SceneBuilder();
        scene.AddBox(new Vector3F(5, 0, 0), new Vector3F(0.5f));

        Assert.That(scene.World.CastShape(new SphereCollisionShape(0.5f), Pose.Identity, new Vector3F(3.9f, 0, 0),
            out _), Is.False);
    }

    [Test]
    public void AShapeMovingAway_OrPassingBesideABody_MeetsNothing()
    {
        var scene = new SceneBuilder();
        scene.AddBox(new Vector3F(5, 0, 0), new Vector3F(0.5f));
        var ball = new SphereCollisionShape(0.5f);

        var away = scene.World.CastShape(ball, Pose.Identity, new Vector3F(-20, 0, 0), out _);
        var beside = scene.World.CastShape(ball, new Pose(new WorldPosition(0, 1.01f, 0)), new Vector3F(20, 0, 0), out _);
        var grazing = scene.World.CastShape(ball, new Pose(new WorldPosition(0, 0.99f, 0)), new Vector3F(20, 0, 0), out _);

        Assert.Multiple(() =>
        {
            Assert.That(away, Is.False);
            Assert.That(beside, Is.False, "a centimeter clear");
            Assert.That(grazing, Is.True, "a centimeter in");
        });
    }

    // Starting in a body, the cast meets it at once; the normal still faces the shape, as a contact's would.
    [Test]
    public void AShapeStartingInABody_MeetsItAtOnce()
    {
        var scene = new SceneBuilder();
        var box = scene.AddBox(Vector3F.Zero, new Vector3F(1));

        var met = scene.World.CastShape(new SphereCollisionShape(0.5f), new Pose(new WorldPosition(1.2f, 0, 0)),
            new Vector3F(5, 0, 0), out var hit);

        Assert.Multiple(() =>
        {
            Assert.That(met, Is.True);
            Assert.That(hit.Body, Is.EqualTo(box));
            Assert.That(hit.Distance, Is.Zero);
            Assert.That(hit.Normal.X, Is.EqualTo(1).Within(1e-3f));
        });
    }

    // Not moving, a shape meets what it is in or within the meeting gap of - half a millimeter - and nothing else.
    [Test]
    public void NoTranslation_MeetsWhatTheShapeTouches_AndNothingElse()
    {
        var scene = new SceneBuilder();
        scene.AddBox(new Vector3F(2, 0, 0), new Vector3F(0.5f));
        var ball = new SphereCollisionShape(0.5f);

        var inside = scene.World.CastShape(ball, new Pose(new WorldPosition(1.01f, 0, 0)), Vector3F.Zero, out var hit);
        var touching = scene.World.CastShape(ball, new Pose(new WorldPosition(1, 0, 0)), Vector3F.Zero, out _);
        var twoMillimetersOff = scene.World.CastShape(ball, new Pose(new WorldPosition(0.998f, 0, 0)), Vector3F.Zero, out _);
        var apart = scene.World.CastShape(ball, Pose.Identity, Vector3F.Zero, out _);

        Assert.Multiple(() =>
        {
            Assert.That(inside, Is.True);
            Assert.That(hit.Distance, Is.Zero);
            Assert.That(hit.Position.X, Is.EqualTo(1.5f).Within(Near), "on the box's face, not inside it");
            Assert.That(touching, Is.True, "within half a millimeter");
            Assert.That(twoMillimetersOff, Is.False);
            Assert.That(apart, Is.False);
        });
    }

    // A shape left where a cast stopped it, on a box or on a height field, can be cast along the floor and away from
    // it; cast toward it again it meets it at once.
    [Test]
    public void AShapeStoppedByACast_MovesAlongOrAway_ButNotIn()
    {
        var scene = new SceneBuilder();
        scene.AddGround();
        scene.World.Add(new BodyDescription
        {
            Kind = BodyKind.Static, Shape = new HeightFieldCollisionShape(11, 11, new float[11 * 11], 1, 1),
            Position = new WorldPosition(30, 0, 0)
        });
        var box = new BoxCollisionShape(0.3f, 0.3f, 0.3f);

        foreach (var x in new[] { 0f, 30f })
        {
            scene.World.CastShape(box, new Pose(new WorldPosition(x, 2, 0)), new Vector3F(0, -5, 0), out var landed);
            var resting = new Pose(new WorldPosition(x, 2 - landed.Distance, 0));

            var along = scene.World.CastShape(box, resting, new Vector3F(2, 0, 0), out _);
            var away = scene.World.CastShape(box, resting, new Vector3F(0, 2, 0), out _);
            var into = scene.World.CastShape(box, resting, new Vector3F(0, -1, 0), out var again);
            var halfTheGap = scene.World.CastShape(box, resting, new Vector3F(0, -0.0005f, 0), out var nudged);

            Assert.Multiple(() =>
            {
                Assert.That(along, Is.False, $"along the floor at {x}");
                Assert.That(away, Is.False, $"up from the floor at {x}");
                Assert.That(into, Is.True, $"down into the floor at {x}");
                Assert.That(again.Distance, Is.LessThan(Near), $"at once at {x}");
                Assert.That(halfTheGap, Is.True, $"half a millimeter down at {x}");
                Assert.That(nudged.Distance, Is.Zero, $"half a millimeter down at {x}");
            });
        }
    }

    // A box set 0.3 mm over the floor - within the meeting gap - slides along it and lifts off freely, and meets it
    // at once moving down.
    [Test]
    public void AShapeStartingWithinTheMeetingGap_MeetsOnlyMovingIn()
    {
        var scene = new SceneBuilder();
        scene.AddGround();
        scene.World.Add(new BodyDescription
        {
            Kind = BodyKind.Static, Shape = new HeightFieldCollisionShape(11, 11, new float[11 * 11], 1, 1),
            Position = new WorldPosition(30, 0, 0)
        });
        var box = new BoxCollisionShape(0.3f, 0.3f, 0.3f);

        // Along the field the box comes as near the triangles ahead, and meets them: only on the one slab is along free.
        foreach (var x in new[] { 0f, 30f })
        {
            var touching = new Pose(new WorldPosition(x, 0.3003f, 0));
            Assert.Multiple(() =>
            {
                if (x == 0)
                {
                    Assert.That(scene.World.CastShape(box, touching, new Vector3F(2, 0, 0), out _), Is.False, "along");
                    Assert.That(scene.World.CastShape(box, touching, new Vector3F(3, -0.000001f, 0), out _), Is.False,
                        "along, nearing by rounding");
                }

                Assert.That(scene.World.CastShape(box, touching, new Vector3F(0, 2, 0), out _), Is.False, $"up {x}");
                Assert.That(scene.World.CastShape(box, touching, new Vector3F(0, -1, 0), out var down), Is.True,
                    $"down {x}");
                Assert.That(down.Distance, Is.Zero, $"down at once {x}");
                Assert.That(scene.World.CastShape(box, touching, new Vector3F(0, -0.000009f, 0), out _), Is.True,
                    $"down by nine microns {x}");
                Assert.That(scene.World.CastShape(box, touching, new Vector3F(3, -0.0009f, 0), out var dipping),
                    Is.True, $"a long way dipping in {x}");
                Assert.That(dipping.Distance, Is.Zero, $"dipping in at once {x}");
            });
        }
    }

    // A box two millimeters up moved down to end 0.51 mm or 0.53 mm clear: the first is met and stopped short, the
    // second not; from where either is left, it can be cast up.
    [Test]
    public void AWayEndingNearTheMeetingGap_LeavesTheShapeFree()
    {
        var scene = new SceneBuilder();
        scene.AddGround();
        var box = new BoxCollisionShape(0.3f, 0.3f, 0.3f);
        var start = new Pose(new WorldPosition(0, 0.302f, 0));

        var within = scene.World.CastShape(box, start, new Vector3F(0, -0.00149f, 0), out var stopped);
        var leftWithin = new Pose(start.Position - new Vector3F(0, stopped.Distance, 0));
        var clear = scene.World.CastShape(box, start, new Vector3F(0, -0.00147f, 0), out _);
        var leftClear = new Pose(start.Position - new Vector3F(0, 0.00147f, 0));

        Assert.Multiple(() =>
        {
            Assert.That(within, Is.True);
            Assert.That(stopped.Distance, Is.InRange(0.001f, 0.00113f), "stopped within a millimeter, well clear");
            Assert.That(clear, Is.False);
            Assert.That(scene.World.CastShape(box, leftWithin, new Vector3F(0, 2, 0), out _), Is.False);
            Assert.That(scene.World.CastShape(box, leftClear, new Vector3F(0, 2, 0), out _), Is.False);
            Assert.That(scene.World.CastShape(box, leftClear, new Vector3F(2, 0, 0), out _), Is.False);
        });
    }

    // A floor of two slabs meeting at x = 0: a box or a ball stopped on one slides across the seam onto the other.
    [Test]
    public void AShapeSlidingAcrossASeam_DoesNotMeetTheNextSlab()
    {
        var scene = new SceneBuilder();
        scene.World.Add(new BodyDescription
        {
            Kind = BodyKind.Static, Shape = new BoxCollisionShape(5, 1, 5), Position = new WorldPosition(-5, -1, 0)
        });
        scene.World.Add(new BodyDescription
        {
            Kind = BodyKind.Static, Shape = new BoxCollisionShape(5, 1, 5), Position = new WorldPosition(5, -1, 0)
        });
        CollisionShape[] shapes = [new BoxCollisionShape(0.3f, 0.3f, 0.3f), new SphereCollisionShape(0.3f)];

        foreach (var shape in shapes)
        {
            foreach (var x in new[] { -0.5f, -0.31f, -0.3f, -0.29f, -0.01f })
            {
                scene.World.CastShape(shape, new Pose(new WorldPosition(x, 2, 0)), new Vector3F(0, -5, 0), out var landed);
                var resting = new Pose(new WorldPosition(x, 2 - landed.Distance, 0));

                var across = scene.World.CastShape(shape, resting, new Vector3F(1, 0, 0), out var hit);

                Assert.That(across, Is.False, $"{shape.Kind} from x = {x}, met at {hit.Distance} facing {hit.Normal}");
            }
        }
    }

    // Two slabs under a box, one 0.42 mm higher, and a height field 0.2 mm above a ground slab: the higher is met,
    // whichever the world holds first, and the box stopped on it can slide off.
    [Test]
    public void TheHigherOfTwoBodies_IsMet_WhicheverComesFirst()
    {
        foreach (var higherFirst in new[] { true, false })
        {
            var slabs = new SceneBuilder();
            var lowSlab = new BodyDescription
            {
                Kind = BodyKind.Static, Shape = new BoxCollisionShape(5, 1, 5), Position = new WorldPosition(-5, -1, 0)
            };
            var highSlab = new BodyDescription
            {
                Kind = BodyKind.Static, Shape = new BoxCollisionShape(5, 1, 5), Position = new WorldPosition(5, -1 + 0.00042f, 0)
            };
            var high = higherFirst ? slabs.World.Add(highSlab) : default;
            slabs.World.Add(lowSlab);
            if (!higherFirst)
                high = slabs.World.Add(highSlab);

            var box = new BoxCollisionShape(0.3f, 0.3f, 0.3f);
            slabs.World.CastShape(box, new Pose(new WorldPosition(0, 2, 0)), new Vector3F(0, -5, 0), out var onSlabs);
            var resting = new Pose(new WorldPosition(0, 2 - onSlabs.Distance, 0));
            var slidOff = slabs.World.CastShape(box, resting, new Vector3F(0, 0, 1), out _);

            var fields = new SceneBuilder();
            var ground = new BodyDescription
            {
                Kind = BodyKind.Static, Shape = new BoxCollisionShape(5, 1, 5), Position = new WorldPosition(0, -1, 0)
            };
            var raised = new BodyDescription
            {
                Kind = BodyKind.Static, Shape = new HeightFieldCollisionShape(11, 11, new float[11 * 11], 1, 1),
                Position = new WorldPosition(0, 0.0002f, 0)
            };
            var field = higherFirst ? fields.World.Add(raised) : default;
            fields.World.Add(ground);
            if (!higherFirst)
                field = fields.World.Add(raised);

            fields.World.CastShape(new CylinderCollisionShape(0.15f, 0.4f),
                new Pose(new WorldPosition(0.2f, 2, 0.3f), Quaternion.CreateFromAxisAngle(Vector3F.UnitZ, MathF.PI / 2)),
                new Vector3F(0, -5, 0), out var onField);

            Assert.Multiple(() =>
            {
                Assert.That(onSlabs.Body, Is.EqualTo(high), $"the higher slab, it first: {higherFirst}");
                Assert.That(slidOff, Is.False, $"slid off the slabs, the higher first: {higherFirst}");
                Assert.That(onField.Body, Is.EqualTo(field), $"the raised field, it first: {higherFirst}");
            });
        }
    }

    // A box gliding down toward the floor so slowly it would meet it only near the end, and a wall straight ahead it
    // reaches first: the wall is met, though the box came within a millimeter of the floor before it.
    [Test]
    public void TheBodyMetFirst_IsMet_NotTheOneFirstCameNear()
    {
        // The second wall is met when the floor is half a millimeter and a hair off.
        foreach (var wallAt in new[] { 3.8f, 4.52f })
        foreach (var wallFirst in new[] { true, false })
        {
            var scene = new SceneBuilder();
            var floor = new BodyDescription
            {
                Kind = BodyKind.Static, Shape = new BoxCollisionShape(10, 1, 10), Position = new WorldPosition(0, -1, 0)
            };
            var wallDescription = new BodyDescription
            {
                Kind = BodyKind.Static, Shape = new BoxCollisionShape(0.5f, 2, 2), Position = new WorldPosition(wallAt, 1, 0)
            };
            var wall = wallFirst ? scene.World.Add(wallDescription) : default;
            scene.World.Add(floor);
            if (!wallFirst)
                wall = scene.World.Add(wallDescription);

            var met = scene.World.CastShape(new BoxCollisionShape(0.3f, 0.3f, 0.3f),
                new Pose(new WorldPosition(0, 0.302f, 0)), new Vector3F(4, -0.0016f, 0), out var hit);

            Assert.Multiple(() =>
            {
                Assert.That(met, Is.True);
                Assert.That(hit.Body, Is.EqualTo(wall), $"the wall at {wallAt}, it first: {wallFirst}");
                Assert.That(hit.Distance, Is.EqualTo(wallAt - 0.8f).Within(Near), $"the wall at {wallAt}, it first: {wallFirst}");
                Assert.That(hit.Normal.X, Is.EqualTo(-1).Within(1e-3f), $"the wall at {wallAt}, it first: {wallFirst}");
            });
        }
    }

    // A ball stopped on a flat height field turned a tenth of a radian slides down along it, from wherever it stands.
    [Test]
    public void ABallSlidingAlongATurnedFlatField_MeetsNothing()
    {
        var scene = new SceneBuilder();
        var turn = Quaternion.CreateFromAxisAngle(Vector3F.UnitZ, 0.1f);
        scene.World.Add(new BodyDescription
        {
            Kind = BodyKind.Static, Shape = new HeightFieldCollisionShape(41, 41, new float[41 * 41], 0.5f, 0.5f),
            Rotation = turn
        });
        var ball = new SphereCollisionShape(0.4f);
        var down = Vector3F.Transform(Vector3F.UnitY, turn);
        var along = Vector3F.Transform(Vector3F.UnitX, turn);

        foreach (var offset in new[] { -3.1f, -2.27f, -1.5f, -0.6f, 0.13f, 1.71f })
        {
            var above = along * offset + down * 2 + new Vector3F(0, 0, offset * 0.37f);
            scene.World.CastShape(ball, new Pose(new WorldPosition(above)), -down * 5, out var landed);
            var resting = new Pose(new WorldPosition(above - down * landed.Distance));

            var slid = scene.World.CastShape(ball, resting, along * 3, out var hit);

            Assert.That(slid, Is.False, $"from {offset}, met at {hit.Distance} facing {hit.Normal}");
        }
    }

    // A height field turned on its side, as a wall facing +X: a ball cast at it meets its face where the turn puts it.
    [Test]
    public void ATurnedHeightField_IsMetWhereItsTurnPutsIt()
    {
        var scene = new SceneBuilder();
        var wall = scene.World.Add(new BodyDescription
        {
            Kind = BodyKind.Static, Shape = new HeightFieldCollisionShape(11, 11, new float[11 * 11], 1, 1),
            Position = new WorldPosition(-3, 0, 0), Rotation = Quaternion.CreateFromAxisAngle(Vector3F.UnitZ, -MathF.PI / 2)
        });

        var met = scene.World.CastShape(new SphereCollisionShape(0.5f), new Pose(new WorldPosition(2, 0.3f, 0.2f)),
            new Vector3F(-10, 0, 0), out var hit);

        Assert.Multiple(() =>
        {
            Assert.That(met, Is.True);
            Assert.That(hit.Body, Is.EqualTo(wall));
            Assert.That(hit.Distance, Is.EqualTo(4.5f).Within(Near));
            Assert.That(hit.Normal.X, Is.EqualTo(1).Within(1e-3f));
        });
    }

    // A wheel lying on its side cast down its suspension onto a height field: it stops with its rim on the ground.
    [Test]
    public void AWheel_CastDownOntoAHeightField_StopsOnIt()
    {
        var scene = new SceneBuilder();
        var ground = scene.World.Add(new BodyDescription
        {
            Kind = BodyKind.Static, Shape = new HeightFieldCollisionShape(11, 11, new float[11 * 11], 1, 1)
        });
        var wheel = new CylinderCollisionShape(0.15f, 0.4f);
        var lying = Quaternion.CreateFromAxisAngle(Vector3F.UnitZ, MathF.PI / 2);

        var met = scene.World.CastShape(wheel, new Pose(new WorldPosition(0.3f, 2, 0.2f), lying), new Vector3F(0, -3, 0),
            out var hit);

        Assert.Multiple(() =>
        {
            Assert.That(met, Is.True);
            Assert.That(hit.Body, Is.EqualTo(ground));
            Assert.That(hit.Distance, Is.EqualTo(1.6f).Within(Near));
            Assert.That(hit.Normal.Y, Is.EqualTo(1).Within(1e-3f));
            Assert.That(hit.Position.Y, Is.EqualTo(0).Within(Near));
        });
    }

    // A peak half a meter high under the wheel, its slopes' planes reaching up past it: the wheel meets the peak, not
    // the slopes around it, nor the flat beyond.
    [Test]
    public void AWheel_OverAPeak_MeetsThePeak()
    {
        var scene = new SceneBuilder();
        var heights = new float[11 * 11];
        heights[5 * 11 + 5] = 0.5f;
        scene.World.Add(new BodyDescription
        {
            Kind = BodyKind.Static, Shape = new HeightFieldCollisionShape(11, 11, heights, 1, 1)
        });
        var lying = Quaternion.CreateFromAxisAngle(Vector3F.UnitZ, MathF.PI / 2);

        var met = scene.World.CastShape(new CylinderCollisionShape(0.15f, 0.4f), new Pose(new WorldPosition(0, 2, 0), lying),
            new Vector3F(0, -3, 0), out var hit);

        Assert.Multiple(() =>
        {
            Assert.That(met, Is.True);
            Assert.That(hit.Distance, Is.EqualTo(1.1f).Within(Near));
            Assert.That(hit.Position.Y, Is.EqualTo(0.5f).Within(Near));
        });
    }

    // The same wheel on a triangle mesh road, turned a quarter about Y, comes down on it the same.
    [Test]
    public void AWheel_CastDownOntoAMesh_StopsOnIt()
    {
        var scene = new SceneBuilder();
        Vector3F[] corners = [new(-5, 0, -5), new(-5, 0, 5), new(5, 0, 5), new(5, 0, -5)];
        var road = scene.World.Add(new BodyDescription
        {
            Kind = BodyKind.Static, Shape = new TriangleMeshCollisionShape(corners, [0, 1, 2, 0, 2, 3])
        });
        var wheel = new CylinderCollisionShape(0.15f, 0.4f);
        var lying = Quaternion.CreateFromAxisAngle(Vector3F.UnitY, MathF.PI / 2)
                    * Quaternion.CreateFromAxisAngle(Vector3F.UnitZ, MathF.PI / 2);

        var met = scene.World.CastShape(wheel, new Pose(new WorldPosition(1, 1, -1), lying), new Vector3F(0, -2, 0),
            out var hit);

        Assert.Multiple(() =>
        {
            Assert.That(met, Is.True);
            Assert.That(hit.Body, Is.EqualTo(road));
            Assert.That(hit.Distance, Is.EqualTo(0.6f).Within(Near));
            Assert.That(hit.Normal.Y, Is.EqualTo(1).Within(1e-3f));
        });
    }

    // A box turned on its edge comes down corner first: half its diagonal above its center.
    [Test]
    public void ATurnedBox_MeetsTheGroundWithItsEdge()
    {
        var scene = new SceneBuilder();
        scene.AddGround();
        var onEdge = Quaternion.CreateFromAxisAngle(Vector3F.UnitZ, MathF.PI / 4);

        var met = scene.World.CastShape(new BoxCollisionShape(0.5f, 0.5f, 0.5f), new Pose(new WorldPosition(0, 3, 0), onEdge),
            new Vector3F(0, -5, 0), out var hit);

        Assert.Multiple(() =>
        {
            Assert.That(met, Is.True);
            Assert.That(hit.Distance, Is.EqualTo(3 - MathF.Sqrt(0.5f)).Within(Near));
            Assert.That(hit.Normal.Y, Is.EqualTo(1).Within(1e-3f));
        });
    }

    // A rounded hull reaches its full size: the rounding is part of what meets.
    [Test]
    public void ARoundedHull_MeetsWithItsRounding()
    {
        var scene = new SceneBuilder();
        scene.AddGround();
        Vector3F[] cube =
        [
            new(-0.5f, -0.5f, -0.5f), new(0.5f, -0.5f, -0.5f), new(0.5f, 0.5f, -0.5f), new(-0.5f, 0.5f, -0.5f),
            new(-0.5f, -0.5f, 0.5f), new(0.5f, -0.5f, 0.5f), new(0.5f, 0.5f, 0.5f), new(-0.5f, 0.5f, 0.5f)
        ];

        var met = scene.World.CastShape(new ConvexHullCollisionShape(cube, 0.1f), new Pose(new WorldPosition(0, 2, 0)),
            new Vector3F(0, -5, 0), out var hit);

        Assert.That(met, Is.True);
        Assert.That(hit.Distance, Is.EqualTo(1.5f).Within(Near));
    }

    // A compound is cast by its parts: the one that meets first, though it is not the first part, sets the hit.
    [Test]
    public void ACompound_MeetsWithThePartThatComesFirst()
    {
        var scene = new SceneBuilder();
        scene.AddGround();
        var dumbbell = new CompoundCollisionShape([
            new CompoundPart(new SphereCollisionShape(0.25f), new LocalPose(new Vector3F(0, 1, 0))),
            new CompoundPart(new SphereCollisionShape(0.5f), new LocalPose(new Vector3F(0, -1, 0)))
        ]);

        var met = scene.World.CastShape(dumbbell, new Pose(new WorldPosition(0, 4, 0)), new Vector3F(0, -10, 0),
            out var hit);
        var upsideDown = Quaternion.CreateFromAxisAngle(Vector3F.UnitX, MathF.PI);
        var turnedMet = scene.World.CastShape(dumbbell, new Pose(new WorldPosition(0, 4, 0), upsideDown),
            new Vector3F(0, -10, 0), out var turnedHit);

        Assert.Multiple(() =>
        {
            Assert.That(met, Is.True);
            Assert.That(hit.Distance, Is.EqualTo(2.5f).Within(Near));
            Assert.That(turnedMet, Is.True);
            Assert.That(turnedHit.Distance, Is.EqualTo(2.75f).Within(Near), "turned over, the small ball comes first");
        });
    }

    [Test]
    public void ASurface_OrATranslationNotFinite_IsRefused()
    {
        var scene = new SceneBuilder();
        var field = new HeightFieldCollisionShape(2, 2, new float[4], 1, 1);
        var ball = new SphereCollisionShape(0.5f);

        Assert.Multiple(() =>
        {
            Assert.That(() => scene.World.CastShape(field, Pose.Identity, Vector3F.UnitX, out _),
                Throws.ArgumentException);
            Assert.That(() => scene.World.CastShape(ball, Pose.Identity, new Vector3F(float.NaN, 0, 0), out _),
                Throws.InstanceOf<ArgumentOutOfRangeException>());
        });
    }
}
