using Adamantium.Core;
using Adamantium.Core.DependencyInjection;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.ECSTests;

/// <summary>An entity moved under another owner - dragged into a group in a hierarchy - stays where it stands.</summary>
[TestFixture]
public class TransformStayInWorldTests
{
    [Test]
    public void MovedIntoAGroupAndOutAgain_TheEntityStaysWhereItStands()
    {
        var world = new EntityWorld(new AdamantiumDependencyContainer(), new Satellites());
        var group = new Entity(null, "Group");
        group.Transform.Position = new Vector3(5, -1, 2);
        group.Transform.Rotation = QuaternionF.RotationAxis(Vector3F.UnitY, MathHelper.DegreesToRadians(90f));
        group.Transform.ScaleFactor = new Vector3F(2);
        var entity = new Entity(null, "Entity");
        entity.Transform.Position = new Vector3(1, 2, 3);
        entity.Transform.Rotation = QuaternionF.RotationAxis(Vector3F.UnitX, 0.4f);
        world.EntityManager.AddEntity(group);
        world.EntityManager.AddEntity(entity);
        world.ForceUpdate();
        var standing = entity.Transform.GetWorldMatrixF();

        entity.Transform.StayInWorldUnder(group);
        world.EntityManager.SetOwner(entity, group);
        var inGroup = entity.Transform.GetWorldMatrixF();

        entity.Transform.StayInWorldUnder(null);
        world.EntityManager.RemoveOwner(entity);
        var outAgain = entity.Transform.GetWorldMatrixF();

        Assert.Multiple(() =>
        {
            AssertSame(inGroup, standing, "in the group");
            AssertSame(outAgain, standing, "out of it again");
            Assert.That(entity.Transform.ScaleFactor.X, Is.EqualTo(1).Within(1e-4), "the group's scale undone");
        });
    }

    // Its shape is its own: a pivot turned under an uneven scale, a mirror - kept as they are, only the group's turn and
    // even scale joining in.
    [TestCase(false)]
    [TestCase(true)]
    public void AnEntityShapedByItsPivotOrMirrored_StaysAsItIs(bool mirrored)
    {
        var world = new EntityWorld(new AdamantiumDependencyContainer(), new Satellites());
        var group = new Entity(null, "Group");
        group.Transform.Position = new Vector3(5, 0, 0);
        group.Transform.Rotation = QuaternionF.RotationAxis(Vector3F.UnitZ, 0.7f);
        group.Transform.ScaleFactor = new Vector3F(1.5f);
        var entity = new Entity(null, "Entity");
        entity.Transform.ScaleFactor = mirrored ? new Vector3F(-1, 1, 1) : new Vector3F(3, 1, 1);
        entity.Transform.PivotRotation = QuaternionF.RotationAxis(Vector3F.UnitZ, MathHelper.DegreesToRadians(45f));
        entity.Transform.Position = new Vector3(1, 2, 0);
        world.EntityManager.AddEntity(group);
        world.EntityManager.AddEntity(entity);
        world.ForceUpdate();
        var standing = entity.Transform.GetWorldMatrixF();

        var kept = entity.Transform.StayInWorldUnder(group);
        world.EntityManager.SetOwner(entity, group);

        Assert.That(kept, Is.True);
        AssertSame(entity.Transform.GetWorldMatrixF(), standing, mirrored ? "mirrored" : "pivot turned");
    }

    [Test]
    public void UnderAFlattenedOwner_NothingCouldKeepIt_AndTheTransformIsLeft()
    {
        var group = new Entity(null, "Group");
        group.Transform.ScaleFactor = new Vector3F(1, 0, 1);
        var entity = new Entity(null, "Entity");
        entity.Transform.Position = new Vector3(1, 2, 3);

        var kept = entity.Transform.StayInWorldUnder(group);

        Assert.Multiple(() =>
        {
            Assert.That(kept, Is.False);
            Assert.That(entity.Transform.Position, Is.EqualTo(new Vector3(1, 2, 3)));
        });
    }

    [Test]
    public void BetweenOwnersThatStandAlike_TheTransformIsLeftAsItIs()
    {
        var world = new EntityWorld(new AdamantiumDependencyContainer(), new Satellites());
        var group = new Entity(null, "Group");
        var entity = new Entity(null, "Entity");
        entity.Transform.Position = new Vector3(123456.789, 2, 3);
        world.EntityManager.AddEntity(group);
        world.EntityManager.AddEntity(entity);
        world.ForceUpdate();

        entity.Transform.StayInWorldUnder(group);

        Assert.That(entity.Transform.Position, Is.EqualTo(new Vector3(123456.789, 2, 3)), "no float round trip");
    }

    private static void AssertSame(Matrix4x4F actual, Matrix4x4F expected, string when)
    {
        var a = actual.ToArray();
        var e = expected.ToArray();
        for (var i = 0; i < a.Length; i++)
        {
            Assert.That(a[i], Is.EqualTo(e[i]).Within(1e-3), $"{when}: element {i}");
        }
    }
}
