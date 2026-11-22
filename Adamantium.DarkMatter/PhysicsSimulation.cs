using System;
using System.Collections.Generic;
using System.Diagnostics;
using Adamantium.DarkMatter.Simulation;

namespace Adamantium.DarkMatter;

/// <summary>
/// The physics as a caller uses it: bodies and joints by id, every action a method of the simulation. An action is
/// checked when asked and done at the start of the next step, in the order asked; what the caller reads is what the
/// last step read left, the same for everyone until the next. Stepped by hand - <see cref="Step"/>,
/// <see cref="Advance"/> - or on a thread of its own once <see cref="Start"/>ed, read then at each
/// <see cref="Update"/>. Called from one thread.
/// </summary>
public sealed partial class PhysicsSimulation : IDisposable
{
    private const int NoSlot = -1;
    private const int MaxStepsPerAdvance = 8;
    private const int MaxUnreadResults = 32;
    // The batches of commands going round to its thread, made at once: however far the thread lags, no more are made.
    private const int MaxBatchesHanded = 4;
    // The simulation's own thread's stack: the core takes what it needs of it, the caller's stack is not its.
    private const int ThreadStackSize = 1024 * 1024;

    private readonly SimulationStepper _stepper;
    private readonly StepClock _clock;
    private readonly List<SimulationCommand> _commands = [];
    // Run on the caller's thread when it steps by hand.
    private readonly SimulationResult _handResult = new();
    private Vector3F _gravity;
    // Advanced by time, it draws between its last two steps by the time left over; stepped one by one, at the last.
    private bool _blendsByClock;
    private bool _isDisposed;

    /// <summary>A simulation stepped as <paramref name="settings"/> say, or with the defaults.</summary>
    public PhysicsSimulation(PhysicsSettings settings = null)
    {
        _stepper = new SimulationStepper(settings);
        Settings = _stepper.World.Settings;
        _gravity = _stepper.World.Gravity;
        _clock = new StepClock(Settings.StepRate, MaxStepsPerAdvance);
        Layers = new PhysicsLayers(this);
        for (var batch = 0; batch < MaxBatchesHanded; batch++)
            _spareBatches.Writer.TryWrite(new CommandBatch());
    }

    /// <summary>How the simulation steps and solves, read once when it is made.</summary>
    public PhysicsSettings Settings { get; }

    /// <summary>Its 64 layers by name, and which of them collide.</summary>
    public PhysicsLayers Layers { get; }

    /// <summary>The length of one step, in seconds.</summary>
    public float StepSeconds => _stepper.World.StepSeconds;

    /// <summary>Whether it steps on a thread of its own.</summary>
    public SimulationState State { get; private set; }

    /// <summary>How many steps it has taken, as the last run read says.</summary>
    public long StepNumber { get; private set; }

    /// <summary>What the last step read did.</summary>
    public StepStatistics LastStep { get; private set; }

    /// <summary>The pull every dynamic body feels, in meters per second squared; a change takes with the next step.
    /// </summary>
    public Vector3F Gravity
    {
        get => _gravity;
        set
        {
            ThrowIfDisposed();
            RequireFinite(value, nameof(value));
            _gravity = value;
            _commands.Add(new SimulationCommand { Kind = SimulationCommandKind.SetGravity, Vector = value });
        }
    }

    /// <summary>Has <paramref name="hook"/> act at every step from the next, before the step is solved, on the thread
    /// that steps; added again, it acts once a step still.</summary>
    public void AddStepHook(IStepHook hook)
    {
        ArgumentNullException.ThrowIfNull(hook);
        ThrowIfDisposed();
        _commands.Add(new SimulationCommand { Kind = SimulationCommandKind.AddStepHook, StepHook = hook });
    }

    /// <summary>Has <paramref name="hook"/> act no more, from the next step.</summary>
    public void RemoveStepHook(IStepHook hook)
    {
        ArgumentNullException.ThrowIfNull(hook);
        ThrowIfDisposed();
        _commands.Add(new SimulationCommand { Kind = SimulationCommandKind.RemoveStepHook, StepHook = hook });
    }

    /// <summary>Does what was asked since the last step, then moves the bodies by one step of
    /// <see cref="StepSeconds"/>; what the callers read is then this step's. Only while it does not step on its own
    /// thread.</summary>
    public void Step()
    {
        RequireStopped();
        _blendsByClock = false;
        _contactEvents.Clear();
        RunHere(1);
    }

    /// <summary>Takes as many steps as <paramref name="elapsedSeconds"/> more of time make due - at most
    /// <see cref="MaxStepsPerAdvance"/>, the rest dropped - and gives how many it took; what is drawn between them
    /// follows the time left over. Only while it does not step on its own thread.</summary>
    public int Advance(double elapsedSeconds)
    {
        RequireStopped();
        _blendsByClock = true;
        _contactEvents.Clear();
        var steps = _clock.Advance(elapsedSeconds);
        if (steps > 0)
            RunHere(steps);

        return steps;
    }

    /// <summary>Lets go of every body and joint, its thread stopped; the simulation takes no more calls.</summary>
    public void Dispose()
    {
        if (_isDisposed)
            return;

        StopThread();
        _wake.Dispose();
        _parked.Dispose();
        _heldByUnreadResults.Dispose();
        _isDisposed = true;
        _commands.Clear();
        _bodySlots.Clear();
        _liveBodies.Clear();
        _movedBodies.Clear();
        _refusedBodies.Clear();
        _jointSlots.Clear();
        _liveJoints.Clear();
        _refusedJoints.Clear();
        _snapshotMoves.Clear();
        _jumpingBodies.Clear();
        _contactEvents.Clear();
        _untoldTouches.Clear();
        _snapshot = null;
    }

    // Asked by its layers: two layers collide, or stop, from the next step.
    internal void AskLayersCollide(Layer first, Layer second, bool collides) =>
        _commands.Add(new SimulationCommand
        {
            Kind = SimulationCommandKind.SetLayersCollide, Layer = first, OtherLayer = second, Collides = collides
        });

    // Stepped by hand: the stepper runs here, and its result is read at once.
    private void RunHere(int steps)
    {
        _handedBatches++;
        _stepper.Run(_commands, steps, _handedBatches, _handResult);
        _commands.Clear();
        ForgetRefusals();
        Read(_handResult);
        _lastResultSteps = steps;
    }

    private void RequireStopped()
    {
        ThrowIfDisposed();
        if (State != SimulationState.Stopped)
            throw new InvalidOperationException("The simulation steps on its own thread; Stop it to step it by hand.");
    }

    internal void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_isDisposed, this);

    private static void RequireFinite(Vector3F vector, string parameterName)
    {
        if (!float.IsFinite(vector.X) || !float.IsFinite(vector.Y) || !float.IsFinite(vector.Z))
            throw new ArgumentOutOfRangeException(parameterName, vector, "The vector must be finite.");
    }

    private static void RequireFinite(WorldPosition position, string parameterName)
    {
        if (!position.IsFinite)
            throw new ArgumentOutOfRangeException(parameterName, position, "The position must be finite.");
    }

    private static void RequireTurn(QuaternionF rotation, string parameterName)
    {
        var lengthSquared = rotation.LengthSquared();
        if (!(lengthSquared > 1e-12f) || !float.IsFinite(lengthSquared))
            throw new ArgumentException("A rotation must be a finite quaternion, not zero.", parameterName);
    }
}
