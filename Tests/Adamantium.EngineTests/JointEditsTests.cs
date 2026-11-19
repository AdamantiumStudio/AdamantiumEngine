using Adamantium.DarkMatter;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.Engine.Physics;
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
            Assert.That(door.GetComponent<RigidBody>(), Is.Not.Null, "a joint brings the body it needs");
            Assert.That(PhysicsParts.JointOf(made, "Frame"), Is.EqualTo(edits));
        });
    }

    [Test]
    public void AnotherKind_TakesThePlaceOfTheJointThePartHas()
    {
        var part = new Entity(null, "Part");
        PhysicsParts.SetJoint(part, new JointEdits { Kind = JointKind.Ball }, null);

        PhysicsParts.SetJoint(part, new JointEdits { Kind = JointKind.Slider, Axis = Vector3F.UnitX }, null);

        Assert.Multiple(() =>
        {
            Assert.That(part.GetComponent<BallJoint>(), Is.Null);
            Assert.That(part.GetComponent<SliderJoint>()?.Axis, Is.EqualTo(Vector3F.UnitX));
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
    public void AModelsEdits_KeepTheJoint()
    {
        var joint = new JointEdits
        {
            Kind = JointKind.Distance, ConnectedPart = "Chassis", Anchor = new Vector3F(0, 1, 0),
            ConnectedAnchor = new Vector3F(0, -1, 0), UsesLimits = true, LowerLimit = 0.5f, UpperLimit = 2,
            SpringHertz = 3, SpringDampingRatio = 0.7f
        };
        var edits = new ModelEdits();
        edits.Parts["Wheel"] = new PartEdits { Joint = joint };

        var back = ModelEditsSerializer.Deserialize(ModelEditsSerializer.Serialize(edits));

        Assert.That(back.Parts["Wheel"].Joint, Is.EqualTo(joint));
    }
}
