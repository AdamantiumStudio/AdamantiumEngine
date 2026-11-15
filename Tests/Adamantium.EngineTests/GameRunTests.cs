using System;
using System.Linq;
using Adamantium.Core;
using Adamantium.ECS;
using NUnit.Framework;

namespace Adamantium.EngineTests;

/// <summary>The game playing a scene in the editor: the services the game's code brings, made for that scene alone when it
/// starts and let go when it stops.</summary>
[TestFixture]
public class GameRunTests
{
    private TestUniverse _universe;
    private EntityWorld _world;

    [SetUp]
    public void MakeWorld()
    {
        _universe = new TestUniverse();
        _world = _universe.EntityWorld;
        _world.Code.Replace([], [typeof(GameRunTests).Assembly]);
    }

    [Test]
    public void AGame_MakesOneServiceOfEachKindTheCodeBrings_ForItsScene_AndStartsThem()
    {
        var scene = _world.MainScene;

        var run = GameRun.Start(scene);
        NextFrame();

        var pushing = run.Services.OfType<PushingService>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(run.Services.Select(service => service.GetType()),
                Is.EquivalentTo(new[] { typeof(PushingService), typeof(ThrowingService) }));
            Assert.That(pushing.Scene, Is.SameAs(scene));
            Assert.That(pushing.Started, Is.True);
            Assert.That(_world.GetServices<GameRun>(), Is.EqualTo(new[] { run }));
        });
    }

    // The editor plays a copy of the scene beside the one edited: what the game moves is the copy's.
    [Test]
    public void AFrame_MovesTheComponentsOfThePlayedScene_AndNoOther()
    {
        var played = _world.MainScene;
        var edited = _world.CreateScene("Edited");
        var inPlay = Placed(played, 2);
        var inEdit = Placed(edited, 2);
        var run = GameRun.Start(played);

        run.Update(new AppTime { FrameTime = 0.5 });
        run.Update(new AppTime { FrameTime = 0.5 });

        Assert.Multiple(() =>
        {
            Assert.That(inPlay.Transform.Position.X, Is.EqualTo(2).Within(1e-9));
            Assert.That(inEdit.Transform.Position.X, Is.Zero);
            Assert.That(run.PlayTime, Is.EqualTo(TimeSpan.FromSeconds(1)));
        });
    }

    [Test]
    public void AServiceThatThrows_IsTakenOutOfTheGame_AndTheOthersPlayOn()
    {
        var entity = Placed(_world.MainScene, 1);
        var run = GameRun.Start(_world.MainScene);

        run.Update(new AppTime { FrameTime = 1 });
        run.Update(new AppTime { FrameTime = 1 });

        Assert.Multiple(() =>
        {
            Assert.That(run.Services.OfType<ThrowingService>(), Is.Empty);
            Assert.That(entity.Transform.Position.X, Is.EqualTo(2).Within(1e-9));
        });
    }

    [Test]
    public void AStoppedGame_TellsItsServices_AndLeavesTheWorld()
    {
        var run = GameRun.Start(_world.MainScene);
        NextFrame();
        var pushing = run.Services.OfType<PushingService>().Single();

        run.Stop();
        NextFrame();

        Assert.Multiple(() =>
        {
            Assert.That(pushing.Stopped, Is.True);
            Assert.That(run.Services, Is.Empty);
            Assert.That(_world.GetServices<GameRun>(), Is.Empty);
        });
    }

    [Test]
    public void APausedGame_IsNotRunByTheWorld()
    {
        var run = GameRun.Start(_world.MainScene);

        run.IsPaused = true;

        Assert.That(run.IsActive, Is.False);
    }

    private Entity Placed(Scene scene, double speed)
    {
        var entity = new Entity(null, "Pushed");
        entity.AddComponent(new Pushed { Speed = speed });
        _world.EntityManager.AddEntity(entity, scene);
        NextFrame();
        return entity;
    }

    private void NextFrame()
    {
        _world.ForceUpdate();
        _universe.Settle();
    }
}
