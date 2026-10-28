using System;
using System.Diagnostics;
using System.Threading;
using Adamantium.Core;
using Adamantium.Core.DependencyInjection;
using Adamantium.ECS;
using Adamantium.Mathematics;
using Adamantium.Multiverse;
using NUnit.Framework;

namespace Adamantium.EngineTests;

/// <summary>The watcher is the one place that wakes a universe drawing on demand: once for any number of changes between
/// its frames, owing as many frames and as long a time as asked.</summary>
[TestFixture]
public class UniverseWatcherTests
{
    private int wakes;
    private UniverseWatcher watcher;
    private EntityWorld world;
    private Entity model;

    [SetUp]
    public void SetUp()
    {
        wakes = 0;
        watcher = new UniverseWatcher(Wake);
        world = new EntityWorld(new AdamantiumDependencyContainer(), new Satellites());
        model = new Entity(null, "Model");
        world.EntityManager.AddEntity(model);
        world.ForceUpdate();
        watcher.Watch(world);
        watcher.IsWatching = true;
    }

    private void Wake()
    {
        wakes++;
    }

    [Test]
    public void ChangesBetweenFrames_WakeTheUniverseOnce_AndOweOneFrame()
    {
        model.Name = "One";
        model.Name = "Two";
        model.Transform.Position = new Vector3<double>(1, 0, 0);

        Assert.Multiple(() =>
        {
            Assert.That(wakes, Is.EqualTo(1));
            Assert.That(watcher.Owes, Is.True);
            Assert.That(watcher.TakeFrame(), Is.True);
            Assert.That(watcher.Owes, Is.False);
            Assert.That(watcher.TakeFrame(), Is.False);
        });
    }

    [Test]
    public void OnceTheUniverseTookItsMessages_TheNextChangeWakesItAgain()
    {
        model.Name = "One";
        watcher.Woken();
        model.Name = "Two";

        Assert.That(wakes, Is.EqualTo(2));
    }

    [Test]
    public void FramesAskedFor_AreOwedOneByOne_TheMostAskedWinning()
    {
        watcher.Watch(3);
        watcher.Watch(2);

        Assert.That(new[] { watcher.TakeFrame(), watcher.TakeFrame(), watcher.TakeFrame(), watcher.TakeFrame() },
            Is.EqualTo(new[] { true, true, true, false }));
    }

    [Test]
    public void TimeAskedFor_IsOwedUntilItPasses()
    {
        var asked = Stopwatch.StartNew();
        watcher.Watch(TimeSpan.FromMilliseconds(40));

        Assert.That(watcher.Owes, Is.True);
        Assert.That(SpinWait.SpinUntil(() => !watcher.Owes, 2000), Is.True);
        Assert.That(asked.Elapsed, Is.GreaterThanOrEqualTo(TimeSpan.FromMilliseconds(40)));
    }

    [Test]
    public void AReleasedWorld_WakesNothing_EvenIfWatchedTwice()
    {
        watcher.Watch(world);
        watcher.ReleaseWatch(world);

        model.Name = "Unwatched";

        Assert.Multiple(() =>
        {
            Assert.That(wakes, Is.Zero);
            Assert.That(watcher.Owes, Is.False);
        });
    }

    [Test]
    public void WhileTheUniverseDrawsEveryFrame_TheWatcherHearsNothingAndOwesNothing_UntilItWatchesAgain()
    {
        watcher.Watch(3);
        watcher.IsWatching = false;

        model.Name = "Unheard";
        watcher.Watch(5);
        watcher.Watch(TimeSpan.FromSeconds(10));
        var idle = (wakes, watcher.Owes);
        watcher.Woken();
        watcher.IsWatching = true;
        model.Name = "Heard";

        Assert.Multiple(() =>
        {
            Assert.That(idle, Is.EqualTo((1, false)), "the one wake is from before; nothing after it");
            Assert.That(wakes, Is.EqualTo(2), "watching again listens to the world it was given");
        });
    }

    [Test]
    public void NoFramesAskedFor_IsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => watcher.Watch(0));
    }
}
