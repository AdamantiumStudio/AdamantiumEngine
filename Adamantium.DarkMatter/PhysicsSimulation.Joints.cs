using System;
using System.Collections.Generic;
using Adamantium.DarkMatter.Simulation;

namespace Adamantium.DarkMatter;

// Joints: two bodies held together, or one to the world, by id.
public sealed partial class PhysicsSimulation
{
    private readonly List<SimulatedJoint> _jointSlots = [];
    private readonly Stack<int> _freeJointSlots = new();
    private readonly List<JointId> _liveJoints = [];
    private readonly List<JointId> _refusedJoints = [];

    /// <summary>Adds a joint as <paramref name="settings"/> say and gives its id; a step takes it, where its bodies
    /// stand then.</summary>
    public JointId AddJoint(in JointSettings settings)
    {
        ThrowIfDisposed();
        if (!Enum.IsDefined(settings.Kind))
            throw new ArgumentOutOfRangeException(nameof(settings), settings.Kind, "Unknown kind of joint.");

        if (!Enum.IsDefined(settings.Space))
            throw new ArgumentOutOfRangeException(nameof(settings), settings.Space, "Unknown space of a joint.");

        Find(settings.First);
        if (!settings.Second.IsNone)
            Find(settings.Second);

        if (settings.First == settings.Second)
            throw new ArgumentException("A joint holds two bodies, or one to the world.", nameof(settings));

        RequireFinite(settings.Point, nameof(settings));
        RequireFinite(settings.SecondPoint, nameof(settings));
        RequireFinite(settings.FirstAnchor, nameof(settings));
        RequireFinite(settings.SecondAnchor, nameof(settings));
        RequireFinite(settings.Axis, nameof(settings));
        var slot = _freeJointSlots.Count > 0 ? _freeJointSlots.Pop() : NewJointSlot();
        var joint = _jointSlots[slot];
        joint.Generation++;
        joint.Status = PhysicsStatus.Adding;
        joint.Refusal = null;
        joint.FirstBody = settings.First.Index;
        joint.SecondBody = settings.Second.IsNone ? NoSlot : settings.Second.Index;
        joint.Position = 0;
        var id = new JointId(slot, joint.Generation);
        joint.ListIndex = _liveJoints.Count;
        _liveJoints.Add(id);
        _commands.Add(new SimulationCommand
        {
            Kind = SimulationCommandKind.AddJoint, Slot = slot, Generation = joint.Generation, Joint = settings
        });
        return id;
    }

    /// <summary>Takes <paramref name="joint"/> out, refused or not; its bodies wake, and collide again if it kept them
    /// from it.</summary>
    public void RemoveJoint(JointId joint)
    {
        var record = FindAnyJoint(joint);
        if (record.Status == PhysicsStatus.Removed)
            throw new InvalidOperationException($"{joint} is removed.");

        TakeOut(joint, record);
    }

    /// <summary>Whether <paramref name="joint"/> is being added or is in the simulation.</summary>
    public bool Contains(JointId joint) =>
        TryFindJoint(joint, out var record) && record.Status is PhysicsStatus.Adding or PhysicsStatus.Active;

    /// <summary>Where <paramref name="joint"/> is in its life; removed for an id the simulation never gave.</summary>
    public PhysicsStatus StatusOf(JointId joint) =>
        TryFindJoint(joint, out var record) ? record.Status : PhysicsStatus.Removed;

    /// <summary>Why the simulation would not take <paramref name="joint"/>, or a change asked of it, as the last read
    /// told; null when it did them all.</summary>
    public string RefusalOf(JointId joint) => FindAnyJoint(joint).Refusal;

    /// <summary>Where <paramref name="joint"/> stood after the last step read: a hinge's angle in radians or a slider's
    /// offset in meters from where it was added, a distance joint's length; zero for a weld or a ball.</summary>
    public float PositionOf(JointId joint) => FindAnyJoint(joint).Position;

    /// <summary>Holds a hinge, a slider or a distance joint within <paramref name="limits"/>; none frees it.</summary>
    public void SetLimits(JointId joint, JointLimits? limits)
    {
        if (limits is { } held && (!float.IsFinite(held.Lower) || !float.IsFinite(held.Upper) || held.Lower > held.Upper))
            throw new ArgumentException("A joint's limits must be finite, the lower not above the upper.",
                nameof(limits));

        FindJoint(joint);
        _commands.Add(new SimulationCommand
        {
            Kind = SimulationCommandKind.SetLimits, Slot = joint.Index, Limits = limits
        });
    }

    /// <summary>Drives a hinge or a slider with <paramref name="motor"/>; none stops it.</summary>
    public void SetMotor(JointId joint, JointMotor? motor)
    {
        if (motor is { } driven && (!float.IsFinite(driven.Speed) || !float.IsFinite(driven.MaxForce)
                                                                || driven.MaxForce < 0))
            throw new ArgumentException("A motor's speed and force must be finite, the force not below zero.",
                nameof(motor));

        FindJoint(joint);
        _commands.Add(new SimulationCommand { Kind = SimulationCommandKind.SetMotor, Slot = joint.Index, Motor = motor });
    }

    private void TakeOut(JointId joint, SimulatedJoint record)
    {
        record.Status = PhysicsStatus.Removed;
        var last = _liveJoints[^1];
        _liveJoints[record.ListIndex] = last;
        _jointSlots[last.Index].ListIndex = record.ListIndex;
        _liveJoints.RemoveAt(_liveJoints.Count - 1);
        _takenOutSlots.Add(new TakenOutSlot(joint.Index, true, _handedBatches + 1));
        _commands.Add(new SimulationCommand { Kind = SimulationCommandKind.RemoveJoint, Slot = joint.Index });
    }

    private void ReadJointNews(in SimulationNews news)
    {
        var joint = _jointSlots[news.Slot];
        if (joint.Generation != news.Generation)
            return;

        switch (news.Kind)
        {
            case NewsKind.Taken when joint.Status == PhysicsStatus.Adding:
                joint.Status = PhysicsStatus.Active;
                break;
            case NewsKind.Refused when joint.Status == PhysicsStatus.Adding:
                joint.Status = PhysicsStatus.Refused;
                joint.Refusal = news.Refusal;
                break;
            case NewsKind.ChangeRefused when joint.Refusal == null:
                joint.Refusal = news.Refusal;
                _refusedJoints.Add(new JointId(news.Slot, news.Generation));
                break;
        }
    }

    private int NewJointSlot()
    {
        _jointSlots.Add(new SimulatedJoint());
        return _jointSlots.Count - 1;
    }

    private bool TryFindJoint(JointId joint, out SimulatedJoint record)
    {
        ThrowIfDisposed();
        record = null;
        if (joint.IsNone || joint.Index < 0 || joint.Index >= _jointSlots.Count)
            return false;

        record = _jointSlots[joint.Index];
        return record.Generation == joint.Generation;
    }

    private SimulatedJoint FindAnyJoint(JointId joint) =>
        TryFindJoint(joint, out var record)
            ? record
            : throw new InvalidOperationException($"{joint} is not of this simulation.");

    private SimulatedJoint FindJoint(JointId joint)
    {
        var record = FindAnyJoint(joint);
        if (record.Status is PhysicsStatus.Removed or PhysicsStatus.Refused)
            throw new InvalidOperationException($"{joint} is {record.Status.ToString().ToLowerInvariant()}.");

        return record;
    }
}
