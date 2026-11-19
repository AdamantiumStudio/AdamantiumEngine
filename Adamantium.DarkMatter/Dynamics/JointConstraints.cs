using System;

namespace Adamantium.DarkMatter.Dynamics;

/// <summary>One pass's solving of a joint's constraints: each a direction two points must not part along, or an axis
/// two bodies must not turn apart about, with the impulse it holds kept in the joint from substep to substep.</summary>
internal readonly ref struct JointConstraints
{
    private readonly Joint _joint;
    private readonly JointPass _pass;
    private readonly float _inverseSubstep;
    private readonly float _substep;
    private readonly Softness _softness;

    public JointConstraints(Joint joint, JointPass pass, float inverseSubstep, float substep, Softness softness)
    {
        _joint = joint;
        _pass = pass;
        _inverseSubstep = inverseSubstep;
        _substep = substep;
        _softness = softness;
    }

    /// <summary>Keeps the points from parting along <paramref name="direction"/>; <paramref name="error"/> is how far
    /// they part along it now.</summary>
    public void Along(ref BodyState first, ref BodyState second, Vector3F armOfFirst, Vector3F armOfSecond,
        Vector3F direction, float error, int constraint)
    {
        if (WarmStartsAlong(ref first, ref second, armOfFirst, armOfSecond, direction, constraint))
            return;

        var speed = Vector3F.Dot(RelativeVelocity(first, second, armOfFirst, armOfSecond), direction);
        var mass = LinearMass(first, second, armOfFirst, armOfSecond, direction);
        var impulse = Solve(speed, error, false, mass, constraint, float.NegativeInfinity);
        Push(ref first, ref second, armOfFirst, armOfSecond, direction * impulse);
    }

    /// <summary>Keeps <paramref name="error"/> - how far the points stand past a limit along
    /// <paramref name="direction"/> - from going below zero, pushing one way only.</summary>
    public void AlongAtLeast(ref BodyState first, ref BodyState second, Vector3F armOfFirst, Vector3F armOfSecond,
        Vector3F direction, float error, int constraint)
    {
        if (WarmStartsAlong(ref first, ref second, armOfFirst, armOfSecond, direction, constraint))
            return;

        var speed = Vector3F.Dot(RelativeVelocity(first, second, armOfFirst, armOfSecond), direction);
        var mass = LinearMass(first, second, armOfFirst, armOfSecond, direction);
        var impulse = Solve(speed, error, true, mass, constraint, 0);
        Push(ref first, ref second, armOfFirst, armOfSecond, direction * impulse);
    }

    /// <summary>Keeps the bodies from turning apart about <paramref name="axis"/>; <paramref name="error"/> is how far
    /// they have.</summary>
    public void About(ref BodyState first, ref BodyState second, Vector3F axis, float error, int constraint)
    {
        if (WarmStartsAbout(ref first, ref second, axis, constraint))
            return;

        var speed = Vector3F.Dot(second.AngularVelocity - first.AngularVelocity, axis);
        var mass = AngularMass(first, second, axis);
        var impulse = Solve(speed, error, false, mass, constraint, float.NegativeInfinity);
        Turn(ref first, ref second, axis * impulse);
    }

    /// <summary>Keeps <paramref name="error"/> - how far the bodies stand past a limit about <paramref name="axis"/> -
    /// from going below zero, turning one way only.</summary>
    public void AboutAtLeast(ref BodyState first, ref BodyState second, Vector3F axis, float error, int constraint)
    {
        if (WarmStartsAbout(ref first, ref second, axis, constraint))
            return;

        var speed = Vector3F.Dot(second.AngularVelocity - first.AngularVelocity, axis);
        var mass = AngularMass(first, second, axis);
        var impulse = Solve(speed, error, true, mass, constraint, 0);
        Turn(ref first, ref second, axis * impulse);
    }

    /// <summary>Drives the turn about <paramref name="axis"/> toward <paramref name="speed"/>, with no more than
    /// <paramref name="maxTorque"/>.</summary>
    public void DriveAbout(ref BodyState first, ref BodyState second, Vector3F axis, float speed, float maxTorque,
        int constraint)
    {
        if (WarmStartsAbout(ref first, ref second, axis, constraint))
            return;

        var turning = Vector3F.Dot(second.AngularVelocity - first.AngularVelocity, axis);
        var mass = AngularMass(first, second, axis);
        var impulse = Drive(turning - speed, mass, maxTorque, constraint);
        Turn(ref first, ref second, axis * impulse);
    }

    /// <summary>Drives the slide along <paramref name="direction"/> toward <paramref name="speed"/>, with no more than
    /// <paramref name="maxForce"/>.</summary>
    public void DriveAlong(ref BodyState first, ref BodyState second, Vector3F armOfFirst, Vector3F armOfSecond,
        Vector3F direction, float speed, float maxForce, int constraint)
    {
        if (WarmStartsAlong(ref first, ref second, armOfFirst, armOfSecond, direction, constraint))
            return;

        var sliding = Vector3F.Dot(RelativeVelocity(first, second, armOfFirst, armOfSecond), direction);
        var mass = LinearMass(first, second, armOfFirst, armOfSecond, direction);
        var impulse = Drive(sliding - speed, mass, maxForce, constraint);
        Push(ref first, ref second, armOfFirst, armOfSecond, direction * impulse);
    }

    /// <summary>Pulls <paramref name="error"/> along <paramref name="direction"/> toward zero by a spring of
    /// <paramref name="hertz"/> and <paramref name="dampingRatio"/>; relaxing leaves it alone, as its swing is its own.
    /// </summary>
    public void Spring(ref BodyState first, ref BodyState second, Vector3F armOfFirst, Vector3F armOfSecond,
        Vector3F direction, float error, float hertz, float dampingRatio, int constraint)
    {
        if (WarmStartsAlong(ref first, ref second, armOfFirst, armOfSecond, direction, constraint)
            || _pass == JointPass.Relax)
            return;

        ref var held = ref _joint.Impulses[constraint];

        var softness = Softness.Create(hertz, dampingRatio, _substep);
        var speed = Vector3F.Dot(RelativeVelocity(first, second, armOfFirst, armOfSecond), direction);
        var mass = LinearMass(first, second, armOfFirst, armOfSecond, direction);
        var impulse = -mass * softness.MassScale * (speed + softness.BiasRate * error) - softness.ImpulseScale * held;
        held += impulse;
        Push(ref first, ref second, armOfFirst, armOfSecond, direction * impulse);
    }

    // Warm starting, the constraint gives the impulse it held and nothing more is worked out.
    private bool WarmStartsAlong(ref BodyState first, ref BodyState second, Vector3F armOfFirst, Vector3F armOfSecond,
        Vector3F direction, int constraint)
    {
        if (_pass != JointPass.WarmStart)
            return false;

        Push(ref first, ref second, armOfFirst, armOfSecond, direction * _joint.Impulses[constraint]);
        return true;
    }

    private bool WarmStartsAbout(ref BodyState first, ref BodyState second, Vector3F axis, int constraint)
    {
        if (_pass != JointPass.WarmStart)
            return false;

        Turn(ref first, ref second, axis * _joint.Impulses[constraint]);
        return true;
    }

    // A constraint's impulse this pass, the held one kept at least lowest; a one-sided one not yet past its limit only
    // keeps the bodies from crossing it this substep, as a contact does.
    private float Solve(float speed, float error, bool isOneSided, float mass, int constraint, float lowest)
    {
        ref var held = ref _joint.Impulses[constraint];
        float bias;
        var massScale = 1f;
        var impulseScale = 0f;
        if (isOneSided && error > 0)
        {
            bias = error * _inverseSubstep;
        }
        else if (_pass == JointPass.Push)
        {
            bias = _softness.BiasRate * error;
            massScale = _softness.MassScale;
            impulseScale = _softness.ImpulseScale;
        }
        else
        {
            bias = 0;
        }

        var impulse = -mass * massScale * (speed + bias) - impulseScale * held;
        var total = MathF.Max(held + impulse, lowest);
        impulse = total - held;
        held = total;
        return impulse;
    }

    // A motor's impulse this pass, the held one kept within what its force gives in a substep.
    private float Drive(float speedPast, float mass, float maxForce, int constraint)
    {
        ref var held = ref _joint.Impulses[constraint];
        var most = maxForce * _substep;
        var total = Math.Clamp(held - mass * speedPast, -most, most);
        var impulse = total - held;
        held = total;
        return impulse;
    }

    private static Vector3F RelativeVelocity(in BodyState first, in BodyState second, Vector3F armOfFirst,
        Vector3F armOfSecond) =>
        second.LinearVelocity + Vector3F.Cross(second.AngularVelocity, armOfSecond)
        - first.LinearVelocity - Vector3F.Cross(first.AngularVelocity, armOfFirst);

    private static float LinearMass(in BodyState first, in BodyState second, Vector3F armOfFirst, Vector3F armOfSecond,
        Vector3F direction)
    {
        var turnFirst = Vector3F.Cross(armOfFirst, direction);
        var turnSecond = Vector3F.Cross(armOfSecond, direction);
        var inverse = first.InverseMass + second.InverseMass
                      + Vector3F.Dot(turnFirst, InertiaMath.Apply(first.WorldInverseInertia, turnFirst))
                      + Vector3F.Dot(turnSecond, InertiaMath.Apply(second.WorldInverseInertia, turnSecond));
        return inverse > 0 ? 1f / inverse : 0;
    }

    private static float AngularMass(in BodyState first, in BodyState second, Vector3F axis)
    {
        var inverse = Vector3F.Dot(axis, InertiaMath.Apply(first.WorldInverseInertia, axis))
                      + Vector3F.Dot(axis, InertiaMath.Apply(second.WorldInverseInertia, axis));
        return inverse > 0 ? 1f / inverse : 0;
    }

    private static void Push(ref BodyState first, ref BodyState second, Vector3F armOfFirst, Vector3F armOfSecond,
        Vector3F impulse)
    {
        if (first.IsDynamic)
        {
            first.LinearVelocity -= impulse * first.InverseMass;
            first.AngularVelocity -= InertiaMath.Apply(first.WorldInverseInertia, Vector3F.Cross(armOfFirst, impulse));
        }

        if (second.IsDynamic)
        {
            second.LinearVelocity += impulse * second.InverseMass;
            second.AngularVelocity += InertiaMath.Apply(second.WorldInverseInertia, Vector3F.Cross(armOfSecond, impulse));
        }
    }

    private static void Turn(ref BodyState first, ref BodyState second, Vector3F angularImpulse)
    {
        if (first.IsDynamic)
            first.AngularVelocity -= InertiaMath.Apply(first.WorldInverseInertia, angularImpulse);

        if (second.IsDynamic)
            second.AngularVelocity += InertiaMath.Apply(second.WorldInverseInertia, angularImpulse);
    }
}
