using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.ECSTests;

/// <summary>An entity counts its changes: its values and its components', components added and taken off, entities hung
/// under it and taken from it.</summary>
[TestFixture]
public class EntityChangeCountTests
{
    [Test]
    public void EachKindOfChange_IsCounted()
    {
        var entity = new Entity(null, "Box");

        Assert.Multiple(() =>
        {
            Assert.That(Grows(entity, () => entity.IsEnabled = false), Is.True, "its own value");
            Assert.That(Grows(entity, () => entity.Transform.Position = new Vector3<double>(1, 2, 3)), Is.True,
                "a component's value");
            Assert.That(Grows(entity, () => entity.AddComponent(new RigidBody())), Is.True, "a component added");
            Assert.That(Grows(entity, () => entity.RemoveComponent<RigidBody>()), Is.True, "a component taken off");
            Assert.That(Grows(entity, () => _ = new Entity(entity, "Child")), Is.True, "an entity hung under it");
            Assert.That(Grows(entity, () => { }), Is.False, "nothing");
        });
    }

    private static bool Grows(Entity entity, System.Action change)
    {
        var before = entity.ChangeCount;
        change();
        return entity.ChangeCount > before;
    }
}
