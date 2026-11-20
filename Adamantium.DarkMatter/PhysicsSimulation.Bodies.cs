using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Adamantium.DarkMatter.Dynamics;
using Adamantium.DarkMatter.Shapes;
using Adamantium.DarkMatter.Simulation;

namespace Adamantium.DarkMatter;

// Bodies: added, read, pushed and taken out by id.
public sealed partial class PhysicsSimulation
{
    private readonly List<SimulatedBody> _bodySlots = [];
    private readonly Stack<int> _freeBodySlots = new();
    private readonly List<int> _removedBodySlots = [];
    private readonly List<BodyId> _liveBodies = [];
    // The slot of the body the core keeps at each of its indices.
    private readonly List<int> _slotOfCoreBody = [];
    // The bodies with a force or a torque; those read after every step - the core's not static, a static one read
    // only when taken or changed; those moved to a pose over this step; those told a refusal at the last step.
    private readonly List<BodyId> _pushedBodies = [];
    private readonly List<BodyId> _readBodies = [];
    private readonly List<BodyId> _movedBodies = [];
    private readonly List<BodyId> _refusedBodies = [];

    // The caller's own of a body of one shape, before any is given: shared, as it is never written.
    private static readonly object[] OnePart = [null];

    /// <summary>The bodies added and not taken out, in no order kept; good until the next add or removal.</summary>
    public ReadOnlySpan<BodyId> Bodies => CollectionsMarshal.AsSpan(_liveBodies);

    /// <summary>Adds a body as <paramref name="settings"/> say and gives its id; it is <see cref="PhysicsStatus.Adding"/>
    /// until the next step takes it, and actions on it can be asked at once.</summary>
    public BodyId AddBody(in BodySettings settings)
    {
        ThrowIfDisposed();
        var description = Describe(settings, out var partData);
        var slot = _freeBodySlots.Count > 0 ? _freeBodySlots.Pop() : NewBodySlot();
        var body = _bodySlots[slot];
        body.Generation++;
        body.Status = PhysicsStatus.Adding;
        body.Refusal = null;
        body.Data = settings.Data;
        body.PartData = partData;
        body.Handle = default;
        body.Pose = new Pose(settings.Position, QuaternionF.Normalize(settings.Rotation));
        body.CenterOfMass = settings.Position;
        body.LinearVelocity = settings.LinearVelocity;
        body.AngularVelocity = settings.AngularVelocity;
        body.Mass = 0;
        body.Kind = settings.Kind;
        body.KindAsked = settings.Kind;
        body.IsSleeping = false;
        body.Force = Vector3F.Zero;
        body.Torque = Vector3F.Zero;
        body.IsPushed = false;
        body.IsFollowed = false;
        body.MustRead = false;
        body.IsMovedToPose = false;
        body.StopsMoving = false;
        body.StopsTurning = false;
        var id = new BodyId(slot, body.Generation);
        body.ListIndex = _liveBodies.Count;
        _liveBodies.Add(id);
        _commands.Add(new SimulationCommand { Kind = SimulationCommandKind.AddBody, Slot = slot, Body = description });
        return id;
    }

    /// <summary>Takes <paramref name="body"/> out, its joints with it, refused or not; its id names nothing from now on.
    /// </summary>
    public void RemoveBody(BodyId body)
    {
        var record = FindAny(body);
        if (record.Status == PhysicsStatus.Removed)
            throw new InvalidOperationException($"{body} is removed.");

        record.Status = PhysicsStatus.Removed;
        LetGoOfBody(body.Index, record);
        // Its joints go with it; from the end, as each one let go of takes the last one's place.
        for (var index = _liveJoints.Count - 1; index >= 0; index--)
        {
            var joint = _liveJoints[index];
            var jointRecord = _jointSlots[joint.Index];
            if (jointRecord.FirstBody == body.Index || jointRecord.SecondBody == body.Index)
            {
                jointRecord.Status = PhysicsStatus.Removed;
                LetGoOfJoint(joint.Index, jointRecord);
            }
        }

        _commands.Add(new SimulationCommand { Kind = SimulationCommandKind.RemoveBody, Slot = body.Index });
    }

    /// <summary>Whether <paramref name="body"/> is being added or is in the simulation.</summary>
    public bool Contains(BodyId body) =>
        TryFind(body, out var record) && record.Status is PhysicsStatus.Adding or PhysicsStatus.Active;

    /// <summary>Where <paramref name="body"/> is in its life; removed for an id the simulation never gave.</summary>
    public PhysicsStatus StatusOf(BodyId body) => TryFind(body, out var record) ? record.Status : PhysicsStatus.Removed;

    /// <summary>Why the simulation would not take <paramref name="body"/>, or a change asked of it at the last step; null
    /// when it did them all.</summary>
    public string RefusalOf(BodyId body) => FindAny(body).Refusal;

    /// <summary>The caller's own given with <paramref name="body"/>.</summary>
    public object DataOf(BodyId body) => FindAny(body).Data;

    /// <summary>The caller's own given with part <paramref name="part"/> of <paramref name="body"/>.</summary>
    public object DataOf(BodyId body, int part)
    {
        var partData = FindAny(body).PartData;
        ArgumentOutOfRangeException.ThrowIfNegative(part);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(part, partData.Length);
        return partData[part];
    }

    /// <summary>Where <paramref name="body"/>'s origin stood after the last step, and how it was turned.</summary>
    public Pose PoseOf(BodyId body) => FindAny(body).Pose;

    /// <summary>Where to draw <paramref name="body"/> this frame. Stepped by hand, where it stood after the last step.
    /// </summary>
    public Pose InterpolatedPoseOf(BodyId body) => FindAny(body).Pose;

    /// <summary>Where <paramref name="body"/>'s center of mass stood after the last step.</summary>
    public WorldPosition CenterOfMassOf(BodyId body) => FindAny(body).CenterOfMass;

    /// <summary>The velocity of <paramref name="body"/>'s center of mass after the last step, in meters per second.
    /// </summary>
    public Vector3F LinearVelocityOf(BodyId body) => FindAny(body).LinearVelocity;

    /// <summary>The spin of <paramref name="body"/> after the last step, in radians per second around each world axis.
    /// </summary>
    public Vector3F AngularVelocityOf(BodyId body) => FindAny(body).AngularVelocity;

    /// <summary>What <paramref name="body"/> weighs, in kilograms; zero for one not dynamic or not yet taken.</summary>
    public float MassOf(BodyId body) => FindAny(body).Mass;

    /// <summary>How <paramref name="body"/> moves.</summary>
    public BodyKind KindOf(BodyId body) => FindAny(body).Kind;

    /// <summary>Whether <paramref name="body"/> was asleep after the last step; a static body always is.</summary>
    public bool IsSleeping(BodyId body) => FindAny(body).IsSleeping;

    /// <summary>Strikes a dynamic body at its center of mass with <paramref name="impulse"/>, in newton seconds.</summary>
    public void ApplyImpulse(BodyId body, Vector3F impulse)
    {
        RequireFinite(impulse, nameof(impulse));
        FindKind(body, true, false);
        Ask(SimulationCommandKind.ApplyImpulse, body.Index, impulse);
    }

    /// <summary>Strikes a dynamic body at <paramref name="point"/> of the world with <paramref name="impulse"/>.</summary>
    public void ApplyImpulse(BodyId body, Vector3F impulse, WorldPosition point)
    {
        RequireFinite(impulse, nameof(impulse));
        RequireFinite(point, nameof(point));
        FindKind(body, true, false);
        _commands.Add(new SimulationCommand
        {
            Kind = SimulationCommandKind.ApplyImpulseAtPoint, Slot = body.Index, Vector = impulse, Point = point
        });
    }

    /// <summary>Turns a dynamic body by <paramref name="impulse"/>, in newton meter seconds around each world axis.
    /// </summary>
    public void ApplyAngularImpulse(BodyId body, Vector3F impulse)
    {
        RequireFinite(impulse, nameof(impulse));
        FindKind(body, true, false);
        Ask(SimulationCommandKind.ApplyAngularImpulse, body.Index, impulse);
    }

    /// <summary>Sets the velocity of a dynamic or kinematic body's center of mass, in meters per second.</summary>
    public void SetLinearVelocity(BodyId body, Vector3F velocity)
    {
        RequireFinite(velocity, nameof(velocity));
        FindKind(body, false, false);
        Ask(SimulationCommandKind.SetLinearVelocity, body.Index, velocity);
    }

    /// <summary>Sets the spin of a dynamic or kinematic body, in radians per second around each world axis.</summary>
    public void SetAngularVelocity(BodyId body, Vector3F velocity)
    {
        RequireFinite(velocity, nameof(velocity));
        FindKind(body, false, false);
        Ask(SimulationCommandKind.SetAngularVelocity, body.Index, velocity);
    }

    /// <summary>Pushes a dynamic body's center of mass with <paramref name="force"/>, in newtons, at every step until set
    /// again; zero stops it.</summary>
    public void SetForce(BodyId body, Vector3F force)
    {
        RequireFinite(force, nameof(force));
        var record = FindKind(body, true, false);
        record.Force = force;
        Push(body, record);
    }

    /// <summary>Turns a dynamic body with <paramref name="torque"/>, in newton meters around each world axis, at every
    /// step until set again; zero stops it.</summary>
    public void SetTorque(BodyId body, Vector3F torque)
    {
        RequireFinite(torque, nameof(torque));
        var record = FindKind(body, true, false);
        record.Torque = torque;
        Push(body, record);
    }

    /// <summary>Puts <paramref name="body"/>'s origin at <paramref name="pose"/> at once, as a jump, not a motion.</summary>
    public void SetPose(BodyId body, in Pose pose)
    {
        RequireFinite(pose.Position, nameof(pose));
        RequireTurn(pose.Rotation, nameof(pose));
        Find(body);
        _commands.Add(new SimulationCommand { Kind = SimulationCommandKind.SetPose, Slot = body.Index, Pose = pose });
    }

    /// <summary>Moves a kinematic body's origin to <paramref name="pose"/> over the next step, pushing what it meets; it
    /// stops there unless moved again.</summary>
    public void MoveKinematic(BodyId body, in Pose pose)
    {
        RequireFinite(pose.Position, nameof(pose));
        RequireTurn(pose.Rotation, nameof(pose));
        FindKind(body, false, true);
        _commands.Add(new SimulationCommand
        {
            Kind = SimulationCommandKind.MoveKinematic, Slot = body.Index, Pose = pose
        });
    }

    /// <summary>Wakes <paramref name="body"/> and everything asleep with it.</summary>
    public void WakeUp(BodyId body)
    {
        Find(body);
        _commands.Add(new SimulationCommand { Kind = SimulationCommandKind.WakeUp, Slot = body.Index });
    }

    /// <summary>Makes <paramref name="body"/> move as <paramref name="kind"/> says, where it stands, from rest.</summary>
    public void SetKind(BodyId body, BodyKind kind)
    {
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown kind of body.");

        var record = Find(body);
        record.KindAsked = kind;
        // Only a dynamic body is pushed; a force asked after this goes with the kind asked last.
        if (kind != BodyKind.Dynamic)
        {
            record.Force = Vector3F.Zero;
            record.Torque = Vector3F.Zero;
        }

        _commands.Add(new SimulationCommand { Kind = SimulationCommandKind.SetKind, Slot = body.Index, BodyKind = kind });
    }

    /// <summary>Gives <paramref name="body"/> <paramref name="shape"/> in place of its own, its origin where it stands.
    /// </summary>
    public void SetShape(BodyId body, CollisionShape shape)
    {
        ArgumentNullException.ThrowIfNull(shape);
        Find(body);
        _commands.Add(new SimulationCommand
        {
            Kind = SimulationCommandKind.SetShape, Slot = body.Index, Shape = shape, PartData = OnePart
        });
    }

    /// <summary>Gives <paramref name="body"/> the shapes of <paramref name="parts"/>, copied, in place of its own.
    /// </summary>
    public void SetParts(BodyId body, ReadOnlySpan<BodyPart> parts)
    {
        Find(body);
        var shape = Compose(parts, out var partData, nameof(parts));
        _commands.Add(new SimulationCommand
        {
            Kind = SimulationCommandKind.SetShape, Slot = body.Index, Shape = shape, PartData = partData
        });
    }

    /// <summary>Sets how <paramref name="body"/> rubs and bounces.</summary>
    public void SetMaterial(BodyId body, in PhysicsMaterial material)
    {
        RequireMaterial(material, nameof(material));
        Find(body);
        _commands.Add(new SimulationCommand
        {
            Kind = SimulationCommandKind.SetMaterial, Slot = body.Index, Material = material
        });
    }

    private void DoBodyCommand(in SimulationCommand command)
    {
        var body = _bodySlots[command.Slot];
        if (command.Kind == SimulationCommandKind.AddBody)
        {
            PutBody(command.Slot, body, command.Body);
            return;
        }

        // Its add refused, or taken out before: nothing to do it on.
        if (!body.Handle.IsValid)
            return;

        try
        {
            switch (command.Kind)
            {
                case SimulationCommandKind.RemoveBody:
                    _world.Remove(body.Handle);
                    _slotOfCoreBody[body.Handle.Index] = NoSlot;
                    body.Handle = default;
                    break;
                case SimulationCommandKind.ApplyImpulse:
                    _world.ApplyImpulse(body.Handle, command.Vector, _world.GetCenterOfMass(body.Handle));
                    break;
                case SimulationCommandKind.ApplyImpulseAtPoint:
                    _world.ApplyImpulse(body.Handle, command.Vector, command.Point);
                    break;
                case SimulationCommandKind.ApplyAngularImpulse:
                    _world.ApplyAngularImpulse(body.Handle, command.Vector);
                    break;
                case SimulationCommandKind.SetLinearVelocity:
                    _world.SetLinearVelocity(body.Handle, command.Vector);
                    body.StopsMoving = false;
                    break;
                case SimulationCommandKind.SetAngularVelocity:
                    _world.SetAngularVelocity(body.Handle, command.Vector);
                    body.StopsTurning = false;
                    break;
                case SimulationCommandKind.SetPose:
                    _world.SetPose(body.Handle, command.Pose);
                    Follow(command.Slot, body);
                    break;
                case SimulationCommandKind.MoveKinematic:
                    StartKinematicMove(command.Slot, body, command.Pose);
                    break;
                case SimulationCommandKind.WakeUp:
                    _world.WakeUp(body.Handle);
                    break;
                case SimulationCommandKind.SetKind:
                    _world.SetKind(body.Handle, command.BodyKind);
                    Follow(command.Slot, body);
                    break;
                case SimulationCommandKind.SetShape:
                    _world.SetShape(body.Handle, command.Shape);
                    body.PartData = command.PartData;
                    Follow(command.Slot, body);
                    break;
                case SimulationCommandKind.SetMaterial:
                    _world.SetMaterial(body.Handle, command.Material.Friction, command.Material.Restitution);
                    break;
            }
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            Refuse(command.Slot, body, exception.Message);
        }

        // Every kind asked was asked before the step: the last one done, taken or not, leaves the kind it will have.
        if (command.Kind == SimulationCommandKind.SetKind && body.Handle.IsValid)
            body.KindAsked = _world.GetKind(body.Handle);
    }

    private void PutBody(int slot, SimulatedBody body, BodyDescription description)
    {
        // Taken out again before this step took it.
        if (body.Status == PhysicsStatus.Removed)
            return;

        try
        {
            body.Handle = _world.Add(description);
        }
        catch (ArgumentException exception)
        {
            body.Status = PhysicsStatus.Refused;
            body.Refusal = exception.Message;
            return;
        }

        body.Status = PhysicsStatus.Active;
        while (_slotOfCoreBody.Count <= body.Handle.Index)
        {
            _slotOfCoreBody.Add(NoSlot);
        }

        _slotOfCoreBody[body.Handle.Index] = slot;
        Follow(slot, body);
    }

    // Over the next step by its velocity: where the center of mass is to go, and the turn the body is to make.
    private void StartKinematicMove(int slot, SimulatedBody body, in Pose pose)
    {
        var handle = body.Handle;
        var from = _world.GetPose(handle);
        var centerTo = pose.TransformPoint(_world.GetLocalCenterOfMass(handle));
        var linear = centerTo.OffsetFrom(_world.GetCenterOfMass(handle)) / StepSeconds;
        var turn = QuaternionF.Normalize(pose.Rotation) * QuaternionF.Conjugate(from.Rotation);
        if (turn.W < 0)
            turn = -turn;

        var sine = MathF.Sqrt(MathF.Max(0, 1 - turn.W * turn.W));
        var angle = 2 * MathF.Atan2(sine, turn.W);
        var angular = sine > 1e-7f
            ? new Vector3F(turn.X, turn.Y, turn.Z) / sine * (angle / StepSeconds)
            : Vector3F.Zero;
        _world.SetLinearVelocity(handle, linear);
        _world.SetAngularVelocity(handle, angular);
        body.StopsMoving = true;
        body.StopsTurning = true;
        if (body.IsMovedToPose)
            return;

        body.IsMovedToPose = true;
        _movedBodies.Add(new BodyId(slot, body.Generation));
    }

    private void Push(BodyId id, SimulatedBody body)
    {
        if (body.IsPushed || (body.Force == Vector3F.Zero && body.Torque == Vector3F.Zero))
            return;

        body.IsPushed = true;
        _pushedBodies.Add(id);
    }

    private void PushBodies()
    {
        // From the end, as one let go of takes the last one's place.
        for (var index = _pushedBodies.Count - 1; index >= 0; index--)
        {
            var id = _pushedBodies[index];
            var body = _bodySlots[id.Index];
            if (body.Generation != id.Generation || body.Status != PhysicsStatus.Active
                || (body.Force == Vector3F.Zero && body.Torque == Vector3F.Zero))
            {
                if (body.Generation == id.Generation)
                    body.IsPushed = false;

                RemoveSwapping(_pushedBodies, index);
                continue;
            }

            // Too hard for its weight: that push is dropped, and told.
            try
            {
                if (body.Force != Vector3F.Zero)
                    _world.ApplyForce(body.Handle, body.Force);
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
            {
                body.Force = Vector3F.Zero;
                Refuse(id.Index, body, exception.Message);
            }

            try
            {
                if (body.Torque != Vector3F.Zero)
                    _world.ApplyTorque(body.Handle, body.Torque);
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
            {
                body.Torque = Vector3F.Zero;
                Refuse(id.Index, body, exception.Message);
            }
        }
    }

    // What the callers read, from what the step left. A body asleep before the step and after it, and no command at
    // it, has nothing new, and a joint none of whose bodies was read stands as it stood (see _skipsSleepers).
    private void ReadBack()
    {
        // From the end, as one let go of takes the last one's place.
        for (var index = _readBodies.Count - 1; index >= 0; index--)
        {
            var id = _readBodies[index];
            var body = _bodySlots[id.Index];
            if (body.Generation != id.Generation || !body.Handle.IsValid)
            {
                if (body.Generation == id.Generation)
                    body.IsFollowed = false;

                RemoveSwapping(_readBodies, index);
                continue;
            }

            ref readonly var state = ref _world.StateOf(body.Handle);
            if (body.MustRead || !_skipsSleepers || !body.IsSleeping || state.IsAwake)
                ReadBody(body, state);

            // A static body was read for its change; no step moves it after.
            if (body.Kind == BodyKind.Static)
            {
                body.IsFollowed = false;
                RemoveSwapping(_readBodies, index);
            }
        }

        foreach (var id in CollectionsMarshal.AsSpan(_liveJoints))
        {
            var joint = _jointSlots[id.Index];
            if (joint.Handle.IsValid && (_bodySlots[joint.FirstBody].ReadAt == StepNumber
                                         || (joint.SecondBody != NoSlot
                                             && _bodySlots[joint.SecondBody].ReadAt == StepNumber)))
                joint.Position = _world.GetJointPosition(joint.Handle);
        }
    }

    // Taken or changed by a command: read after this step whatever it was, and after every step while not static.
    private void Follow(int slot, SimulatedBody body)
    {
        body.MustRead = true;
        if (body.IsFollowed)
            return;

        body.IsFollowed = true;
        _readBodies.Add(new BodyId(slot, body.Generation));
    }

    private static void RemoveSwapping(List<BodyId> bodies, int index)
    {
        bodies[index] = bodies[^1];
        bodies.RemoveAt(bodies.Count - 1);
    }

    private void ReadBody(SimulatedBody body, in BodyState state)
    {
        body.Pose = state.OriginPose;
        body.CenterOfMass = state.Position;
        body.LinearVelocity = state.LinearVelocity;
        body.AngularVelocity = state.AngularVelocity;
        body.Mass = state.WeighedMass;
        body.Kind = state.Kind;
        body.IsSleeping = !state.IsAwake;
        body.ReadAt = StepNumber;
        body.MustRead = false;
    }

    // A kinematic body moved over the step stands where it was sent, unless sent on.
    private void StopKinematicMoves()
    {
        foreach (var id in CollectionsMarshal.AsSpan(_movedBodies))
        {
            var body = _bodySlots[id.Index];
            if (body.Generation != id.Generation)
                continue;

            var stopsMoving = body.StopsMoving;
            var stopsTurning = body.StopsTurning;
            body.StopsMoving = false;
            body.StopsTurning = false;
            body.IsMovedToPose = false;
            if (!body.Handle.IsValid || _world.GetKind(body.Handle) != BodyKind.Kinematic)
                continue;

            if (stopsMoving)
                _world.SetLinearVelocity(body.Handle, Vector3F.Zero);

            if (stopsTurning)
                _world.SetAngularVelocity(body.Handle, Vector3F.Zero);
        }

        _movedBodies.Clear();
    }

    // The first refusal of a step is told: those after it often only follow from it.
    private void Refuse(int slot, SimulatedBody body, string reason)
    {
        if (body.Refusal != null)
            return;

        body.Refusal = reason;
        _refusedBodies.Add(new BodyId(slot, body.Generation));
    }

    // A refusal told for a change lasts one step; one told for the body's add, its life.
    private void ForgetRefusals()
    {
        foreach (var id in CollectionsMarshal.AsSpan(_refusedBodies))
        {
            var body = _bodySlots[id.Index];
            if (body.Generation == id.Generation && body.Status != PhysicsStatus.Refused)
                body.Refusal = null;
        }

        _refusedBodies.Clear();
        foreach (var id in CollectionsMarshal.AsSpan(_refusedJoints))
        {
            var joint = _jointSlots[id.Index];
            if (joint.Generation == id.Generation && joint.Status != PhysicsStatus.Refused)
                joint.Refusal = null;
        }

        _refusedJoints.Clear();
    }

    private BodyDescription Describe(in BodySettings settings, out object[] partData)
    {
        if (!Enum.IsDefined(settings.Kind))
            throw new ArgumentOutOfRangeException(nameof(settings), settings.Kind, "Unknown kind of body.");

        if ((settings.Shape == null) == settings.Parts.IsEmpty)
            throw new ArgumentException("A body needs a shape, or parts, and not both.", nameof(settings));

        RequireFinite(settings.Position, nameof(settings));
        RequireTurn(settings.Rotation, nameof(settings));
        RequireFinite(settings.LinearVelocity, nameof(settings));
        RequireFinite(settings.AngularVelocity, nameof(settings));
        RequireMaterial(settings.Material, nameof(settings));
        var shape = settings.Shape;
        partData = OnePart;
        if (shape == null)
            shape = Compose(settings.Parts, out partData, nameof(settings));

        return new BodyDescription
        {
            Kind = settings.Kind,
            Shape = shape,
            Position = settings.Position,
            Rotation = settings.Rotation,
            LinearVelocity = settings.LinearVelocity,
            AngularVelocity = settings.AngularVelocity,
            Mass = settings.Mass,
            Density = settings.Density,
            Friction = settings.Material.Friction,
            Restitution = settings.Material.Restitution,
            LinearDamping = settings.LinearDamping,
            AngularDamping = settings.AngularDamping,
            GravityScale = settings.GravityScale,
            CanSleep = settings.CanSleep
        };
    }

    // The parts as one compound, and each part's own in a copy.
    private static CollisionShape Compose(ReadOnlySpan<BodyPart> parts, out object[] partData, string parameterName)
    {
        if (parts.IsEmpty)
            throw new ArgumentException("A body needs at least one part.", parameterName);

        var compoundParts = new CompoundPart[parts.Length];
        partData = new object[parts.Length];
        for (var index = 0; index < parts.Length; index++)
        {
            if (parts[index].Shape == null)
                throw new ArgumentException("A part needs a shape.", parameterName);

            compoundParts[index] = new CompoundPart(parts[index].Shape, parts[index].Pose);
            partData[index] = parts[index].Data;
        }

        return new CompoundCollisionShape(compoundParts);
    }

    private static void RequireMaterial(in PhysicsMaterial material, string parameterName)
    {
        if (!float.IsFinite(material.Friction) || material.Friction < 0 || !float.IsFinite(material.Restitution)
            || material.Restitution < 0)
            throw new ArgumentOutOfRangeException(parameterName, "Friction and restitution must be finite, not below zero.");
    }

    private void Ask(SimulationCommandKind kind, int slot, Vector3F vector) =>
        _commands.Add(new SimulationCommand { Kind = kind, Slot = slot, Vector = vector });

    private int NewBodySlot()
    {
        _bodySlots.Add(new SimulatedBody());
        return _bodySlots.Count - 1;
    }

    private void LetGoOfBody(int slot, SimulatedBody body)
    {
        var last = _liveBodies[^1];
        _liveBodies[body.ListIndex] = last;
        _bodySlots[last.Index].ListIndex = body.ListIndex;
        _liveBodies.RemoveAt(_liveBodies.Count - 1);
        _removedBodySlots.Add(slot);
    }

    // The slots of bodies and joints taken out, free once their removal was done, so no command asks a new one.
    private void FreeRemovedSlots()
    {
        foreach (var slot in _removedBodySlots)
        {
            if (_bodySlots[slot].Generation < int.MaxValue)
                _freeBodySlots.Push(slot);
        }

        _removedBodySlots.Clear();
        foreach (var slot in _removedJointSlots)
        {
            if (_jointSlots[slot].Generation < int.MaxValue)
                _freeJointSlots.Push(slot);
        }

        _removedJointSlots.Clear();
    }

    private bool TryFind(BodyId body, out SimulatedBody record)
    {
        ThrowIfDisposed();
        record = null;
        if (body.IsNone || body.Index < 0 || body.Index >= _bodySlots.Count)
            return false;

        record = _bodySlots[body.Index];
        return record.Generation == body.Generation;
    }

    // A body to read: any the simulation gave, taken out or not.
    private SimulatedBody FindAny(BodyId body) =>
        TryFind(body, out var record) ? record : throw new InvalidOperationException($"{body} is not of this simulation.");

    // A body to act on: being added or in the simulation.
    private SimulatedBody Find(BodyId body)
    {
        var record = FindAny(body);
        if (record.Status is PhysicsStatus.Removed or PhysicsStatus.Refused)
            throw new InvalidOperationException($"{body} is {record.Status.ToString().ToLowerInvariant()}.");

        return record;
    }

    // A body to act on that moves: dynamic only, or kinematic only, as it will be once the commands are done.
    private SimulatedBody FindKind(BodyId body, bool dynamicOnly, bool kinematicOnly)
    {
        var record = Find(body);
        if (dynamicOnly && record.KindAsked != BodyKind.Dynamic)
            throw new InvalidOperationException("Only a dynamic body is moved by forces and impulses.");

        if (kinematicOnly && record.KindAsked != BodyKind.Kinematic)
            throw new InvalidOperationException("Only a kinematic body is moved to a pose over a step.");

        if (record.KindAsked == BodyKind.Static)
            throw new InvalidOperationException("A static body does not move; set its pose instead.");

        return record;
    }
}
