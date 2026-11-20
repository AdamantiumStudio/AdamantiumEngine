using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Adamantium.DarkMatter.Simulation;

namespace Adamantium.DarkMatter;

/// <summary>
/// The physics as a caller uses it: bodies and joints by id, every action a method of the simulation. An action is
/// checked when asked and done at the start of the next step, in the order asked; what the caller reads is what the
/// last step left, the same for everyone until the next. Called from one thread.
/// </summary>
public sealed partial class PhysicsSimulation : IDisposable
{
    private const int NoSlot = -1;

    private readonly PhysicsWorld _world;
    private readonly List<SimulationCommand> _commands = [];
    // Woken, a body's time at rest starts again and grows by a step a step: it sleeps again within the step only if
    // TimeToSleep is no longer than one, and only then must a sleeper be read again.
    private readonly bool _skipsSleepers;
    private Vector3F _gravity;
    private bool _isDisposed;

    /// <summary>A simulation stepped as <paramref name="settings"/> say, or with the defaults.</summary>
    public PhysicsSimulation(PhysicsSettings settings = null)
    {
        _world = new PhysicsWorld(settings);
        Settings = _world.Settings;
        _gravity = _world.Gravity;
        _skipsSleepers = Settings.TimeToSleep > StepSeconds;
    }

    /// <summary>How the simulation steps and solves, read once when it is made.</summary>
    public PhysicsSettings Settings { get; }

    /// <summary>The length of one step, in seconds.</summary>
    public float StepSeconds => _world.StepSeconds;

    /// <summary>How many steps it has taken.</summary>
    public long StepNumber { get; private set; }

    /// <summary>What the last step did.</summary>
    public StepStatistics LastStep => _world.LastStep;

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

    /// <summary>Does what was asked since the last step, then moves the bodies by one step of
    /// <see cref="StepSeconds"/>; what the callers read is then this step's.</summary>
    public void Step()
    {
        ThrowIfDisposed();
        DoCommands();
        PushBodies();
        _world.Step();
        StepNumber++;
        ReadBack();
        StopKinematicMoves();
    }

    /// <summary>Lets go of every body and joint; the simulation takes no more calls.</summary>
    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        _commands.Clear();
        _bodySlots.Clear();
        _liveBodies.Clear();
        _pushedBodies.Clear();
        _readBodies.Clear();
        _movedBodies.Clear();
        _refusedBodies.Clear();
        _jointSlots.Clear();
        _liveJoints.Clear();
        _refusedJoints.Clear();
    }

    private void DoCommands()
    {
        ForgetRefusals();
        // Each runs once, in order; one the core will not take is told on its body or joint, not thrown.
        foreach (ref var command in CollectionsMarshal.AsSpan(_commands))
        {
            switch (command.Kind)
            {
                case SimulationCommandKind.SetGravity:
                    _world.Gravity = command.Vector;
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

        _commands.Clear();
        FreeRemovedSlots();
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_isDisposed, this);

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
