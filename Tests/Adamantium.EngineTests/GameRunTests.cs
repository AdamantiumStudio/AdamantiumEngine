using System;
using System.Linq;
using Adamantium.Core;
using Adamantium.ECS;
using Adamantium.Engine.Scenes;
using NUnit.Framework;

namespace Adamantium.EngineTests;

/// <summary>The game's services: made with the code that brings them, asleep while a scene is edited, woken for the
/// scene a game plays, and run by the world as its own.</summary>
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
        SceneCode.Replace(_world, [], [typeof(GameRunTests).Assembly]);
        NextFrame();
    }

    [Test]
    public void TheCode_BringsOneServiceOfEachKind_AsleepUntilAGamePlays()
    {
        var entity = Placed(_world.MainScene, 1);

        Frame(1);

        Assert.Multiple(() =>
        {
            Assert.That(_world.GetServices<IGameService>().Select(service => service.GetType()),
                Is.EquivalentTo(new[] { typeof(PushingService), typeof(ThrowingService), typeof(OwnGameService) }));
            Assert.That(_world.GetServices<IGameService>().Any(service => service.IsActive), Is.False);
            Assert.That(entity.Transform.Position.X, Is.Zero);
        });
    }

    [Test]
    public void TheServicesOfLeavingCode_LeaveTheWorld()
    {
        var pushing = _world.GetService<PushingService>();

        SceneCode.Replace(_world, [typeof(GameRunTests).Assembly], []);
        NextFrame();

        Assert.Multiple(() =>
        {
            Assert.That(_world.GetServices<IGameService>(), Is.Empty);
            Assert.That(pushing.IsActive, Is.False);
        });
    }

    [Test]
    public void AGame_GivesTheServicesItsScene_StartsAndWakesThem()
    {
        var scene = _world.MainScene;

        var run = GameRun.Start(scene);

        var pushing = _world.GetService<PushingService>();
        Assert.Multiple(() =>
        {
            Assert.That(run.Services, Is.EquivalentTo(_world.GetServices<IGameService>()));
            Assert.That(pushing.Scene, Is.SameAs(scene));
            Assert.That(pushing.Started, Is.True);
            Assert.That(pushing.IsActive, Is.True);
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
        GameRun.Start(played);

        Frame(0.5);
        Frame(0.5);

        Assert.Multiple(() =>
        {
            Assert.That(inPlay.Transform.Position.X, Is.EqualTo(2).Within(1e-9));
            Assert.That(inEdit.Transform.Position.X, Is.Zero);
        });
    }

    [Test]
    public void AServiceThatThrows_IsPutToSleep_AndTheOthersPlayOn_EvenAfterAPause()
    {
        var entity = Placed(_world.MainScene, 1);
        var run = GameRun.Start(_world.MainScene);
        var throwing = _world.GetService<ThrowingService>();

        Frame(1);
        Frame(1);
        run.IsPaused = true;
        run.IsPaused = false;
        Frame(1);

        Assert.Multiple(() =>
        {
            Assert.That(throwing.Updates, Is.EqualTo(1));
            Assert.That(throwing.IsActive, Is.False);
            Assert.That(entity.Transform.Position.X, Is.EqualTo(3).Within(1e-9));
        });
    }

    // An engine service that throws fails the frame, as before, and the universe runs the next one: it is not put to
    // sleep, or one bad frame would take the camera or the tools away for good.
    [Test]
    public void AnEngineServiceThatThrows_FailsTheFrame_AndStaysAwake()
    {
        var failing = new FailingEngineService(_world);
        _world.AddService(failing);
        NextFrame();

        Assert.Multiple(() =>
        {
            Assert.That(() => Frame(1), Throws.InvalidOperationException);
            Assert.That(failing.IsActive, Is.True);
        });
    }

    [Test]
    public void APausedGame_StandsStill_AndGoesOnWhenLetGo()
    {
        var entity = Placed(_world.MainScene, 1);
        var run = GameRun.Start(_world.MainScene);

        run.IsPaused = true;
        Frame(1);
        var paused = entity.Transform.Position.X;
        run.IsPaused = false;
        Frame(1);

        Assert.Multiple(() =>
        {
            Assert.That(paused, Is.Zero);
            Assert.That(entity.Transform.Position.X, Is.EqualTo(1).Within(1e-9));
        });
    }

    [Test]
    public void AStoppedGame_TellsItsServices_PutsThemToSleep_AndTakesItsScene()
    {
        var entity = Placed(_world.MainScene, 1);
        var run = GameRun.Start(_world.MainScene);
        var pushing = _world.GetService<PushingService>();

        run.Stop();
        Frame(1);

        Assert.Multiple(() =>
        {
            Assert.That(pushing.Stopped, Is.True);
            Assert.That(pushing.IsActive, Is.False);
            Assert.That(pushing.Scene, Is.Null);
            Assert.That(run.Services, Is.Empty);
            Assert.That(entity.Transform.Position.X, Is.Zero);
            Assert.That(_world.GetServices<IGameService>(), Has.Length.EqualTo(3));
        });
    }

    [Test]
    public void CodeReplacedTwiceBetweenFrames_LeavesOneServiceOfEachKind_TheNewest()
    {
        var first = _world.GetService<PushingService>();

        SceneCode.Replace(_world, [typeof(GameRunTests).Assembly], []);
        SceneCode.Replace(_world, [], [typeof(GameRunTests).Assembly]);
        var second = _world.GetService<PushingService>();
        SceneCode.Replace(_world, [typeof(GameRunTests).Assembly], []);
        SceneCode.Replace(_world, [], [typeof(GameRunTests).Assembly]);
        NextFrame();

        var services = _world.GetServices<IGameService>();
        Assert.Multiple(() =>
        {
            Assert.That(services.Select(service => service.GetType()),
                Is.EquivalentTo(new[] { typeof(PushingService), typeof(ThrowingService), typeof(OwnGameService) }));
            Assert.That(services, Does.Not.Contain(first));
            Assert.That(services, Does.Not.Contain(second));
        });
    }

    // A build lands and Play is pressed before the next frame: the game plays the new code's services.
    [Test]
    public void AGameStartedRightAfterTheCodeIsReplaced_PlaysTheNewServices()
    {
        var old = _world.GetService<PushingService>();
        SceneCode.Replace(_world, [typeof(GameRunTests).Assembly], [typeof(GameRunTests).Assembly]);

        var run = GameRun.Start(_world.MainScene);

        Assert.Multiple(() =>
        {
            Assert.That(run.Services, Does.Not.Contain(old));
            Assert.That(run.Services.OfType<PushingService>().Single().Started, Is.True);
            Assert.That(old.Started, Is.False);
        });
    }

    [Test]
    public void AServiceThatFailsToStart_SleepsThroughTheGame_AndIsToldWhenItStops()
    {
        var throwing = _world.GetService<ThrowingService>();
        throwing.FailsToStart = true;

        var run = GameRun.Start(_world.MainScene);
        run.IsPaused = true;
        run.IsPaused = false;
        Frame(1);
        var sceneWhilePlaying = throwing.Scene;
        run.Stop();

        Assert.Multiple(() =>
        {
            Assert.That(throwing.Updates, Is.Zero);
            Assert.That(throwing.IsActive, Is.False);
            Assert.That(sceneWhilePlaying, Is.SameAs(_world.MainScene));
            Assert.That(throwing.Stopped, Is.True);
            Assert.That(throwing.Scene, Is.Null);
        });
    }

    [Test]
    public void AGameStoppedWhilePaused_LeavesTheNextOneAwake()
    {
        var entity = Placed(_world.MainScene, 1);
        var first = GameRun.Start(_world.MainScene);
        first.IsPaused = true;
        first.Stop();

        GameRun.Start(_world.MainScene);
        Frame(1);

        Assert.That(entity.Transform.Position.X, Is.EqualTo(1).Within(1e-9));
    }

    // The engine works with game services through IGameService: one written without GameService is made, started,
    // woken, paused and stopped as the others.
    [Test]
    public void AGameServiceOnTheInterfaceAlone_IsPlayedAsAnyOther()
    {
        var own = _world.GetService<OwnGameService>();
        var asleepBefore = !own.IsActive;

        var run = GameRun.Start(_world.MainScene);
        var started = own.Scene;
        var awake = own.IsActive;
        run.IsPaused = true;
        var asleepOnPause = !own.IsActive;
        run.IsPaused = false;
        run.Stop();

        Assert.Multiple(() =>
        {
            Assert.That(asleepBefore, Is.True);
            Assert.That(started, Is.SameAs(_world.MainScene));
            Assert.That(awake, Is.True);
            Assert.That(asleepOnPause, Is.True);
            Assert.That(own.IsActive, Is.False);
            Assert.That(own.Scene, Is.Null);
            Assert.That(own.Starts, Is.EqualTo(1));
            Assert.That(own.Stops, Is.EqualTo(1));
        });
    }

    [Test]
    public void OneGameAtATime_PlaysInAWorld()
    {
        var first = GameRun.Start(_world.MainScene);

        Assert.That(() => GameRun.Start(_world.CreateScene("Other")), Throws.InvalidOperationException);

        first.Stop();
        Assert.That(() => GameRun.Start(_world.MainScene).Stop(), Throws.Nothing);
    }

    // Paused, the services sleep, yet what an edit changes shows at once - without a frame or time going on.
    [Test]
    public void AnEditWhilePaused_IsHeardByTheServices_WithNoFrame()
    {
        var entity = Placed(_world.MainScene, 1);
        var run = GameRun.Start(_world.MainScene);
        run.IsPaused = true;
        var pushed = entity.GetComponent<Pushed>();
        pushed.Speed = 4;

        run.TellComponentEdited(pushed);

        var pushing = _world.GetService<PushingService>();
        Assert.Multiple(() =>
        {
            Assert.That(pushing.EditsHeard, Is.EqualTo(new[] { pushed }));
            Assert.That(entity.Transform.Position.Y, Is.EqualTo(4));
            Assert.That(entity.Transform.Position.X, Is.Zero);
            Assert.That(pushing.IsActive, Is.False);
        });
    }

    [Test]
    public void AnEditOfAnotherScene_OrAfterTheGame_IsNotHeard()
    {
        var edited = Placed(_world.CreateScene("Edited"), 1);
        var played = Placed(_world.MainScene, 1);
        var run = GameRun.Start(_world.MainScene);

        run.TellComponentEdited(edited.GetComponent<Pushed>());
        run.Stop();
        run.TellComponentEdited(played.GetComponent<Pushed>());

        Assert.That(_world.GetService<PushingService>().EditsHeard, Is.Empty);
    }

    [Test]
    public void AServiceThatThrowsOnAnEdit_SleepsForTheGame_AndTheOthersHearIt()
    {
        var entity = Placed(_world.MainScene, 1);
        var run = GameRun.Start(_world.MainScene);
        var throwing = _world.GetService<ThrowingService>();
        throwing.FailsOnEdit = true;
        var pushed = entity.GetComponent<Pushed>();

        run.TellComponentEdited(pushed);
        run.IsPaused = true;
        run.IsPaused = false;
        run.TellComponentEdited(pushed);

        Assert.Multiple(() =>
        {
            Assert.That(throwing.EditsHeard, Is.EqualTo(1));
            Assert.That(throwing.IsActive, Is.False);
            Assert.That(_world.GetService<PushingService>().EditsHeard, Has.Count.EqualTo(2));
        });
    }

    // Asleep for throwing on a frame, a service is not woken by an edit, nor told of it, paused or not.
    [Test]
    public void AServiceAsleepForThrowing_DoesNotHearEdits()
    {
        var entity = Placed(_world.MainScene, 1);
        var run = GameRun.Start(_world.MainScene);
        var throwing = _world.GetService<ThrowingService>();
        var pushed = entity.GetComponent<Pushed>();
        Frame(1);

        run.TellComponentEdited(pushed);
        run.IsPaused = true;
        run.TellComponentEdited(pushed);

        Assert.Multiple(() =>
        {
            Assert.That(throwing.EditsHeard, Is.Zero);
            Assert.That(throwing.IsActive, Is.False);
            Assert.That(_world.GetService<PushingService>().EditsHeard, Has.Count.EqualTo(2));
        });
    }

    private void Frame(double seconds)
    {
        _world.ServiceManager.Update(new AppTime { FrameTime = seconds });
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
