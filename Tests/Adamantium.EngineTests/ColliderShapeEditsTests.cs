using Adamantium.ECS.Components;
using Adamantium.Engine.Physics;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.EngineTests;

/// <summary>A collider's sizes and placement as a model's edits and a scene keep them: read from a collider, given to
/// another, and through the asset file.</summary>
[TestFixture]
public class ColliderShapeEditsTests
{
    private static readonly QuaternionF Turn = Quaternion.CreateFromAxisAngle(Vector3F.UnitX, 0.5f);

    [Test]
    public void AFittedUnscaledCollider_HasNoShape_AndAScaledOneOnlyItsScale()
    {
        var scaled = new SphereCollider { Scale = new Vector3F(2) };

        Assert.Multiple(() =>
        {
            Assert.That(new BoxCollider().GetShape(), Is.Null);
            Assert.That(scaled.GetShape(), Is.EqualTo(new ColliderShapeEdits { Scale = new Vector3F(2) }));
            Assert.That(new CylinderCollider { ChoosesAxis = false }.GetShape(),
                Is.EqualTo(new ColliderShapeEdits { ChoosesAxis = false }));
            Assert.That(new CylinderCollider { ChoosesAxis = false, Axis = ColliderAxis.X, IsFitted = true }.GetShape(),
                Is.EqualTo(new ColliderShapeEdits { ChoosesAxis = false, Axis = ColliderAxis.X }),
                "a fit that keeps the axis keeps it written");
        });
    }

    [Test]
    public void ASizedCollider_IsGivenToAnother_AsItIs()
    {
        Collider[] sized =
        [
            new BoxCollider { Size = new Vector3F(1, 2, 3), Position = new Vector3F(0, 1, 0), Rotation = Turn },
            new SphereCollider { Radius = 2, Scale = new Vector3F(1, 2, 1) },
            new CapsuleCollider { Axis = ColliderAxis.Z, Radius = 0.5f, Height = 3, Position = new Vector3F(1, 0, 0) },
            new CylinderCollider { Axis = ColliderAxis.X, Radius = 1, Height = 0.4f, Rotation = Turn },
            new ConvexHullCollider { Position = new Vector3F(0, 0, 2) }
        ];
        Collider[] fresh = [new BoxCollider(), new SphereCollider(), new CapsuleCollider(), new CylinderCollider(), new ConvexHullCollider()];

        Assert.Multiple(() =>
        {
            for (int i = 0; i < sized.Length; i++)
            {
                var shape = sized[i].GetShape();
                Assert.That(shape.IsFitted, Is.False, sized[i].GetType().Name);
                Assert.That(PhysicsParts.SetShape(fresh[i], shape), Is.True);
                Assert.That(fresh[i].GetShape(), Is.EqualTo(shape), sized[i].GetType().Name);
                Assert.That(fresh[i].Bounds, Is.EqualTo(sized[i].Bounds), sized[i].GetType().Name);
            }
        });
    }

    [Test]
    public void AShapeThatIsNoShape_LeavesTheCollider()
    {
        var box = new BoxCollider { Size = new Vector3F(2) };

        Assert.Multiple(() =>
        {
            Assert.That(PhysicsParts.SetShape(box, new ColliderShapeEdits { IsFitted = false, Size = new Vector3F(float.NaN) }), Is.False);
            Assert.That(PhysicsParts.SetShape(box, new ColliderShapeEdits { IsFitted = false, Radius = -1 }), Is.False);
            Assert.That(PhysicsParts.SetShape(box, new ColliderShapeEdits { IsFitted = false, Rotation = default }), Is.False);
            Assert.That(PhysicsParts.SetShape(box, new ColliderShapeEdits { IsFitted = false, Axis = (ColliderAxis)7 }), Is.False);
            Assert.That(box.Size, Is.EqualTo(new Vector3F(2)));
        });
    }

    [Test]
    public void AModelsEdits_KeepTheShape()
    {
        var shape = new ColliderShapeEdits
        {
            IsFitted = false, Position = new Vector3F(1, 2, 3), Rotation = Turn, Scale = new Vector3F(1, 2, 1),
            Size = new Vector3F(4, 5, 6), Radius = 0.25f, Height = 7, Axis = ColliderAxis.Z, ChoosesAxis = false
        };
        var edits = new ModelEdits();
        edits.Parts["Wheel"] = new PartEdits { Collider = ColliderKind.Cylinder, ColliderShape = shape };
        edits.Parts["Body"] = new PartEdits { Collider = ColliderKind.Box };

        var back = ModelEditsSerializer.Deserialize(ModelEditsSerializer.Serialize(edits));

        Assert.Multiple(() =>
        {
            Assert.That(back.Parts["Wheel"].ColliderShape, Is.EqualTo(shape));
            Assert.That(back.Parts["Body"].ColliderShape, Is.Null);
        });
    }
}
