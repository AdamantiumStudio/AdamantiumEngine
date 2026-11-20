using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Adamantium.DarkMatter.Shapes;
using Adamantium.DarkMatter.Simulation;

namespace Adamantium.DarkMatter;

// Bodies: added, read, pushed and taken out by id.
public sealed partial class PhysicsSimulation
{
    private readonly List<SimulatedBody> _bodySlots = [];
    private readonly Stack<int> _freeBodySlots = new();
    private readonly List<TakenOutSlot> _takenOutSlots = [];
    private readonly List<BodyId> _liveBodies = [];
    // The bodies the last run read moved, and those this one does: one moved before and not now is drawn where it
    // stopped.
    private readonly List<BodyId> _movedBodies = [];
    private readonly List<BodyId> _movingBodies = [];
    // Those told a refusal of a change at the last read; those the reads of this Update moved, for the snapshot once.
    private readonly List<BodyId> _refusedBodies = [];
    private readonly List<BodyId> _snapshotMoves = [];
    // Those put by hand whose jump's batch was not read yet.
    private readonly List<BodyId> _jumpingBodies = [];
    // Counts the Updates that read, so a body's snapshot copy moves once in each.
    private long _snapshotMark;
    // The last batch of commands read as run.
    private long _batchesRead;
    private QuerySnapshot _snapshot;
    private long _resultNumber;

    // The caller's own of a body of one shape, before any is given: shared, as it is never written.
    private static readonly object[] OnePart = [null];

    /// <summary>The bodies added and not taken out, in no order kept; good until the next add or removal.</summary>
    public ReadOnlySpan<BodyId> Bodies => CollectionsMarshal.AsSpan(_liveBodies);

    /// <summary>Adds a body as <paramref name="settings"/> say and gives its id; it is <see cref="PhysicsStatus.Adding"/>
    /// until a step takes it, and actions on it can be asked at once.</summary>
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
        body.Shape = description.Shape;
        body.Pose = new Pose(settings.Position, QuaternionF.Normalize(settings.Rotation));
        body.PreviousPose = body.Pose;
        body.CenterOfMass = settings.Position;
        body.LinearVelocity = settings.LinearVelocity;
        body.AngularVelocity = settings.AngularVelocity;
        body.Mass = 0;
        body.Kind = settings.Kind;
        body.IsSleeping = false;
        body.ReadIn = -1;
        body.JumpBatch = 0;
        body.SnapshotMark = 0;
        body.KindAsked = settings.Kind;
        body.KindAsks = 0;
        var id = new BodyId(slot, body.Generation);
        body.ListIndex = _liveBodies.Count;
        _liveBodies.Add(id);
        _commands.Add(new SimulationCommand
        {
            Kind = SimulationCommandKind.AddBody, Slot = slot, Generation = body.Generation, Body = description,
            PartData = partData
        });
        return id;
    }

    /// <summary>Takes <paramref name="body"/> out, its joints with it, refused or not; its id names nothing from now on.
    /// </summary>
    public void RemoveBody(BodyId body)
    {
        var record = FindAny(body);
        if (record.Status == PhysicsStatus.Removed)
            throw new InvalidOperationException($"{body} is removed.");

        // Its joints go before it; from the end, as each one let go of takes the last one's place.
        for (var index = _liveJoints.Count - 1; index >= 0; index--)
        {
            var joint = _liveJoints[index];
            var jointRecord = _jointSlots[joint.Index];
            if (jointRecord.FirstBody == body.Index || jointRecord.SecondBody == body.Index)
                TakeOut(joint, jointRecord);
        }

        record.Status = PhysicsStatus.Removed;
        LetGoOfBody(body.Index, record);
        _snapshot?.Remove(body.Index);
        _commands.Add(new SimulationCommand { Kind = SimulationCommandKind.RemoveBody, Slot = body.Index });
    }

    /// <summary>Whether <paramref name="body"/> is being added or is in the simulation.</summary>
    public bool Contains(BodyId body) =>
        TryFind(body, out var record) && record.Status is PhysicsStatus.Adding or PhysicsStatus.Active;

    /// <summary>Where <paramref name="body"/> is in its life; removed for an id the simulation never gave.</summary>
    public PhysicsStatus StatusOf(BodyId body) => TryFind(body, out var record) ? record.Status : PhysicsStatus.Removed;

    /// <summary>Why the simulation would not take <paramref name="body"/>, or a change asked of it, as the last read
    /// told; null when it did them all.</summary>
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

    /// <summary>Where <paramref name="body"/>'s origin stood after the last step read, and how it was turned.</summary>
    public Pose PoseOf(BodyId body) => FindAny(body).Pose;

    /// <summary>Where to draw <paramref name="body"/> now: between its last two steps read by the time since, turned
    /// about its center of mass, so its motion is smooth at any frame rate; put somewhere by hand, there until a step
    /// from there is read. Stepped one step at a time, where it stood after the last.</summary>
    public Pose InterpolatedPoseOf(BodyId body)
    {
        var record = FindAny(body);
        if (record.JumpBatch != 0)
            return record.JumpPose;

        var blend = MeasureBlend();
        var turn = QuaternionF.Slerp(record.PreviousPose.Rotation, record.Pose.Rotation, blend);
        // Its center of mass in its own axes: the point it turns about, carried from each pose to blend between.
        var center = Vector3F.Transform(record.CenterOfMass.OffsetFrom(record.Pose.Position),
            QuaternionF.Conjugate(record.Pose.Rotation));
        var from = record.PreviousPose.TransformPoint(center);
        var to = record.Pose.TransformPoint(center);
        return new Pose(WorldPosition.Lerp(from, to, blend) - Vector3F.Transform(center, turn), turn);
    }

    /// <summary>Where <paramref name="body"/>'s center of mass stood after the last step read.</summary>
    public WorldPosition CenterOfMassOf(BodyId body) => FindAny(body).CenterOfMass;

    /// <summary>The velocity of <paramref name="body"/>'s center of mass after the last step read, in meters per
    /// second.</summary>
    public Vector3F LinearVelocityOf(BodyId body) => FindAny(body).LinearVelocity;

    /// <summary>The spin of <paramref name="body"/> after the last step read, in radians per second around each world
    /// axis.</summary>
    public Vector3F AngularVelocityOf(BodyId body) => FindAny(body).AngularVelocity;

    /// <summary>What <paramref name="body"/> weighs, in kilograms; zero for one not dynamic or not yet taken.</summary>
    public float MassOf(BodyId body) => FindAny(body).Mass;

    /// <summary>How <paramref name="body"/> moves.</summary>
    public BodyKind KindOf(BodyId body) => FindAny(body).Kind;

    /// <summary>Whether <paramref name="body"/> was asleep after the last step read; a static body always is.</summary>
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
    /// again; zero stops it, and so does the body made other than dynamic.</summary>
    public void SetForce(BodyId body, Vector3F force)
    {
        RequireFinite(force, nameof(force));
        FindKind(body, true, false);
        Ask(SimulationCommandKind.SetForce, body.Index, force);
    }

    /// <summary>Turns a dynamic body with <paramref name="torque"/>, in newton meters around each world axis, at every
    /// step until set again; zero stops it, and so does the body made other than dynamic.</summary>
    public void SetTorque(BodyId body, Vector3F torque)
    {
        RequireFinite(torque, nameof(torque));
        FindKind(body, true, false);
        Ask(SimulationCommandKind.SetTorque, body.Index, torque);
    }

    /// <summary>Puts <paramref name="body"/>'s origin at <paramref name="pose"/> at the next step, as a jump, not a
    /// motion: drawn there at once (<see cref="InterpolatedPoseOf"/>), read there once that step is read.</summary>
    public void SetPose(BodyId body, in Pose pose)
    {
        RequireFinite(pose.Position, nameof(pose));
        RequireTurn(pose.Rotation, nameof(pose));
        var record = Find(body);
        // Drawn where it was put, not back where the steps under way leave it, until the step of the jump is read.
        record.JumpPose = new Pose(pose.Position, QuaternionF.Normalize(pose.Rotation));
        if (record.JumpBatch == 0)
            _jumpingBodies.Add(body);

        record.JumpBatch = _handedBatches + 1;
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
        record.KindAsks++;
        _commands.Add(new SimulationCommand
        {
            Kind = SimulationCommandKind.SetKind, Slot = body.Index, BodyKind = kind, KindAsk = record.KindAsks
        });
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

    /// <summary>Weighs <paramref name="body"/> by <paramref name="mass"/> in kilograms, or by
    /// <paramref name="density"/> in kilograms per cubic meter when the mass is zero; kept for a body not dynamic until
    /// it is.</summary>
    public void SetMass(BodyId body, float mass, float density)
    {
        RequireAmount(mass, nameof(mass));
        RequireAmount(density, nameof(density));
        Find(body);
        _commands.Add(new SimulationCommand
        {
            Kind = SimulationCommandKind.SetMass, Slot = body.Index, Mass = mass, Density = density
        });
    }

    /// <summary>Sets how fast <paramref name="body"/>'s velocity and spin fade by themselves, per second.</summary>
    public void SetDamping(BodyId body, float linearDamping, float angularDamping)
    {
        RequireAmount(linearDamping, nameof(linearDamping));
        RequireAmount(angularDamping, nameof(angularDamping));
        Find(body);
        _commands.Add(new SimulationCommand
        {
            Kind = SimulationCommandKind.SetDamping, Slot = body.Index, LinearDamping = linearDamping,
            AngularDamping = angularDamping
        });
    }

    /// <summary>Sets how much gravity pulls <paramref name="body"/>: 1 as the simulation's, 0 not at all.</summary>
    public void SetGravityScale(BodyId body, float gravityScale)
    {
        if (!float.IsFinite(gravityScale))
            throw new ArgumentOutOfRangeException(nameof(gravityScale), gravityScale, "The scale must be finite.");

        Find(body);
        _commands.Add(new SimulationCommand
        {
            Kind = SimulationCommandKind.SetGravityScale, Slot = body.Index, GravityScale = gravityScale
        });
    }

    /// <summary>Sets whether <paramref name="body"/> may fall asleep at rest; forbidden, it wakes.</summary>
    public void SetCanSleep(BodyId body, bool canSleep)
    {
        Find(body);
        _commands.Add(new SimulationCommand
        {
            Kind = SimulationCommandKind.SetCanSleep, Slot = body.Index, CanSleep = canSleep
        });
    }

    // What a run left, brought into what the caller reads: what it tells, then where the bodies and joints stand.
    private void Read(SimulationResult result)
    {
        _resultNumber++;
        foreach (ref readonly var news in CollectionsMarshal.AsSpan(result.News))
        {
            if (news.IsJoint)
                ReadJointNews(news);
            else
                ReadBodyNews(news);
        }

        foreach (ref readonly var reading in CollectionsMarshal.AsSpan(result.Bodies))
        {
            var body = _bodySlots[reading.Slot];
            if (body.Generation != reading.Generation || body.Status == PhysicsStatus.Removed)
                continue;

            // From where it stood before - as it was added, for its first - unless it was put here by hand.
            body.PreviousPose = reading.Jumped ? reading.Pose : body.Pose;
            body.Pose = reading.Pose;
            body.CenterOfMass = reading.CenterOfMass;
            body.LinearVelocity = reading.LinearVelocity;
            body.AngularVelocity = reading.AngularVelocity;
            body.Mass = reading.Mass;
            body.Kind = reading.Kind;
            body.IsSleeping = reading.IsSleeping;
            body.ReadIn = _resultNumber;
            _movingBodies.Add(new BodyId(reading.Slot, reading.Generation));
            if (_snapshot != null && body.SnapshotMark != _snapshotMark)
            {
                body.SnapshotMark = _snapshotMark;
                _snapshotMoves.Add(new BodyId(reading.Slot, reading.Generation));
            }
        }

        EndJumps(result.CommandBatch);

        // One moved by the run before and not by this one stopped: drawn where it stands.
        foreach (var id in CollectionsMarshal.AsSpan(_movedBodies))
        {
            var body = _bodySlots[id.Index];
            if (body.Generation == id.Generation && body.ReadIn != _resultNumber)
                body.PreviousPose = body.Pose;
        }

        _movedBodies.Clear();
        _movedBodies.AddRange(_movingBodies);
        _movingBodies.Clear();
        foreach (ref readonly var reading in CollectionsMarshal.AsSpan(result.Joints))
        {
            var joint = _jointSlots[reading.Slot];
            if (joint.Generation == reading.Generation)
                joint.Position = reading.Position;
        }

        StepNumber = result.StepNumber;
        LastStep = result.Statistics;
        _batchesRead = Math.Max(_batchesRead, result.CommandBatch);
        FreeTakenOutSlots(result.CommandBatch);
    }

    private void ReadBodyNews(in SimulationNews news)
    {
        var body = _bodySlots[news.Slot];
        if (body.Generation != news.Generation)
            return;

        switch (news.Kind)
        {
            case NewsKind.Taken when body.Status == PhysicsStatus.Adding:
                body.Status = PhysicsStatus.Active;
                body.Shape = news.Shape;
                _snapshot?.Place(news.Slot, news.Generation, news.Shape, body.Pose);
                break;
            case NewsKind.Refused when body.Status == PhysicsStatus.Adding:
                body.Status = PhysicsStatus.Refused;
                body.Refusal = news.Refusal;
                break;
            case NewsKind.ChangeRefused when body.Refusal == null:
                body.Refusal = news.Refusal;
                _refusedBodies.Add(new BodyId(news.Slot, news.Generation));
                break;
            case NewsKind.ShapeTaken when body.Status == PhysicsStatus.Active:
                body.Shape = news.Shape;
                body.PartData = news.PartData;
                _snapshot?.Place(news.Slot, news.Generation, news.Shape, body.Pose);
                break;
            case NewsKind.KindSettled when news.KindAsk == body.KindAsks:
                body.KindAsked = news.BodyKind;
                break;
        }
    }

    // The snapshot's copies moved where the reads of this Update left their bodies, each once.
    private void MoveSnapshot()
    {
        foreach (var id in CollectionsMarshal.AsSpan(_snapshotMoves))
        {
            var body = _bodySlots[id.Index];
            if (body.Generation == id.Generation && body.Status == PhysicsStatus.Active)
                _snapshot.Move(id.Index, id.Generation, body.Pose);
        }

        _snapshotMoves.Clear();
    }

    // A jump's batch run and read - its step read, or its jump refused - the body is drawn as it moves from there.
    private void EndJumps(long batchRun)
    {
        for (var index = _jumpingBodies.Count - 1; index >= 0; index--)
        {
            var id = _jumpingBodies[index];
            var body = _bodySlots[id.Index];
            if (body.Generation == id.Generation && body.JumpBatch > batchRun)
                continue;

            if (body.Generation == id.Generation)
                body.JumpBatch = 0;

            _jumpingBodies[index] = _jumpingBodies[^1];
            _jumpingBodies.RemoveAt(_jumpingBodies.Count - 1);
        }
    }

    // A refusal told for a change lasts until the next read; one told for an add, the body's life.
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

    // The queries' world as the caller reads it now, for a thread about to step.
    private QuerySnapshot TakeSnapshot()
    {
        var snapshot = new QuerySnapshot();
        foreach (var id in CollectionsMarshal.AsSpan(_liveBodies))
        {
            var body = _bodySlots[id.Index];
            if (body.Status == PhysicsStatus.Active)
                snapshot.Place(id.Index, id.Generation, body.Shape, body.Pose);
        }

        return snapshot;
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

    private static void RequireAmount(float value, string parameterName)
    {
        if (!float.IsFinite(value) || value < 0)
            throw new ArgumentOutOfRangeException(parameterName, value, "The amount must be finite, not below zero.");
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
        _takenOutSlots.Add(new TakenOutSlot(slot, false, _handedBatches + 1));
    }

    // The slots of bodies and joints whose removal was run by now are free for others.
    private void FreeTakenOutSlots(long batchRun)
    {
        for (var index = _takenOutSlots.Count - 1; index >= 0; index--)
        {
            var taken = _takenOutSlots[index];
            if (taken.Batch > batchRun)
                continue;

            _takenOutSlots[index] = _takenOutSlots[^1];
            _takenOutSlots.RemoveAt(_takenOutSlots.Count - 1);
            if (taken.IsJoint)
            {
                if (_jointSlots[taken.Slot].Generation < int.MaxValue)
                    _freeJointSlots.Push(taken.Slot);
            }
            else if (_bodySlots[taken.Slot].Generation < int.MaxValue)
            {
                _freeBodySlots.Push(taken.Slot);
            }
        }
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
