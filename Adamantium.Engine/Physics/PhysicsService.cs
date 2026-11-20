using System;
using System.Collections.Generic;
using Adamantium.Core;
using Adamantium.DarkMatter;
using Adamantium.DarkMatter.Shapes;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.Mathematics;
using Serilog;

namespace Adamantium.Engine.Physics;

/// <summary>The physics of a game: the scene's rigid bodies laid out as bodies of a <see cref="PhysicsSimulation"/>
/// stepping on a thread of its own, the entities stood where it draws them each frame, the game's pushes asked of it.
/// </summary>
public sealed partial class PhysicsService : GameService, IPhysicsService
{
    /// <summary>The service's <see cref="EntityService.Priority"/>: before the others, so the entities it stands are
    /// placed by the transforms in the same frame.</summary>
    public const int UpdatePriority = -1;

    private readonly bool _stepsOnOwnThread;
    private readonly List<PhysicsBodyLink> _links = [];
    private readonly Dictionary<Entity, int> _keys = new();
    // The links of bodies that move: their entities are stood where the simulation draws them.
    private readonly List<PhysicsBodyLink> _movers = [];
    private readonly List<PhysicsBodyLink> _placing = [];
    private readonly HashSet<int> _toldOfRefusal = [];
    // The entities whose mesh colliders on dynamic bodies were already said in the log, by id, so a body made again does
    // not say it each time and a gone entity is not held.
    private readonly HashSet<UInt128> _toldOfDynamicMesh = [];
    // Likewise the skewed entities whose round colliders were said to be left out.
    private readonly HashSet<UInt128> _toldOfSkewedRound = [];
    private readonly List<CompoundPart> _shapeParts = [];
    private readonly CarriedShapeCache _carriedShapes = new();
    private readonly List<int> _freeKeys = [];
    // Rigid bodies the physics could not make, tried again once what they are made of changes.
    private readonly Dictionary<Entity, BodyParts> _refused = new();
    private readonly List<Entity> _gone = [];

    private PhysicsSettings _settings = new();
    private Vector3F _gravity = new(0, 9.81f, 0);
    private PhysicsSettings _settingsBeforeGame;
    private Vector3F _gravityBeforeGame;
    private PhysicsSimulation _simulation;
    private long _frameNumber;
    private long _lookNumber;

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
            if (_simulation != null)
                _simulation.Gravity = value;
        }
    }

    /// <inheritdoc/>
    public StepStatistics LastStep => _simulation?.LastStep ?? default;

    /// <inheritdoc/>
    public bool Moves(Entity entity) => entity != null && TryFindKey(entity, out _);

    /// <inheritdoc/>
    public Vector3F GetLinearVelocity(Entity entity) => _simulation.LinearVelocityOf(_links[FindKey(entity)].Id);

    /// <inheritdoc/>
    public Vector3F GetAngularVelocity(Entity entity) => _simulation.AngularVelocityOf(_links[FindKey(entity)].Id);

    /// <inheritdoc/>
    public void SetLinearVelocity(Entity entity, Vector3F velocity)
    {
        RequireFinite(velocity, nameof(velocity));
        _simulation.SetLinearVelocity(_links[FindMovingKey(entity)].Id, velocity);
    }

    /// <inheritdoc/>
    public void SetAngularVelocity(Entity entity, Vector3F velocity)
    {
        RequireFinite(velocity, nameof(velocity));
        _simulation.SetAngularVelocity(_links[FindMovingKey(entity)].Id, velocity);
    }

    /// <inheritdoc/>
    public void SetForce(Entity entity, Vector3F force)
    {
        RequireFinite(force, nameof(force));
        _simulation.SetForce(_links[FindDynamicKey(entity)].Id, force);
    }

    /// <inheritdoc/>
    public void ApplyImpulse(Entity entity, Vector3F impulse)
    {
        RequireFinite(impulse, nameof(impulse));
        _simulation.ApplyImpulse(_links[FindDynamicKey(entity)].Id, impulse);
    }

    /// <inheritdoc/>
    public void ApplyImpulse(Entity entity, Vector3F impulse, WorldPosition point)
    {
        RequireFinite(impulse, nameof(impulse));
        if (!point.IsFinite)
            throw new ArgumentOutOfRangeException(nameof(point), point, "The point must be finite.");

        _simulation.ApplyImpulse(_links[FindDynamicKey(entity)].Id, impulse, point);
    }

    /// <inheritdoc/>
    public override void Update(AppTime appTime)
    {
        base.Update(appTime);
        if (_simulation == null)
            return;

        FollowBodies();
        if (_stepsOnOwnThread)
            _simulation.Update();
        else
            _simulation.Advance(appTime.FrameTime);

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
        _simulation = new PhysicsSimulation(Settings) { Gravity = _gravity };
        FollowBodies();
        if (_stepsOnOwnThread)
            _simulation.Start();
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

    // Paused, nothing moves; going on, the time paused is not stepped.
    protected override void OnPause() => _simulation?.Pause();

    protected override void OnResume()
    {
        if (_stepsOnOwnThread)
            _simulation?.Start();
    }

    private void EndGame()
    {
        _simulation?.Dispose();
        _simulation = null;
        _links.Clear();
        _keys.Clear();
        _movers.Clear();
        _placing.Clear();
        _toldOfRefusal.Clear();
        _toldOfDynamicMesh.Clear();
        _toldOfSkewedRound.Clear();
        _carriedShapes.Clear();
        _freeKeys.Clear();
        _refused.Clear();
        ForgetJoints();
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
                if (!KeptBySimulation(link))
                    continue;

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
        FollowJoints();
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

    // What the simulation told of the body at the last read: one it would not take is left, and tried again once
    // what it is made of changes; a change it would not do is said once, the body kept as it was.
    private bool KeptBySimulation(PhysicsBodyLink link)
    {
        var status = _simulation.StatusOf(link.Id);
        if (status == PhysicsStatus.Refused)
        {
            Log.Logger.Warning("The rigid body of {Entity} cannot be added; the physics leaves it until it changes: "
                               + "{Reason}", link.Name, _simulation.RefusalOf(link.Id));
            Forget(link);
            _refused[link.Entity] = link.Parts;
            return false;
        }

        if (_simulation.RefusalOf(link.Id) is { } refusal && _toldOfRefusal.Add(link.Key))
            Log.Logger.Warning("The physics could not change the body of {Entity} so: {Reason}", link.Name, refusal);

        return true;
    }

    // The body brought up to its entities as they are now, where it stands, its contacts kept: put where it was moved
    // by hand, given its new kind, shape and settings; left as it is when none changed - the entity selected, say.
    private void Refresh(PhysicsBodyLink link, RigidBody body)
    {
        var parts = new BodyParts { SeenInLook = _lookNumber };
        var description = Describe(body, parts, out var parentWorld);
        var entity = link.Entity;
        if (description == null)
        {
            _carriedShapes.GiveBack(parts.CarriedShapes);
            Forget(link);
            _refused[entity] = parts;
            return;
        }

        KeepUsableSettings(description, link.Made);
        // Made dynamic with nothing to weigh it by, it cannot be: left until it is given a weight.
        if (description.Kind == BodyKind.Dynamic && !IsWeight(description.Mass, description.Density))
        {
            Log.Logger.Warning("The dynamic body of {Entity} has no mass and no density; the physics leaves it until it "
                               + "has one", entity.Name);
            _carriedShapes.GiveBack(parts.CarriedShapes);
            Forget(link);
            _refused[entity] = parts;
            return;
        }

        // Hung elsewhere in the tree: the same body, followed by a link of its new place.
        if ((entity.Owner != null) != link.IsParented || CountDepth(entity) != link.Depth)
        {
            var transform = entity.Transform;
            var keepsPose = link.Kind != BodyKind.Static && body.Kind != BodyKind.Static
                            && transform.Position == link.WrittenPosition && transform.Rotation == link.WrittenRotation;
            Rehang(link, body, parts, description, parentWorld, keepsPose);
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
            KeepShapes(link, parts, description);
        }

        var before = link.Made;
        link.Parts = parts;
        link.Body = body;
        if (changesKind)
            ChangeKind(link, description.Kind);

        // A moving body's is what it was last stood under, for MoveEntities to see its parent move.
        if (link.Kind == BodyKind.Static)
            link.ParentWorld = parentWorld;

        if (changesShape || changesSettings || moves)
            ChangeBody(link, before, description, changesShape, changesSettings, moves);
    }

    // Its body hung under another, or under none: followed anew from its new place, the same body of the simulation.
    private void Rehang(PhysicsBodyLink previous, RigidBody body, BodyParts parts, BodyDescription description,
        Matrix4x4<double> parentWorld, bool keepsPose)
    {
        var changesShape = !ShapeLikeness.AreAlike(previous.Parts.ShapeParts, parts.ShapeParts);
        var changesSettings = !HasSameSettings(previous.Made, description);
        if (!changesShape)
            KeepShapes(previous, parts, description);

        Retire(previous);
        // Its entity left where its own transform puts it under the new parent: stood back where the body stands.
        var link = new PhysicsBodyLink(body, parts, description, previous.Key, CountDepth(body.Owner))
        {
            Id = previous.Id, ParentWorld = parentWorld, PlacedPose = previous.PlacedPose, MustPlace = keepsPose
        };
        _links[link.Key] = link;
        if (link.Kind != BodyKind.Static)
            _movers.Add(link);

        // A kind changed starts from rest where the body was last drawn, as in Refresh; its entity is stood back there.
        var changesKind = previous.Made.Kind != description.Kind;
        ChangeBody(link, previous.Made, description, changesShape, changesSettings, !keepsPose || changesKind,
            keepsPose ? previous.PlacedPose : null);
    }

    // The body keeps the shapes it has, its link's parts handed to parts: compared with them later, so changes too
    // small to count each time add up, and holding the hull and mesh shapes it uses - the link lets go of none.
    private void KeepShapes(PhysicsBodyLink link, BodyParts parts, BodyDescription description)
    {
        _carriedShapes.GiveBack(parts.CarriedShapes);
        parts.CarriedShapes.AddRange(link.Parts.CarriedShapes);
        link.Parts.CarriedShapes.Clear();
        parts.ShapeParts = link.Parts.ShapeParts;
        description.Shape = link.Made.Shape;
    }

    // Only a body that moves is stood where the simulation draws it.
    private void ChangeKind(PhysicsBodyLink link, BodyKind kind)
    {
        if (link.Kind != BodyKind.Static)
            _movers.Remove(link);

        link.Kind = kind;
        if (kind != BodyKind.Static)
            _movers.Add(link);
    }

    // A parent above a static body changed: brought up to it only when the parents now stand elsewhere, not for a
    // child hung under one of them.
    private void FollowParents(PhysicsBodyLink link, RigidBody body)
    {
        link.Parts.RereadAncestors();
        if (link.Entity.Owner?.Transform.GetWorldMatrix() != link.ParentWorld)
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

    // The body given description's kind, settings and - when it changed - shape, asked of the simulation in an order
    // it can take: a kind that holds no mesh before the mesh; a kind that weighs after the shape and the weight it is
    // weighed by - only a dynamic body is sent a weight, which may be half typed while the body is not. Moved, it is
    // put where its entity stands, or at putAt.
    private void ChangeBody(PhysicsBodyLink link, BodyDescription before, BodyDescription description, bool changesShape,
        bool changesSettings, bool moves, Pose? putAt = null)
    {
        link.Made = description;
        var id = link.Id;
        // A kind changed changes the settings compared too.
        var changesKind = before.Kind != description.Kind;
        if (changesKind && description.Kind != BodyKind.Dynamic)
            _simulation.SetKind(id, description.Kind);

        if (changesShape)
            _simulation.SetShape(id, description.Shape);

        if (changesSettings)
        {
            _simulation.SetMaterial(id, new PhysicsMaterial
            {
                Friction = description.Friction, Restitution = description.Restitution
            });
            if (description.Kind == BodyKind.Dynamic)
                _simulation.SetMass(id, description.Mass, description.Density);

            _simulation.SetDamping(id, description.LinearDamping, description.AngularDamping);
            _simulation.SetGravityScale(id, description.GravityScale);
            _simulation.SetCanSleep(id, description.CanSleep);
        }

        if (changesKind && description.Kind == BodyKind.Dynamic)
            _simulation.SetKind(id, description.Kind);

        if (!moves)
            return;

        var transform = link.Entity.Transform;
        link.WrittenPosition = transform.Position;
        link.WrittenRotation = transform.Rotation;
        _simulation.SetPose(id, putAt ?? new Pose(description.Position, description.Rotation));
        // As the simulation draws it, so the next frame finds it standing there.
        link.PlacedPose = _simulation.InterpolatedPoseOf(id);
    }

    // Makes a body of body under a key of its own; one the physics cannot make is left until it changes.
    private void MakeBody(RigidBody body)
    {
        var parts = new BodyParts { SeenInLook = _lookNumber };
        var description = Describe(body, parts, out var parentWorld);
        var entity = body.Owner;
        if (description == null)
        {
            _carriedShapes.GiveBack(parts.CarriedShapes);
            _refused[entity] = parts;
            return;
        }

        var key = TakeKey();
        var link = new PhysicsBodyLink(body, parts, description, key, CountDepth(entity)) { ParentWorld = parentWorld };
        link.Id = _simulation.AddBody(new BodySettings
        {
            Kind = description.Kind,
            Shape = description.Shape,
            Position = description.Position,
            Rotation = description.Rotation,
            Mass = description.Mass,
            Density = description.Density,
            Material = new PhysicsMaterial { Friction = description.Friction, Restitution = description.Restitution },
            LinearDamping = description.LinearDamping,
            AngularDamping = description.AngularDamping,
            GravityScale = description.GravityScale,
            CanSleep = description.CanSleep,
            Data = entity
        });
        // As the simulation draws it, its turn of length one; a static body is not stood.
        if (link.Kind != BodyKind.Static)
            link.PlacedPose = _simulation.InterpolatedPoseOf(link.Id);

        _keys[entity] = key;
        _links[key] = link;
        if (link.Kind != BodyKind.Static)
            _movers.Add(link);
    }

    private void Forget(PhysicsBodyLink link)
    {
        Retire(link);
        if (_simulation.StatusOf(link.Id) != PhysicsStatus.Removed)
            _simulation.RemoveBody(link.Id);

        _links[link.Key] = null;
        _keys.Remove(link.Entity);
        _freeKeys.Add(link.Key);
        _toldOfRefusal.Remove(link.Key);
    }

    // The link is passed over from now on, its hull and mesh shapes given back.
    private void Retire(PhysicsBodyLink link)
    {
        link.IsGone = true;
        _carriedShapes.GiveBack(link.Parts.CarriedShapes);
        if (link.Kind != BodyKind.Static)
            _movers.Remove(link);
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
        return _links.Count - 1;
    }

    // The body body makes, its entities put in parts; null - logged - for one the physics cannot make.
    private BodyDescription Describe(RigidBody body, BodyParts parts, out Matrix4x4<double> parentWorld)
    {
        var entity = body.Owner;
        parts.Add(entity);
        if (!CanStand(entity, body.Kind, out parentWorld, out var world)
            || !TurnAndScaleOf(world).Decompose(out var scale, out var rotation, out _))
        {
            Log.Logger.Warning("{Entity} or its parent is flattened, mirrored, skewed or stretched unevenly; "
                               + "the physics leaves it", entity.Name);
            parts.WaitsToStand = true;
            return null;
        }

        parts.Scale = scale;
        // A static body stands where its parents put it: one of them moved, it is moved too.
        if (body.Kind == BodyKind.Static)
        {
            for (var owner = entity.Owner; owner != null; owner = owner.Owner)
            {
                parts.AddAncestor(owner);
            }
        }

        var translation = world.TranslationVector;
        var shape = CreateShape(entity, body.Kind, translation, rotation, scale, parts);
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
            Position = new WorldPosition(translation),
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
    private CollisionShape CreateShape(Entity entity, BodyKind kind, Vector3<double> origin, QuaternionF rotation,
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

    private void AddColliders(Entity body, BodyKind kind, Entity entity, Vector3<double> origin, QuaternionF unturn,
        Vector3F bodyScale, List<CarriedShapeKey> carriedShapesTaken)
    {
        // The body's own colliders stand on it exactly, as their offsets say.
        var placed = entity == body;
        var scale = bodyScale;
        var turn = QuaternionF.Identity;
        // Where the entity stands from the body's origin, taken in double: both may be far out together.
        var fromBody = Vector3F.Zero;
        // An entity turned under an uneven scale is skewed: its colliders are carried into the body by its whole matrix.
        var isSkewed = false;
        var carry = Matrix4x4F.Identity;
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
                var placedWorld = entity.Transform.GetWorldMatrix();
                var world = TurnAndScaleOf(placedWorld);
                isSkewed = world.Determinant() != 0 && IsSkewed(world);
                if (!isSkewed && (world.Determinant() == 0 || !world.Decompose(out scale, out turn, out _)))
                {
                    Log.Logger.Warning("{Entity} is flattened; the body of {Body} leaves its colliders", entity.Name,
                        body.Name);
                    return;
                }

                if (isSkewed)
                    carry = CarryIntoBody(body, entity, bodyScale);

                fromBody = (placedWorld.TranslationVector - origin).As<float>();
                placed = true;
            }

            if (isSkewed)
            {
                var position = Vector3F.Transform(fromBody, unturn);
                AddSkewedPartCollider(body, entity, collider, carry, position, carriedShapesTaken);
                continue;
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
                _shapeParts.Add(new CompoundPart(shape.Shape, new LocalPose(shape.Offset, shape.Turn)));
                continue;
            }

            var center = fromBody + Vector3F.Transform(shape.Offset, turn);
            _shapeParts.Add(new CompoundPart(shape.Shape,
                new LocalPose(Vector3F.Transform(center, unturn), unturn * turn * shape.Turn)));
        }
    }

    // A skewed entity's collider as the hull its corners make, carried into the body and standing at position there; a
    // round one has no hull that fits it - a ball skewed is an egg - and is left out, said once.
    private void AddSkewedPartCollider(Entity body, Entity entity, Collider collider, in Matrix4x4F carry,
        Vector3F position, List<CarriedShapeKey> carriedShapesTaken)
    {
        try
        {
            if (!ColliderShape.TryCreateSkewed(collider, carry, _carriedShapes, carriedShapesTaken, out var shape))
            {
                if (_toldOfSkewedRound.Add(entity.Uid))
                    Log.Logger.Warning("{Entity} is skewed, and its {Collider} is round; the body of {Body} leaves it",
                        entity.Name, collider.GetType().Name, body.Name);

                return;
            }

            _shapeParts.Add(new CompoundPart(shape, new LocalPose(position)));
        }
        catch (ArgumentException exception)
        {
            Log.Logger.Warning(exception, "The body of {Body} leaves the collider of {Entity}", body.Name, entity.Name);
        }
    }

    // How the part's own axes stand in the body's, its place left out: the turns and scales between them, then the
    // body's scale. The body's own turn and its parents' are the body's frame, so they do not enter: no rounding of a
    // body turned in the world reaches the shape, and the shape stays the same while the body moves.
    private static Matrix4x4F CarryIntoBody(Entity body, Entity part, Vector3F bodyScale)
    {
        var carry = Matrix4x4F.Identity;
        for (var at = part; at != body && at != null; at = at.Owner)
        {
            var local = at.Transform.GetLocalMatrixF();
            local.TranslationVector = Vector3F.Zero;
            carry *= local;
        }

        return carry * Matrix4x4.CreateScale<float>(bodyScale);
    }

    // Each entity of a body that moves stood where the simulation draws its body now, once it moved or its parent did;
    // parents before their children.
    private void MoveEntities()
    {
        _frameNumber++;
        _placing.Clear();
        foreach (var link in _movers)
        {
            if (!link.IsGone)
                _placing.Add(link);
        }

        _placing.Sort(CompareDepths);
        foreach (var link in _placing)
        {
            var pose = _simulation.InterpolatedPoseOf(link.Id);
            var parent = link.Entity.Owner;
            var parentWorld = parent?.Transform.GetWorldMatrix() ?? Matrix4x4<double>.Identity;
            var stands = pose.Position == link.PlacedPose.Position && pose.Rotation == link.PlacedPose.Rotation;
            if (stands && parentWorld == link.ParentWorld && !link.MustPlace)
                continue;

            link.MustPlace = false;
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
            link.PlacedPose = pose;
            PlaceEntity(link, parent != null, parentWorld, pose);
        }
    }

    private void PlaceEntity(PhysicsBodyLink link, bool hasParent, in Matrix4x4<double> parentWorld, in Pose pose)
    {
        var transform = link.Entity.Transform;
        // Changed by another since the last look: left for the next one to see, not taken as the physics' own.
        var isAsRead = link.Parts.IsBodyAsRead();
        if (!hasParent)
        {
            transform.SetPose(pose.Position, pose.Rotation, transform.BaseScale);
        }
        else
        {
            if (!TurnAndScaleOf(parentWorld).Decompose(out _, out var parentRotation, out _))
                return;

            // Into the parent's axes in double: both may stand far out, the way between them small.
            var local = new WorldPosition(Vector3<double>.TransformCoordinate(pose.Position.ToVector(),
                Matrix4x4<double>.Invert(parentWorld)));
            transform.SetPose(local, QuaternionF.Conjugate(parentRotation) * pose.Rotation, transform.BaseScale);
        }

        // The physics' own change, not one to make the body anew for.
        link.WrittenPosition = transform.Position;
        link.WrittenRotation = transform.Rotation;
        if (isAsRead)
            link.Parts.RereadBody();
    }

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
        // One the simulation would not take is left now, not pushed in vain.
        if (_keys.TryGetValue(entity, out key) && KeptBySimulation(_links[key]))
            return true;

        if (_simulation == null || entity.Scene != Scene || entity.GetComponent<RigidBody>() is not { } body
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
                if (owner.GetComponent<RigidBody>() is { } ownerBody && KeptBySimulation(_links[ownerKey]))
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
    private static bool CanStand(Entity entity, BodyKind kind, out Matrix4x4<double> parentWorld,
        out Matrix4x4<double> world)
    {
        parentWorld = entity.Owner?.Transform.GetWorldMatrix() ?? Matrix4x4<double>.Identity;
        world = entity.Transform.GetWorldMatrix();
        var turnAndScale = TurnAndScaleOf(world);
        if (kind != BodyKind.Static && (turnAndScale.Determinant() <= 0 || !IsTurnAndEvenScale(parentWorld)))
            return false;

        return turnAndScale.Determinant() != 0 && !IsSkewed(turnAndScale);
    }

    // A world's turn and scale in float, its place left out: what the checks of its shape look at.
    private static Matrix4x4F TurnAndScaleOf(in Matrix4x4<double> world)
    {
        var turnAndScale = world;
        turnAndScale.TranslationVector = Vector3<double>.Zero;
        return turnAndScale.As<float>();
    }

    private static bool IsSkewed(in Matrix4x4F world) => Matrix4x4.IsSkewed(world);

    private static bool IsTurnAndEvenScale(in Matrix4x4<double> placedWorld)
    {
        var world = TurnAndScaleOf(placedWorld);
        if (world.Determinant() <= 0 || IsSkewed(world))
            return false;

        var lengthX = Vector3F.TransformNormal(Vector3F.UnitX, world).Length();
        var lengthY = Vector3F.TransformNormal(Vector3F.UnitY, world).Length();
        var lengthZ = Vector3F.TransformNormal(Vector3F.UnitZ, world).Length();
        return MathF.Abs(lengthX - lengthY) <= 1e-4f * lengthX && MathF.Abs(lengthX - lengthZ) <= 1e-4f * lengthX;
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
        if (!IsFinite(vector))
            throw new ArgumentOutOfRangeException(parameterName, vector, "The vector must be finite.");
    }

    private static bool IsFinite(Vector3F vector) =>
        float.IsFinite(vector.X) && float.IsFinite(vector.Y) && float.IsFinite(vector.Z);
}
