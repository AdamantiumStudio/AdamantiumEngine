using Adamantium.ECS;
using Adamantium.Engine;
using Adamantium.Engine.EntityServices;
using Adamantium.Multiverse;
using Adamantium.Multiverse.Input;
using NUnit.Framework;

namespace Adamantium.EngineTests;

/// <summary>The entity the player is put in charge of is let go of once it leaves the world, alone or with what holds
/// it: the camera keys would follow it there.</summary>
[TestFixture]
public class UserControlledEntityTests
{
    private TestUniverse universe;
    private EntityManager manager;
    private InputService input;
    private Entity model;
    private Entity part;

    [SetUp]
    public void BuildScene()
    {
        universe = new TestUniverse();
        universe.Satellites.Add<IUniverse>(universe);
        universe.Satellites.Add(new Selection());
        universe.Satellites.Add(new Observatory(universe, universe.EntityWorld));
        universe.Satellites.Add(new InputActions());
        manager = universe.EntityWorld.EntityManager;
        input = new InputService(universe.EntityWorld);
        model = new Entity(null, "Model");
        part = new Entity(model, "Part");
        manager.AddEntity(model);
        NextFrame();
    }

    [Test]
    public void TheModelInCharge_TakenOut_IsLetGoOf()
    {
        input.UserControlledEntity = model;

        manager.RemoveEntity(model);
        NextFrame();

        Assert.That(input.UserControlledEntity, Is.Null);
    }

    [Test]
    public void APartInCharge_IsLetGoOf_WhenTheModelHoldingItIsTakenOut()
    {
        input.UserControlledEntity = part;

        manager.RemoveEntity(model);
        NextFrame();

        Assert.That(input.UserControlledEntity, Is.Null);
    }

    [Test]
    public void TheModelInCharge_IsKept_WhenAPartOfItIsTakenOut()
    {
        input.UserControlledEntity = model;

        manager.RemoveEntity(part);
        NextFrame();

        Assert.That(input.UserControlledEntity, Is.SameAs(model));
    }

    private void NextFrame()
    {
        universe.EntityWorld.ForceUpdate();
        universe.Settle();
    }
}
