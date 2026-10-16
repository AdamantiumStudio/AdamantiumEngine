using System.Collections.Generic;
using System.Collections.Specialized;
using Adamantium.ECS;
using NUnit.Framework;

namespace Adamantium.EngineTests;

[TestFixture]
public class EntityManagerTests
{
    // A hierarchy view binds to the roots: an entity added to the world and one taken out are both announced.
    [Test]
    public void TheRoots_AnnounceWhatComesAndGoes()
    {
        var universe = new TestUniverse();
        var manager = universe.EntityWorld.EntityManager;
        var changes = new List<NotifyCollectionChangedAction>();
        manager.RootEntities.CollectionChanged += (_, e) => changes.Add(e.Action);
        var entity = new Entity(null, "Entity");

        manager.AddEntity(entity);
        NextFrame(universe);
        manager.RemoveEntity(entity);
        NextFrame(universe);

        Assert.That(changes, Is.EqualTo(new[] { NotifyCollectionChangedAction.Add, NotifyCollectionChangedAction.Remove }));
        Assert.That(manager.RootEntities, Is.Empty);
    }

    private static void NextFrame(TestUniverse universe)
    {
        universe.EntityWorld.ForceUpdate();
        universe.Settle();
    }
}
