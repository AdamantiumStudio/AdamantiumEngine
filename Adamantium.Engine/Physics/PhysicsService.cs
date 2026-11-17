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
    private readonly List<PhysicsBodyLink> _placing = [];
    private readonly Dictionary<int, PhysicsForce> _forces = new();
    private readonly Dictionary<int, Vector3F> _lateImpulses = new();
    private readonly Dictionary<int, float> _replacedSeconds = new();
    private readonly Dictionary<int, float> _forceShares = new();
    private readonly List<int> _refusedForces = [];
    private readonly HashSet<int> _toldOfRefusal = [];
    private readonly Channel<PhysicsCommand> _commands = Channel.CreateUnbounded<PhysicsCommand>(OneReader);
    private readonly Channel<PhysicsStepResult> _results = Channel.CreateUnbounded<PhysicsStepResult>(OneReader);
    private readonly Channel<PhysicsStepResult> _spareResults = Channel.CreateUnbounded<PhysicsStepResult>(OneReader);

    private Dictionary<int, Vector3F> _frameForces = new();
    private Dictionary<int, Vector3F> _lastFrameForces = new();
    private PhysicsSettings _settings = new();
    private Vector3F _gravity = new(0, 9.81f, 0);
    private Vector3<double> _origin;
    private IPhysicsWorld _world;
    private StepClock _clock;
    private Thread _thread;
    private volatile bool _stopping;
    private int _unreadResults;
    private long _resultNumber;
    private long _frameNumber;
    private long _batch;
    private bool _warnedOfOverflow;
    private long _lastResultAt;
    private int _lastResultSteps = 1;
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
        var key = FindDynamicKey(entity);
        var total = _frameForces.TryGetValue(key, out var earlier) ? earlier + force : force;
        if (!IsFinite(total))
        {
            if (!_warnedOfOverflow)
                Log.Logger.Warning("The pushes of {Entity} in a frame add up past any number; such pushes are dropped",
                    entity.Name);

            _warnedOfOverflow = true;
            return;
        }

        _frameForces[key] = total;
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
        if (_world == null)
            return;

        SendFrameForces((float)appTime.FrameTime);
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
        _placing.Clear();
        _frameForces.Clear();
        _lastFrameForces.Clear();
        _forces.Clear();
        _lateImpulses.Clear();
        _toldOfRefusal.Clear();
        _warnedOfOverflow = false;
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

        var parentWorld = entity.Owner?.Transform.GetWorldMatrixF() ?? Matrix4x4F.Identity;
        var world = entity.Transform.GetWorldMatrixF();
        var moves = body.Kind != BodyKind.Static;
        var cannotStand = moves && (world.Determinant() <= 0 || !IsTurnAndEvenScale(parentWorld));
        if (cannotStand || world.Determinant() == 0 || IsSkewed(world)
            || !world.Decompose(out var scale, out var rotation, out var translation))
        {
            Log.Logger.Warning("{Entity} or its parent is flattened, mirrored, skewed or stretched unevenly; "
                               + "the physics leaves it", entity.Name);
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

        var link = new PhysicsBodyLink(entity, body.Kind, shape, position, turn, CountDepth(entity))
        {
            ParentWorld = parentWorld
        };
        _keys[entity] = _links.Count;
        _links.Add(link);
        _handles.Add(handle);
        _wasAwake.Add(body.Kind != BodyKind.Static);
        if (entity.Owner != null && body.Kind != BodyKind.Static)
            _parented.Add(link);
    }

    private void SendFrameForces(float frameSeconds)
    {
        foreach (var force in _frameForces)
        {
            _commands.Writer.TryWrite(new PhysicsCommand(PhysicsCommandKind.SetForce, force.Key, force.Value,
                Vector3F.Zero, frameSeconds));
        }

        foreach (var force in _lastFrameForces)
        {
            if (!_frameForces.ContainsKey(force.Key))
                _commands.Writer.TryWrite(new PhysicsCommand(PhysicsCommandKind.SetForce, force.Key, Vector3F.Zero,
                    Vector3F.Zero, frameSeconds));
        }

        var spent = _lastFrameForces;
        _lastFrameForces = _frameForces;
        _frameForces = spent;
        _frameForces.Clear();
    }

    private void RunPhysics()
    {
        var previous = Stopwatch.GetTimestamp();
        while (!_stopping)
        {
            var now = Stopwatch.GetTimestamp();
            if (!IsActive)
            {
                _clock.Reset();
                previous = now;
                Thread.Sleep(5);
                continue;
            }

            if (Volatile.Read(ref _unreadResults) >= MaxUnreadResults)
            {
                Thread.Sleep(1);
                continue;
            }

            var elapsed = Stopwatch.GetElapsedTime(previous, now).TotalSeconds;
            previous = now;
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

        _batch++;
        DoCommands();
        ApplyLateImpulses();
        ShareOutForces((float)(steps * _clock.StepSeconds));
        for (var step = 0; step < steps; step++)
        {
            ApplyForces();
            _world.Step();
        }

        _forceShares.Clear();

        Publish(steps);
        return steps;
    }

    private void ApplyForces()
    {
        _refusedForces.Clear();
        foreach (var force in _forces)
        {
            try
            {
                var share = _forceShares.TryGetValue(force.Key, out var part) ? part : 1f;
                _world.ApplyForce(_handles[force.Key], force.Value.Force * share);
            }
            catch (ArgumentException exception)
            {
                TellOfRefusal(force.Key, exception);
                _refusedForces.Add(force.Key);
            }
        }

        foreach (var key in _refusedForces)
        {
            _forces.Remove(key);
        }
    }

    private void SetForce(in PhysicsCommand command)
    {
        if (_forces.TryGetValue(command.Key, out var last) && last.Batch == _batch)
        {
            var late = last.Force * command.Seconds;
            _lateImpulses[command.Key] = _lateImpulses.TryGetValue(command.Key, out var earlier) ? earlier + late : late;
            var spent = _replacedSeconds.TryGetValue(command.Key, out var before) ? before : 0;
            _replacedSeconds[command.Key] = spent + command.Seconds;
        }

        if (command.Value == Vector3F.Zero)
            _forces.Remove(command.Key);
        else
            _forces[command.Key] = new PhysicsForce(command.Value, _batch);
    }

    private void ShareOutForces(float batchSeconds)
    {
        foreach (var replaced in _replacedSeconds)
        {
            if (_forces.ContainsKey(replaced.Key) && replaced.Value > 0)
                _forceShares[replaced.Key] = Math.Clamp((batchSeconds - replaced.Value) / batchSeconds, 0f, 1f);
        }

        _replacedSeconds.Clear();
    }

    private void ApplyLateImpulses()
    {
        foreach (var impulse in _lateImpulses)
        {
            var handle = _handles[impulse.Key];
            try
            {
                _world.ApplyImpulse(handle, impulse.Value, _world.GetPose(handle).Position);
            }
            catch (ArgumentException exception)
            {
                TellOfRefusal(impulse.Key, exception);
            }
        }

        _lateImpulses.Clear();
    }

    private void TellOfRefusal(int key, ArgumentException exception)
    {
        if (_toldOfRefusal.Add(key))
            Log.Logger.Warning(exception, "The physics drops a push of {Entity} it cannot apply", _links[key].Entity.Name);
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
            case PhysicsCommandKind.SetForce:
                SetForce(command);
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

            _lastResultAt = result.SteppedAt;
            _lastResultSteps = result.Steps;
            _lastStep = result.Statistics;
            _spareResults.Writer.TryWrite(result);
        }
    }

    private void MoveEntities()
    {
        _frameNumber++;
        _placing.Clear();
        Gather(_moving, true);
        Gather(_stopped, true);
        Gather(_parented, false);
        _stopped.Clear();
        _placing.Sort(CompareDepths);

        var blend = MeasureBlend();
        foreach (var link in _placing)
        {
            var parent = link.Entity.Owner;
            var parentWorld = parent?.Transform.GetWorldMatrixF() ?? Matrix4x4F.Identity;
            if (!link.MustPlace && parentWorld == link.ParentWorld)
                continue;

            link.ParentWorld = parentWorld;
            if (parent != null && !IsTurnAndEvenScale(parentWorld))
            {
                if (!link.WarnedOfParent)
                    Log.Logger.Warning("The parent of {Entity} got stretched or mirrored; it is not stood until it is not",
                        link.Entity.Name);

                link.WarnedOfParent = true;
                continue;
            }

            link.WarnedOfParent = false;
            PlaceEntity(link, parent != null, parentWorld,
                Vector3F.Lerp(link.PreviousPosition, link.CurrentPosition, blend),
                QuaternionF.Slerp(link.PreviousRotation, link.CurrentRotation, blend));
        }
    }

    private void Gather(List<PhysicsBodyLink> links, bool mustPlace)
    {
        foreach (var link in links)
        {
            if (link.PlacedInFrame == _frameNumber)
            {
                link.MustPlace |= mustPlace;
                continue;
            }

            link.PlacedInFrame = _frameNumber;
            link.MustPlace = mustPlace;
            _placing.Add(link);
        }
    }

    private float MeasureBlend()
    {
        var sinceStep = _stepsOnOwnThread
            ? Stopwatch.GetElapsedTime(_lastResultAt).TotalSeconds / _clock.StepSeconds
            : _clock.Blend;
        return (float)Math.Clamp((_lastResultSteps - 1 + sinceStep) / _lastResultSteps, 0, 1);
    }

    private void PlaceEntity(PhysicsBodyLink link, bool hasParent, in Matrix4x4F parentWorld, Vector3F position,
        QuaternionF rotation)
    {
        var entityRotation = rotation * QuaternionF.Conjugate(link.Turn);
        var entityPosition = ToWorld(position - Vector3F.Transform(link.Offset, entityRotation));
        var transform = link.Entity.Transform;
        if (!hasParent)
        {
            transform.SetPose(entityPosition, entityRotation, transform.BaseScale);
            return;
        }

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

    private static bool IsSkewed(in Matrix4x4F world)
    {
        var axisX = Vector3F.TransformNormal(Vector3F.UnitX, world);
        var axisY = Vector3F.TransformNormal(Vector3F.UnitY, world);
        var axisZ = Vector3F.TransformNormal(Vector3F.UnitZ, world);
        return !IsSquare(axisX, axisY) || !IsSquare(axisY, axisZ) || !IsSquare(axisZ, axisX);
    }

    private static bool IsTurnAndEvenScale(in Matrix4x4F world)
    {
        if (world.Determinant() <= 0 || IsSkewed(world))
            return false;

        var lengthX = Vector3F.TransformNormal(Vector3F.UnitX, world).Length();
        var lengthY = Vector3F.TransformNormal(Vector3F.UnitY, world).Length();
        var lengthZ = Vector3F.TransformNormal(Vector3F.UnitZ, world).Length();
        return MathF.Abs(lengthX - lengthY) <= 1e-4f * lengthX && MathF.Abs(lengthX - lengthZ) <= 1e-4f * lengthX;
    }

    private static bool IsSquare(Vector3F first, Vector3F second) =>
        MathF.Abs(Vector3F.Dot(first, second)) <= 1e-4f * first.Length() * second.Length();

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
        if (!IsFinite(vector))
            throw new ArgumentOutOfRangeException(parameterName, vector, "The vector must be finite.");
    }

    private static bool IsFinite(Vector3F vector) =>
        float.IsFinite(vector.X) && float.IsFinite(vector.Y) && float.IsFinite(vector.Z);
}
