using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.ECSTests;

[TestFixture]
public class LightTests
{
    [Test]
    public void Direction_IsTheEntitysDownAxis_AsTheEntityIsTurned()
    {
        var parent = new Entity(null, "Rig");
        parent.Transform.Rotation = Quaternion.CreateFromAxisAngle<float>(Vector3F.UnitZ, float.DegreesToRadians(90));
        var entity = new Entity(parent, "Light");
        entity.Transform.Rotation = Quaternion.CreateFromAxisAngle<float>(Vector3F.UnitX, float.DegreesToRadians(90));
        entity.AddComponent(new Light(LightType.Spot));

        var expected = Vector3F.Normalize(Vector3F.TransformNormal(Vector3F.Down, entity.Transform.GetWorldMatrixF()));

        Assert.That((entity.GetComponent<Light>().Direction - expected).Length(), Is.LessThan(1e-5f));
        Assert.That(Vector3F.Dot(expected, Vector3F.Down), Is.LessThan(0.5f));
    }
}
