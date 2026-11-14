using System;
using System.Linq;
using System.Threading.Tasks;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.Engine.Scenes;
using Adamantium.Multiverse;
using NUnit.Framework;

namespace Adamantium.EngineTests;

/// <summary>The game's code built anew: its components move to the new build with their values, an entry whose type the
/// new build lacks stays an entry, and the old build is not found again. A component whose code throws keeps its entry
/// and leaves the rest of the scene loaded.</summary>
[TestFixture]
public class SceneCodeTests
{
    private const string PatrolType = "Game.Patrol";
    private static readonly UInt128 Start = 1;

    private TestUniverse _universe;
    private EntityWorld _world;

    [SetUp]
    public void SetUp()
    {
        _universe = new TestUniverse();
        _universe.Satellites.Add<IUniverse>(_universe);
        _world = _universe.EntityWorld;
    }

    private static SceneFile PatrolAtStart(params SceneValue[] values)
    {
        var scene = new SceneFile();
        scene.SpawnPoints.Add(new SceneSpawnPoint { Uid = Start, Name = "Start" });
        var patrol = new SceneGameComponent { On = Start, Type = PatrolType };
        patrol.Values.AddRange(values);
        scene.GameComponents.Add(patrol);
        return scene;
    }

    private async Task<SceneBuilder> Build(SceneFile scene)
    {
        var builder = new SceneBuilder(_world.MainScene, new CountingAssets());
        await builder.Build(scene);
        _world.ForceUpdate();
        return builder;
    }

    private Entity StartPoint() => _world.MainScene.RootEntities.Single(entity => entity.Uid == Start);

    private static TestGameCode Code(string name, params TestGameField[] patrolFields) =>
        new TestGameCode(name).AddComponent(PatrolType, patrolFields);

    [Test]
    public async Task ANewBuild_TakesTheComponents_WithTheirValues()
    {
        var first = Code("Game.First", new TestGameField("Speed", typeof(double))).Assembly;
        _world.Code.Replace([], [first]);
        await Build(PatrolAtStart(new SceneValue("Speed", "2.5")));
        var second = Code("Game.Second", new TestGameField("Speed", typeof(double)), new TestGameField("Route", typeof(string)))
            .Assembly;

        var problems = SceneCode.Replace(_world, [first], [second]);

        var patrol = StartPoint().Components.OfType<GameComponent>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(problems, Is.Empty);
            Assert.That(patrol.GetType().Assembly, Is.SameAs(second));
            Assert.That(patrol.GetType().GetProperty("Speed").GetValue(patrol), Is.EqualTo(2.5));
            Assert.That(StartPoint().ContainsComponent<UnloadedGameComponents>(), Is.False);
        });
    }

    [Test]
    public async Task ATypeTheNewBuildLacks_StaysAnEntry_AndIsWrittenBack()
    {
        var first = Code("Game.First", new TestGameField("Speed", typeof(double))).Assembly;
        _world.Code.Replace([], [first]);
        await Build(PatrolAtStart(new SceneValue("Speed", "2.5")));
        var second = new TestGameCode("Game.Second").AddComponent("Game.Other").Assembly;

        SceneCode.Replace(_world, [first], [second]);

        var back = SceneCapture.Capture(_world.MainScene).GameComponents.Single();
        Assert.Multiple(() =>
        {
            Assert.That(StartPoint().Components.OfType<GameComponent>(), Is.Empty);
            Assert.That(_world.Code.FindType<GameComponent>(PatrolType), Is.Null, "the old build is not found again");
            Assert.That(back.Type, Is.EqualTo(PatrolType));
            Assert.That(back.Values, Is.EqualTo(new[] { new SceneValue("Speed", "2.5") }));
        });
    }

    [Test]
    public async Task ASceneReadBeforeTheCodeIsBuilt_GetsItsComponents_WhenTheCodeComes()
    {
        await Build(PatrolAtStart(new SceneValue("Speed", "4")));
        Assert.That(StartPoint().ContainsComponent<UnloadedGameComponents>(), Is.True);
        var code = Code("Game.First", new TestGameField("Speed", typeof(double))).Assembly;

        SceneCode.Replace(_world, [], [code]);

        var patrol = StartPoint().Components.OfType<GameComponent>().Single();
        Assert.That(patrol.GetType().GetProperty("Speed").GetValue(patrol), Is.EqualTo(4.0));
    }

    [Test]
    public async Task AFieldTheNewBuildLacks_IsLeftOut_AndTheComponentIsMade()
    {
        var first = Code("Game.First", new TestGameField("Speed", typeof(double))).Assembly;
        _world.Code.Replace([], [first]);
        await Build(PatrolAtStart(new SceneValue("Speed", "2.5")));
        var second = Code("Game.Second", new TestGameField("Route", typeof(string))).Assembly;

        var problems = SceneCode.Replace(_world, [first], [second]);

        Assert.Multiple(() =>
        {
            Assert.That(problems, Is.Empty);
            Assert.That(StartPoint().Components.OfType<GameComponent>().Single().GetType().Assembly, Is.SameAs(second));
            Assert.That(SceneCapture.Capture(_world.MainScene).GameComponents.Single().Values, Is.Empty);
        });
    }

    [Test]
    public async Task AComponentWhoseConstructorThrows_KeepsItsEntry_AndTheSceneLoads()
    {
        var code = new TestGameCode("Game.Throwing").AddThrowingComponent(PatrolType).Assembly;
        _world.Code.Replace([], [code]);
        var scene = PatrolAtStart(new SceneValue("Speed", "2.5"));
        scene.SpawnPoints.Add(new SceneSpawnPoint { Uid = 2, Name = "Exit" });

        var builder = await Build(scene);

        var back = SceneCapture.Capture(_world.MainScene);
        Assert.Multiple(() =>
        {
            Assert.That(builder.Problems.Select(problem => problem.Type), Is.EqualTo(new[] { PatrolType }));
            Assert.That(builder.Problems[0].Exception, Is.InstanceOf<InvalidOperationException>());
            Assert.That(back.SpawnPoints.Select(point => point.Name), Is.EquivalentTo(new[] { "Start", "Exit" }));
            Assert.That(back.GameComponents.Single().Values, Is.EqualTo(new[] { new SceneValue("Speed", "2.5") }));
        });
    }

    [Test]
    public async Task ASecondComponentOfOneType_IsKept_AndWrittenBack()
    {
        var scene = new SceneFile();
        scene.SpawnPoints.Add(new SceneSpawnPoint { Uid = Start, Name = "Start" });
        var first = new SceneGameComponent { On = Start, Type = typeof(Patrol).FullName };
        first.Values.Add(new SceneValue(nameof(Patrol.Speed), "1"));
        var second = new SceneGameComponent { On = Start, Type = typeof(Patrol).FullName };
        second.Values.Add(new SceneValue(nameof(Patrol.Speed), "2"));
        scene.GameComponents.Add(first);
        scene.GameComponents.Add(second);

        await Build(scene);

        var speeds = SceneCapture.Capture(_world.MainScene).GameComponents
            .Select(entry => entry.Values.Single(value => value.Name == nameof(Patrol.Speed)).Text);
        Assert.That(speeds, Is.EquivalentTo(new[] { "1", "2" }));
    }

    // A component renamed in code, the new class saying the name it had: the entry under the old name becomes it, with
    // its values, and is written back under the new name.
    [Test]
    public async Task AComponentRenamedWithItsFormerName_TakesTheEntry_AndIsWrittenUnderItsNewName()
    {
        var first = Code("Game.First", new TestGameField("Speed", typeof(double))).Assembly;
        _world.Code.Replace([], [first]);
        await Build(PatrolAtStart(new SceneValue("Speed", "2.5")));
        var second = new TestGameCode("Game.Second")
            .AddMovedComponent("Game.AI.Guard", PatrolType, new TestGameField("Speed", typeof(double))).Assembly;

        SceneCode.Replace(_world, [first], [second]);

        var guard = StartPoint().Components.OfType<GameComponent>().Single();
        var back = SceneCapture.Capture(_world.MainScene).GameComponents.Single();
        Assert.Multiple(() =>
        {
            Assert.That(guard.GetType().FullName, Is.EqualTo("Game.AI.Guard"));
            Assert.That(guard.GetType().GetProperty("Speed").GetValue(guard), Is.EqualTo(2.5));
            Assert.That(back.Type, Is.EqualTo("Game.AI.Guard"));
        });
    }

    // Two classes saying they had one name: the values go to neither - a guess would put them on the wrong one.
    [Test]
    public void TwoClassesClaimingOneFormerName_TakeNothing()
    {
        var code = new TestGameCode("Game.Twice")
            .AddMovedComponent("Game.Guard", PatrolType)
            .AddMovedComponent("Game.Sentry", PatrolType).Assembly;

        _world.Code.Replace([], [code]);

        Assert.That(_world.Code.FindType<GameComponent>(PatrolType), Is.Null);
    }

    // ...and a class of that name now wins over one that only had it.
    [Test]
    public void AClassOfTheNameNow_WinsOverOneThatHadIt()
    {
        var code = new TestGameCode("Game.Both").AddComponent(PatrolType).AddMovedComponent("Game.Guard", PatrolType).Assembly;

        _world.Code.Replace([], [code]);

        Assert.That(_world.Code.FindType<GameComponent>(PatrolType).FullName, Is.EqualTo(PatrolType));
    }

    // An entry loaded as another component - the class renamed without saying so: the fields that class has, by name;
    // the entry is no longer kept.
    [Test]
    public async Task AnEntryLoadedAsAnotherComponent_TakesItsFields_AndIsNoLongerKept()
    {
        await Build(PatrolAtStart(new SceneValue("Speed", "3"), new SceneValue("Route", "A")));
        var code = new TestGameCode("Game.Renamed").AddComponent("Game.Guard", new TestGameField("Speed", typeof(double))).Assembly;
        _world.Code.Replace([], [code]);
        var entry = StartPoint().GetComponent<UnloadedGameComponents>().Entries.Single();

        var problem = SceneCode.Load(StartPoint(), entry, _world.Code.FindType<GameComponent>("Game.Guard"));

        var guard = StartPoint().Components.OfType<GameComponent>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(problem, Is.Null);
            Assert.That(guard.GetType().GetProperty("Speed").GetValue(guard), Is.EqualTo(3.0));
            Assert.That(StartPoint().ContainsComponent<UnloadedGameComponents>(), Is.False);
        });
    }

    // ...and one whose code throws keeps its entry.
    [Test]
    public async Task AnEntryLoadedAsAComponentThatThrows_StaysAnEntry()
    {
        await Build(PatrolAtStart(new SceneValue("Speed", "3")));
        var code = new TestGameCode("Game.Failing").AddThrowingComponent("Game.Guard").Assembly;
        _world.Code.Replace([], [code]);
        var entry = StartPoint().GetComponent<UnloadedGameComponents>().Entries.Single();

        var problem = SceneCode.Load(StartPoint(), entry, _world.Code.FindType<GameComponent>("Game.Guard"));

        Assert.Multiple(() =>
        {
            Assert.That(problem?.Exception, Is.InstanceOf<InvalidOperationException>());
            Assert.That(StartPoint().GetComponent<UnloadedGameComponents>().Entries, Is.EqualTo(new[] { entry }));
            Assert.That(StartPoint().Components.OfType<GameComponent>(), Is.Empty);
        });
    }

    // A component whose own code throws as it is put on the entity - its Initialize - keeps its entry; the components
    // after it on the entity, and the entities after it, still move to the new code.
    [Test]
    public async Task AComponentThatThrowsAsItIsAdded_KeepsItsEntry_AndTheRestMove()
    {
        var scene = PatrolAtStart(new SceneValue("Speed", "2.5"));
        scene.GameComponents.Add(new SceneGameComponent { On = Start, Type = "Game.Spinner" });
        scene.SpawnPoints.Add(new SceneSpawnPoint { Uid = 2, Name = "Exit" });
        scene.GameComponents.Add(new SceneGameComponent { On = 2, Type = "Game.Spinner" });
        await Build(scene);
        var code = new TestGameCode("Game.Failing")
            .AddComponentThrowingOnInitialize(PatrolType, new TestGameField("Speed", typeof(double)))
            .AddComponent("Game.Spinner").Assembly;

        var problems = SceneCode.Replace(_world, [], [code]);

        var exit = _world.MainScene.RootEntities.Single(entity => entity.Uid == 2);
        var back = SceneCapture.Capture(_world.MainScene).GameComponents;
        Assert.Multiple(() =>
        {
            Assert.That(problems.Select(problem => problem.Type), Is.EqualTo(new[] { PatrolType }));
            Assert.That(problems[0].Exception, Is.InstanceOf<InvalidOperationException>());
            Assert.That(StartPoint().Components.OfType<GameComponent>().Select(each => each.GetType().FullName),
                Is.EqualTo(new[] { "Game.Spinner" }));
            Assert.That(exit.Components.OfType<GameComponent>().Select(each => each.GetType().FullName),
                Is.EqualTo(new[] { "Game.Spinner" }));
            Assert.That(back.Single(entry => entry.Type == PatrolType).Values, Is.EqualTo(new[] { new SceneValue("Speed", "2.5") }));
        });
    }

    // A new component put on an entity: one whose own code throws as it is added leaves the entity as it was.
    [Test]
    public async Task AComponentAddedThatThrows_LeavesTheEntityAsItWas()
    {
        await Build(PatrolAtStart());
        var code = new TestGameCode("Game.Adding").AddComponentThrowingOnInitialize("Game.Guard").Assembly;
        _world.Code.Replace([], [code]);

        var problem = SceneCode.Add(StartPoint(), _world.Code.FindType<GameComponent>("Game.Guard"));

        Assert.Multiple(() =>
        {
            Assert.That(problem?.Exception, Is.InstanceOf<InvalidOperationException>());
            Assert.That(StartPoint().Components.OfType<GameComponent>(), Is.Empty);
        });
    }

    // A copy of an entity keeps the components whose code is not loaded, written on the copy.
    [Test]
    public async Task ACopiedEntity_KeepsItsComponentsNotLoaded()
    {
        await Build(PatrolAtStart(new SceneValue("Speed", "2.5")));

        var copy = (UnloadedGameComponents)StartPoint().GetComponent<UnloadedGameComponents>().Clone();

        Assert.That(copy.Entries.Single().Values, Is.EqualTo(new[] { new SceneValue("Speed", "2.5") }));
    }

    [Test]
    public void TheGivenCode_IsListed_TheApplicationsIsNot()
    {
        var code = Code("Game.First").Assembly;

        _world.Code.Replace([], [code]);

        Assert.Multiple(() =>
        {
            Assert.That(_world.Code.GetGivenTypes<GameComponent>().Select(type => type.FullName), Is.EqualTo(new[] { PatrolType }));
            Assert.That(_world.Code.FindType<GameComponent>(typeof(Patrol).FullName), Is.EqualTo(typeof(Patrol)),
                "the application's own types are found");
        });
    }
}
