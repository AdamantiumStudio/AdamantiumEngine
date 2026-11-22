using System;
using System.Collections.Generic;
using Adamantium.DarkMatter;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.ECS.Physics;
using Adamantium.Engine.Scenes;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.EngineTests;

/// <summary>A joint as a model's edits keep it: read from a component, given to a part held to another, kept in the
/// model's file.</summary>
[TestFixture]
public class JointEditsTests
{
    [Test]
    public void AJointsEdits_GiveAnotherPartTheSameJoint_HeldWhereTheyName()
    {
        var hinge = new HingeJoint
        {
            Anchor = new Vector3F(0.5f, 0, 0), Axis = Vector3F.UnitZ, UsesLimits = true, LowerAngle = -30,
            UpperAngle = 60, UsesMotor = true, MotorSpeed = 90, MaxMotorTorque = 20, CollideConnected = true
        };
        var edits = PhysicsParts.JointOf(hinge, "Frame");
        var door = new Entity(null, "Door");
        var frame = new Entity(null, "Frame");

        var given = PhysicsParts.SetJoint(door, edits, frame);
        var made = door.GetComponent<HingeJoint>();

        Assert.Multiple(() =>
        {
            Assert.That(given, Is.True);
            Assert.That(edits.ConnectedPart, Is.EqualTo("Frame"));
            Assert.That(made?.ConnectedBody, Is.SameAs(frame));
            Assert.That(door.GetComponent<RigidBody>(), Is.Null, "a joint may stand under its body, as a collider");
            Assert.That(PhysicsParts.JointOf(made, "Frame"), Is.EqualTo(edits));
        });
    }

    // A limit set past the other takes it along, so the limits are never the wrong way round - a joint whose edits the
    // engine refuses, and a scene would lose. Settings given whole, in either order, end as they were given.
    [Test]
    public void ALimitSetPastTheOther_TakesItAlong_AndAJointsEditsComeOutAsGiven()
    {
        var hinge = new HingeJoint { UpperAngle = 10 };
        hinge.LowerAngle = 50;
        var slider = new SliderJoint { LowerLimit = 2 };
        slider.UpperLimit = -3;
        var distance = new DistanceJoint { MaxDistance = 1 };
        distance.MinDistance = 5;
        var gate = new Entity(null, "Gate");
        gate.AddComponent(new HingeJoint { LowerAngle = -170, UpperAngle = -150 });
        var wanted = new JointEdits { Kind = JointKind.Hinge, UsesLimits = true, LowerLimit = 100, UpperLimit = 120 };

        PhysicsParts.SetJoint(gate, wanted, null);
        var given = gate.GetComponent<HingeJoint>();

        Assert.Multiple(() =>
        {
            Assert.That((hinge.LowerAngle, hinge.UpperAngle), Is.EqualTo((50f, 50f)));
            Assert.That((slider.LowerLimit, slider.UpperLimit), Is.EqualTo((-3f, -3f)));
            Assert.That((distance.MinDistance, distance.MaxDistance), Is.EqualTo((5f, 5f)));
            Assert.That((given.LowerAngle, given.UpperAngle), Is.EqualTo((100f, 120f)));
        });
    }

    [Test]
    public void AnotherKind_StandsBesideTheJointThePartHas_TheSameKindTakesItsPlace()
    {
        var part = new Entity(null, "Part");
        PhysicsParts.SetJoint(part, new JointEdits { Kind = JointKind.Ball }, null);
        var ball = part.GetComponent<BallJoint>();

        PhysicsParts.SetJoint(part, new JointEdits { Kind = JointKind.Slider, Axis = Vector3F.UnitX }, null);
        PhysicsParts.SetJoint(part, new JointEdits { Kind = JointKind.Ball, Anchor = Vector3F.UnitY }, null);

        Assert.Multiple(() =>
        {
            Assert.That(part.GetComponent<BallJoint>(), Is.SameAs(ball));
            Assert.That(ball.Anchor, Is.EqualTo(Vector3F.UnitY));
            Assert.That(part.GetComponent<SliderJoint>()?.Axis, Is.EqualTo(Vector3F.UnitX));
        });
    }

    [Test]
    public void JointsNotKept_AreTakenOff()
    {
        var part = new Entity(null, "Part");
        PhysicsParts.SetJoint(part, new JointEdits { Kind = JointKind.Ball }, null);
        PhysicsParts.SetJoint(part, new JointEdits { Kind = JointKind.Fixed }, null);

        PhysicsParts.RemoveJointsNotIn(part, [new JointEdits { Kind = JointKind.Fixed }]);
        var fixedKept = part.GetComponent<FixedJoint>() != null;
        PhysicsParts.RemoveJointsNotIn(part, null);

        Assert.Multiple(() =>
        {
            Assert.That(fixedKept, Is.True);
            Assert.That(part.GetComponent<Joint>(), Is.Null);
        });
    }

    // An entry's joint left out - its other entry not in the scene, or its settings unsound - leaves the entity's joint
    // of that kind as it is; a kind the entry does not name is taken off.
    [Test]
    public void AnEntrysJointLeftOut_LeavesTheJointOfThatKind()
    {
        var gate = new Entity(null, "Gate");
        PhysicsParts.SetJoint(gate, new JointEdits { Kind = JointKind.Hinge, Anchor = Vector3F.UnitX }, null);
        PhysicsParts.SetJoint(gate, new JointEdits { Kind = JointKind.Ball, Anchor = Vector3F.UnitY }, null);
        PhysicsParts.SetJoint(gate, new JointEdits { Kind = JointKind.Fixed }, null);
        var entry = new SceneMarker
        {
            Name = "Gate",
            Joints =
            [
                new SceneJoint { Kind = JointKind.Hinge, Connected = 99 },
                new SceneJoint { Kind = JointKind.Ball, UsesLimits = true, LowerLimit = 1 }
            ]
        };

        SceneBuilder.ApplyJoints(gate, entry, new Dictionary<UInt128, Entity>());

        Assert.Multiple(() =>
        {
            Assert.That(gate.GetComponent<HingeJoint>()?.Anchor, Is.EqualTo(Vector3F.UnitX));
            Assert.That(gate.GetComponent<BallJoint>()?.Anchor, Is.EqualTo(Vector3F.UnitY));
            Assert.That(gate.GetComponent<FixedJoint>(), Is.Null);
        });
    }

    [Test]
    public void EditsThatAreNoJoint_LeaveThePart()
    {
        var part = new Entity(null, "Part");

        var given = PhysicsParts.SetJoint(part,
            new JointEdits { Kind = JointKind.Hinge, UsesLimits = true, LowerLimit = 10, UpperLimit = 0 }, null);
        var unused = PhysicsParts.SetJoint(new Entity(null, "Other"),
            new JointEdits { Kind = JointKind.Hinge, LowerLimit = 10, UpperLimit = 0 }, null);

        Assert.Multiple(() =>
        {
            Assert.That(given, Is.False);
            Assert.That(part.GetComponent<Joint>(), Is.Null);
            Assert.That(unused, Is.True, "limits unused may stand in any order");
        });
    }

    [Test]
    public void AModelsEdits_KeepTheJoints()
    {
        var distance = new JointEdits
        {
            Kind = JointKind.Distance, ConnectedPart = "Chassis", Anchor = new Vector3F(0, 1, 0),
            ConnectedAnchor = new Vector3F(0, -1, 0), UsesLimits = true, LowerLimit = 0.5f, UpperLimit = 2,
            SpringHertz = 3, SpringDampingRatio = 0.7f
        };
        var hinge = new JointEdits { Kind = JointKind.Hinge, ConnectedPart = "Axle", Axis = Vector3F.UnitX };
        var edits = new ModelEdits();
        edits.Parts["Wheel"] = new PartEdits { Joints = [distance, hinge] };

        var back = ModelEditsSerializer.Deserialize(ModelEditsSerializer.Serialize(edits));

        Assert.That(back.Parts["Wheel"].Joints, Is.EqualTo(new[] { distance, hinge }));
    }
}
