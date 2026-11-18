using System;
using System.Collections.Generic;
using System.Diagnostics;
using Adamantium.DarkMatter.CollisionDetection;
using Adamantium.DarkMatter.Dynamics;
using Adamantium.DarkMatter.Shapes;

namespace Adamantium.DarkMatter;

/// <summary>The simulation: the same bodies and calls always step to the same result. One thread owns a world.
/// </summary>
public sealed partial class PhysicsWorld : IPhysicsWorld
{
    private const int NoBody = -1;
    private const int NoPart = -1;
    private const float MatchDistanceSquared = 0.02f * 0.02f;

    private readonly DynamicAabbTree<int> _tree;
    private readonly Dictionary<ulong, Contact> _contactsByKey = new(PairKeyComparer.Instance);
    private readonly List<Contact> _contacts = [];
    private readonly Stack<Contact> _spareContacts = new();
    private readonly Stack<int> _freeSlots = new();
    private readonly Stack<int> _freeParts = new();
    private readonly List<int> _found = [];
    private readonly List<int> _woken = [];
    private readonly List<int> _marked = [];
    private readonly ManifoldBuffer _manifold = new();
    private readonly ContactSolver _solver = new();
    private readonly ContactPointState[] _previousPoints;
    private readonly bool[] _matched;

    private BodyState[] _bodies = new BodyState[64];
    private int[] _islandParent = new int[64];
    private float[] _islandSleepTime = new float[64];
    private int[] _islandFirst = new int[64];
    private int[] _islandLast = new int[64];
    private PartState[] _parts = new PartState[64];
    private int _slotCount;
    private int _partCount;
    private int _newSleepers;
    private Vector3F _gravity;

    /// <summary>A world stepped as <paramref name="settings"/> say, or with the defaults.</summary>
    public PhysicsWorld(PhysicsSettings settings = null)
    {
        Settings = settings ?? new PhysicsSettings();
        ValidateSettings(Settings);
        MaxAngularSpeed = 0.25f * MathF.PI * Settings.StepRate * Settings.Substeps;
        _gravity = Settings.Gravity;
        _tree = new DynamicAabbTree<int>(0.1f, Math.Max(0.05f, Settings.SpeculativeDistance));
        _previousPoints = new ContactPointState[Settings.MaxContactPointsPerPair];
        _matched = new bool[Settings.MaxContactPointsPerPair];
    }

    /// <inheritdoc/>
    public PhysicsSettings Settings { get; }

    /// <inheritdoc/>
    public Vector3F Gravity
    {
        get => _gravity;
        set
        {
            RequireFinite(value, nameof(value));
            if (value == _gravity)
                return;

            _gravity = value;
            for (var index = 0; index < _slotCount; index++)
            {
                if (_bodies[index].IsUsed && _bodies[index].IsDynamic && _bodies[index].GravityScale != 0)
                    WakeIsland(index);
            }
        }
    }

    /// <inheritdoc/>
    public int BodyCount { get; private set; }

    /// <inheritdoc/>
    public StepStatistics LastStep { get; private set; }

    /// <inheritdoc/>
    public float StepSeconds => 1f / Settings.StepRate;

    /// <inheritdoc/>
    public float MaxAngularSpeed { get; }

    /// <inheritdoc/>
    public BodyHandle Add(BodyDescription description)
    {
        ValidateDescription(description);
        var rotation = NormalizeRotation(description.Rotation, nameof(description));
        var mass = default(MassProperties);
        if (description.Kind == BodyKind.Dynamic)
            mass = ComputeMass(description);

        var index = TakeSlot();
        ref var state = ref _bodies[index];
        var generation = state.Generation + 1;
        var listed = state.Marks & BodyMarks.Listed;
        state = default;
        state.IsUsed = true;
        state.Generation = generation;
        state.Marks = listed;
        state.Kind = description.Kind;
        state.FirstPart = NoPart;
        state.CenterOfMass = mass.Center;
        state.InertiaRotation = description.Kind == BodyKind.Dynamic ? mass.InertiaRotation : QuaternionF.Identity;
        state.Position = description.Position + Vector3F.Transform(mass.Center, rotation);
        state.Rotation = rotation;
        state.StartPosition = state.Position;
        state.StartRotation = state.Rotation;
        state.Friction = description.Friction;
        state.Restitution = description.Restitution;
        state.LinearDamping = description.LinearDamping;
        state.AngularDamping = description.AngularDamping;
        state.GravityScale = description.GravityScale;
        state.CanSleep = description.CanSleep;
        state.IslandHead = NoBody;
        state.IslandNext = NoBody;

        if (description.Kind == BodyKind.Kinematic || (description.Kind == BodyKind.Dynamic && description.IsAwake))
        {
            state.LinearVelocity = LimitSpeed(description.LinearVelocity, Settings.MaxLinearSpeed);
            state.AngularVelocity = LimitSpeed(description.AngularVelocity, MaxAngularSpeed);
            state.IsAwake = description.Kind == BodyKind.Dynamic || description.LinearVelocity != Vector3F.Zero
                                                                 || description.AngularVelocity != Vector3F.Zero;
        }

        if (description.Kind == BodyKind.Dynamic)
        {
            state.InverseMass = 1f / mass.Mass;
            state.InverseInertia = new Vector3F(1f / mass.Inertia.X, 1f / mass.Inertia.Y, 1f / mass.Inertia.Z);
            state.WorldInverseInertia = state.MeasureWorldInverseInertia();
            if (!state.IsAwake)
                state.IslandHead = index;
        }

        AddParts(index, description.Shape);
        var marks = BodyMarks.Moved | BodyMarks.New;
        if (state.IsDynamic && !state.IsAwake)
            _newSleepers++;
        else
            marks |= BodyMarks.Placed;

        Mark(index, marks);
        BodyCount++;
        return new BodyHandle(this, index, generation);
    }

    /// <inheritdoc/>
    public void Remove(BodyHandle body)
    {
        var index = Resolve(body);
        WakeIsland(index);
        ref var state = ref _bodies[index];
        while (state.Contacts != null)
        {
            var contact = state.Contacts;
            if (contact.IsTouching)
                WakeIsland(contact.GetOther(index));

            DestroyContact(contact);
        }

        WakeNewSleepersNear(index);
        RemoveParts(index);
        var generation = state.Generation;
        var listed = state.Marks & BodyMarks.Listed;
        state = default;
        state.Generation = generation;
        state.Marks = listed;
        if (generation < int.MaxValue)
            _freeSlots.Push(index);

        BodyCount--;
    }

    /// <inheritdoc/>
    public bool Contains(BodyHandle body) =>
        ReferenceEquals(body.World, this) && body.Index >= 0 && body.Index < _slotCount && _bodies[body.Index].IsUsed
        && _bodies[body.Index].Generation == body.Generation;

    /// <inheritdoc/>
    public BodyKind GetKind(BodyHandle body) => _bodies[Resolve(body)].Kind;

    /// <inheritdoc/>
    public Pose GetPose(BodyHandle body) => _bodies[Resolve(body)].OriginPose;

    /// <inheritdoc/>
    public Vector3F GetCenterOfMass(BodyHandle body) => _bodies[Resolve(body)].Position;

    /// <inheritdoc/>
    public Vector3F GetLocalCenterOfMass(BodyHandle body) => _bodies[Resolve(body)].CenterOfMass;

    /// <inheritdoc/>
    public void SetPose(BodyHandle body, in Pose pose)
    {
        var index = Resolve(body);
        RequireFinite(pose.Position, nameof(pose));
        var rotation = NormalizeRotation(pose.Rotation, nameof(pose));
        WakeNewSleepersNear(index);
        ref var state = ref _bodies[index];
        state.Position = pose.Position + Vector3F.Transform(state.CenterOfMass, rotation);
        state.Rotation = rotation;
        if (state.IsDynamic)
            state.WorldInverseInertia = state.MeasureWorldInverseInertia();

        var moved = MoveParts(index);
        Mark(index, moved ? BodyMarks.Placed | BodyMarks.Moved : BodyMarks.Placed);
        WakeIsland(index);
        WakeTouching(index);
    }

    /// <inheritdoc/>
    public Vector3F GetLinearVelocity(BodyHandle body) => _bodies[Resolve(body)].LinearVelocity;

    /// <inheritdoc/>
    public void SetLinearVelocity(BodyHandle body, Vector3F velocity)
    {
        RequireFinite(velocity, nameof(velocity));
        var index = ResolveMoving(body);
        _bodies[index].LinearVelocity = LimitSpeed(velocity, Settings.MaxLinearSpeed);
        WakeIsland(index);
    }

    /// <inheritdoc/>
    public Vector3F GetAngularVelocity(BodyHandle body) => _bodies[Resolve(body)].AngularVelocity;

    /// <inheritdoc/>
    public void SetAngularVelocity(BodyHandle body, Vector3F velocity)
    {
        RequireFinite(velocity, nameof(velocity));
        var index = ResolveMoving(body);
        _bodies[index].AngularVelocity = LimitSpeed(velocity, MaxAngularSpeed);
        WakeIsland(index);
    }

    /// <inheritdoc/>
    public void ApplyForce(BodyHandle body, Vector3F force)
    {
        var index = ResolveDynamic(body);
        if (force == Vector3F.Zero)
            return;

        var total = _bodies[index].Force + force;
        RequireFinite(total * _bodies[index].InverseMass, nameof(force));
        _bodies[index].Force = total;
        MarkPushed(index);
    }

    /// <inheritdoc/>
    public void ApplyTorque(BodyHandle body, Vector3F torque)
    {
        var index = ResolveDynamic(body);
        if (torque == Vector3F.Zero)
            return;

        var total = _bodies[index].Torque + torque;
        RequireFinite(InertiaMath.Apply(_bodies[index].WorldInverseInertia, total), nameof(torque));
        _bodies[index].Torque = total;
        MarkPushed(index);
    }

    /// <inheritdoc/>
    public void ApplyImpulse(BodyHandle body, Vector3F impulse, Vector3F point)
    {
        RequireFinite(impulse, nameof(impulse));
        RequireFinite(point, nameof(point));
        var index = ResolveDynamic(body);
        ref var state = ref _bodies[index];
        var linear = state.LinearVelocity + impulse * state.InverseMass;
        var angular = state.AngularVelocity
                      + InertiaMath.Apply(state.WorldInverseInertia, Vector3F.Cross(point - state.Position, impulse));
        RequireFinite(linear, nameof(impulse));
        RequireFinite(angular, nameof(impulse));
        state.LinearVelocity = LimitSpeed(linear, Settings.MaxLinearSpeed);
        state.AngularVelocity = LimitSpeed(angular, MaxAngularSpeed);
        WakeIsland(index);
    }

    /// <inheritdoc/>
    public bool IsAwake(BodyHandle body) => _bodies[Resolve(body)].IsAwake;

    /// <inheritdoc/>
    public void WakeUp(BodyHandle body) => WakeIsland(Resolve(body));

    /// <inheritdoc/>
    public BoundingBox<float> GetBounds(BodyHandle body)
    {
        ref var state = ref _bodies[Resolve(body)];
        var part = state.FirstPart;
        var bounds = _parts[part].Shape.ComputeBounds(PlacePart(state, _parts[part]));
        for (part = _parts[part].Next; part != NoPart; part = _parts[part].Next)
        {
            var partBounds = _parts[part].Shape.ComputeBounds(PlacePart(state, _parts[part]));
            bounds = new BoundingBox<float>(Vector3F.Min(bounds.Minimum, partBounds.Minimum),
                Vector3F.Max(bounds.Maximum, partBounds.Maximum));
        }

        return bounds;
    }

    /// <inheritdoc/>
    public void GetContactPoints(ICollection<ContactPoint> into)
    {
        ArgumentNullException.ThrowIfNull(into);
        foreach (var contact in _contacts)
        {
            var first = GetHandle(contact.First);
            var second = GetHandle(contact.Second);
            for (var index = 0; index < contact.PointCount; index++)
            {
                ref var point = ref contact.Points[index];
                into.Add(new ContactPoint(first, second, point.Position, contact.Normal, point.Separation,
                    point.NormalImpulse));
            }
        }
    }

    /// <inheritdoc/>
    public void Step()
    {
        var startedAt = Stopwatch.GetTimestamp();
        var stepSeconds = StepSeconds;
        var substep = stepSeconds / Settings.Substeps;
        var inverseSubstep = 1f / substep;

        UpdateBroadPhase();
        FindNewPairs();
        UpdateContacts(stepSeconds);
        ClearMarks();
        WakeTouchedSleepers();

        for (var index = 0; index < _slotCount; index++)
        {
            ref var body = ref _bodies[index];
            body.StartPosition = body.Position;
            body.StartRotation = body.Rotation;
        }

        _solver.Prepare(_bodies, _contacts);
        var contactHertz = MathF.Min(Settings.ContactHertz, 0.25f * inverseSubstep);
        var contactSoftness = Softness.Create(contactHertz, Settings.ContactDampingRatio, substep);
        var staticSoftness = Softness.Create(2f * contactHertz, Settings.ContactDampingRatio, substep);

        for (var pass = 0; pass < Settings.Substeps; pass++)
        {
            IntegrateVelocities(substep);
            _solver.WarmStart(_bodies);
            _solver.Solve(_bodies, inverseSubstep, true, contactSoftness, staticSoftness, Settings.MaxPushOutSpeed);
            IntegratePositions(substep);
            _solver.Solve(_bodies, inverseSubstep, false, contactSoftness, staticSoftness, Settings.MaxPushOutSpeed);
        }

        _solver.ApplyRestitution(_bodies, Settings.RestitutionThreshold);
        FinishStep(stepSeconds);
        RecordStatistics(startedAt);
    }

    private void UpdateBroadPhase()
    {
        for (var index = 0; index < _slotCount; index++)
        {
            ref var body = ref _bodies[index];
            if (!body.IsUsed || !body.IsMoving)
                continue;

            if (MoveParts(index))
                Mark(index, BodyMarks.Moved);
        }
    }

    private bool MoveParts(int index)
    {
        ref var body = ref _bodies[index];
        var moved = false;
        for (var part = body.FirstPart; part != NoPart; part = _parts[part].Next)
        {
            moved |= _tree.Move(_parts[part].Proxy, ComputeProxyBounds(body, _parts[part]));
        }

        return moved;
    }

    private BoundingBox<float> ComputeProxyBounds(in BodyState body, in PartState part)
    {
        var bounds = part.Shape.ComputeBounds(PlacePart(body, part));
        var travel = body.LinearVelocity * StepSeconds;
        var swing = new Vector3F(MeasureSweepSpeed(body, part) * StepSeconds);
        return new BoundingBox<float>(bounds.Minimum + Vector3F.Min(travel, Vector3F.Zero) - swing,
            bounds.Maximum + Vector3F.Max(travel, Vector3F.Zero) + swing);
    }

    private static Pose PlacePart(in BodyState body, in PartState part) =>
        part.HasBodyPose ? body.Pose : body.Pose.Transform(part.Pose);

    /// <summary>How fast the space the part fills sweeps outward as its body spins: a part off the center of mass
    /// swings around it.</summary>
    private static float MeasureSweepSpeed(in BodyState body, in PartState part) =>
        part.IsCentered
            ? part.Shape.MeasureSweepSpeed(body.AngularVelocity, body.Rotation * part.Pose.Rotation)
            : body.AngularVelocity.Length() * (part.Pose.Position.Length() + part.Shape.BoundingRadius);

    private void FindNewPairs()
    {
        foreach (var index in _marked)
        {
            ref var body = ref _bodies[index];
            if (!body.IsUsed || !body.IsMoved)
                continue;

            for (var part = body.FirstPart; part != NoPart; part = _parts[part].Next)
            {
                _found.Clear();
                _tree.Query(_tree.BoxOf(_parts[part].Proxy), _found);
                foreach (var otherPart in _found)
                {
                    var other = _parts[otherPart].Body;
                    if (other == index)
                        continue;

                    ref var otherBody = ref _bodies[other];
                    if (otherBody.IsMoved && otherPart < part)
                        continue;

                    if (!body.IsDynamic && !otherBody.IsDynamic)
                        continue;

                    var key = Contact.MakeKey(part, otherPart);
                    if (!_contactsByKey.ContainsKey(key))
                        CreateContact(key, Math.Min(part, otherPart), Math.Max(part, otherPart));
                }
            }
        }
    }

    private void UpdateContacts(float stepSeconds)
    {
        var contactIndex = 0;
        while (contactIndex < _contacts.Count)
        {
            var contact = _contacts[contactIndex];
            ref var first = ref _bodies[contact.First];
            ref var second = ref _bodies[contact.Second];
            if (!first.IsMoving && !second.IsMoving && first.Marks == BodyMarks.None && second.Marks == BodyMarks.None)
            {
                contactIndex++;
                continue;
            }

            ref var firstPart = ref _parts[contact.FirstPart];
            ref var secondPart = ref _parts[contact.SecondPart];
            if (!Collision.BoxIntersectsBox(_tree.BoxOf(firstPart.Proxy), _tree.BoxOf(secondPart.Proxy)))
            {
                DestroyContact(contact);
                continue;
            }

            var closingSpeed = (second.LinearVelocity - first.LinearVelocity).Length()
                               + MeasureSweepSpeed(first, firstPart) + MeasureSweepSpeed(second, secondPart);
            var speculativeDistance = Settings.SpeculativeDistance + closingSpeed * stepSeconds;
            if (Narrowphase.Collide(firstPart.Shape, PlacePart(first, firstPart), secondPart.Shape,
                    PlacePart(second, secondPart), speculativeDistance, _manifold))
            {
                _manifold.ReduceTo(Settings.MaxContactPointsPerPair);
                RefreshPoints(contact, first.Pose, second.Pose);
            }
            else
            {
                contact.PointCount = 0;
            }

            if (contact.IsTouching)
            {
                if (first.IsDynamic && !first.IsAwake && (second.IsMoving || (second.IsPlaced && !first.IsNew)))
                    WakeIsland(contact.First);

                if (second.IsDynamic && !second.IsAwake && (first.IsMoving || (first.IsPlaced && !second.IsNew)))
                    WakeIsland(contact.Second);
            }

            contactIndex++;
        }
    }

    private void WakeTouchedSleepers()
    {
        for (var woken = 0; woken < _woken.Count; woken++)
        {
            var index = _woken[woken];
            ref var body = ref _bodies[index];
            if (!body.IsUsed || !body.IsDynamic || !body.IsAwake)
                continue;

            for (var contact = _bodies[index].Contacts; contact != null; contact = contact.GetNext(index))
            {
                if (contact.IsTouching && _bodies[contact.GetOther(index)].IsDynamic)
                    WakeIsland(contact.GetOther(index));
            }
        }

        _woken.Clear();
    }

    private void RefreshPoints(Contact contact, in Pose firstPose, in Pose secondPose)
    {
        var previousCount = contact.PointCount;
        for (var index = 0; index < previousCount; index++)
        {
            _previousPoints[index] = contact.Points[index];
            _matched[index] = false;
        }

        contact.Normal = _manifold.Normal;
        contact.PointCount = _manifold.Count;
        for (var index = 0; index < _manifold.Count; index++)
        {
            ref var point = ref contact.Points[index];
            point = default;
            point.Position = _manifold.Positions[index];
            point.Separation = _manifold.Separations[index];
            point.LocalAnchorFirst = firstPose.InverseTransformPoint(point.Position);
            point.LocalAnchorSecond = secondPose.InverseTransformPoint(point.Position);

            var match = NoBody;
            var matchDistance = MatchDistanceSquared;
            for (var previous = 0; previous < previousCount; previous++)
            {
                if (_matched[previous])
                    continue;

                var onFirst = (_previousPoints[previous].LocalAnchorFirst - point.LocalAnchorFirst).LengthSquared();
                var onSecond = (_previousPoints[previous].LocalAnchorSecond - point.LocalAnchorSecond).LengthSquared();
                var distance = MathF.Min(onFirst, onSecond);
                if (distance < matchDistance)
                {
                    matchDistance = distance;
                    match = previous;
                }
            }

            if (match == NoBody)
                continue;

            _matched[match] = true;
            point.NormalImpulse = _previousPoints[match].NormalImpulse;
            point.FrictionImpulse = _previousPoints[match].FrictionImpulse;
        }
    }

    private void IntegrateVelocities(float substep)
    {
        for (var index = 0; index < _slotCount; index++)
        {
            ref var body = ref _bodies[index];
            if (!body.IsUsed || !body.IsDynamic || !body.IsAwake)
                continue;

            var linear = body.LinearVelocity + (Gravity * body.GravityScale + body.Force * body.InverseMass) * substep;
            var angular = body.AngularVelocity + InertiaMath.Apply(body.WorldInverseInertia, body.Torque) * substep;
            linear *= 1f / (1f + substep * body.LinearDamping);
            angular *= 1f / (1f + substep * body.AngularDamping);
            body.LinearVelocity = linear;
            body.AngularVelocity = angular;
            LimitSpeeds(ref body);
        }
    }

    private void IntegratePositions(float substep)
    {
        for (var index = 0; index < _slotCount; index++)
        {
            ref var body = ref _bodies[index];
            if (!body.IsUsed || !body.IsMoving)
                continue;

            LimitSpeeds(ref body);
            body.Position += body.LinearVelocity * substep;
            var spin = new QuaternionF(body.AngularVelocity.X, body.AngularVelocity.Y, body.AngularVelocity.Z, 0);
            body.Rotation = QuaternionF.Normalize(body.Rotation + spin * body.Rotation * (0.5f * substep));
            if (body.IsDynamic)
                body.WorldInverseInertia = body.MeasureWorldInverseInertia();
        }
    }

    private void FinishStep(float stepSeconds)
    {
        var sleepSpeedSquared = Settings.SleepSpeed * Settings.SleepSpeed;
        for (var index = 0; index < _slotCount; index++)
        {
            ref var body = ref _bodies[index];
            _islandParent[index] = index;
            _islandSleepTime[index] = float.MaxValue;
            _islandFirst[index] = NoBody;
            _islandLast[index] = NoBody;
            if (!body.IsUsed || !body.IsAwake)
                continue;

            LimitSpeeds(ref body);
            if (body.Kind == BodyKind.Kinematic)
            {
                if (body.LinearVelocity == Vector3F.Zero && body.AngularVelocity == Vector3F.Zero)
                    body.IsAwake = false;

                continue;
            }

            var isPushed = body.IsPushed;
            body.Force = Vector3F.Zero;
            body.Torque = Vector3F.Zero;
            body.IsPushed = false;
            var spinSpeed = body.AngularVelocity.Length() * body.Reach;
            if (!body.CanSleep || isPushed || body.LinearVelocity.LengthSquared() > sleepSpeedSquared
                               || spinSpeed * spinSpeed > sleepSpeedSquared)
            {
                body.SleepTime = 0;
            }
            else
            {
                body.SleepTime += stepSeconds;
            }
        }

        foreach (var contact in _solver.Active)
        {
            ref var first = ref _bodies[contact.First];
            ref var second = ref _bodies[contact.Second];
            if (first.IsDynamic && second.IsDynamic)
            {
                JoinIslands(contact.First, contact.Second);
                continue;
            }

            if (first.Kind == BodyKind.Kinematic && first.IsAwake)
                second.SleepTime = 0;

            if (second.Kind == BodyKind.Kinematic && second.IsAwake)
                first.SleepTime = 0;
        }

        for (var index = 0; index < _slotCount; index++)
        {
            ref var body = ref _bodies[index];
            if (!body.IsUsed || !body.IsDynamic || !body.IsAwake)
                continue;

            var root = FindIsland(index);
            _islandSleepTime[root] = MathF.Min(_islandSleepTime[root], body.SleepTime);
        }

        for (var index = 0; index < _slotCount; index++)
        {
            ref var body = ref _bodies[index];
            if (!body.IsUsed || !body.IsDynamic || !body.IsAwake)
                continue;

            var root = FindIsland(index);
            if (_islandSleepTime[root] < Settings.TimeToSleep)
                continue;

            if (_islandFirst[root] == NoBody)
                _islandFirst[root] = index;
            else
                _bodies[_islandLast[root]].IslandNext = index;

            _islandLast[root] = index;
            body.IslandHead = _islandFirst[root];
            body.IslandNext = NoBody;
            body.IsAwake = false;
            body.LinearVelocity = Vector3F.Zero;
            body.AngularVelocity = Vector3F.Zero;
        }
    }

    private void RecordStatistics(long startedAt)
    {
        var awake = 0;
        for (var index = 0; index < _slotCount; index++)
        {
            if (_bodies[index].IsUsed && _bodies[index].IsAwake)
                awake++;
        }

        var touching = 0;
        var points = 0;
        foreach (var contact in _contacts)
        {
            if (!contact.IsTouching)
                continue;

            touching++;
            points += contact.PointCount;
        }

        LastStep = new StepStatistics(awake, _contacts.Count, touching, points,
            Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
    }

    private int FindIsland(int index)
    {
        var root = index;
        while (_islandParent[root] != root)
        {
            root = _islandParent[root];
        }

        while (_islandParent[index] != root)
        {
            var next = _islandParent[index];
            _islandParent[index] = root;
            index = next;
        }

        return root;
    }

    private void JoinIslands(int first, int second)
    {
        var firstRoot = FindIsland(first);
        var secondRoot = FindIsland(second);
        if (firstRoot == secondRoot)
            return;

        if (firstRoot < secondRoot)
            _islandParent[secondRoot] = firstRoot;
        else
            _islandParent[firstRoot] = secondRoot;
    }

    private void MarkPushed(int index)
    {
        _bodies[index].IsPushed = true;
        WakeIsland(index);
    }

    private void WakeIsland(int index)
    {
        ref var body = ref _bodies[index];
        if (body.IsAwake || body.Kind == BodyKind.Static)
            return;

        if (body.Kind == BodyKind.Kinematic && body.LinearVelocity == Vector3F.Zero
                                            && body.AngularVelocity == Vector3F.Zero)
            return;

        if (body.Kind == BodyKind.Kinematic || body.IslandHead == NoBody)
        {
            body.IsAwake = true;
            body.SleepTime = 0;
            _woken.Add(index);
            return;
        }

        var member = body.IslandHead;
        while (member != NoBody)
        {
            ref var sleeper = ref _bodies[member];
            var next = sleeper.IslandNext;
            sleeper.IsAwake = true;
            sleeper.SleepTime = 0;
            sleeper.IslandHead = NoBody;
            sleeper.IslandNext = NoBody;
            _woken.Add(member);
            member = next;
        }
    }

    private void WakeNewSleepersNear(int index)
    {
        if (_newSleepers == 0)
            return;

        for (var part = _bodies[index].FirstPart; part != NoPart; part = _parts[part].Next)
        {
            _found.Clear();
            _tree.Query(_tree.BoxOf(_parts[part].Proxy), _found);
            foreach (var otherPart in _found)
            {
                var other = _parts[otherPart].Body;
                ref var body = ref _bodies[other];
                if (other != index && body.IsDynamic && !body.IsAwake && body.IsNew)
                    WakeIsland(other);
            }
        }
    }

    private void WakeTouching(int index)
    {
        for (var contact = _bodies[index].Contacts; contact != null; contact = contact.GetNext(index))
        {
            if (contact.IsTouching)
                WakeIsland(contact.GetOther(index));
        }
    }

    private void CreateContact(ulong key, int firstPart, int secondPart)
    {
        var first = _parts[firstPart].Body;
        var second = _parts[secondPart].Body;
        var contact = _spareContacts.Count > 0 ? _spareContacts.Pop() : new Contact(Settings.MaxContactPointsPerPair);
        contact.Key = key;
        contact.First = first;
        contact.Second = second;
        contact.FirstPart = firstPart;
        contact.SecondPart = secondPart;
        contact.PointCount = 0;
        contact.Friction = MathF.Sqrt(_bodies[first].Friction * _bodies[second].Friction);
        contact.Restitution = MathF.Max(_bodies[first].Restitution, _bodies[second].Restitution);
        contact.ListIndex = _contacts.Count;
        _contacts.Add(contact);
        _contactsByKey.Add(key, contact);
        LinkContact(contact, first);
        LinkContact(contact, second);
    }

    private void DestroyContact(Contact contact)
    {
        UnlinkContact(contact, contact.First);
        UnlinkContact(contact, contact.Second);
        var last = _contacts[^1];
        _contacts[contact.ListIndex] = last;
        last.ListIndex = contact.ListIndex;
        _contacts.RemoveAt(_contacts.Count - 1);
        _contactsByKey.Remove(contact.Key);
        _spareContacts.Push(contact);
    }

    private void LinkContact(Contact contact, int body)
    {
        ref var state = ref _bodies[body];
        var head = state.Contacts;
        contact.SetPrevious(body, null);
        contact.SetNext(body, head);
        head?.SetPrevious(body, contact);
        state.Contacts = contact;
    }

    private void UnlinkContact(Contact contact, int body)
    {
        var previous = contact.GetPrevious(body);
        var next = contact.GetNext(body);
        if (previous != null)
            previous.SetNext(body, next);
        else
            _bodies[body].Contacts = next;

        next?.SetPrevious(body, previous);
        contact.SetPrevious(body, null);
        contact.SetNext(body, null);
    }

    private void Mark(int index, BodyMarks marks)
    {
        ref var body = ref _bodies[index];
        if ((body.Marks & BodyMarks.Listed) == 0)
            _marked.Add(index);

        body.Marks |= marks | BodyMarks.Listed;
    }

    private void ClearMarks()
    {
        foreach (var index in _marked)
        {
            _bodies[index].Marks = BodyMarks.None;
        }

        _marked.Clear();
        _newSleepers = 0;
    }

    private int TakeSlot()
    {
        if (_freeSlots.Count > 0)
            return _freeSlots.Pop();

        if (_slotCount == _bodies.Length)
        {
            var capacity = _bodies.Length * 2;
            Array.Resize(ref _bodies, capacity);
            Array.Resize(ref _islandParent, capacity);
            Array.Resize(ref _islandSleepTime, capacity);
            Array.Resize(ref _islandFirst, capacity);
            Array.Resize(ref _islandLast, capacity);
        }

        return _slotCount++;
    }

    private void AddParts(int body, CollisionShape shape)
    {
        var center = _bodies[body].CenterOfMass;
        if (shape is not CompoundCollisionShape compound)
        {
            AddPart(body, shape, new Pose(-center));
            return;
        }

        // Added last to first, so the body's list keeps the compound's order.
        var parts = compound.Parts;
        for (var index = parts.Count - 1; index >= 0; index--)
        {
            AddPart(body, parts[index].Shape, new Pose(parts[index].Pose.Position - center, parts[index].Pose.Rotation));
        }
    }

    private void AddPart(int body, CollisionShape shape, in Pose pose)
    {
        var part = TakePartSlot();
        ref var state = ref _bodies[body];
        ref var slot = ref _parts[part];
        slot.Body = body;
        slot.Shape = shape;
        slot.Pose = pose;
        slot.IsCentered = pose.Position == Vector3F.Zero;
        slot.HasBodyPose = slot.IsCentered && pose.Rotation == QuaternionF.Identity;
        slot.Next = state.FirstPart;
        slot.Proxy = _tree.Add(ComputeProxyBounds(state, slot), part);
        state.FirstPart = part;
        state.Reach = MathF.Max(state.Reach, pose.Position.Length() + shape.BoundingRadius);
    }

    private void RemoveParts(int body)
    {
        var part = _bodies[body].FirstPart;
        while (part != NoPart)
        {
            var next = _parts[part].Next;
            _tree.Remove(_parts[part].Proxy);
            _parts[part] = default;
            _parts[part].Body = NoBody;
            _freeParts.Push(part);
            part = next;
        }

        _bodies[body].FirstPart = NoPart;
    }

    private int TakePartSlot()
    {
        if (_freeParts.Count > 0)
            return _freeParts.Pop();

        if (_partCount == _parts.Length)
            Array.Resize(ref _parts, _parts.Length * 2);

        return _partCount++;
    }

    private BodyHandle GetHandle(int index) => new(this, index, _bodies[index].Generation);

    private int Resolve(BodyHandle body)
    {
        if (!Contains(body))
            throw new ArgumentException("The body is not in this world.", nameof(body));

        return body.Index;
    }

    private int ResolveMoving(BodyHandle body)
    {
        var index = Resolve(body);
        if (_bodies[index].Kind == BodyKind.Static)
            throw new InvalidOperationException("A static body does not move; set its pose instead.");

        return index;
    }

    private int ResolveDynamic(BodyHandle body)
    {
        var index = Resolve(body);
        if (!_bodies[index].IsDynamic)
            throw new InvalidOperationException("Only a dynamic body is moved by forces and impulses.");

        return index;
    }

    private static void ValidateSettings(PhysicsSettings settings)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(settings.StepRate, nameof(settings));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(settings.Substeps, nameof(settings));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(settings.MaxContactPointsPerPair, nameof(settings));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(settings.MaxContactPointsPerPair, ManifoldBuffer.Capacity,
            nameof(settings));
        RequireFinite(settings.Gravity, nameof(settings));
        RequirePositiveFinite(settings.MaxLinearSpeed, nameof(settings));
        if (!float.IsFinite(settings.MaxLinearSpeed * settings.MaxLinearSpeed))
            throw new ArgumentOutOfRangeException(nameof(settings), settings.MaxLinearSpeed, "The speed limit is too large.");
        RequireFiniteNotNegative(settings.SpeculativeDistance, nameof(settings));
        RequireFiniteNotNegative(settings.ContactHertz, nameof(settings));
        RequireFiniteNotNegative(settings.ContactDampingRatio, nameof(settings));
        RequireFiniteNotNegative(settings.MaxPushOutSpeed, nameof(settings));
        RequireFiniteNotNegative(settings.RestitutionThreshold, nameof(settings));
        RequireFiniteNotNegative(settings.SleepSpeed, nameof(settings));
        if (!(settings.TimeToSleep >= 0))
            throw new ArgumentOutOfRangeException(nameof(settings), "The time to sleep must not be negative.");
    }

    private static void ValidateDescription(BodyDescription description)
    {
        ArgumentNullException.ThrowIfNull(description);
        if (description.Shape == null)
            throw new ArgumentException("A body needs a shape.", nameof(description));

        if (!Enum.IsDefined(description.Kind))
            throw new ArgumentOutOfRangeException(nameof(description), description.Kind, "Unknown kind of body.");

        RequireFinite(description.Position, nameof(description));
        RequireFinite(description.LinearVelocity, nameof(description));
        RequireFinite(description.AngularVelocity, nameof(description));
        RequireFiniteNotNegative(description.Friction, nameof(description));
        RequireFiniteNotNegative(description.Restitution, nameof(description));
        RequireFiniteNotNegative(description.LinearDamping, nameof(description));
        RequireFiniteNotNegative(description.AngularDamping, nameof(description));
        if (!float.IsFinite(description.GravityScale))
            throw new ArgumentOutOfRangeException(nameof(description), "The gravity scale must be finite.");
    }

    private static void RequireFiniteNotNegative(float value, string parameterName)
    {
        if (!float.IsFinite(value) || value < 0)
            throw new ArgumentOutOfRangeException(parameterName, value, "The value must be finite and not negative.");
    }

    private static void RequireFinite(Vector3F vector, string parameterName)
    {
        if (!IsFinite(vector))
            throw new ArgumentOutOfRangeException(parameterName, vector, "The vector must be finite.");
    }

    private static bool IsFinite(Vector3F vector) =>
        float.IsFinite(vector.X) && float.IsFinite(vector.Y) && float.IsFinite(vector.Z);

    private static MassProperties ComputeMass(BodyDescription description)
    {
        if (description.Mass < 0 || description.Density < 0 || !float.IsFinite(description.Mass)
            || !float.IsFinite(description.Density))
            throw new ArgumentOutOfRangeException(nameof(description), "Mass and density must be finite and not negative.");

        var density = description.Mass > 0
            ? description.Mass / description.Shape.ComputeMass(1f).Mass
            : description.Density;
        var mass = description.Shape.ComputeMass(density);
        var inertia = mass.Inertia;
        if (!IsInvertible(mass.Mass) || !IsInvertible(inertia.X) || !IsInvertible(inertia.Y) || !IsInvertible(inertia.Z))
            throw new ArgumentException("A dynamic body needs a density or a mass that gives it a finite weight.",
                nameof(description));

        return mass;
    }

    private void LimitSpeeds(ref BodyState body)
    {
        body.LinearVelocity = LimitSpeed(body.LinearVelocity, Settings.MaxLinearSpeed);
        body.AngularVelocity = LimitSpeed(body.AngularVelocity, MaxAngularSpeed);
    }

    private static Vector3F LimitSpeed(Vector3F velocity, float maxSpeed)
    {
        if (float.IsNaN(velocity.X) || float.IsNaN(velocity.Y) || float.IsNaN(velocity.Z))
            return Vector3F.Zero;

        if (IsFinite(velocity) && velocity.LengthSquared() <= maxSpeed * maxSpeed)
            return velocity;

        var largest = Vector3F.Max(Vector3F.Abs(velocity));
        var direction = float.IsPositiveInfinity(largest)
            ? new Vector3F(InfinityToUnit(velocity.X), InfinityToUnit(velocity.Y), InfinityToUnit(velocity.Z))
            : velocity / largest;
        return direction * (maxSpeed / direction.Length());
    }

    private static float InfinityToUnit(float value) => float.IsInfinity(value) ? MathF.Sign(value) : 0;

    private static void RequirePositiveFinite(float value, string parameterName)
    {
        if (!float.IsFinite(value) || value <= 0)
            throw new ArgumentOutOfRangeException(parameterName, value, "The value must be finite and above zero.");
    }

    private static bool IsInvertible(float value) => value > 0 && float.IsFinite(value) && float.IsFinite(1f / value);

    private static QuaternionF NormalizeRotation(QuaternionF rotation, string parameterName)
    {
        var lengthSquared = rotation.LengthSquared();
        if (!(lengthSquared > 1e-12f) || !float.IsFinite(lengthSquared))
            throw new ArgumentException("A rotation must be a finite quaternion, not zero.", parameterName);

        return QuaternionF.Normalize(rotation);
    }
}
