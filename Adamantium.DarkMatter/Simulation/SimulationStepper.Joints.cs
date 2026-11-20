using System;
using System.Collections.Generic;

namespace Adamantium.DarkMatter.Simulation;

// Joints: made where their bodies stand when taken, changed, and taken out.
internal sealed partial class SimulationStepper
{
    private readonly List<CoreJoint> _joints = [];
    // The slots of the joints the core holds, each with its place in the list.
    private readonly List<int> _liveJoints = [];
    private readonly List<int> _placeOfLiveJoint = [];
    private readonly List<int> _refusedJoints = [];

    private void DoJointCommand(in SimulationCommand command)
    {
        if (command.Kind == SimulationCommandKind.AddJoint)
        {
            PutJoint(command.Slot, command.Generation, command.Joint);
            return;
        }

        var joint = _joints[command.Slot];
        if (!joint.Handle.IsValid)
            return;

        try
        {
            switch (command.Kind)
            {
                case SimulationCommandKind.RemoveJoint:
                    if (_world.Contains(joint.Handle))
                        _world.Remove(joint.Handle);

                    joint.Handle = default;
                    LetGoOfJoint(command.Slot);
                    break;
                case SimulationCommandKind.SetLimits:
                    var limits = command.Limits;
                    _world.SetLimits(joint.Handle, limits.HasValue, limits?.Lower ?? 0, limits?.Upper ?? 0);
                    break;
                case SimulationCommandKind.SetMotor:
                    _world.SetMotor(joint.Handle, command.Motor?.Speed ?? 0, command.Motor?.MaxForce ?? 0);
                    break;
            }
        }
        // The first refusal of a run is told, as for a body.
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            if (joint.IsRefusedInRun)
                return;

            joint.IsRefusedInRun = true;
            _refusedJoints.Add(command.Slot);
            Tell(new SimulationNews
            {
                Kind = NewsKind.ChangeRefused, IsJoint = true, Slot = command.Slot, Generation = joint.Generation,
                Refusal = exception.Message
            });
        }
    }

    // Its bodies are the core's by now, unless one was refused or taken out: then neither is the joint.
    private void PutJoint(int slot, int generation, in JointSettings settings)
    {
        while (_joints.Count <= slot)
        {
            _joints.Add(new CoreJoint());
            _placeOfLiveJoint.Add(NoSlot);
        }

        var joint = _joints[slot];
        joint.Generation = generation;
        joint.Handle = default;
        joint.FirstBody = settings.First.Index;
        joint.SecondBody = settings.Second.IsNone ? NoSlot : settings.Second.Index;
        var first = HandleOf(joint.FirstBody);
        var second = joint.SecondBody == NoSlot ? default : HandleOf(joint.SecondBody);
        if (!first.IsValid || (joint.SecondBody != NoSlot && !second.IsValid))
        {
            RefuseJoint(slot, generation, "A body of the joint is not in the simulation.");
            return;
        }

        try
        {
            joint.Handle = _world.Add(Describe(settings, first, second));
        }
        catch (ArgumentException exception)
        {
            RefuseJoint(slot, generation, exception.Message);
            return;
        }

        _placeOfLiveJoint[slot] = _liveJoints.Count;
        _liveJoints.Add(slot);
        Tell(new SimulationNews { Kind = NewsKind.Taken, IsJoint = true, Slot = slot, Generation = generation });
        _result.Joints.Add(new JointReading
        {
            Slot = slot, Generation = generation, Position = _world.GetJointPosition(joint.Handle)
        });
    }

    // Points on the bodies are put in the world where the bodies stand now.
    private JointDescription Describe(in JointSettings settings, BodyHandle first, BodyHandle second)
    {
        var point = settings.Point;
        var secondPoint = settings.SecondPoint;
        var axis = settings.Axis;
        if (settings.Space == JointSpace.Bodies)
        {
            var firstPose = _world.GetPose(first);
            point = firstPose.TransformPoint(settings.FirstAnchor);
            axis = firstPose.TransformDirection(settings.Axis);
            if (second.IsValid)
                secondPoint = _world.GetPose(second).TransformPoint(settings.SecondAnchor);
        }

        return new JointDescription
        {
            Kind = settings.Kind,
            First = first,
            Second = second,
            Point = point,
            SecondPoint = secondPoint,
            Axis = axis,
            CollideConnected = settings.CollideConnected,
            HasLimits = settings.Limits.HasValue,
            LowerLimit = settings.Limits?.Lower ?? 0,
            UpperLimit = settings.Limits?.Upper ?? 0,
            MotorSpeed = settings.Motor?.Speed ?? 0,
            MaxMotorForce = settings.Motor?.MaxForce ?? 0,
            SpringHertz = settings.Spring?.Hertz ?? 0,
            SpringDampingRatio = settings.Spring?.DampingRatio ?? 1
        };
    }

    private void RefuseJoint(int slot, int generation, string reason) =>
        Tell(new SimulationNews
        {
            Kind = NewsKind.Refused, IsJoint = true, Slot = slot, Generation = generation, Refusal = reason
        });

    private void LetGoOfJoint(int slot)
    {
        var place = _placeOfLiveJoint[slot];
        if (place == NoSlot)
            return;

        var last = _liveJoints[^1];
        _liveJoints[place] = last;
        _placeOfLiveJoint[last] = place;
        _liveJoints.RemoveAt(_liveJoints.Count - 1);
        _placeOfLiveJoint[slot] = NoSlot;
    }
}
