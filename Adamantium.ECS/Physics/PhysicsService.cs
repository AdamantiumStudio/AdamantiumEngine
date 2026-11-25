using System;
using System.Collections.Generic;
using Adamantium.Core;
using Adamantium.DarkMatter;
using Adamantium.Graphics.Core.Assets;
using Adamantium.Mathematics;
using Serilog;

namespace Adamantium.ECS.Physics;

/// <summary>The physics of a game, the system: owns the game's <see cref="PhysicsSimulation"/>, stepping on a thread of
/// its own while the game plays, answers the game's code (<see cref="IPhysicsService"/>), and hosts the processors - the
/// main one (<see cref="MainProcessor"/>) laying the scene out in the simulation and standing the entities, then the
/// others attached, which read what the frame left.</summary>
public sealed partial class PhysicsService : GameService, IPhysicsService
{
    /// <summary>The service's <see cref="EntityService.Priority"/>: before the others, so the entities it stands are
    /// placed by the transforms in the same frame.</summary>
    public const int UpdatePriority = -1;

    private readonly bool _stepsOnOwnThread;
    private readonly List<EntityContactEventArgs> _contacts = [];

    private PhysicsProcessor _mainProcessor = new();
    private PhysicsSettings _settings = new();
    private Vector3F _gravity = new(0, 9.81f, 0);
    private PhysicsSettings _settingsBeforeGame;
    private Vector3F _gravityBeforeGame;
    private PhysicsSimulation _simulation;
    private ProjectPhysics _project;

    public PhysicsService(EntityWorld world) : this(world, true)
    {
    }

    /// <summary>A service stepping on the world's thread, in its frames, when <paramref name="stepsOnOwnThread"/> is
    /// false: for tests that step by hand.</summary>
    internal PhysicsService(EntityWorld world, bool stepsOnOwnThread) : base(world)
    {
        _stepsOnOwnThread = stepsOnOwnThread;
        Priority = UpdatePriority;
    }

    /// <summary>The processor that lays the scene out in the simulation and stands the entities where it draws them:
    /// a <see cref="PhysicsProcessor"/>, or one derived from it in its place; changed only while no game plays.
    /// </summary>
    public PhysicsProcessor MainProcessor
    {
        get => _mainProcessor;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (_simulation != null)
                throw new InvalidOperationException("The main processor is changed only while no game plays.");

            _mainProcessor = value;
        }
    }

    /// <inheritdoc/>
    public ProjectPhysics Project
    {
        get => _project;
        set
        {
            if (_simulation != null)
                throw new InvalidOperationException("The project's physics is changed only while no game plays.");

            if (value?.FindProblem() is { } problem)
                throw new ArgumentException(problem, nameof(value));

            _project = value;
        }
    }

    /// <inheritdoc/>
    public PhysicsSettings Settings
    {
        get => _settings;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _settings = value;
        }
    }

    /// <inheritdoc/>
    public Vector3F Gravity
    {
        get => _gravity;
        set
        {
            RequireFinite(value, nameof(value));
            _gravity = value;
            if (_simulation != null)
                _simulation.Gravity = value;
        }
    }

    /// <inheritdoc/>
    public StepStatistics LastStep => _simulation?.LastStep ?? default;

    /// <inheritdoc/>
    public bool Moves(Entity entity) => _mainProcessor.TryFindBody(entity, out _);

    /// <inheritdoc/>
    public Vector3F GetLinearVelocity(Entity entity) => _simulation.LinearVelocityOf(_mainProcessor.FindBody(entity));

    /// <inheritdoc/>
    public Vector3F GetAngularVelocity(Entity entity) => _simulation.AngularVelocityOf(_mainProcessor.FindBody(entity));

    /// <inheritdoc/>
    public float GetMass(Entity entity) => _simulation.MassOf(_mainProcessor.FindBody(entity));

    /// <inheritdoc/>
    public bool IsSleeping(Entity entity) => _simulation.IsSleeping(_mainProcessor.FindBody(entity));

    /// <inheritdoc/>
    public void SetLinearVelocity(Entity entity, Vector3F velocity)
    {
        RequireFinite(velocity, nameof(velocity));
        _simulation.SetLinearVelocity(_mainProcessor.FindMovingBody(entity), velocity);
    }

    /// <inheritdoc/>
    public void SetAngularVelocity(Entity entity, Vector3F velocity)
    {
        RequireFinite(velocity, nameof(velocity));
        _simulation.SetAngularVelocity(_mainProcessor.FindMovingBody(entity), velocity);
    }

    /// <inheritdoc/>
    public void SetForce(Entity entity, Vector3F force)
    {
        RequireFinite(force, nameof(force));
        _simulation.SetForce(_mainProcessor.FindDynamicBody(entity), force);
    }

    /// <inheritdoc/>
    public void SetTorque(Entity entity, Vector3F torque)
    {
        RequireFinite(torque, nameof(torque));
        _simulation.SetTorque(_mainProcessor.FindDynamicBody(entity), torque);
    }

    /// <inheritdoc/>
    public void ApplyImpulse(Entity entity, Vector3F impulse)
    {
        RequireFinite(impulse, nameof(impulse));
        _simulation.ApplyImpulse(_mainProcessor.FindDynamicBody(entity), impulse);
    }

    /// <inheritdoc/>
    public void ApplyImpulse(Entity entity, Vector3F impulse, WorldPosition point)
    {
        RequireFinite(impulse, nameof(impulse));
        if (!point.IsFinite)
            throw new ArgumentOutOfRangeException(nameof(point), point, "The point must be finite.");

        _simulation.ApplyImpulse(_mainProcessor.FindDynamicBody(entity), impulse, point);
    }

    /// <inheritdoc/>
    public void ApplyAngularImpulse(Entity entity, Vector3F impulse)
    {
        RequireFinite(impulse, nameof(impulse));
        _simulation.ApplyAngularImpulse(_mainProcessor.FindDynamicBody(entity), impulse);
    }

    /// <inheritdoc/>
    public void MoveKinematic(Entity entity, in Pose pose) =>
        _simulation.MoveKinematic(_mainProcessor.FindKinematicBody(entity), pose);

    /// <inheritdoc/>
    public void WakeUp(Entity entity) => _simulation.WakeUp(_mainProcessor.FindBody(entity));

    /// <inheritdoc/>
    public void WakeUp(in BoundingBox<double> area) => RequireGame().WakeUp(area);

    /// <inheritdoc/>
    public void ApplyRadialImpulse(WorldPosition center, float radius, float strength) =>
        RequireGame().ApplyRadialImpulse(center, radius, strength);

    /// <summary>The frame of the game playing: the main processor brings the bodies up to the scene, the simulation
    /// steps, the contacts are told, the main processor stands the entities - then the other processors.</summary>
    public override void Update(AppTime appTime)
    {
        if (_simulation != null)
        {
            _mainProcessor.BeginFrame();
            if (_stepsOnOwnThread)
                _simulation.Update();
            else
                _simulation.Advance(appTime.FrameTime);

            TellContacts();
            _mainProcessor.EndFrame();
        }

        base.Update(appTime);
    }

    /// <summary>Stops the game's physics too when the service leaves its world while a game plays.</summary>
    public override void UnloadContent()
    {
        EndGame();
        base.UnloadContent();
    }

    protected override void OnStart()
    {
        var settings = _project?.ToSettings() ?? _settings;
        var gravity = _project?.Gravity ?? _gravity;
        _simulation = new PhysicsSimulation(settings) { Gravity = gravity };
        _settingsBeforeGame = _settings;
        _gravityBeforeGame = _gravity;
        _gravity = gravity;
        try
        {
            DefineLayers();
            EntityWorld.Satellites.TryGet<IAssetService>(out var assets);
            _mainProcessor.Start(Scene, _simulation, assets);
        }
        catch
        {
            OnStop();
            throw;
        }

        if (_stepsOnOwnThread)
            _simulation.Start();
    }

    protected override void OnStop()
    {
        EndGame();
        if (_settingsBeforeGame == null)
            return;

        _settings = _settingsBeforeGame;
        _gravity = _gravityBeforeGame;
        _settingsBeforeGame = null;
    }

    protected override void OnPause() => _simulation?.Pause();

    protected override void OnResume()
    {
        if (_stepsOnOwnThread)
            _simulation?.Start();
    }

    private void EndGame()
    {
        try
        {
            _mainProcessor.Stop();
        }
        finally
        {
            _simulation?.Dispose();
            _simulation = null;
            _contacts.Clear();
        }
    }

    private void DefineLayers()
    {
        if (_project == null)
            return;

        var layers = _simulation.Layers;
        foreach (var layer in _project.Layers ?? [])
        {
            if (string.IsNullOrWhiteSpace(layer?.Name) || layers.TryFind(layer.Name, out _))
            {
                Log.Logger.Warning("The project's physics names a layer \"{Layer}\" with no name or twice; it is left out",
                    layer?.Name);
                continue;
            }

            if (!layers.TryDefine(layer.Name))
            {
                Log.Logger.Warning("The project's physics names more than {Count} layers; those after are left out",
                    PhysicsLayers.Count);
                break;
            }
        }

        foreach (var pair in _project.Apart ?? [])
        {
            if (pair?.First == null || pair.Second == null || !layers.TryFind(pair.First, out var first)
                || !layers.TryFind(pair.Second, out var second))
            {
                Log.Logger.Warning("The project's physics keeps apart \"{First}\" and \"{Second}\", not both its layers; "
                                   + "they collide", pair?.First, pair?.Second);
                continue;
            }

            layers.SetCollides(first, second, false);
        }
    }

    private static void RequireFinite(Vector3F vector, string parameterName)
    {
        if (!float.IsFinite(vector.X) || !float.IsFinite(vector.Y) || !float.IsFinite(vector.Z))
            throw new ArgumentOutOfRangeException(parameterName, vector, "The vector must be finite.");
    }
}
