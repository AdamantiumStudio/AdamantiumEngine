using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.ECS.Components.Extensions;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.ECSTests;

[TestFixture]
public class WorldPlacementTests
{
    [Test]
    public void WorldPosition_IsWhereTheRenderPutsTheOrigin()
    {
        var scene = new PickScene();
        var parent = new Entity(null, "Parent");
        parent.Transform.Position = new Vector3<double>(3, 0, 0);
        parent.Transform.Rotation = Quaternion.CreateFromAxisAngle<float>(Vector3F.UnitY, float.DegreesToRadians(90));
        parent.Transform.BaseScale = new Vector3F(2);
        parent.Transform.Pivot = parent.Transform.Position + new Vector3<double>(0, 0, 4);
        var child = new Entity(parent, "Child");
        child.Transform.Position = new Vector3<double>(1, 0, 0);
        scene.Place(parent);

        var rendered = child.Transform.GetMetadata(scene.Camera).AbsoluteWorld.TranslationVector;

        Assert.That((child.Transform.WorldPosition.As<float>() - rendered).Length(), Is.LessThan(1e-4f));
    }

    [Test]
    public void CenterAbsolute_IsWhereTheRenderPutsTheCenter()
    {
        var scene = new PickScene();
        var model = new Entity(null, "Model");
        PickScene.Bounds(model, new Vector3F(-1, 4, -1), new Vector3F(1, 6, 1));
        model.Transform.BaseScale = new Vector3F(0.01f);
        model.Transform.Position = new Vector3<double>(0, 0, 10);
        model.Transform.Pivot = model.Transform.Position + new Vector3<double>(0, 5, 0);
        scene.Place(model);

        var world = model.Transform.GetMetadata(scene.Camera).AbsoluteWorld;
        var rendered = Vector3F.TransformCoordinate(model.GetComponent<Collider>().LocalCenter, world);

        Assert.That((model.GetCenterAbsolute().As<float>() - rendered).Length(), Is.LessThan(1e-4f));
    }
}
