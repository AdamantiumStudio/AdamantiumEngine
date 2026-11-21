using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Adamantium.DarkMatter.Simulation;

/// <summary>
/// The core side of a <see cref="PhysicsSimulation"/>: the world, the commands done on it, its steps, and what each run
/// leaves for the caller. On the caller's thread when it steps by hand, on the simulation's own thread when that runs;
/// never on both at once.
/// </summary>
internal sealed partial class SimulationStepper
{
    private const int NoSlot = -1;

    private readonly PhysicsWorld _world;
    // Woken, a body's time at rest starts again and grows by a step a step: it sleeps again within the step only if
    // TimeToSleep is no longer than one, and only then must a sleeper be read again.
    private readonly bool _skipsSleepers;
    private SimulationResult _result;
    private long _run;

    public SimulationStepper(PhysicsSettings settings)
    {
        _world = new PhysicsWorld(settings);
        _skipsSleepers = _world.Settings.TimeToSleep > _world.StepSeconds;
    }

    /// <summary>The world, for queries while nothing else steps it.</summary>
    public PhysicsWorld World => _world;

    public long StepNumber { get; private set; }

    /// <summary>Does <paramref name="commands"/> in order, takes <paramref name="steps"/> steps, and fills
    /// <paramref name="result"/> with what they left; <paramref name="batch"/> is the caller's count of the last batch
    /// of commands among them.</summary>
    public void Run(List<SimulationCommand> commands, int steps, long batch, SimulationResult result)
    {
        _run++;
        _result = result;
        result.Clear();
        ForgetRefusalsOfLastRun();
        foreach (ref var command in CollectionsMarshal.AsSpan(commands))
        {
            Do(command);
            TakeContactEvents();
        }

        for (var step = 0; step < steps; step++)
        {
            PushBodies();
            _world.Step();
            TakeContactEvents();
            StepNumber++;
            // A move to a pose is over one step: after the last, it is read first.
            if (step < steps - 1)
                StopKinematicMoves();
        }

        ReadBack();
        StopKinematicMoves();
        result.Steps = steps;
        result.StepNumber = StepNumber;
        result.Statistics = _world.LastStep;
        result.CommandBatch = batch;
        result.SteppedAt = Stopwatch.GetTimestamp();
        _result = null;
    }

    private void Do(in SimulationCommand command)
    {
        switch (command.Kind)
        {
            case SimulationCommandKind.SetGravity:
                _world.Gravity = command.Vector;
                break;
            case SimulationCommandKind.SetLayersCollide:
                _world.SetLayersCollide(command.Layer, command.OtherLayer, command.Collides);
                break;
            case SimulationCommandKind.AddJoint:
            case SimulationCommandKind.RemoveJoint:
            case SimulationCommandKind.SetLimits:
            case SimulationCommandKind.SetMotor:
                DoJointCommand(command);
                break;
            default:
                DoBodyCommand(command);
                break;
        }
    }

    private void Tell(in SimulationNews news) => _result.News.Add(news);

    // The world's touches told by the caller's ids, while the core still knows whose bodies they were: after each
    // command, so a body taken out is told by its own id.
    private void TakeContactEvents()
    {
        var told = _world.ContactEvents;
        if (told.IsEmpty)
            return;

        foreach (ref readonly var touch in told)
        {
            _result.Contacts.Add(new ContactEventArgs(IdOf(touch.First), IdOf(touch.Second), touch));
        }

        _world.ForgetContactEvents();
    }

    private void ForgetRefusalsOfLastRun()
    {
        foreach (var slot in CollectionsMarshal.AsSpan(_refusedBodies))
        {
            _bodies[slot].IsRefusedInRun = false;
        }

        _refusedBodies.Clear();
        foreach (var slot in CollectionsMarshal.AsSpan(_refusedJoints))
        {
            _joints[slot].IsRefusedInRun = false;
        }

        _refusedJoints.Clear();
    }

    private static void RemoveSwapping(List<int> slots, int index)
    {
        slots[index] = slots[^1];
        slots.RemoveAt(slots.Count - 1);
    }
}
