using System;
using System.Collections.Generic;
using Adamantium.DarkMatter.CollisionDetection;
using Adamantium.DarkMatter.Dynamics;

namespace Adamantium.DarkMatter;

// Joints: bodies held together, solved with the contacts in each substep.
public sealed partial class PhysicsWorld
{
    private readonly List<Joint> _jointSlots = [];
    private readonly List<Joint> _joints = [];
    private readonly Stack<int> _freeJointSlots = new();
    private readonly JointSolver _jointSolver = new();
    // The pairs of bodies a joint keeps from colliding, by their key, and how many joints do.
    private readonly Dictionary<ulong, int> _unpaired = new();

    /// <inheritdoc/>
    public int JointCount => _joints.Count;

    /// <inheritdoc/>
    public JointHandle Add(JointDescription description)
    {
        ArgumentNullException.ThrowIfNull(description);
        var first = Resolve(description.First);
        var second = description.Second.IsValid ? Resolve(description.Second) : NoBody;
        if (first == second)
            throw new ArgumentException("A joint holds two bodies, or one to the world.", nameof(description));

        ValidateJoint(description);
        var slot = _freeJointSlots.Count > 0 ? _freeJointSlots.Pop() : AddJointSlot();
        var joint = _jointSlots[slot];
        joint.IsUsed = true;
        joint.Generation++;
        joint.Kind = description.Kind;
        // The solver measures its second body against its first: the description's first body moves against its
        // second, or the world, so they are kept the other way round.
        joint.First = second;
        joint.Second = first;
        joint.CollideConnected = description.CollideConnected;
        joint.HasLimits = description.HasLimits;
        joint.LowerLimit = description.LowerLimit;
        joint.UpperLimit = description.UpperLimit;
        joint.MotorSpeed = description.MotorSpeed;
        joint.MaxMotorForce = description.MaxMotorForce;
        joint.SpringHertz = description.SpringHertz;
        joint.SpringDampingRatio = description.SpringDampingRatio;
        Array.Clear(joint.Impulses);
        PlaceJoint(joint, description);
        joint.ListIndex = _joints.Count;
        _joints.Add(joint);

        if (!joint.CollideConnected && second != NoBody)
        {
            var key = Contact.MakeKey(first, second);
            _unpaired[key] = _unpaired.GetValueOrDefault(key) + 1;
            LetGoOfContactsBetween(first, second);
        }

        WakeJointBodies(joint);
        return new JointHandle(this, slot, joint.Generation);
    }

    /// <inheritdoc/>
    public void Remove(JointHandle joint) => RemoveJoint(_jointSlots[ResolveJoint(joint)]);

    /// <inheritdoc/>
    public bool Contains(JointHandle joint) =>
        ReferenceEquals(joint.World, this) && joint.Index >= 0 && joint.Index < _jointSlots.Count
        && _jointSlots[joint.Index].IsUsed && _jointSlots[joint.Index].Generation == joint.Generation;

    /// <inheritdoc/>
    public void SetMotor(JointHandle joint, float speed, float maxForce)
    {
        var state = _jointSlots[ResolveJoint(joint)];
        if (!float.IsFinite(speed))
            throw new ArgumentOutOfRangeException(nameof(speed), speed, "A motor's speed must be finite.");

        RequireFiniteNotNegative(maxForce, nameof(maxForce));
        if (state.Kind is not (JointKind.Hinge or JointKind.Slider))
            throw new InvalidOperationException("Only a hinge or a slider has a motor.");

        if (state.MotorSpeed == speed && state.MaxMotorForce == maxForce)
            return;

        state.MotorSpeed = speed;
        state.MaxMotorForce = maxForce;
        // A motor set anew starts from nothing: the impulse it held at another force would kick.
        state.Impulses[Joint.MotorConstraint] = 0;
        WakeJointBodies(state);
    }

    // A body moving, or put somewhere by hand, wakes a sleeping one a joint holds to it: a platform driven off takes
    // what hangs from it along, a lamp's hook moved takes the lamp.
    private void WakeJointPartners()
    {
        for (var index = 0; index < _joints.Count; index++)
        {
            var joint = _joints[index];
            if (joint.First == NoBody || joint.Second == NoBody)
                continue;

            ref var first = ref _bodies[joint.First];
            ref var second = ref _bodies[joint.Second];
            if ((first.IsMoving || first.IsPlaced) && second.IsDynamic && !second.IsAwake)
                WakeIsland(joint.Second);
            else if ((second.IsMoving || second.IsPlaced) && first.IsDynamic && !first.IsAwake)
                WakeIsland(joint.First);
        }
    }

    /// <inheritdoc/>
    public float GetJointPosition(JointHandle joint) => _jointSolver.Measure(_bodies, _jointSlots[ResolveJoint(joint)]);

    // The point, the axis and a direction square to it, in each body's own axes as they stand now; a slider's offset
    // and a distance joint's length as they are now count as rest. The description's first body is the joint's second.
    private void PlaceJoint(Joint joint, JointDescription description)
    {
        var axis = Vector3F.Normalize(description.Axis);
        var reference = AxisMath.GetPerpendicular(axis);
        var pointOnFirst = joint.Kind == JointKind.Distance ? description.SecondPoint : description.Point;
        // The world holds the joint from where its point stands, so the point is kept in double, not as an offset in
        // float from the world's origin.
        joint.WorldAnchor = joint.First == NoBody ? pointOnFirst : Vector3<double>.Zero;
        var firstPose = joint.First != NoBody ? _bodies[joint.First].OriginPose : new Pose(joint.WorldAnchor);
        var secondPose = _bodies[joint.Second].OriginPose;
        joint.PointOnFirst = firstPose.InverseTransformPoint(pointOnFirst);
        joint.PointOnSecond = secondPose.InverseTransformPoint(description.Point);
        joint.AxisOnFirst = firstPose.InverseTransformDirection(axis);
        joint.AxisOnSecond = secondPose.InverseTransformDirection(axis);
        joint.ReferenceOnFirst = firstPose.InverseTransformDirection(reference);
        joint.ReferenceOnSecond = secondPose.InverseTransformDirection(reference);
        joint.RestTurn = QuaternionF.Conjugate(firstPose.Rotation) * secondPose.Rotation;
        joint.RestLength = joint.Kind == JointKind.Distance
            ? (float)(description.SecondPoint - description.Point).Length()
            : 0;
    }

    private void WakeJointBodies(Joint joint)
    {
        if (joint.First != NoBody)
            WakeIsland(joint.First);

        if (joint.Second != NoBody)
            WakeIsland(joint.Second);
    }

    private void RemoveJoint(Joint joint)
    {
        if (!joint.CollideConnected && joint.First != NoBody)
        {
            var key = Contact.MakeKey(joint.First, joint.Second);
            if (_unpaired.TryGetValue(key, out var count) && count > 1)
                _unpaired[key] = count - 1;
            else
                _unpaired.Remove(key);

            // Free to collide again, they find their pair as they move.
            Mark(joint.First, BodyMarks.Moved);
            Mark(joint.Second, BodyMarks.Moved);
        }

        WakeJointBodies(joint);
        var last = _joints[^1];
        _joints[joint.ListIndex] = last;
        last.ListIndex = joint.ListIndex;
        _joints.RemoveAt(_joints.Count - 1);
        joint.IsUsed = false;
        // A slot used as often as a handle can count is not used again, so no old handle names a new joint.
        if (joint.Generation < int.MaxValue)
            _freeJointSlots.Push(joint.Slot);
    }

    // A body taken out takes its joints with it.
    private void RemoveJointsOf(int body)
    {
        if (_joints.Count == 0)
            return;

        for (var index = _joints.Count - 1; index >= 0; index--)
        {
            if (index < _joints.Count && (_joints[index].First == body || _joints[index].Second == body))
                RemoveJoint(_joints[index]);
        }
    }

    // Whether a joint keeps the two bodies from colliding.
    private bool AreHeldApart(int first, int second) =>
        _unpaired.Count > 0 && _unpaired.ContainsKey(Contact.MakeKey(first, second));

    private void LetGoOfContactsBetween(int first, int second)
    {
        var contact = _bodies[first].Contacts;
        while (contact != null)
        {
            var next = contact.GetNext(first);
            if (contact.GetOther(first) == second && contact.Holder == null)
                DestroyContact(contact);

            contact = next;
        }
    }

    private int AddJointSlot()
    {
        _jointSlots.Add(new Joint { Slot = _jointSlots.Count });
        return _jointSlots.Count - 1;
    }

    private int ResolveJoint(JointHandle joint)
    {
        if (!Contains(joint))
            throw new ArgumentException("The joint is not in this world.", nameof(joint));

        return joint.Index;
    }

    private static void ValidateJoint(JointDescription description)
    {
        if (!Enum.IsDefined(description.Kind))
            throw new ArgumentOutOfRangeException(nameof(description), description.Kind, "Unknown kind of joint.");

        RequireFinite(description.Point, nameof(description));
        RequireFinite(description.SecondPoint, nameof(description));
        RequireFinite(description.Axis, nameof(description));
        if (!(description.Axis.LengthSquared() > 1e-12f))
            throw new ArgumentException("A joint's axis needs a length.", nameof(description));

        if (!float.IsFinite(description.LowerLimit) || !float.IsFinite(description.UpperLimit)
                                                    || description.LowerLimit > description.UpperLimit)
            throw new ArgumentException("A joint's limits must be finite, the lower not above the upper.",
                nameof(description));

        // A hinge's angle is read between minus and plus a half turn: a limit past that would never be met.
        if (description.Kind == JointKind.Hinge && description.HasLimits
                                                && (description.LowerLimit < -MathF.PI || description.UpperLimit > MathF.PI))
            throw new ArgumentException("A hinge's limits lie within half a turn either way.", nameof(description));

        if (!float.IsFinite(description.MotorSpeed))
            throw new ArgumentException("A motor's speed must be finite.", nameof(description));

        RequireFiniteNotNegative(description.MaxMotorForce, nameof(description));
        RequireFiniteNotNegative(description.SpringHertz, nameof(description));
        RequireFiniteNotNegative(description.SpringDampingRatio, nameof(description));

        // A rigid rod of no length has no way to hold along: a ball joint is what holds a point.
        if (description.Kind == JointKind.Distance && !description.HasLimits && description.SpringHertz == 0
            && !((description.SecondPoint - description.Point).LengthSquared() > 1e-12))
            throw new ArgumentException("A rigid distance joint needs two points apart; a ball joint holds one point.",
                nameof(description));
    }
}
