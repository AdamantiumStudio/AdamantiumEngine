using System;
using System.Collections.Generic;
using System.Diagnostics;
using Adamantium.DarkMatter.CollisionDetection;
using Adamantium.DarkMatter.Dynamics;
using Adamantium.DarkMatter.Shapes;

namespace Adamantium.DarkMatter;

/// <summary>Rigid bodies stepped at a fixed rate; the same bodies and calls always step to the same result. One thread
/// owns a world.</summary>
public sealed partial class PhysicsWorld
{
    private const int NoBody = -1;
    private const float MatchDistanceSquared = 0.02f * 0.02f;

    private readonly DynamicAabbTree<int> _tree;
    private readonly Dictionary<ulong, Contact> _contactsByKey = new();
    private readonly List<Contact> _contacts = [];
    private readonly Stack<Contact> _spareContacts = new();
    private readonly Stack<int> _freeSlots = new();
    private readonly List<int> _moved = [];
    private readonly List<int> _found = [];
    private readonly List<int> _woken = [];
    private readonly ManifoldBuffer _manifold = new();
    private readonly ContactSolver _solver = new();
    private readonly ContactPointState[] _previousPoints;
    private readonly bool[] _matched;

    private BodyState[] _bodies = new BodyState[64];
    private int[] _islandParent = new int[64];
    private float[] _islandSleepTime = new float[64];
    private int[] _islandFirst = new int[64];
    private int[] _islandLast = new int[64];
    private int _slotCount;
    private Vector3F _gravity;

    /// <summary>A world stepped as <paramref name="settings"/> say, or with the defaults.</summary>
    public PhysicsWorld(PhysicsSettings settings = null)
    {
        Settings = settings ?? new PhysicsSettings();
        ValidateSettings(Settings);
        _gravity = Settings.Gravity;
        _tree = new DynamicAabbTree<int>(0.1f, Math.Max(0.05f, Settings.SpeculativeDistance));
        _previousPoints = new ContactPointState[Settings.MaxContactPointsPerPair];
        _matched = new bool[Settings.MaxContactPointsPerPair];
    }

    /// <summary>How the world steps and solves.</summary>
    public PhysicsSettings Settings { get; }

    /// <summary>The pull every dynamic body feels, in meters per second squared; a change wakes the sleeping ones.
    /// </summary>
    public Vector3F Gravity
    {
        get => _gravity;
        set
        {
            if (!IsFinite(value))
                throw new ArgumentOutOfRangeException(nameof(value), "Gravity must be finite.");

            if (value == _gravity)
                return;

            _gravity = value;
            for (var index = 0; index < _slotCount; index++)
            {
                if (_bodies[index].IsUsed && _bodies[index].IsDynamic)
                    WakeIsland(index);
            }
        }
    }

    /// <summary>How many bodies the world holds.</summary>
    public int BodyCount { get; private set; }

    /// <summary>What the last step did.</summary>
    public StepStatistics LastStep { get; private set; }

    /// <summary>The length of one step, in seconds.</summary>
    public float StepSeconds => 1f / Settings.StepRate;

    /// <summary>Adds a body as <paramref name="description"/> describes it and gives its handle.</summary>
    public RigidBody Add(RigidBodyDescription description)
    {
        ValidateDescription(description);
        var rotation = NormalizeRotation(description.Rotation, nameof(description));
        var mass = default(MassProperties);
        if (description.Kind == BodyKind.Dynamic)
            mass = ComputeMass(description);

        var index = TakeSlot();
        ref var body = ref _bodies[index];
        var generation = body.Generation + 1;
        body = default;
        body.IsUsed = true;
        body.Generation = generation;
        body.Kind = description.Kind;
        body.Shape = description.Shape;
        body.Position = description.Position;
        body.Rotation = rotation;
        body.StartPosition = body.Position;
        body.StartRotation = body.Rotation;
        body.Friction = description.Friction;
        body.Restitution = description.Restitution;
        body.LinearDamping = description.LinearDamping;
        body.AngularDamping = description.AngularDamping;
        body.GravityScale = description.GravityScale;
        body.CanSleep = description.CanSleep;
        body.IslandHead = NoBody;
        body.IslandNext = NoBody;

        if (description.Kind != BodyKind.Static)
        {
            body.LinearVelocity = description.LinearVelocity;
            body.AngularVelocity = description.AngularVelocity;
            body.IsAwake = description.IsAwake;
        }

        if (description.Kind == BodyKind.Dynamic)
        {
            body.InverseMass = 1f / mass.Mass;
            body.InverseInertia = new Vector3F(Invert(mass.Inertia.X), Invert(mass.Inertia.Y), Invert(mass.Inertia.Z));
            body.WorldInverseInertia = InertiaMath.ToWorld(body.Rotation, body.InverseInertia);
            if (!body.IsAwake)
                body.IslandHead = index;
        }

        body.Proxy = _tree.Add(ComputeProxyBounds(body), index);
        MarkMoved(index);
        BodyCount++;
        return new RigidBody(index, generation);
    }

    /// <summary>Takes <paramref name="body"/> out of the world; bodies resting on it wake.</summary>
    public void Remove(RigidBody body)
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

        if (state.IsMoved)
            _moved.Remove(index);

        _tree.Remove(state.Proxy);
        var generation = state.Generation;
        state = default;
        state.Generation = generation;
        _freeSlots.Push(index);
        BodyCount--;
    }

    /// <summary>Whether <paramref name="body"/> is in this world.</summary>
    public bool Contains(RigidBody body) =>
        body.Index >= 0 && body.Index < _slotCount && _bodies[body.Index].IsUsed
        && _bodies[body.Index].Generation == body.Generation;

    /// <summary>How <paramref name="body"/> moves.</summary>
    public BodyKind GetKind(RigidBody body) => _bodies[Resolve(body)].Kind;

    /// <summary>Where <paramref name="body"/> stands.</summary>
    public Pose GetPose(RigidBody body) => _bodies[Resolve(body)].Pose;

    /// <summary>Moves <paramref name="body"/> to <paramref name="pose"/> at once, as a jump, not a motion; it and the
    /// bodies it touches, there or here, wake.</summary>
    public void SetPose(RigidBody body, in Pose pose)
    {
        var index = Resolve(body);
        if (!IsFinite(pose.Position))
            throw new ArgumentOutOfRangeException(nameof(pose), "A position must be finite.");

        var rotation = NormalizeRotation(pose.Rotation, nameof(pose));
        ref var state = ref _bodies[index];
        state.Position = pose.Position;
        state.Rotation = rotation;
        if (state.IsDynamic)
            state.WorldInverseInertia = InertiaMath.ToWorld(state.Rotation, state.InverseInertia);

        if (_tree.Move(state.Proxy, ComputeProxyBounds(state)))
            MarkMoved(index);

        WakeIsland(index);
        WakeTouching(index);
        for (var contact = state.Contacts; contact != null; contact = contact.GetNext(index))
        {
            contact.IsFresh = true;
            contact.FirstMoved = contact.First == index;
            contact.SecondMoved = contact.Second == index;
        }
    }

    /// <summary>The velocity of <paramref name="body"/>'s center, in meters per second.</summary>
    public Vector3F GetLinearVelocity(RigidBody body) => _bodies[Resolve(body)].LinearVelocity;

    /// <summary>Sets the velocity of a dynamic or kinematic body's center and wakes it.</summary>
    public void SetLinearVelocity(RigidBody body, Vector3F velocity)
    {
        var index = ResolveMoving(body);
        _bodies[index].LinearVelocity = velocity;
        WakeIsland(index);
    }

    /// <summary>The spin of <paramref name="body"/>, in radians per second around each world axis.</summary>
    public Vector3F GetAngularVelocity(RigidBody body) => _bodies[Resolve(body)].AngularVelocity;

    /// <summary>Sets the spin of a dynamic or kinematic body and wakes it.</summary>
    public void SetAngularVelocity(RigidBody body, Vector3F velocity)
    {
        var index = ResolveMoving(body);
        _bodies[index].AngularVelocity = velocity;
        WakeIsland(index);
    }

    /// <summary>Pushes a dynamic body's center with <paramref name="force"/>, in newtons, through the next step, and
    /// wakes it.</summary>
    public void ApplyForce(RigidBody body, Vector3F force)
    {
        var index = ResolveDynamic(body);
        _bodies[index].Force += force;
        WakeIsland(index);
    }

    /// <summary>Turns a dynamic body with <paramref name="torque"/>, in newton meters around each world axis, through
    /// the next step, and wakes it.</summary>
    public void ApplyTorque(RigidBody body, Vector3F torque)
    {
        var index = ResolveDynamic(body);
        _bodies[index].Torque += torque;
        WakeIsland(index);
    }

    /// <summary>Strikes a dynamic body at <paramref name="point"/> of the world with <paramref name="impulse"/>, in
    /// newton seconds, changing its velocity at once, and wakes it.</summary>
    public void ApplyImpulse(RigidBody body, Vector3F impulse, Vector3F point)
    {
        var index = ResolveDynamic(body);
        ref var state = ref _bodies[index];
        state.LinearVelocity += impulse * state.InverseMass;
        state.AngularVelocity += InertiaMath.Apply(state.WorldInverseInertia,
            Vector3F.Cross(point - state.Position, impulse));
        WakeIsland(index);
    }

    /// <summary>Whether <paramref name="body"/> is awake; static bodies never are.</summary>
    public bool IsAwake(RigidBody body) => _bodies[Resolve(body)].IsAwake;

    /// <summary>Wakes <paramref name="body"/> and everything asleep with it.</summary>
    public void WakeUp(RigidBody body) => WakeIsland(Resolve(body));

    /// <summary>The box, aligned with the world's axes, that holds <paramref name="body"/>'s shape where it stands.
    /// </summary>
    public BoundingBox<float> GetBounds(RigidBody body)
    {
        ref var state = ref _bodies[Resolve(body)];
        return state.Shape.ComputeBounds(state.Pose);
    }

    /// <summary>Adds to <paramref name="into"/> every contact point the last step held: touching, or about to, as a
    /// positive separation says.</summary>
    public void GetContactPoints(List<ContactPoint> into)
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

    /// <summary>Moves the world by one step of <see cref="StepSeconds"/>.</summary>
    public void Step()
    {
        var startedAt = Stopwatch.GetTimestamp();
        var stepSeconds = StepSeconds;
        var substep = stepSeconds / Settings.Substeps;
        var inverseSubstep = 1f / substep;

        UpdateBroadPhase();
        FindNewPairs();
        UpdateContacts(stepSeconds);
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

            if (_tree.Move(body.Proxy, ComputeProxyBounds(body)))
                MarkMoved(index);
        }
    }

    private BoundingBox<float> ComputeProxyBounds(in BodyState body)
    {
        var bounds = body.Shape.ComputeBounds(body.Pose);
        var travel = body.LinearVelocity * StepSeconds;
        var swing = new Vector3F(MeasureSpinSpeed(body) * StepSeconds);
        return new BoundingBox<float>(bounds.Minimum + Vector3F.Min(travel, Vector3F.Zero) - swing,
            bounds.Maximum + Vector3F.Max(travel, Vector3F.Zero) + swing);
    }

    private static float MeasureSpinSpeed(in BodyState body) =>
        body.AngularVelocity.Length() * body.Shape.BoundingRadius;

    private void FindNewPairs()
    {
        foreach (var index in _moved)
        {
            ref var body = ref _bodies[index];
            _found.Clear();
            _tree.Query(_tree.BoxOf(body.Proxy), _found);
            foreach (var other in _found)
            {
                if (other == index)
                    continue;

                ref var otherBody = ref _bodies[other];
                if (otherBody.IsMoved && other < index)
                    continue;

                if (!body.IsDynamic && !otherBody.IsDynamic)
                    continue;

                var key = Contact.MakeKey(index, other);
                if (!_contactsByKey.ContainsKey(key))
                    CreateContact(key, Math.Min(index, other), Math.Max(index, other));
            }
        }

        foreach (var index in _moved)
        {
            _bodies[index].IsMoved = false;
        }

        _moved.Clear();
    }

    private void UpdateContacts(float stepSeconds)
    {
        var contactIndex = 0;
        while (contactIndex < _contacts.Count)
        {
            var contact = _contacts[contactIndex];
            ref var first = ref _bodies[contact.First];
            ref var second = ref _bodies[contact.Second];
            var isFresh = contact.IsFresh;
            contact.IsFresh = false;
            if (!first.IsMoving && !second.IsMoving && !isFresh)
            {
                contactIndex++;
                continue;
            }

            if (!Collision.BoxIntersectsBox(_tree.BoxOf(first.Proxy), _tree.BoxOf(second.Proxy)))
            {
                DestroyContact(contact);
                continue;
            }

            var closingSpeed = (second.LinearVelocity - first.LinearVelocity).Length() + MeasureSpinSpeed(first)
                               + MeasureSpinSpeed(second);
            var speculativeDistance = Settings.SpeculativeDistance + closingSpeed * stepSeconds;
            if (Narrowphase.Collide(first.Shape, first.Pose, second.Shape, second.Pose, speculativeDistance, _manifold))
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
                if (first.IsDynamic && !first.IsAwake && (second.IsMoving || (isFresh && !contact.FirstMoved)))
                    WakeIsland(contact.First);

                if (second.IsDynamic && !second.IsAwake && (first.IsMoving || (isFresh && !contact.SecondMoved)))
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
            if (!_bodies[index].IsUsed || !_bodies[index].IsDynamic)
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
        var maxSpin = 0.25f * MathF.PI / substep;
        for (var index = 0; index < _slotCount; index++)
        {
            ref var body = ref _bodies[index];
            if (!body.IsUsed || !body.IsDynamic || !body.IsAwake)
                continue;

            var linear = body.LinearVelocity + (Gravity * body.GravityScale + body.Force * body.InverseMass) * substep;
            var angular = body.AngularVelocity + InertiaMath.Apply(body.WorldInverseInertia, body.Torque) * substep;
            linear *= 1f / (1f + substep * body.LinearDamping);
            angular *= 1f / (1f + substep * body.AngularDamping);
            var spin = angular.Length();
            if (spin > maxSpin)
                angular *= maxSpin / spin;

            body.LinearVelocity = linear;
            body.AngularVelocity = angular;
        }
    }

    private void IntegratePositions(float substep)
    {
        for (var index = 0; index < _slotCount; index++)
        {
            ref var body = ref _bodies[index];
            if (!body.IsUsed || !body.IsMoving)
                continue;

            body.Position += body.LinearVelocity * substep;
            var spin = new QuaternionF(body.AngularVelocity.X, body.AngularVelocity.Y, body.AngularVelocity.Z, 0);
            body.Rotation = QuaternionF.Normalize(body.Rotation + spin * body.Rotation * (0.5f * substep));
            if (body.IsDynamic)
                body.WorldInverseInertia = InertiaMath.ToWorld(body.Rotation, body.InverseInertia);
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

            if (body.Kind == BodyKind.Kinematic)
            {
                if (body.LinearVelocity == Vector3F.Zero && body.AngularVelocity == Vector3F.Zero)
                    body.IsAwake = false;

                continue;
            }

            body.Force = Vector3F.Zero;
            body.Torque = Vector3F.Zero;
            var spinSpeed = body.AngularVelocity.Length() * body.Shape.BoundingRadius;
            if (!body.CanSleep || body.LinearVelocity.LengthSquared() > sleepSpeedSquared
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

    private void WakeIsland(int index)
    {
        ref var body = ref _bodies[index];
        if (body.IsAwake || body.Kind == BodyKind.Static)
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

    private void WakeTouching(int index)
    {
        for (var contact = _bodies[index].Contacts; contact != null; contact = contact.GetNext(index))
        {
            if (contact.IsTouching)
                WakeIsland(contact.GetOther(index));
        }
    }

    private void CreateContact(ulong key, int first, int second)
    {
        var contact = _spareContacts.Count > 0 ? _spareContacts.Pop() : new Contact(Settings.MaxContactPointsPerPair);
        contact.Key = key;
        contact.First = first;
        contact.Second = second;
        contact.PointCount = 0;
        contact.IsFresh = true;
        contact.FirstMoved = _bodies[first].IsMoved;
        contact.SecondMoved = _bodies[second].IsMoved;
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

    private void MarkMoved(int index)
    {
        ref var body = ref _bodies[index];
        if (body.IsMoved)
            return;

        body.IsMoved = true;
        _moved.Add(index);
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

    private RigidBody GetHandle(int index) => new(index, _bodies[index].Generation);

    private int Resolve(RigidBody body)
    {
        if (!Contains(body))
            throw new ArgumentException("The body is not in this world.", nameof(body));

        return body.Index;
    }

    private int ResolveMoving(RigidBody body)
    {
        var index = Resolve(body);
        if (_bodies[index].Kind == BodyKind.Static)
            throw new InvalidOperationException("A static body does not move; set its pose instead.");

        return index;
    }

    private int ResolveDynamic(RigidBody body)
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
        if (!IsFinite(settings.Gravity))
            throw new ArgumentOutOfRangeException(nameof(settings), "Gravity must be finite.");

        RequireFiniteNotNegative(settings.SpeculativeDistance, nameof(settings));
        RequireFiniteNotNegative(settings.ContactHertz, nameof(settings));
        RequireFiniteNotNegative(settings.ContactDampingRatio, nameof(settings));
        RequireFiniteNotNegative(settings.MaxPushOutSpeed, nameof(settings));
        RequireFiniteNotNegative(settings.RestitutionThreshold, nameof(settings));
        RequireFiniteNotNegative(settings.SleepSpeed, nameof(settings));
        if (!(settings.TimeToSleep >= 0))
            throw new ArgumentOutOfRangeException(nameof(settings), "The time to sleep must not be negative.");
    }

    private static void ValidateDescription(RigidBodyDescription description)
    {
        ArgumentNullException.ThrowIfNull(description);
        if (description.Shape == null)
            throw new ArgumentException("A body needs a shape.", nameof(description));

        if (!IsFinite(description.Position) || !IsFinite(description.LinearVelocity)
                                            || !IsFinite(description.AngularVelocity))
            throw new ArgumentOutOfRangeException(nameof(description), "Position and velocities must be finite.");

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

    private static bool IsFinite(Vector3F vector) =>
        float.IsFinite(vector.X) && float.IsFinite(vector.Y) && float.IsFinite(vector.Z);

    private static MassProperties ComputeMass(RigidBodyDescription description)
    {
        if (description.Mass < 0 || description.Density < 0 || !float.IsFinite(description.Mass)
            || !float.IsFinite(description.Density))
            throw new ArgumentOutOfRangeException(nameof(description), "Mass and density must be finite and not negative.");

        if (description.Mass > 0)
            return description.Shape.ComputeMass(1f).ScaleTo(description.Mass);

        if (description.Density == 0)
            throw new ArgumentException("A dynamic body needs a density or a mass.", nameof(description));

        return description.Shape.ComputeMass(description.Density);
    }

    private static QuaternionF NormalizeRotation(QuaternionF rotation, string parameterName)
    {
        var lengthSquared = rotation.LengthSquared();
        if (!(lengthSquared > 1e-12f) || !float.IsFinite(lengthSquared))
            throw new ArgumentException("A rotation must be a finite quaternion, not zero.", parameterName);

        return QuaternionF.Normalize(rotation);
    }

    private static float Invert(float value) => value > 0 ? 1f / value : 0;
}
