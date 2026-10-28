using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.ECSTests;

[TestFixture]
public class BoxColliderMergeTests
{
    [Test]
    public void Merge_PlacesAPartWhereItStands()
    {
        var model = new Entity(null, "Model");
        var part = PickScene.Box(model, "Part", new Vector3<double>(10, 0, 0), 1);

        var bounds = MergeParts(model, part);

        AssertBox(bounds, new Vector3F(10, 0, 0), new Vector3F(1, 1, 1));
    }

    [Test]
    public void Merge_SpansEveryPart()
    {
        var model = new Entity(null, "Model");
        var left = PickScene.Box(model, "Left", new Vector3<double>(-4, 0, 0), 1);
        var right = PickScene.Box(model, "Right", new Vector3<double>(4, 0, 0), 1);

        var bounds = MergeParts(model, left, right);

        AssertBox(bounds, Vector3F.Zero, new Vector3F(5, 1, 1));
    }

    [Test]
    public void Merge_TurnsAndScalesAPartAsItIsPlaced()
    {
        var model = new Entity(null, "Model");
        var part = PickScene.Box(model, "Part", new Vector3<double>(0, 0, 5), 1);
        part.Transform.BaseScale = new Vector3F(2, 1, 1);
        part.Transform.Rotation = Quaternion.CreateFromAxisAngle<float>(Vector3F.UnitY, float.DegreesToRadians(90));

        var bounds = MergeParts(model, part);

        AssertBox(bounds, new Vector3F(0, 0, 5), new Vector3F(1, 1, 2));
    }

    [Test]
    public void Merge_ThroughANodeOfItsOwn()
    {
        var model = new Entity(null, "Model");
        var node = new Entity(model, "Node");
        node.Transform.Position = new Vector3<double>(10, 0, 0);
        var part = PickScene.Box(node, "Part", new Vector3<double>(1, 0, 0), 1);

        MergeParts(node, part);
        var bounds = MergeParts(model, node);

        AssertBox(bounds, new Vector3F(11, 0, 0), new Vector3F(1, 1, 1));
    }

    private static Bounds MergeParts(Entity owner, params Entity[] parts)
    {
        var collider = owner.GetOrCreateComponent<BoxCollider>();
        foreach (var part in parts)
        {
            collider.Merge(part.GetComponent<Collider>());
        }

        return collider.Bounds;
    }

    private static void AssertBox(Bounds bounds, Vector3F center, Vector3F halfExtent)
    {
        Assert.That((bounds.Center.As<float>() - center).Length(), Is.LessThan(1e-4f), $"center {bounds.Center}");
        Assert.That((bounds.HalfExtent.As<float>() - halfExtent).Length(), Is.LessThan(1e-4f), $"half extent {bounds.HalfExtent}");
    }
}
