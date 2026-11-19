using System;
using System.Collections.Generic;

namespace Adamantium.DarkMatter.Dynamics;

/// <summary>Solves joints as soft constraints in the substeps the contacts are solved in, row by row, as Box2D v3 and
/// Jolt do: a row along a direction keeps two points from parting that way, a row about an axis keeps two bodies from
/// turning apart about it; a limit pushes one way only, a motor no harder than its force. A joint's motor goes first,
/// its limits next, what it holds last - the last word is the joint's own.</summary>
internal sealed class JointSolver
{
    private readonly List<Joint> _active = [];
    private BodyState _world = new() { Rotation = QuaternionF.Identity, Kind = BodyKind.Static };

    /// <summary>The joints solved this step.</summary>
    public List<Joint> Active => _active;

    /// <summary>Takes the joints with a moving body.</summary>
    public void Prepare(BodyState[] bodies, List<Joint> joints)
    {
        _active.Clear();
        foreach (var joint in joints)
        {
            ref var first = ref BodyOf(bodies, joint.First);
            ref var second = ref BodyOf(bodies, joint.Second);
            if ((first.IsDynamic && first.IsAwake) || (second.IsDynamic && second.IsAwake))
                _active.Add(joint);
        }
    }

    /// <summary>Gives every row the impulse it ended the last substep with.</summary>
    public void WarmStart(BodyState[] bodies) => Pass(bodies, JointPass.WarmStart, 0, default);

    /// <summary>One pass over the joints; pushing, the soft spring closes their errors, relaxing, only their speeds are
    /// taken out.</summary>
    public void Solve(BodyState[] bodies, bool push, float substep, Softness softness) =>
        Pass(bodies, push ? JointPass.Push : JointPass.Relax, substep, softness);

    /// <summary>Where the joint stands: a hinge's angle from where it was added, a slider's offset from where it was
    /// added, a distance joint's length; nothing for the rest.</summary>
    public float Measure(BodyState[] bodies, Joint joint)
    {
        ref var first = ref BodyOf(bodies, joint.First);
        ref var second = ref BodyOf(bodies, joint.Second);
        var frame = new JointFrame(joint, first, second);
        return joint.Kind switch
        {
            JointKind.Hinge => frame.MeasureAngle(joint, second),
            JointKind.Slider => Vector3F.Dot(frame.Apart, frame.AxisOfFirst),
            JointKind.Distance => frame.Apart.Length(),
            _ => 0
        };
    }

    private void Pass(BodyState[] bodies, JointPass pass, float substep, Softness softness)
    {
        var inverseSubstep = substep > 0 ? 1f / substep : 0;
        for (var index = 0; index < _active.Count; index++)
        {
            var joint = _active[index];
            ref var first = ref BodyOf(bodies, joint.First);
            ref var second = ref BodyOf(bodies, joint.Second);
            var rows = new JointRows(joint, pass, inverseSubstep, substep, softness);
            var frame = new JointFrame(joint, first, second);
            switch (joint.Kind)
            {
                case JointKind.Fixed:
                    KeepTurn(ref rows, ref first, ref second, joint, 0);
                    KeepPoint(ref rows, ref first, ref second, frame, 3);
                    break;
                case JointKind.Ball:
                    KeepPoint(ref rows, ref first, ref second, frame, 0);
                    break;
                case JointKind.Hinge:
                    DriveAngle(ref rows, ref first, ref second, joint, frame);
                    KeepAxis(ref rows, ref first, ref second, frame, 3);
                    KeepPoint(ref rows, ref first, ref second, frame, 5);
                    break;
                case JointKind.Slider:
                    DriveOffset(ref rows, ref first, ref second, joint, frame);
                    KeepTurn(ref rows, ref first, ref second, joint, 3);
                    KeepOnAxis(ref rows, ref first, ref second, frame, 6);
                    break;
                case JointKind.Distance:
                    KeepDistance(ref rows, ref first, ref second, joint, frame);
                    break;
            }
        }
    }

    // A body of the world's, or the world itself for no body: still, never moved, weighing nothing it can give.
    private ref BodyState BodyOf(BodyState[] bodies, int body)
    {
        if (body >= 0)
            return ref bodies[body];

        return ref _world;
    }

    // The two points, three rows along the world's axes.
    private static void KeepPoint(ref JointRows rows, ref BodyState first, ref BodyState second, in JointFrame frame,
        int row)
    {
        rows.Along(ref first, ref second, frame.ArmOfFirst, frame.ArmOfSecond, Vector3F.UnitX, frame.Apart.X, row);
        rows.Along(ref first, ref second, frame.ArmOfFirst, frame.ArmOfSecond, Vector3F.UnitY, frame.Apart.Y, row + 1);
        rows.Along(ref first, ref second, frame.ArmOfFirst, frame.ArmOfSecond, Vector3F.UnitZ, frame.Apart.Z, row + 2);
    }

    // The turn between the bodies as it was when added, three rows about the world's axes; the error the small turn
    // that takes the second body back.
    private static void KeepTurn(ref JointRows rows, ref BodyState first, ref BodyState second, Joint joint, int row)
    {
        var error = second.Rotation * QuaternionF.Conjugate(first.Rotation * joint.RestTurn);
        if (error.W < 0)
            error = -error;

        var turned = new Vector3F(error.X, error.Y, error.Z) * 2;
        rows.About(ref first, ref second, Vector3F.UnitX, turned.X, row);
        rows.About(ref first, ref second, Vector3F.UnitY, turned.Y, row + 1);
        rows.About(ref first, ref second, Vector3F.UnitZ, turned.Z, row + 2);
    }

    // The second body's axis kept on the first's: two rows about directions square to it, fixed on the first body so
    // their held impulses keep their way from substep to substep.
    private static void KeepAxis(ref JointRows rows, ref BodyState first, ref BodyState second, in JointFrame frame,
        int row)
    {
        var error = Vector3F.Cross(frame.AxisOfFirst, frame.AxisOfSecond);
        var side = frame.ReferenceOfFirst;
        var otherSide = Vector3F.Cross(frame.AxisOfFirst, side);
        rows.About(ref first, ref second, side, Vector3F.Dot(error, side), row);
        rows.About(ref first, ref second, otherSide, Vector3F.Dot(error, otherSide), row + 1);
    }

    // A slider's points kept on its axis: two rows square to it, from the first body's point under the second's.
    private static void KeepOnAxis(ref JointRows rows, ref BodyState first, ref BodyState second, in JointFrame frame,
        int row)
    {
        var side = frame.ReferenceOfFirst;
        var otherSide = Vector3F.Cross(frame.AxisOfFirst, side);
        var reach = frame.ArmOfFirst + frame.Apart;
        rows.Along(ref first, ref second, reach, frame.ArmOfSecond, side, Vector3F.Dot(frame.Apart, side), row);
        rows.Along(ref first, ref second, reach, frame.ArmOfSecond, otherSide, Vector3F.Dot(frame.Apart, otherSide),
            row + 1);
    }

    // A hinge's motor and limits, about its axis: the motor in the motor row, the limits in the two after it.
    private static void DriveAngle(ref JointRows rows, ref BodyState first, ref BodyState second, Joint joint,
        in JointFrame frame)
    {
        var axis = frame.AxisOfFirst;
        if (joint.MaxMotorForce > 0)
            rows.DriveAbout(ref first, ref second, axis, joint.MotorSpeed, joint.MaxMotorForce, Joint.MotorRow);

        if (joint.HasLimits)
        {
            var angle = frame.MeasureAngle(joint, second);
            rows.AboutAtLeast(ref first, ref second, axis, angle - joint.LowerLimit, Joint.MotorRow + 1);
            rows.AboutAtLeast(ref first, ref second, -axis, joint.UpperLimit - angle, Joint.MotorRow + 2);
        }
    }

    // A slider's motor and limits, along its axis.
    private static void DriveOffset(ref JointRows rows, ref BodyState first, ref BodyState second, Joint joint,
        in JointFrame frame)
    {
        var axis = frame.AxisOfFirst;
        var reach = frame.ArmOfFirst + frame.Apart;
        if (joint.MaxMotorForce > 0)
            rows.DriveAlong(ref first, ref second, reach, frame.ArmOfSecond, axis, joint.MotorSpeed, joint.MaxMotorForce,
                Joint.MotorRow);

        if (joint.HasLimits)
        {
            var offset = Vector3F.Dot(frame.Apart, axis);
            rows.AlongAtLeast(ref first, ref second, reach, frame.ArmOfSecond, axis, offset - joint.LowerLimit,
                Joint.MotorRow + 1);
            rows.AlongAtLeast(ref first, ref second, reach, frame.ArmOfSecond, -axis, joint.UpperLimit - offset,
                Joint.MotorRow + 2);
        }
    }

    // A rod at its length, a spring pulling toward it, limits holding the length between them.
    private static void KeepDistance(ref JointRows rows, ref BodyState first, ref BodyState second, Joint joint,
        in JointFrame frame)
    {
        var length = frame.Apart.Length();
        var direction = length > 1e-6f ? frame.Apart / length : Vector3F.UnitY;
        if (joint.SpringHertz > 0)
        {
            rows.Spring(ref first, ref second, frame.ArmOfFirst, frame.ArmOfSecond, direction, length - joint.RestLength,
                joint.SpringHertz, joint.SpringDampingRatio, 0);
        }
        else if (!joint.HasLimits)
        {
            rows.Along(ref first, ref second, frame.ArmOfFirst, frame.ArmOfSecond, direction, length - joint.RestLength, 0);
        }

        if (joint.HasLimits)
        {
            rows.AlongAtLeast(ref first, ref second, frame.ArmOfFirst, frame.ArmOfSecond, direction,
                length - joint.LowerLimit, 1);
            rows.AlongAtLeast(ref first, ref second, frame.ArmOfFirst, frame.ArmOfSecond, -direction,
                joint.UpperLimit - length, 2);
        }
    }
}
