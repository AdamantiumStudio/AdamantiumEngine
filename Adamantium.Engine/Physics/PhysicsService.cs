using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Channels;
using Adamantium.Core;
using Adamantium.DarkMatter;
using Adamantium.DarkMatter.Shapes;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.Mathematics;
using Serilog;

namespace Adamantium.Engine.Physics;

/// <summary>The physics of a game: a world of the scene's rigid bodies stepped on a thread of its own, the entities
/// stood between its last two steps each frame, the game's pushes sent to it as commands.</summary>
public sealed class PhysicsService : GameService, IPhysicsService
{
    /// <summary>The service's <see cref="EntityService.Priority"/>: before the others, so the entities it stands are
    /// placed by the transforms in the same frame.</summary>
    public const int UpdatePriority = -1;

    private const int MaxStepsPerAdvance = 8;
    private const int MaxUnreadResults = 32;

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
    private readonly Dictionary<int, Vector3F> _forces = new();
    private readonly HashSet<int> _toldOfRefusal = [];
    // The entities whose mesh colliders on dynamic bodies were already said in the log, by id, so a body made again does
    // not say it each time and a gone entity is not held.
    private readonly HashSet<UInt128> _toldOfDynamicMesh = [];
    private readonly List<CompoundPart> _shapeParts = [];
    private readonly CarriedShapeCache _carriedShapes = new();
    private readonly List<int> _freeKeys = [];
    private readonly List<int> _keyGenerations = [];
    // Rigid bodies the physics could not make, tried again once what they are made of changes.
    private readonly Dictionary<Entity, BodyParts> _refused = new();
    private readonly List<Entity> _gone = [];
    // The physics thread's own: the generation and the entity's name of the body under each key, and the bodies the
    // world would not take since the last result.
    private readonly List<int> _generations = [];
    private readonly List<string> _names = [];
    private readonly List<BodyRefusal> _refusals = [];
    private readonly Channel<PhysicsCommand> _commands = Channel.CreateUnbounded<PhysicsCommand>(OneReader);
    private readonly Channel<PhysicsStepResult> _results = Channel.CreateUnbounded<PhysicsStepResult>(OneReader);
    private readonly Channel<PhysicsStepResult> _spareResults = Channel.CreateUnbounded<PhysicsStepResult>(OneReader);

    private PhysicsSettings _settings = new();
    private Vector3F _gravity = new(0, 9.81f, 0);
    private PhysicsSettings _settingsBeforeGame;
    private Vector3F _gravityBeforeGame;
    private Vector3<double> _origin;
    private IPhysicsWorld _world;
    private StepClock _clock;
    private Thread _thread;
    private volatile bool _stopping;
    private int _unreadResults;
    private long _resultNumber;
    private long _frameNumber;
    private long _lookNumber;
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
        Priority = UpdatePriority;
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
    public bool Moves(Entity entity) => entity != null && TryFindKey(entity, out _);

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
    public void SetForce(Entity entity, Vector3F force)
    {
        RequireFinite(force, nameof(force));
        Send(PhysicsCommandKind.SetForce, FindDynamicKey(entity), force, Vector3F.Zero);
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

        FollowBodies();
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
        _settingsBeforeGame = _settings;
        _gravityBeforeGame = _gravity;
        _origin = Vector3<double>.Zero;
        _world = new PhysicsWorld(Settings) { Gravity = _gravity };
        FollowBodies();
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

    // The game's own settings and gravity go with it.
    protected override void OnStop()
    {
        EndGame();
        if (_settingsBeforeGame == null)
            return;

        _settings = _settingsBeforeGame;
        _gravity = _gravityBeforeGame;
        _settingsBeforeGame = null;
    }

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
        _forces.Clear();
        _toldOfRefusal.Clear();
        _toldOfDynamicMesh.Clear();
        _carriedShapes.Clear();
        _freeKeys.Clear();
        _keyGenerations.Clear();
        _refused.Clear();
        _generations.Clear();
        _names.Clear();
        _refusals.Clear();
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

    // Brings the bodies up to the scene: a rigid body new to it, or on an entity enabled again, is made; one whose
    // entities changed other than by the physics is made anew; one taken off or disabled is taken out.
    private void FollowBodies()
    {
        _lookNumber++;
        foreach (var body in Each<RigidBody>())
        {
            var entity = body.Owner;
            if (_keys.TryGetValue(entity, out var key))
            {
                var link = _links[key];
                // A second rigid body on one entity is passed over.
                if (link.Parts.SeenInLook == _lookNumber)
                    continue;

                link.Parts.SeenInLook = _lookNumber;
                if (link.Body != body || link.Parts.HaveChanged())
                    Refresh(link, body);
                else if (link.Parts.HaveAncestorsChanged())
                    FollowParents(link, body);

                continue;
            }

            if (_refused.TryGetValue(entity, out var refused))
            {
                if (refused.SeenInLook == _lookNumber)
                    continue;

                refused.SeenInLook = _lookNumber;
                if (!refused.HaveChanged() && !(refused.WaitsToStand && CanStand(entity, body.Kind, out _, out _)))
                    continue;

                _refused.Remove(entity);
            }

            MakeBody(body);
        }

        for (var key = 0; key < _links.Count; key++)
        {
            if (_links[key] is not { } link || link.Parts.SeenInLook == _lookNumber)
                continue;

            Forget(link);
            MarkOwnerStale(link.Entity);
        }

        ForgetRefusalsGone();
    }

    // An entity's own body gone, it may be a part of the body above it now: that body is made anew.
    private void MarkOwnerStale(Entity entity)
    {
        for (var owner = entity.Owner; owner != null; owner = owner.Owner)
        {
            if (_keys.TryGetValue(owner, out var key))
            {
                _links[key].Parts.MarkStale();
                return;
            }

            if (_refused.TryGetValue(owner, out var refused))
            {
                refused.MarkStale();
                return;
            }
        }
    }

    private void ForgetRefusalsGone()
    {
        foreach (var refused in _refused)
        {
            if (refused.Value.SeenInLook != _lookNumber)
                _gone.Add(refused.Key);
        }

        foreach (var entity in _gone)
        {
            _refused.Remove(entity);
            MarkOwnerStale(entity);
        }

        _gone.Clear();
    }

    // The body brought up to its entities as they are now, where it stands, its contacts kept: put where it was moved
    // by hand, given its new kind, shape and settings; left as it is when none changed - the entity selected, say. Made
    // anew only once its entity hangs elsewhere in the tree, moving on as it moved.
    private void Refresh(PhysicsBodyLink link, RigidBody body)
    {
        var parts = new BodyParts { SeenInLook = _lookNumber };
        var description = Describe(body, parts, out var parentWorld);
        var entity = link.Entity;
        if (description != null)
            KeepUsableSettings(description, link.Made);

        if (description == null || (entity.Owner != null) != link.IsParented || CountDepth(entity) != link.Depth)
        {
            var transform = entity.Transform;
            var keepsPose = link.Kind != BodyKind.Static && body.Kind != BodyKind.Static
                            && transform.Position == link.WrittenPosition && transform.Rotation == link.WrittenRotation;
            MakeBody(body, link, keepsPose, parts, description, parentWorld);
            return;
        }

        var changesShape = !ShapeLikeness.AreAlike(link.Parts.ShapeParts, parts.ShapeParts);
        var changesSettings = !HasSameSettings(link.Made, description);
        var changesKind = link.Kind != description.Kind;
        // A kind changed starts from rest where the entity is drawn, not where the physics' last step left it.
        var moves = WasMovedByHand(link, description) || changesKind;
        if (changesShape)
        {
            // After the new parts took their hull and mesh shapes, so the ones both use are not made again.
            _carriedShapes.GiveBack(link.Parts.CarriedShapes);
        }
        else
        {
            // The body keeps the shapes it has: compared with them later, so changes too small to count each time add
            // up, and holding the hull and mesh shapes it uses.
            _carriedShapes.GiveBack(parts.CarriedShapes);
            parts.CarriedShapes.AddRange(link.Parts.CarriedShapes);
            parts.ShapeParts = link.Parts.ShapeParts;
        }

        link.Parts = parts;
        link.Body = body;
        if (changesKind)
            ChangeKind(link, description.Kind);

        // A moving body's is what it was last stood under, for MoveEntities to see its parent move.
        if (link.Kind == BodyKind.Static)
            link.ParentWorld = parentWorld;

        if (changesShape || changesSettings || moves)
            ChangeBody(link, description, changesShape, changesSettings, moves);
    }

    // A kind changed starts from rest; only a body that moves under a parent is stood when the parent moves.
    private void ChangeKind(PhysicsBodyLink link, BodyKind kind)
    {
        if (link.IsParented && link.Kind != BodyKind.Static)
            _parented.Remove(link);

        link.Kind = kind;
        link.LinearVelocity = Vector3F.Zero;
        link.AngularVelocity = Vector3F.Zero;
        // Its center of mass moves with its kind: not drawn until the body sends where it stands now.
        link.MovedInResult = -1;
        if (link.IsParented && kind != BodyKind.Static)
            _parented.Add(link);
    }

    // A parent above a static body changed: brought up to it only when the parents now stand elsewhere, not for a
    // child hung under one of them.
    private void FollowParents(PhysicsBodyLink link, RigidBody body)
    {
        link.Parts.RereadAncestors();
        if (link.Entity.Owner?.Transform.GetWorldMatrixF() != link.ParentWorld)
            Refresh(link, body);
    }

    // A setting the body cannot have - typed halfway: cleared, a lone minus - is not sent to be refused: the body keeps
    // the one it has until one it can have is given. A dynamic body staying dynamic needs a mass or a density: it keeps
    // the weight it has.
    private static void KeepUsableSettings(BodyDescription description, BodyDescription made)
    {
        if (!IsAmount(description.Friction))
            description.Friction = made.Friction;

        if (!IsAmount(description.Restitution))
            description.Restitution = made.Restitution;

        if (!IsAmount(description.LinearDamping))
            description.LinearDamping = made.LinearDamping;

        if (!IsAmount(description.AngularDamping))
            description.AngularDamping = made.AngularDamping;

        if (!float.IsFinite(description.GravityScale))
            description.GravityScale = made.GravityScale;

        if (description.Kind == BodyKind.Dynamic && made.Kind == BodyKind.Dynamic
            && !IsWeight(description.Mass, description.Density))
        {
            description.Mass = made.Mass;
            description.Density = made.Density;
        }
    }

    private static bool IsAmount(float value) => float.IsFinite(value) && value >= 0;

    // What a dynamic body can be weighed by: a mass, or a density when the mass is zero.
    private static bool IsWeight(float mass, float density) =>
        IsAmount(mass) && IsAmount(density) && (mass > 0 || density > 0);

    // A weight counts only for a dynamic body: one that is not moves nothing by it.
    private static bool HasSameSettings(BodyDescription made, BodyDescription description) =>
        made.Kind == description.Kind
        && (description.Kind != BodyKind.Dynamic
            || BodyDescription.AreWeighedAlike(made.Mass, made.Density, description.Mass, description.Density))
        && made.Friction == description.Friction && made.Restitution == description.Restitution
        && made.LinearDamping == description.LinearDamping && made.AngularDamping == description.AngularDamping
        && made.GravityScale == description.GravityScale && made.CanSleep == description.CanSleep;

    // A static body stands where its entity does, its parent moved or itself; a moving one is moved by hand when its
    // entity no longer stands where the physics put it.
    private static bool WasMovedByHand(PhysicsBodyLink link, BodyDescription description)
    {
        if (link.Kind == BodyKind.Static)
            return description.Position != link.Made.Position || description.Rotation != link.Made.Rotation;

        var transform = link.Entity.Transform;
        return transform.Position != link.WrittenPosition || transform.Rotation != link.WrittenRotation;
    }

    // The body given description's kind, settings and - when it changed - shape, sent to the physics thread. Moved, it
    // is put where its entity stands and drawn from there until it sends a motion.
    private void ChangeBody(PhysicsBodyLink link, BodyDescription description, bool changesShape, bool changesSettings,
        bool moves)
    {
        link.Made = description;
        if (moves || changesShape)
        {
            // Motions sent before the change are passed over; a center of mass that moved is not drawn until the
            // body sends where it stands now.
            link.Generation = ++_keyGenerations[link.Key];
            if (changesShape)
                link.MovedInResult = -1;
        }

        if (moves)
        {
            var transform = link.Entity.Transform;
            link.WrittenPosition = transform.Position;
            link.WrittenRotation = transform.Rotation;
            var center = description.Position + Vector3F.Transform(link.CenterOfMass, description.Rotation);
            link.PreviousPosition = center;
            link.CurrentPosition = center;
            link.PreviousRotation = description.Rotation;
            link.CurrentRotation = description.Rotation;
        }

        _commands.Writer.TryWrite(new PhysicsCommand(PhysicsCommandKind.ChangeBody, link.Key, Vector3F.Zero,
            Vector3F.Zero, new BodyRequest(Copy(description), link.Generation, link.AddedAs,
                changesSettings || changesShape ? ++link.RequestsSent : link.RequestsSent, link.Name)
            {
                ChangesSettings = changesSettings,
                ChangesShape = changesShape,
                Moves = moves
            }));
    }

    // Makes a body of body, sent to the physics thread, under a key of its own.
    private void MakeBody(RigidBody body)
    {
        var parts = new BodyParts { SeenInLook = _lookNumber };
        var description = Describe(body, parts, out var parentWorld);
        MakeBody(body, null, false, parts, description, parentWorld);
    }

    // Makes a body of description - null for one the physics cannot make - under a key of its own, or in place of the
    // one previous links.
    private void MakeBody(RigidBody body, PhysicsBodyLink previous, bool keepsPose, BodyParts parts,
        BodyDescription description, Matrix4x4F parentWorld)
    {
        var entity = body.Owner;
        // After the new body took its hull and mesh shapes, so the ones both use are not made again.
        if (previous != null)
            Retire(previous);

        if (description == null)
        {
            _carriedShapes.GiveBack(parts.CarriedShapes);
            _refused[entity] = parts;
            if (previous != null)
                FreeKey(previous);

            return;
        }

        var key = previous?.Key ?? TakeKey();
        var generation = ++_keyGenerations[key];
        var link = new PhysicsBodyLink(body, parts, description, key, generation, CountDepth(entity))
        {
            ParentWorld = parentWorld
        };
        // Moving on as it moved, told so until the new body sends its own motion.
        if (previous != null && previous.Kind == link.Kind)
        {
            link.LinearVelocity = previous.LinearVelocity;
            link.AngularVelocity = previous.AngularVelocity;
        }

        _keys[entity] = key;
        _links[key] = link;
        if (link.IsParented && link.Kind != BodyKind.Static)
            _parented.Add(link);

        var kind = previous == null ? PhysicsCommandKind.AddBody : PhysicsCommandKind.ReplaceBody;
        _commands.Writer.TryWrite(new PhysicsCommand(kind, key, Vector3F.Zero, Vector3F.Zero,
            new BodyRequest(Copy(description), generation, generation, ++link.RequestsSent, entity.Name)
            {
                KeepsPose = keepsPose
            }));
    }

    // The physics thread changes the description it is sent; the game's side keeps its own.
    private static BodyDescription Copy(BodyDescription description) => new()
    {
        Kind = description.Kind,
        Shape = description.Shape,
        Position = description.Position,
        Rotation = description.Rotation,
        Mass = description.Mass,
        Density = description.Density,
        Friction = description.Friction,
        Restitution = description.Restitution,
        LinearDamping = description.LinearDamping,
        AngularDamping = description.AngularDamping,
        GravityScale = description.GravityScale,
        CanSleep = description.CanSleep
    };

    private void Forget(PhysicsBodyLink link)
    {
        Retire(link);
        FreeKey(link);
    }

    // The link is passed over from now on, its hull and mesh shapes given back; its key stays the entity's.
    private void Retire(PhysicsBodyLink link)
    {
        link.IsGone = true;
        _carriedShapes.GiveBack(link.Parts.CarriedShapes);
        if (link.IsParented && link.Kind != BodyKind.Static)
            _parented.Remove(link);
    }

    // The body under the link's key taken out, and the key free for another.
    private void FreeKey(PhysicsBodyLink link)
    {
        Send(PhysicsCommandKind.RemoveBody, link.Key, Vector3F.Zero, Vector3F.Zero);
        _links[link.Key] = null;
        _keys.Remove(link.Entity);
        _freeKeys.Add(link.Key);
    }

    private int TakeKey()
    {
        if (_freeKeys.Count > 0)
        {
            var free = _freeKeys[^1];
            _freeKeys.RemoveAt(_freeKeys.Count - 1);
            return free;
        }

        _links.Add(null);
        _keyGenerations.Add(0);
        return _links.Count - 1;
    }

    // The body body makes, its entities put in parts; null - logged - for one the physics cannot make.
    private BodyDescription Describe(RigidBody body, BodyParts parts, out Matrix4x4F parentWorld)
    {
        var entity = body.Owner;
        parts.Add(entity);
        if (!CanStand(entity, body.Kind, out parentWorld, out var world)
            || !world.Decompose(out var scale, out var rotation, out var translation))
        {
            Log.Logger.Warning("{Entity} or its parent is flattened, mirrored, skewed or stretched unevenly; "
                               + "the physics leaves it", entity.Name);
            parts.WaitsToStand = true;
            return null;
        }

        // A static body stands where its parents put it: one of them moved, it is moved too.
        if (body.Kind == BodyKind.Static)
        {
            for (var owner = entity.Owner; owner != null; owner = owner.Owner)
            {
                parts.AddAncestor(owner);
            }
        }

        var position = ToPhysics(translation.As<double>());
        var shape = CreateShape(entity, body.Kind, position, rotation, scale, parts);
        if (shape == null)
        {
            Log.Logger.Warning("{Entity} has a rigid body but no collider on it or below it; the physics leaves it",
                entity.Name);
            return null;
        }

        return new BodyDescription
        {
            Kind = body.Kind,
            Shape = shape,
            Position = position,
            Rotation = rotation,
            Mass = body.Mass,
            Density = body.Density,
            Friction = body.Friction,
            Restitution = body.Restitution,
            LinearDamping = body.LinearDamping,
            AngularDamping = body.AngularDamping,
            GravityScale = body.GravityScale,
            CanSleep = body.CanSleep
        };
    }

    /// <summary>The shape of the colliders on <paramref name="entity"/> and on the entities below it that have no rigid
    /// body of their own, around the body's origin at <paramref name="origin"/> turned by <paramref name="rotation"/>,
    /// the entity scaled by <paramref name="scale"/>, for a body of <paramref name="kind"/>; null when there are none.
    /// The entities below it it looks at, and the hull and mesh shapes it takes, go in <paramref name="parts"/>.</summary>
    private CollisionShape CreateShape(Entity entity, BodyKind kind, Vector3F origin, QuaternionF rotation,
        Vector3F scale, BodyParts parts)
    {
        _shapeParts.Clear();
        var unturn = QuaternionF.Conjugate(rotation);
        entity.VisitInDepth(current =>
        {
            if (current == entity)
            {
                AddColliders(entity, kind, current, origin, unturn, scale, parts.CarriedShapes);
                return true;
            }

            // One with a body of its own is that body's: its body taken off marks this one stale (MarkOwnerStale). A
            // disabled one has no body the physics follows, so it is watched here.
            if (current.ContainsComponent<RigidBody>())
            {
                if (!current.IsEnabled)
                    parts.Add(current);

                return false;
            }

            // One disabled gives nothing now, but would once enabled.
            parts.Add(current);
            if (!current.IsEnabled)
                return false;

            AddColliders(entity, kind, current, origin, unturn, scale, parts.CarriedShapes);
            return true;
        });

        if (_shapeParts.Count == 0)
            return null;

        parts.ShapeParts = [.. _shapeParts];
        var only = _shapeParts[0];
        if (_shapeParts.Count == 1 && only.Pose.Position == Vector3F.Zero && only.Pose.Rotation == QuaternionF.Identity)
            return only.Shape;

        return new CompoundCollisionShape(_shapeParts);
    }

    private void AddColliders(Entity body, BodyKind kind, Entity entity, Vector3F origin, QuaternionF unturn,
        Vector3F bodyScale, List<CarriedShapeKey> carriedShapesTaken)
    {
        // The body's own colliders stand on it exactly, as their offsets say.
        var placed = entity == body;
        var scale = bodyScale;
        var turn = QuaternionF.Identity;
        var translation = Vector3F.Zero;
        foreach (var component in entity.Components)
        {
            if (component is not Collider { IsEnabled: true } collider)
                continue;

            // A mesh holds no volume to weigh: a body moved by forces cannot hold one.
            if (collider is MeshCollider && kind == BodyKind.Dynamic)
            {
                if (_toldOfDynamicMesh.Add(entity.Uid))
                    Log.Logger.Warning("A mesh collider is for a static or kinematic body; the dynamic body of {Body} "
                                       + "leaves the one of {Entity}", body.Name, entity.Name);

                continue;
            }

            if (!placed)
            {
                var world = entity.Transform.GetWorldMatrixF();
                if (world.Determinant() == 0 || IsSkewed(world) || !world.Decompose(out scale, out turn, out translation))
                {
                    Log.Logger.Warning("{Entity} is flattened or skewed; the body of {Body} leaves its colliders",
                        entity.Name, body.Name);
                    return;
                }

                placed = true;
            }

            ColliderShape shape;
            try
            {
                if (!ColliderShape.TryCreate(collider, scale, _carriedShapes, carriedShapesTaken, out shape))
                {
                    Log.Logger.Warning("The physics has no shape for the {Collider} of {Entity} yet; the body of {Body} "
                                       + "leaves it", collider.GetType().Name, entity.Name, body.Name);
                    continue;
                }
            }
            catch (ArgumentException exception)
            {
                Log.Logger.Warning(exception, "The body of {Body} leaves the collider of {Entity}", body.Name, entity.Name);
                continue;
            }

            if (entity == body)
            {
                _shapeParts.Add(new CompoundPart(shape.Shape, new Pose(shape.Offset, shape.Turn)));
                continue;
            }

            var center = ToPhysics(translation.As<double>()) + Vector3F.Transform(shape.Offset, turn);
            _shapeParts.Add(new CompoundPart(shape.Shape,
                new Pose(Vector3F.Transform(center - origin, unturn), unturn * turn * shape.Turn)));
        }
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

        DoCommands();
        for (var step = 0; step < steps; step++)
        {
            ApplyForces();
            _world.Step();
        }

        Publish(steps);
        return steps;
    }

    private void ApplyForces()
    {
        foreach (var force in _forces)
        {
            // Its body taken out, or never taken by the world: the force goes with it.
            if (!_handles[force.Key].IsValid)
            {
                _forces.Remove(force.Key);
                continue;
            }

            try
            {
                _world.ApplyForce(_handles[force.Key], force.Value);
            }
            catch (ArgumentException exception)
            {
                if (_toldOfRefusal.Add(force.Key))
                    Log.Logger.Warning(exception, "The physics cannot push {Entity} so hard; it drops such forces",
                        _names[force.Key]);

                _forces.Remove(force.Key);
            }
        }
    }

    private void StrikeCenter(int key, Vector3F impulse)
    {
        var handle = _handles[key];
        _world.ApplyImpulse(handle, impulse, _world.GetCenterOfMass(handle));
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
                if (command.Value == Vector3F.Zero)
                    _forces.Remove(command.Key);
                else
                    _forces[command.Key] = command.Value;
                break;
            case PhysicsCommandKind.ApplyCentralImpulse:
                StrikeCenter(command.Key, command.Value);
                break;
            case PhysicsCommandKind.ApplyImpulse:
                _world.ApplyImpulse(_handles[command.Key], command.Value, command.Point);
                break;
            case PhysicsCommandKind.AddBody:
                PutBody(command.Key, command.Body);
                break;
            case PhysicsCommandKind.ReplaceBody:
                ReplaceBody(command.Key, command.Body);
                break;
            case PhysicsCommandKind.ChangeBody:
                ChangeBody(command.Key, command.Body);
                break;
            case PhysicsCommandKind.RemoveBody:
                TakeOutBody(command.Key);
                _forces.Remove(command.Key);
                _toldOfRefusal.Remove(command.Key);
                break;
        }
    }

    private void PutBody(int key, BodyRequest request)
    {
        while (_handles.Count <= key)
        {
            _handles.Add(default);
            _wasAwake.Add(false);
            _generations.Add(0);
            _names.Add(null);
        }

        _generations[key] = request.Generation;
        _names[key] = request.Name;
        try
        {
            _handles[key] = _world.Add(request.Description);
        }
        catch (ArgumentException exception)
        {
            Log.Logger.Warning(exception, "The rigid body of {Entity} cannot be added; the physics leaves it",
                request.Name);
            _refusals.Add(new BodyRefusal(key, request.AddedAs, request.Number));
            _forces.Remove(key);
            return;
        }

        // A body that moves sends where it starts with the next result; only a dynamic one is pushed.
        _wasAwake[key] = request.Description.Kind != BodyKind.Static;
        if (request.Description.Kind != BodyKind.Dynamic)
            _forces.Remove(key);
    }

    // The body made anew in place of the key's one, where it stood when asked to, awake - taking the old one out wakes
    // what touched it anyway. It moves on as that one moved while its kind stays: a falling body made kinematic does not
    // drift on at its fall's speed.
    private void ReplaceBody(int key, BodyRequest request)
    {
        var old = key < _handles.Count ? _handles[key] : default;
        if (old.IsValid)
        {
            var description = request.Description;
            if (request.KeepsPose)
            {
                var pose = _world.GetPose(old);
                description.Position = pose.Position;
                description.Rotation = pose.Rotation;
            }

            if (_world.GetKind(old) == description.Kind)
            {
                description.LinearVelocity = _world.GetLinearVelocity(old);
                description.AngularVelocity = _world.GetAngularVelocity(old);
            }

            TakeOutBody(key);
        }

        PutBody(key, request);
    }

    // The key's body changed where it stands. One the world would not take has nothing to change - its refusal is on
    // its way; a change the world will not take sends the body out with a refusal, as an add it would not take.
    private void ChangeBody(int key, BodyRequest request)
    {
        var handle = _handles[key];
        if (!handle.IsValid)
            return;

        var description = request.Description;
        try
        {
            // Only moved, it is only put there.
            if (request.ChangesSettings || request.ChangesShape)
                _world.Change(handle, description, request.ChangesShape);

            if (request.Moves)
                _world.SetPose(handle, new Pose(description.Position, description.Rotation));
        }
        catch (ArgumentException exception)
        {
            Log.Logger.Warning(exception, "The rigid body of {Entity} cannot be changed so; the physics leaves it",
                request.Name);
            TakeOutBody(key);
            _forces.Remove(key);
            _refusals.Add(new BodyRefusal(key, request.AddedAs, request.Number));
            return;
        }

        // A new generation sends where the body stands with the next result, as a body just added does.
        if (_generations[key] != request.Generation)
            _wasAwake[key] = description.Kind != BodyKind.Static;

        _generations[key] = request.Generation;
        if (description.Kind != BodyKind.Dynamic)
            _forces.Remove(key);
    }

    private void TakeOutBody(int key)
    {
        if (key >= _handles.Count || !_handles[key].IsValid)
            return;

        _world.Remove(_handles[key]);
        _handles[key] = default;
        _wasAwake[key] = false;
    }

    private void Publish(int steps)
    {
        if (!_spareResults.Reader.TryRead(out var result))
            result = new PhysicsStepResult();

        result.Motions.Clear();
        for (var key = 0; key < _handles.Count; key++)
        {
            var handle = _handles[key];
            if (!handle.IsValid)
                continue;

            var isAwake = _world.IsAwake(handle);
            if (isAwake || _wasAwake[key])
            {
                result.Motions.Add(new BodyMotion(key, _generations[key], _world.GetCenterOfMass(handle),
                    _world.GetLocalCenterOfMass(handle), _world.GetPose(handle).Rotation,
                    _world.GetLinearVelocity(handle), _world.GetAngularVelocity(handle)));
            }

            _wasAwake[key] = isAwake;
        }

        result.Refusals.Clear();
        result.Refusals.AddRange(_refusals);
        _refusals.Clear();
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
            foreach (var refusal in result.Refusals)
            {
                if (_links[refusal.Key] is { } refused && refused.AddedAs == refusal.Generation)
                {
                    Forget(refused);
                    _refused[refused.Entity] = refused.Parts;
                    // Something was sent for it after what was refused: what it is now may be taken.
                    if (refused.RequestsSent != refusal.Number)
                        refused.Parts.MarkStale();
                }
            }

            foreach (var motion in result.Motions)
            {
                if (FindMoved(motion) is not { } link)
                    continue;

                // Its first motion: nothing before it to draw from.
                var isFirst = link.MovedInResult < 0;
                link.CenterOfMass = motion.LocalCenterOfMass;
                link.PreviousPosition = isFirst ? motion.CenterOfMass : link.CurrentPosition;
                link.PreviousRotation = isFirst ? motion.Rotation : link.CurrentRotation;
                link.CurrentPosition = motion.CenterOfMass;
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
                if (FindMoved(motion) is { } link)
                    _moving.Add(link);
            }

            _lastResultAt = result.SteppedAt;
            _lastResultSteps = result.Steps;
            _lastStep = result.Statistics;
            _spareResults.Writer.TryWrite(result);
        }
    }

    // The link of the body that made motion; null for one taken out or made anew since.
    private PhysicsBodyLink FindMoved(in BodyMotion motion) =>
        _links[motion.Key] is { } link && link.Generation == motion.Generation ? link : null;

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
            // Gone, or not yet told where it stands.
            if (link.IsGone || link.MovedInResult < 0)
                continue;

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

    private void PlaceEntity(PhysicsBodyLink link, bool hasParent, in Matrix4x4F parentWorld, Vector3F centerOfMass,
        QuaternionF entityRotation)
    {
        var entityPosition = ToWorld(centerOfMass - Vector3F.Transform(link.CenterOfMass, entityRotation));
        var transform = link.Entity.Transform;
        // Changed by another since the last look: left for the next one to see, not taken as the physics' own.
        var isAsRead = link.Parts.IsBodyAsRead();
        if (!hasParent)
        {
            transform.SetPose(entityPosition, entityRotation, transform.BaseScale);
        }
        else
        {
            if (!parentWorld.Decompose(out _, out var parentRotation, out _))
                return;

            var local = Vector3F.TransformCoordinate(entityPosition.As<float>(), Matrix4x4F.Invert(parentWorld));
            transform.SetPose(local.As<double>(), QuaternionF.Conjugate(parentRotation) * entityRotation,
                transform.BaseScale);
        }

        // The physics' own change, not one to make the body anew for.
        link.WrittenPosition = transform.Position;
        link.WrittenRotation = transform.Rotation;
        if (isAsRead)
            link.Parts.RereadBody();
    }

    private Vector3F ToPhysics(Vector3<double> position) => (position - _origin).As<float>();

    private Vector3<double> ToWorld(Vector3F position) => position.As<double>() + _origin;

    private void Send(PhysicsCommandKind kind, int key, Vector3F value, Vector3F point) =>
        _commands.Writer.TryWrite(new PhysicsCommand(kind, key, value, point));

    private int FindKey(Entity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        if (!TryFindKey(entity, out var key))
            throw new ArgumentException($"The physics does not move {entity.Name}.", nameof(entity));

        return key;
    }

    // The entity's key; a rigid body given this frame, before the next look, is made now, so the game can push it at
    // once.
    private bool TryFindKey(Entity entity, out int key)
    {
        if (_keys.TryGetValue(entity, out key))
            return true;

        if (_world == null || entity.Scene != Scene || entity.GetComponent<RigidBody>() is not { } body
            || !IsEnabledUpTheTree(entity))
            return false;

        if (_refused.TryGetValue(entity, out var refused))
        {
            // Tried again as the next look would.
            if (!refused.HaveChanged() && !(refused.WaitsToStand && CanStand(entity, body.Kind, out _, out _)))
                return false;

            _refused.Remove(entity);
        }

        MakeBody(body);
        if (!_keys.TryGetValue(entity, out key))
            return false;

        // Its colliders were the body above it's: that one is made without them now, before the next step.
        for (var owner = entity.Owner; owner != null; owner = owner.Owner)
        {
            if (_keys.TryGetValue(owner, out var ownerKey))
            {
                if (owner.GetComponent<RigidBody>() is { } ownerBody)
                    Refresh(_links[ownerKey], ownerBody);

                break;
            }

            // A body above it the physics could not make holds nothing to take out.
            if (_refused.ContainsKey(owner))
                break;
        }

        return true;
    }

    private static bool IsEnabledUpTheTree(Entity entity)
    {
        for (var current = entity; current != null; current = current.Owner)
        {
            if (!current.IsEnabled)
                return false;
        }

        return true;
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

    // Whether the physics can stand entity: a body that moves needs a turn and an even scale above it and no mirror,
    // and none may be flattened or skewed.
    private static bool CanStand(Entity entity, BodyKind kind, out Matrix4x4F parentWorld, out Matrix4x4F world)
    {
        parentWorld = entity.Owner?.Transform.GetWorldMatrixF() ?? Matrix4x4F.Identity;
        world = entity.Transform.GetWorldMatrixF();
        if (kind != BodyKind.Static && (world.Determinant() <= 0 || !IsTurnAndEvenScale(parentWorld)))
            return false;

        return world.Determinant() != 0 && !IsSkewed(world);
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
