using System.Diagnostics;
using System.Threading;
using Adamantium.DarkMatter.Shapes;
using NUnit.Framework;

namespace Adamantium.DarkMatter.Tests;

/// <summary>Work done at every step on the thread that steps: a spring of the caller's own, a hover against gravity.
/// </summary>
[TestFixture]
public class StepHookTests
{
    private static readonly BoxCollisionShape Crate = new(0.5f, 0.5f, 0.5f);

    // A crate two meters off, pulled back by a damped spring of its own each step: it settles at the anchor.
    [Test]
    public void AStepHook_ActsAtEveryStep()
    {
        using var simulation = new PhysicsSimulation(new PhysicsSettings { Gravity = Vector3F.Zero });
        var crate = simulation.AddBody(new BodySettings { Shape = Crate, Position = new WorldPosition(2, 0, 0) });
        var spring = new SpringHook(crate, WorldPosition.Zero, stiffness: 40000, damping: 12000);
        simulation.AddStepHook(spring);

        for (var step = 0; step < 3 * simulation.Settings.StepRate; step++)
            simulation.Step();

        Assert.Multiple(() =>
        {
            Assert.That(simulation.PoseOf(crate).Position.X, Is.EqualTo(0).Within(0.01));
            Assert.That(simulation.LinearVelocityOf(crate).Length(), Is.LessThan(0.01f));
            Assert.That(spring.Steps, Is.EqualTo(3 * simulation.Settings.StepRate));
        });
    }

    [Test]
    public void AStepHookRemoved_ActsNoMore()
    {
        using var simulation = new PhysicsSimulation(new PhysicsSettings { Gravity = Vector3F.Zero });
        var crate = simulation.AddBody(new BodySettings { Shape = Crate, Position = new WorldPosition(2, 0, 0) });
        var spring = new SpringHook(crate, WorldPosition.Zero, stiffness: 4000, damping: 2000);
        simulation.AddStepHook(spring);
        simulation.Step();
        var steps = spring.Steps;

        simulation.RemoveStepHook(spring);
        simulation.SetLinearVelocity(crate, new Vector3F(1, 0, 0));
        var from = simulation.PoseOf(crate).Position.X;
        for (var step = 0; step < simulation.Settings.StepRate; step++)
            simulation.Step();

        Assert.Multiple(() =>
        {
            Assert.That(spring.Steps, Is.EqualTo(steps));
            Assert.That(simulation.PoseOf(crate).Position.X - from, Is.EqualTo(1).Within(1e-3), "drifts a meter a second");
        });
    }

    // Held up against gravity by a hook on the simulation's own thread, a crate hovers.
    [Test]
    public void AStepHookOnTheOwnThread_HoldsACrateUp()
    {
        using var simulation = new PhysicsSimulation();
        var crate = simulation.AddBody(new BodySettings { Shape = Crate, Mass = 10, CanSleep = false });
        var hover = new HoverHook(crate, simulation.Gravity);
        simulation.AddStepHook(hover);
        simulation.Start();
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed.TotalSeconds < 0.5)
        {
            simulation.Update();
            Thread.Sleep(5);
        }

        simulation.Stop();

        Assert.Multiple(() =>
        {
            Assert.That(hover.Steps, Is.GreaterThan(10));
            Assert.That(simulation.PoseOf(crate).Position.Y, Is.EqualTo(0).Within(1e-3), "it did not fall");
        });
    }

    // A hook pushing a crate made kinematic on the way: the push is passed by, as the simulation's own, and the steps
    // go on - on the simulation's own thread a throw there would stop them.
    [Test]
    public void AStepHooksPush_PassesByABodyThatCannotTakeIt()
    {
        using var simulation = new PhysicsSimulation();
        var crate = simulation.AddBody(new BodySettings { Shape = Crate, Mass = 10 });
        simulation.AddStepHook(new HoverHook(crate, simulation.Gravity));
        simulation.Step();

        simulation.SetKind(crate, BodyKind.Kinematic);
        simulation.SetLinearVelocity(crate, new Vector3F(1, 0, 0));
        for (var step = 0; step < 10; step++)
            simulation.Step();

        Assert.Multiple(() =>
        {
            Assert.That(simulation.KindOf(crate), Is.EqualTo(BodyKind.Kinematic));
            Assert.That(simulation.LinearVelocityOf(crate), Is.EqualTo(new Vector3F(1, 0, 0)), "pushed by nothing");
        });
    }

    [Test]
    public void ABodyTakenOut_IsNotContainedInTheNextStep()
    {
        using var simulation = new PhysicsSimulation();
        var crate = simulation.AddBody(new BodySettings { Shape = Crate });
        var watcher = new ContainsHook(crate);
        simulation.AddStepHook(watcher);
        simulation.Step();
        var before = watcher.LastContained;

        simulation.RemoveBody(crate);
        simulation.Step();

        Assert.That((bool[])[before, watcher.LastContained], Is.EqualTo((bool[])[true, false]));
    }
}
