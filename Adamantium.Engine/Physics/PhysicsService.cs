using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Channels;
using Adamantium.Core;
using Adamantium.DarkMatter;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.Mathematics;
using Serilog;

namespace Adamantium.Engine.Physics;

/// <summary>The physics of a game: a world of the scene's rigid bodies stepped on a thread of its own, the entities
/// stood between its last two steps each frame, the game's pushes sent to it as commands.</summary>
public sealed class PhysicsService : GameService, IPhysicsService
{
    private const int MaxStepsPerAdvance = 8;
    private const int MaxUnreadResults = 4;

    private static readonly UnboundedChannelOptions OneReader = new() { SingleReader = true };

    private readonly bool _stepsOnOwnThread;
    private readonly List<PhysicsBodyLink> _links = [];
    private readonly List<BodyHandle> _handles = [];
    private readonly List<bool> _wasAwake = [];
    private readonly Dictionary<Entity, int> _keys = new();
    private readonly List<PhysicsBodyLink> _moving = [];
    private readonly List<PhysicsBodyLink> _stopped = [];
    private readonly List<PhysicsBodyLink> _parented = [];
    private readonly Channel<PhysicsCommand> _commands = Channel.CreateUnbounded<PhysicsCommand>(OneReader);
    private readonly Channel<PhysicsStepResult> _results = Channel.CreateUnbounded<PhysicsStepResult>(OneReader);
    private readonly Channel<PhysicsStepResult> _spareResults = Channel.CreateUnbounded<PhysicsStepResult>(OneReader);

    private PhysicsSettings _settings = new();
    private Vector3F _gravity = new(0, 9.81f, 0);
    private Vector3<double> _origin;
    private IPhysicsWorld _world;
    private StepClock _clock;
    private Thread _thread;
    private volatile bool _stopping;
    private int _unreadResults;
    private long _resultNumber;
    private long _lastResultAt;
    private int _lastResultSteps = 1;
    private double _frameSeconds;
    private StepStatistics _lastStep;

    public PhysicsService(EntityWorld world) : this(world, true)
    {
    }

    /// <summary>A service stepping on the world's thread, in its frames, when <paramref name="stepsOnOwnThread"/> is
    /// false: for tests that step by hand.</summary>
    internal PhysicsService(EntityWorld world, bool stepsOnOwnThread) : base(world)
    {
        _stepsOnOwnThread = stepsOnOwnThread;
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
            if (_world != null)
                Send(PhysicsCommandKind.SetGravity, 0, value, Vector3F.Zero);
        }
    }

    /// <inheritdoc/>
    public StepStatistics LastStep => _lastStep;

    /// <inheritdoc/>
    public bool Moves(Entity entity) => entity != null && _keys.ContainsKey(entity);

    /// <inheritdoc/>
    public Vector3F GetLinearVelocity(Entity entity) => _links[FindKey(entity)].LinearVelocity;

    /// <inheritdoc/>
    public Vector3F GetAngularVelocity(Entity entity) => _links[FindKey(entity)].AngularVelocity;

    /// <inheritdoc/>
    public void SetLinearVelocity(Entity entity, Vector3F velocity)
    {
        RequireFinite(velocity, nameof(velocity));
        Send(PhysicsCommandKind.SetLinearVelocity, FindMovingKey(entity), velocity, Vector3F.Zero);
    }

    /// <inheritdoc/>
    public void SetAngularVelocity(Entity entity, Vector3F velocity)
    {
        RequireFinite(velocity, nameof(velocity));
        Send(PhysicsCommandKind.SetAngularVelocity, FindMovingKey(entity), velocity, Vector3F.Zero);
    }

    /// <inheritdoc/>
    public void ApplyForce(Entity entity, Vector3F force)
    {
        RequireFinite(force, nameof(force));
        var impulse = force * (float)_frameSeconds;
        Send(PhysicsCommandKind.ApplyCentralImpulse, FindDynamicKey(entity), impulse, Vector3F.Zero);
    }

    /// <inheritdoc/>
    public void ApplyImpulse(Entity entity, Vector3F impulse)
    {
        RequireFinite(impulse, nameof(impulse));
        Send(PhysicsCommandKind.ApplyCentralImpulse, FindDynamicKey(entity), impulse, Vector3F.Zero);
    }

    /// <inheritdoc/>
    public void ApplyImpulse(Entity entity, Vector3F impulse, Vector3<double> point)
    {
        RequireFinite(impulse, nameof(impulse));
        Send(PhysicsCommandKind.ApplyImpulse, FindDynamicKey(entity), impulse, ToPhysics(point));
    }

    /// <inheritdoc/>
    public override void Update(AppTime appTime)
    {
        base.Update(appTime);
        _frameSeconds = appTime.FrameTime;
        if (_world == null)
            return;

        if (!_stepsOnOwnThread)
            Advance(appTime.FrameTime);

        ReadResults();
        MoveEntities();
    }

    /// <summary>Stops the game's physics too when the service leaves its world while a game plays.</summary>
    public override void UnloadContent()
    {
        EndGame();
        base.UnloadContent();
    }

    protected override void OnStart()
    {
        _origin = Vector3<double>.Zero;
        _world = new PhysicsWorld(Settings) { Gravity = _gravity };
        foreach (var body in Each<RigidBody>())
        {
            AddBody(body);
        }

        _parented.Sort(CompareDepths);
        _clock = new StepClock(Settings.StepRate, MaxStepsPerAdvance);
        _resultNumber = 0;
        _unreadResults = 0;
        _lastResultSteps = 1;
        _lastResultAt = Stopwatch.GetTimestamp();
        _stopping = false;
        if (!_stepsOnOwnThread)
            return;

        _thread = new Thread(RunPhysics) { IsBackground = true, Name = "DarkMatter physics" };
        _thread.Start();
    }

    protected override void OnStop() => EndGame();

    private void EndGame()
    {
        _stopping = true;
        _thread?.Join();
        _thread = null;
        _world = null;
        _clock = null;
        _links.Clear();
        _handles.Clear();
        _wasAwake.Clear();
        _keys.Clear();
        _moving.Clear();
        _stopped.Clear();
        _parented.Clear();
        _lastStep = default;
        while (_commands.Reader.TryRead(out _))
        {
        }

        while (_results.Reader.TryRead(out var result))
        {
            _spareResults.Writer.TryWrite(result);
        }

        _unreadResults = 0;
    }

    private void AddBody(RigidBody body)
    {
        var entity = body.Owner;
        var collider = entity.GetComponent<Collider>();
        if (collider == null)
        {
            Log.Logger.Warning("{Entity} has a rigid body but no collider; the physics leaves it", entity.Name);
            return;
        }

        var world = entity.Transform.GetWorldMatrixF();
        if (world.Determinant() <= 0 || !world.Decompose(out var scale, out var rotation, out var translation))
        {
            Log.Logger.Warning("{Entity} is flattened or mirrored; the physics leaves it", entity.Name);
            return;
        }

        BodyHandle handle;
        ColliderShape shape;
        Vector3F position;
        QuaternionF turn;
        try
        {
            if (!ColliderShape.TryCreate(collider, scale, out shape))
            {
                Log.Logger.Warning("The physics has no shape for the {Collider} of {Entity} yet; it leaves it",
                    collider.GetType().Name, entity.Name);
                return;
            }

            position = ToPhysics(translation.As<double>()) + Vector3F.Transform(shape.Offset, rotation);
            turn = rotation * shape.Turn;
            handle = _world.Add(new BodyDescription
            {
                Kind = body.Kind,
                Shape = shape.Shape,
                Position = position,
                Rotation = turn,
                Mass = body.Mass,
                Density = body.Density,
                Friction = body.Friction,
                Restitution = body.Restitution,
                LinearDamping = body.LinearDamping,
                AngularDamping = body.AngularDamping,
                GravityScale = body.GravityScale,
                CanSleep = body.CanSleep
            });
        }
        catch (ArgumentException exception)
        {
            Log.Logger.Warning(exception, "The rigid body of {Entity} cannot be added; the physics leaves it", entity.Name);
            return;
        }

        var link = new PhysicsBodyLink(entity, body.Kind, shape, position, turn, CountDepth(entity));
        _keys[entity] = _links.Count;
        _links.Add(link);
        _handles.Add(handle);
        _wasAwake.Add(body.Kind != BodyKind.Static);
        if (entity.Owner != null && body.Kind != BodyKind.Static)
            _parented.Add(link);
    }

    private void RunPhysics()
    {
        var previous = Stopwatch.GetTimestamp();
        while (!_stopping)
        {
            var now = Stopwatch.GetTimestamp();
            var elapsed = Stopwatch.GetElapsedTime(previous, now).TotalSeconds;
            previous = now;
            if (!IsActive || Volatile.Read(ref _unreadResults) >= MaxUnreadResults)
            {
                _clock.Reset();
                Thread.Sleep(1);
                continue;
            }

            try
            {
                if (Advance(elapsed) == 0)
                    WaitForNextStep();
            }
            catch (Exception exception)
            {
                Log.Logger.Error(exception, "The physics stopped stepping");
                return;
            }
        }
    }

    private int Advance(double elapsedSeconds)
    {
        var steps = _clock.Advance(elapsedSeconds);
        if (steps == 0)
            return 0;

        DoCommands();
        for (var step = 0; step < steps; step++)
        {
            _world.Step();
        }

        Publish(steps);
        return steps;
    }

    private void WaitForNextStep()
    {
        var remaining = (1 - _clock.Blend) * _clock.StepSeconds * 1000;
        if (remaining >= 1)
            Thread.Sleep((int)remaining);
        else
            Thread.Yield();
    }

    private void DoCommands()
    {
        while (_commands.Reader.TryRead(out var command))
        {
            try
            {
                DoCommand(command);
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
            {
                Log.Logger.Warning(exception, "The physics could not do {Command}", command.Kind);
            }
        }
    }

    private void DoCommand(in PhysicsCommand command)
    {
        switch (command.Kind)
        {
            case PhysicsCommandKind.SetGravity:
                _world.Gravity = command.Value;
                break;
            case PhysicsCommandKind.SetLinearVelocity:
                _world.SetLinearVelocity(_handles[command.Key], command.Value);
                break;
            case PhysicsCommandKind.SetAngularVelocity:
                _world.SetAngularVelocity(_handles[command.Key], command.Value);
                break;
            case PhysicsCommandKind.ApplyCentralImpulse:
                var handle = _handles[command.Key];
                _world.ApplyImpulse(handle, command.Value, _world.GetPose(handle).Position);
                break;
            case PhysicsCommandKind.ApplyImpulse:
                _world.ApplyImpulse(_handles[command.Key], command.Value, command.Point);
                break;
        }
    }

    private void Publish(int steps)
    {
        if (!_spareResults.Reader.TryRead(out var result))
            result = new PhysicsStepResult();

        result.Motions.Clear();
        for (var key = 0; key < _handles.Count; key++)
        {
            var handle = _handles[key];
            var isAwake = _world.IsAwake(handle);
            if (isAwake || _wasAwake[key])
            {
                var pose = _world.GetPose(handle);
                result.Motions.Add(new BodyMotion(key, pose.Position, pose.Rotation, _world.GetLinearVelocity(handle),
                    _world.GetAngularVelocity(handle)));
            }

            _wasAwake[key] = isAwake;
        }

        result.Steps = steps;
        result.Statistics = _world.LastStep;
        result.SteppedAt = Stopwatch.GetTimestamp();
        Interlocked.Increment(ref _unreadResults);
        _results.Writer.TryWrite(result);
    }

    private void ReadResults()
    {
        while (_results.Reader.TryRead(out var result))
        {
            Interlocked.Decrement(ref _unreadResults);
            _resultNumber++;
            foreach (var motion in result.Motions)
            {
                var link = _links[motion.Key];
                link.PreviousPosition = link.CurrentPosition;
                link.PreviousRotation = link.CurrentRotation;
                link.CurrentPosition = motion.Position;
                link.CurrentRotation = motion.Rotation;
                link.LinearVelocity = motion.LinearVelocity;
                link.AngularVelocity = motion.AngularVelocity;
                link.MovedInResult = _resultNumber;
            }

            foreach (var link in _moving)
            {
                if (link.MovedInResult == _resultNumber)
                    continue;

                link.PreviousPosition = link.CurrentPosition;
                link.PreviousRotation = link.CurrentRotation;
                _stopped.Add(link);
            }

            _moving.Clear();
            foreach (var motion in result.Motions)
            {
                _moving.Add(_links[motion.Key]);
            }

            _moving.Sort(CompareDepths);
            _lastResultAt = result.SteppedAt;
            _lastResultSteps = result.Steps;
            _lastStep = result.Statistics;
            _spareResults.Writer.TryWrite(result);
        }
    }

    private void MoveEntities()
    {
        var blend = MeasureBlend();
        foreach (var link in _moving)
        {
            PlaceEntity(link, Vector3F.Lerp(link.PreviousPosition, link.CurrentPosition, blend),
                QuaternionF.Slerp(link.PreviousRotation, link.CurrentRotation, blend));
        }

        foreach (var link in _stopped)
        {
            PlaceEntity(link, link.CurrentPosition, link.CurrentRotation);
        }

        _stopped.Clear();
        foreach (var link in _parented)
        {
            if (link.MovedInResult != _resultNumber)
                PlaceEntity(link, link.CurrentPosition, link.CurrentRotation);
        }
    }

    private float MeasureBlend()
    {
        var sinceStep = _stepsOnOwnThread
            ? Stopwatch.GetElapsedTime(_lastResultAt).TotalSeconds / _clock.StepSeconds
            : _clock.Blend;
        return (float)Math.Clamp((_lastResultSteps - 1 + sinceStep) / _lastResultSteps, 0, 1);
    }

    private void PlaceEntity(PhysicsBodyLink link, Vector3F position, QuaternionF rotation)
    {
        var entityRotation = rotation * QuaternionF.Conjugate(link.Turn);
        var entityPosition = ToWorld(position - Vector3F.Transform(link.Offset, entityRotation));
        var transform = link.Entity.Transform;
        var parent = link.Entity.Owner;
        if (parent == null)
        {
            transform.SetPose(entityPosition, entityRotation, transform.BaseScale);
            return;
        }

        var parentWorld = parent.Transform.GetWorldMatrixF();
        if (!parentWorld.Decompose(out _, out var parentRotation, out _))
            return;

        var local = Vector3F.TransformCoordinate(entityPosition.As<float>(), Matrix4x4F.Invert(parentWorld));
        transform.SetPose(local.As<double>(), QuaternionF.Conjugate(parentRotation) * entityRotation, transform.BaseScale);
    }

    private Vector3F ToPhysics(Vector3<double> position) => (position - _origin).As<float>();

    private Vector3<double> ToWorld(Vector3F position) => position.As<double>() + _origin;

    private void Send(PhysicsCommandKind kind, int key, Vector3F value, Vector3F point) =>
        _commands.Writer.TryWrite(new PhysicsCommand(kind, key, value, point));

    private int FindKey(Entity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        if (!_keys.TryGetValue(entity, out var key))
            throw new ArgumentException($"The physics does not move {entity.Name}.", nameof(entity));

        return key;
    }

    private int FindMovingKey(Entity entity)
    {
        var key = FindKey(entity);
        if (_links[key].Kind == BodyKind.Static)
            throw new InvalidOperationException($"{entity.Name} is static; the physics does not move it.");

        return key;
    }

    private int FindDynamicKey(Entity entity)
    {
        var key = FindKey(entity);
        if (_links[key].Kind != BodyKind.Dynamic)
            throw new InvalidOperationException($"Only a dynamic body is pushed; {entity.Name} is {_links[key].Kind}.");

        return key;
    }

    private static int CountDepth(Entity entity)
    {
        var depth = 0;
        for (var owner = entity.Owner; owner != null; owner = owner.Owner)
        {
            depth++;
        }

        return depth;
    }

    private static int CompareDepths(PhysicsBodyLink first, PhysicsBodyLink second) => first.Depth.CompareTo(second.Depth);

    private static void RequireFinite(Vector3F vector, string parameterName)
    {
        if (!float.IsFinite(vector.X) || !float.IsFinite(vector.Y) || !float.IsFinite(vector.Z))
            throw new ArgumentOutOfRangeException(parameterName, vector, "The vector must be finite.");
    }
}
