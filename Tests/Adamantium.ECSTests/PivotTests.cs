using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.ECSTests;

[TestFixture]
public class PivotTests
{
    private static readonly Vector3F[] Probes = [new(0, 0, 0), new(1, 2, 3), new(-4, 0, 7)];

    [Test]
    public void MovingThePivot_LeavesTheEntityInPlace()
    {
        var entity = Turned();
        var before = Placed(entity);

        entity.Transform.Pivot = entity.Transform.Pivot + new Vector3<double>(5, -2, 1);

        AssertSamePlaces(before, Placed(entity));
    }

    [Test]
    public void AnEntity_TurnsAboutItsPivot()
    {
        var entity = Turned();
        var pivot = new Vector3<double>(5, -2, 1);
        entity.Transform.Pivot = pivot;
        var atPivot = Vector3F.TransformCoordinate(pivot.As<float>(), Matrix4x4F.Invert(entity.Transform.GetLocalMatrixF()));

        entity.Transform.Rotation = Quaternion.CreateFromAxisAngle<float>(Vector3F.UnitX, float.DegreesToRadians(40)) * entity.Transform.Rotation;

        var after = Vector3F.TransformCoordinate(atPivot, entity.Transform.GetLocalMatrixF());
        Assert.That((after - pivot.As<float>()).Length(), Is.LessThan(1e-3f));
    }

    [Test]
    public void ResettingThePivot_LeavesTheEntityInPlace()
    {
        var entity = Turned();
        entity.Transform.Pivot = entity.Transform.Pivot + new Vector3<double>(5, -2, 1);
        var before = Placed(entity);

        entity.Transform.ResetPivotPosition();

        AssertSamePlaces(before, Placed(entity));
    }

    [Test]
    public void ACopy_KeepsThePivot()
    {
        var entity = Turned();
        entity.Transform.Pivot = entity.Transform.Pivot + new Vector3<double>(5, -2, 1);
        var copy = new Transform();

        entity.Transform.CloneValues(copy);

        Assert.That(copy.Pivot, Is.EqualTo(entity.Transform.Pivot));
        Assert.That(copy.GetLocalMatrixF(), Is.EqualTo(entity.Transform.GetLocalMatrixF()));
    }

    private static Entity Turned()
    {
        var entity = new Entity(null, "Turned");
        entity.Transform.Position = new Vector3<double>(10, 0, 0);
        entity.Transform.Rotation = Quaternion.CreateFromAxisAngle<float>(Vector3F.UnitY, float.DegreesToRadians(90));
        entity.Transform.BaseScale = new Vector3F(0.5f);
        return entity;
    }

    private static Vector3F[] Placed(Entity entity)
    {
        var world = entity.Transform.GetWorldMatrixF();
        var placed = new Vector3F[Probes.Length];
        for (int i = 0; i < Probes.Length; i++)
        {
            placed[i] = Vector3F.TransformCoordinate(Probes[i], world);
        }

        return placed;
    }

    private static void AssertSamePlaces(Vector3F[] before, Vector3F[] after)
    {
        for (int i = 0; i < before.Length; i++)
        {
            Assert.That((after[i] - before[i]).Length(), Is.LessThan(1e-3f), $"probe {i}: {before[i]} became {after[i]}");
        }
    }
}
