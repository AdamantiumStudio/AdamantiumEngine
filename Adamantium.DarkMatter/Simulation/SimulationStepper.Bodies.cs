using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Adamantium.DarkMatter.Dynamics;

namespace Adamantium.DarkMatter.Simulation;

// Bodies: made, changed, pushed and read. Its lists hold slots, each in a list at most once by its flag; a body taken
// out leaves its flags to the slot, so the next body there is not listed twice.
internal sealed partial class SimulationStepper
{
    private readonly List<CoreBody> _bodies = [];
    // The slot of the body the core keeps at each of its indices.
    private readonly List<int> _slotOfCoreBody = [];
    // The bodies with a force or a torque; those read after every run - the core's not static, a static one read only
    // when taken or changed; those moved to a pose over the next step; those refused a change in this run.
    private readonly List<int> _pushedBodies = [];
    private readonly List<int> _followedBodies = [];
    private readonly List<int> _movedBodies = [];
    private readonly List<int> _refusedBodies = [];

    /// <summary>The core's handle of the body in <paramref name="slot"/>; none for one not in the core.</summary>
    public BodyHandle HandleOf(int slot) => slot >= 0 && slot < _bodies.Count ? _bodies[slot].Handle : default;

    /// <summary>The slot of the body under <paramref name="handle"/>, with its generation.</summary>
    public BodyId IdOf(BodyHandle handle)
    {
        var slot = _slotOfCoreBody[handle.Index];
        return new BodyId(slot, _bodies[slot].Generation);
    }

    private void DoBodyCommand(in SimulationCommand command)
    {
        if (command.Kind == SimulationCommandKind.AddBody)
        {
            PutBody(command.Slot, command.Generation, command.Body, command.PartData);
            return;
        }

        var body = _bodies[command.Slot];
        // Its add refused, or taken out before: nothing to do it on.
        if (!body.Handle.IsValid)
            return;

        try
        {
            switch (command.Kind)
            {
                case SimulationCommandKind.RemoveBody:
                    _world.Remove(body.Handle);
                    // Its touches ended, told by its id before the core's index of it names nothing.
                    TakeContactEvents();
                    _slotOfCoreBody[body.Handle.Index] = NoSlot;
                    body.Handle = default;
                    body.Force = Vector3F.Zero;
                    body.Torque = Vector3F.Zero;
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
                case SimulationCommandKind.SetForce:
                    body.Force = command.Vector;
                    Push(command.Slot, body);
                    break;
                case SimulationCommandKind.SetTorque:
                    body.Torque = command.Vector;
                    Push(command.Slot, body);
                    break;
                case SimulationCommandKind.SetPose:
                    _world.SetPose(body.Handle, command.Pose);
                    body.Jumped = true;
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
                    // Only a dynamic body is pushed.
                    if (command.BodyKind != BodyKind.Dynamic)
                    {
                        body.Force = Vector3F.Zero;
                        body.Torque = Vector3F.Zero;
                    }

                    Follow(command.Slot, body);
                    break;
                case SimulationCommandKind.SetShape:
                    _world.SetShape(body.Handle, command.Shape, command.PartTraits);
                    TakeContactEvents();
                    body.PartData = command.PartData;
                    Tell(new SimulationNews
                    {
                        Kind = NewsKind.ShapeTaken, Slot = command.Slot, Generation = body.Generation,
                        Shape = command.Shape, PartData = command.PartData, PartTraits = command.PartTraits
                    });
                    Follow(command.Slot, body);
                    break;
                case SimulationCommandKind.SetMaterial:
                    _world.SetMaterial(body.Handle, command.Material);
                    Tell(new SimulationNews
                    {
                        Kind = NewsKind.MaterialTaken, Slot = command.Slot, Generation = body.Generation,
                        Material = command.Material
                    });
                    break;
                case SimulationCommandKind.SetLayer:
                    _world.SetLayer(body.Handle, command.Layer);
                    Tell(new SimulationNews
                    {
                        Kind = NewsKind.LayerTaken, Slot = command.Slot, Generation = body.Generation,
                        Layer = command.Layer
                    });
                    break;
                case SimulationCommandKind.SetMass:
                    _world.SetMass(body.Handle, command.Mass, command.Density);
                    Follow(command.Slot, body);
                    break;
                case SimulationCommandKind.SetDamping:
                    _world.SetDamping(body.Handle, command.LinearDamping, command.AngularDamping);
                    break;
                case SimulationCommandKind.SetGravityScale:
                    _world.SetGravityScale(body.Handle, command.GravityScale);
                    break;
                case SimulationCommandKind.SetCanSleep:
                    _world.SetCanSleep(body.Handle, command.CanSleep);
                    Follow(command.Slot, body);
                    break;
            }
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            RefuseChange(command.Slot, body, exception.Message);
        }

        // Every kind asked was asked before the run: the kind the body has once each is done, taken or not.
        if (command.Kind == SimulationCommandKind.SetKind && body.Handle.IsValid)
        {
            Tell(new SimulationNews
            {
                Kind = NewsKind.KindSettled, Slot = command.Slot, Generation = body.Generation,
                BodyKind = _world.GetKind(body.Handle), KindAsk = command.KindAsk
            });
        }
    }

    private void PutBody(int slot, int generation, BodyDescription description, object[] partData)
    {
        while (_bodies.Count <= slot)
        {
            _bodies.Add(new CoreBody());
        }

        var body = _bodies[slot];
        body.Generation = generation;
        body.Handle = default;
        body.PartData = partData;
        body.Force = Vector3F.Zero;
        body.Torque = Vector3F.Zero;
        body.StopsMoving = false;
        body.StopsTurning = false;
        try
        {
            body.Handle = _world.Add(description);
        }
        catch (ArgumentException exception)
        {
            Tell(new SimulationNews
            {
                Kind = NewsKind.Refused, Slot = slot, Generation = generation, Refusal = exception.Message
            });
            return;
        }

        while (_slotOfCoreBody.Count <= body.Handle.Index)
        {
            _slotOfCoreBody.Add(NoSlot);
        }

        _slotOfCoreBody[body.Handle.Index] = slot;
        Tell(new SimulationNews
        {
            Kind = NewsKind.Taken, Slot = slot, Generation = generation, Shape = description.Shape, PartData = partData,
            PartTraits = description.PartTraits, Layer = description.Layer
        });
        Follow(slot, body);
    }

    // Over the next step by its velocity: where the center of mass is to go, and the turn the body is to make.
    private void StartKinematicMove(int slot, CoreBody body, in Pose pose)
    {
        var handle = body.Handle;
        var from = _world.GetPose(handle);
        var centerTo = pose.TransformPoint(_world.GetLocalCenterOfMass(handle));
        var linear = centerTo.OffsetFrom(_world.GetCenterOfMass(handle)) / _world.StepSeconds;
        var turn = QuaternionF.Normalize(pose.Rotation) * QuaternionF.Conjugate(from.Rotation);
        if (turn.W < 0)
            turn = -turn;

        var sine = MathF.Sqrt(MathF.Max(0, 1 - turn.W * turn.W));
        var angle = 2 * MathF.Atan2(sine, turn.W);
        var angular = sine > 1e-7f
            ? new Vector3F(turn.X, turn.Y, turn.Z) / sine * (angle / _world.StepSeconds)
            : Vector3F.Zero;
        _world.SetLinearVelocity(handle, linear);
        _world.SetAngularVelocity(handle, angular);
        body.StopsMoving = true;
        body.StopsTurning = true;
        Follow(slot, body);
        if (body.IsMovedToPose)
            return;

        body.IsMovedToPose = true;
        _movedBodies.Add(slot);
    }

    private void Push(int slot, CoreBody body)
    {
        if (body.IsPushed || (body.Force == Vector3F.Zero && body.Torque == Vector3F.Zero))
            return;

        body.IsPushed = true;
        _pushedBodies.Add(slot);
    }

    private void PushBodies()
    {
        // From the end, as one let go of takes the last one's place.
        for (var index = _pushedBodies.Count - 1; index >= 0; index--)
        {
            var slot = _pushedBodies[index];
            var body = _bodies[slot];
            if (!body.Handle.IsValid || (body.Force == Vector3F.Zero && body.Torque == Vector3F.Zero))
            {
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
                RefuseChange(slot, body, exception.Message);
            }

            try
            {
                if (body.Torque != Vector3F.Zero)
                    _world.ApplyTorque(body.Handle, body.Torque);
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
            {
                body.Torque = Vector3F.Zero;
                RefuseChange(slot, body, exception.Message);
            }
        }
    }

    // What the caller reads, from what the run left. A body asleep before the run and after it, and no command at it,
    // has nothing new, and a joint none of whose bodies was read stands as it stood (see _skipsSleepers).
    private void ReadBack()
    {
        // From the end, as one let go of takes the last one's place.
        for (var index = _followedBodies.Count - 1; index >= 0; index--)
        {
            var slot = _followedBodies[index];
            var body = _bodies[slot];
            if (!body.Handle.IsValid)
            {
                body.IsFollowed = false;
                RemoveSwapping(_followedBodies, index);
                continue;
            }

            ref readonly var state = ref _world.StateOf(body.Handle);
            if (body.MustRead || !_skipsSleepers || !body.WasSleeping || state.IsAwake)
                Read(slot, body, state);

            // A static body was read for its change; no step moves it after.
            if (state.Kind == BodyKind.Static)
            {
                body.IsFollowed = false;
                RemoveSwapping(_followedBodies, index);
            }
        }

        foreach (var joint in CollectionsMarshal.AsSpan(_liveJoints))
        {
            var record = _joints[joint];
            if (record.Handle.IsValid && (_bodies[record.FirstBody].ReadIn == _run
                                          || (record.SecondBody != NoSlot && _bodies[record.SecondBody].ReadIn == _run)))
            {
                _result.Joints.Add(new JointReading
                {
                    Slot = joint, Generation = record.Generation, Position = _world.GetJointPosition(record.Handle)
                });
            }
        }
    }

    // Taken or changed by a command: read after this run whatever it was, and after every run while not static.
    private void Follow(int slot, CoreBody body)
    {
        body.MustRead = true;
        if (body.IsFollowed)
            return;

        body.IsFollowed = true;
        _followedBodies.Add(slot);
    }

    private void Read(int slot, CoreBody body, in BodyState state)
    {
        _result.Bodies.Add(new BodyReading
        {
            Slot = slot,
            Generation = body.Generation,
            Pose = state.OriginPose,
            CenterOfMass = state.Position,
            LinearVelocity = state.LinearVelocity,
            AngularVelocity = state.AngularVelocity,
            Mass = state.WeighedMass,
            Kind = state.Kind,
            IsSleeping = !state.IsAwake,
            Jumped = body.Jumped
        });
        body.WasSleeping = !state.IsAwake;
        body.ReadIn = _run;
        body.MustRead = false;
        body.Jumped = false;
    }

    // A kinematic body moved over the step stands where it was sent, unless sent on.
    private void StopKinematicMoves()
    {
        foreach (var slot in CollectionsMarshal.AsSpan(_movedBodies))
        {
            var body = _bodies[slot];
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

    // The first refusal of a run is told: those after it often only follow from it.
    private void RefuseChange(int slot, CoreBody body, string reason)
    {
        if (body.IsRefusedInRun)
            return;

        body.IsRefusedInRun = true;
        _refusedBodies.Add(slot);
        Tell(new SimulationNews
        {
            Kind = NewsKind.ChangeRefused, Slot = slot, Generation = body.Generation, Refusal = reason
        });
    }
}
