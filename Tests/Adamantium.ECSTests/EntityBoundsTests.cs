using System;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.ECS.Components.Extensions;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.ECSTests;

/// <summary>The size of an entity is that of what it and everything under it hold, in its own axes.</summary>
[TestFixture]
public class EntityBoundsTests
{
    [Test]
    public void Bounds_HoldEveryPartUnderTheEntityInItsOwnAxes()
    {
        var model = new Entity(null, "Model");
        model.Transform.Position = new Vector3<double>(100, 0, 0);
        var near = new Entity(model, "Near");
        near.Transform.Position = new Vector3<double>(-2, 0, 0);
        AddBox(near, 0.5);
        var far = new Entity(model, "Far");
        far.Transform.Position = new Vector3<double>(4, 0, 0);
        far.Transform.Rotation = Quaternion.CreateFromAxisAngle<float>(Vector3F.UnitZ, float.DegreesToRadians(90));
        AddBox(far, 1, new Vector3<double>(2, 0.5, 0.5));

        Assert.That(model.TryMeasureBounds(out var bounds), Is.True);

        Assert.That((bounds.Center - new Vector3<double>(1, 0, 0)).Length(), Is.LessThan(1e-5));
        Assert.That((bounds.HalfExtent - new Vector3<double>(3.5, 2, 0.5)).Length(), Is.LessThan(1e-5),
            "the far box stands on end: two up and down, half a unit across");
    }

    [Test]
    public void EntityHoldingNothing_HasNoBounds()
    {
        var empty = new Entity(null, "Empty");
        _ = new Entity(empty, "Also empty");

        Assert.That(empty.TryMeasureBounds(out _), Is.False);
        Assert.That(empty.GetDiameter(), Is.Zero);
        Assert.Throws<InvalidOperationException>(() => empty.GetRadius());
    }

    private static void AddBox(Entity entity, double half, Vector3<double>? halfExtent = null)
    {
        entity.AddComponent(new BoxCollider { Size = (2 * (halfExtent ?? new Vector3<double>(half))).As<float>() });
    }
}
