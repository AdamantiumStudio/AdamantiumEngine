using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Adamantium.DarkMatter.CollisionDetection;

namespace Adamantium.DarkMatter.Dynamics;

/// <summary>Solves contacts as soft constraints over substeps: a damped spring pushes overlaps apart, friction stays in
/// the cone of the push, a pass without the spring takes out the speed it added; bounces come at the step's end.</summary>
internal sealed class ContactSolver
{
    // Faster across the normal than this, in meters per second, a touch slides by its dynamic friction; slower, it is
    // held by its static.
    private const float SlipSpeed = 0.02f;

    private readonly List<Contact> _active = [];

    /// <summary>The contacts solved this step, until the next
    /// <see cref="Prepare(BodyState[], List{Contact}, List{Contact})"/>: a span, read only, no enumerator.</summary>
    public ReadOnlySpan<Contact> Active => CollectionsMarshal.AsSpan(_active);

    /// <summary>Takes the touching contacts with a moving body, of convex parts and with surfaces' triangles, and works
    /// out, from where the bodies stand at the start of the step, what each point needs.</summary>
    public void Prepare(BodyState[] bodies, List<Contact> contacts, List<Contact> pieces)
    {
        _active.Clear();
        Prepare(bodies, contacts);
        Prepare(bodies, pieces);
    }

    private void Prepare(BodyState[] bodies, List<Contact> contacts)
    {
        foreach (var contact in contacts)
        {
            Debug.Assert(!contact.IsTrigger, "A trigger's pair is kept apart from those solved.");
            if (!contact.IsTouching)
            {
                // Parted, it starts its next touch held and from no turn, as its points start from no push.
                contact.RollingImpulse = Vector3F.Zero;
                contact.IsSliding = false;
                continue;
            }

            ref var first = ref bodies[contact.First];
            ref var second = ref bodies[contact.Second];
            if (!(first.IsDynamic && first.IsAwake) && !(second.IsDynamic && second.IsAwake))
                continue;

            _active.Add(contact);
            var normal = contact.Normal;
            contact.Tangent = AxisMath.GetPerpendicular(normal);
            contact.Bitangent = Vector3F.Cross(normal, contact.Tangent);
            for (var index = 0; index < contact.PointCount; index++)
            {
                ref var point = ref contact.Points[index];
                var anchorFirst = point.Position.OffsetFrom(first.Position);
                var anchorSecond = point.Position.OffsetFrom(second.Position);
                point.AnchorFirst = anchorFirst;
                point.AnchorSecond = anchorSecond;
                point.AdjustedSeparation = point.Separation - Vector3F.Dot(anchorSecond - anchorFirst, normal);
                point.NormalMass = ComputeMass(first, second, anchorFirst, anchorSecond, normal);
                point.TangentMass = ComputeMass(first, second, anchorFirst, anchorSecond, contact.Tangent);
                point.BitangentMass = ComputeMass(first, second, anchorFirst, anchorSecond, contact.Bitangent);
                point.MaxNormalImpulse = 0;
                point.FrictionImpulse -= normal * Vector3F.Dot(point.FrictionImpulse, normal);
                point.RelativeVelocity = Vector3F.Dot(GetRelativeVelocity(first, second, anchorFirst, anchorSecond), normal);
            }

            PrepareRolling(contact, first, second);
        }
    }

    // A pair that rolls - its world measured how - is resisted about its rolling axis, or about both tangents for a
    // ball; it starts from the turn it resisted last step about those, as the points start from their pushes. One that
    // does not roll starts from none.
    private static void PrepareRolling(Contact contact, in BodyState first, in BodyState second)
    {
        if (contact.RollingLever == 0)
        {
            contact.RollingImpulse = Vector3F.Zero;
            return;
        }

        var hasAxis = contact.RollingAxis != Vector3F.Zero;
        contact.RollingFirstAxis = hasAxis ? contact.RollingAxis : contact.Tangent;
        contact.RollingSecondAxis = hasAxis ? Vector3F.Zero : contact.Bitangent;
        contact.RollingFirstMass = ComputeTurnMass(first, second, contact.RollingFirstAxis);
        contact.RollingSecondMass = hasAxis ? 0 : ComputeTurnMass(first, second, contact.RollingSecondAxis);
        var kept = contact.RollingImpulse;
        contact.RollingImpulse = contact.RollingFirstAxis * Vector3F.Dot(kept, contact.RollingFirstAxis)
                                 + contact.RollingSecondAxis * Vector3F.Dot(kept, contact.RollingSecondAxis);
    }

    /// <summary>Gives every point the impulses it ended the last substep with.</summary>
    public void WarmStart(BodyState[] bodies)
    {
        foreach (var contact in _active)
        {
            ref var first = ref bodies[contact.First];
            ref var second = ref bodies[contact.Second];
            for (var index = 0; index < contact.PointCount; index++)
            {
                ref var point = ref contact.Points[index];
                var impulse = contact.Normal * point.NormalImpulse + point.FrictionImpulse;
                ApplyImpulse(ref first, ref second, point.AnchorFirst, point.AnchorSecond, impulse);
            }

            if (contact.RollingLever > 0)
                ApplyTurn(ref first, ref second, contact.RollingImpulse);
        }
    }

    /// <summary>One pass over the contacts; with <paramref name="useBias"/> the spring pushes overlaps apart, without
    /// it only the speed along the normal is taken out.</summary>
    public void Solve(BodyState[] bodies, float inverseSubstep, bool useBias, Softness contactSoftness,
        Softness staticSoftness, float maxPushOutSpeed)
    {
        foreach (var contact in _active)
        {
            ref var first = ref bodies[contact.First];
            ref var second = ref bodies[contact.Second];
            var softness = first.InverseMass == 0 || second.InverseMass == 0 ? staticSoftness : contactSoftness;
            var firstMoved = first.MovedThisStep;
            var secondMoved = second.MovedThisStep;
            var firstTurn = first.Rotation * QuaternionF.Conjugate(first.StartRotation);
            var secondTurn = second.Rotation * QuaternionF.Conjugate(second.StartRotation);
            var normal = contact.Normal;

            for (var index = 0; index < contact.PointCount; index++)
            {
                ref var point = ref contact.Points[index];
                var turnedFirst = Vector3F.Transform(point.AnchorFirst, firstTurn);
                var turnedSecond = Vector3F.Transform(point.AnchorSecond, secondTurn);
                var shift = secondMoved - firstMoved + turnedSecond - turnedFirst;
                var separation = Vector3F.Dot(shift, normal) + point.AdjustedSeparation;

                float bias;
                float massScale;
                float impulseScale;
                if (separation > 0)
                {
                    bias = separation * inverseSubstep;
                    massScale = 1;
                    impulseScale = 0;
                }
                else if (useBias)
                {
                    bias = MathF.Max(softness.BiasRate * separation, -maxPushOutSpeed);
                    massScale = softness.MassScale;
                    impulseScale = softness.ImpulseScale;
                }
                else
                {
                    bias = 0;
                    massScale = 1;
                    impulseScale = 0;
                }

                var normalSpeed = Vector3F.Dot(GetRelativeVelocity(first, second, point.AnchorFirst, point.AnchorSecond), normal);
                var impulse = -point.NormalMass * massScale * (normalSpeed + bias) - impulseScale * point.NormalImpulse;
                var total = MathF.Max(point.NormalImpulse + impulse, 0);
                impulse = total - point.NormalImpulse;
                point.NormalImpulse = total;
                point.MaxNormalImpulse = MathF.Max(point.MaxNormalImpulse, impulse);
                ApplyImpulse(ref first, ref second, point.AnchorFirst, point.AnchorSecond, normal * impulse);
            }

            // A touch held - not slipping faster than SlipSpeed - is held by its static friction whole, against all its
            // points are pressed with, each point taking its share by the friction it carries, as a load shifts from
            // one edge to another; sliding, each point rubs by the dynamic friction, never harder than holding would.
            var carried = 0f;
            var pressed = 0f;
            for (var index = 0; index < contact.PointCount; index++)
            {
                ref var point = ref contact.Points[index];
                point.FrictionCarried = point.FrictionImpulse.Length();
                carried += point.FrictionCarried;
                pressed += point.NormalImpulse;
            }

            var holding = contact.StaticFriction * pressed;
            var sliding = MathF.Min(contact.DynamicFriction, contact.StaticFriction);
            var slip = 0f;
            for (var index = 0; index < contact.PointCount; index++)
            {
                ref var point = ref contact.Points[index];
                var previous = point.FrictionImpulse;
                var velocity = GetRelativeVelocity(first, second, point.AnchorFirst, point.AnchorSecond);
                var total = previous - contact.Tangent * (point.TangentMass * Vector3F.Dot(velocity, contact.Tangent))
                            - contact.Bitangent * (point.BitangentMass * Vector3F.Dot(velocity, contact.Bitangent));
                var length = total.Length();
                var limit = contact.IsSliding
                    ? sliding * point.NormalImpulse
                    : MathF.Max(contact.StaticFriction * point.NormalImpulse,
                        carried > 0 ? holding * point.FrictionCarried / carried : 0);
                if (length > limit)
                {
                    // What it could not stop is how fast it still slips.
                    if (point.TangentMass > 0)
                        slip = MathF.Max(slip, (length - limit) / point.TangentMass);

                    total *= length > 0 ? limit / length : 0;
                }

                point.FrictionImpulse = total;
                ApplyImpulse(ref first, ref second, point.AnchorFirst, point.AnchorSecond, total - previous);
            }

            // Slipping past its hold it slides from now on; stopped again, it is held.
            contact.IsSliding = slip > SlipSpeed;

            if (contact.RollingLever > 0)
                ResistRolling(contact, ref first, ref second);
        }
    }

    // Takes out the pair's spin about its rolling axes, up to its rolling resistance times how hard it is pressed times
    // the radius it rolls on.
    private static void ResistRolling(Contact contact, ref BodyState first, ref BodyState second)
    {
        var pressed = 0f;
        for (var index = 0; index < contact.PointCount; index++)
        {
            pressed += contact.Points[index].NormalImpulse;
        }

        var spin = second.AngularVelocity - first.AngularVelocity;
        var previous = contact.RollingImpulse;
        var total = previous
                    - contact.RollingFirstAxis * (contact.RollingFirstMass * Vector3F.Dot(spin, contact.RollingFirstAxis))
                    - contact.RollingSecondAxis * (contact.RollingSecondMass
                                                   * Vector3F.Dot(spin, contact.RollingSecondAxis));
        var limit = contact.RollingResistance * contact.RollingLever * pressed;
        var length = total.Length();
        if (length > limit)
            total *= length > 0 ? limit / length : 0;

        contact.RollingImpulse = total;
        ApplyTurn(ref first, ref second, total - previous);
    }

    private static void ApplyTurn(ref BodyState first, ref BodyState second, Vector3F turn)
    {
        if (first.IsDynamic)
            first.AngularVelocity -= InertiaMath.Apply(first.WorldInverseInertia, turn);

        if (second.IsDynamic)
            second.AngularVelocity += InertiaMath.Apply(second.WorldInverseInertia, turn);
    }

    /// <summary>Makes points that closed faster than <paramref name="threshold"/> and pushed part with the speed their
    /// restitution keeps.</summary>
    public void ApplyRestitution(BodyState[] bodies, float threshold)
    {
        foreach (var contact in _active)
        {
            if (contact.Restitution == 0)
                continue;

            ref var first = ref bodies[contact.First];
            ref var second = ref bodies[contact.Second];
            for (var index = 0; index < contact.PointCount; index++)
            {
                ref var point = ref contact.Points[index];
                if (point.RelativeVelocity > -threshold || point.MaxNormalImpulse == 0)
                    continue;

                var normalSpeed = Vector3F.Dot(GetRelativeVelocity(first, second, point.AnchorFirst, point.AnchorSecond), contact.Normal);
                var impulse = -point.NormalMass * (normalSpeed + contact.Restitution * point.RelativeVelocity);
                var total = MathF.Max(point.NormalImpulse + impulse, 0);
                impulse = total - point.NormalImpulse;
                point.NormalImpulse = total;
                point.MaxNormalImpulse = MathF.Max(point.MaxNormalImpulse, impulse);
                ApplyImpulse(ref first, ref second, point.AnchorFirst, point.AnchorSecond, contact.Normal * impulse);
            }
        }
    }

    private static float ComputeMass(in BodyState first, in BodyState second, Vector3F anchorFirst, Vector3F anchorSecond,
        Vector3F direction)
    {
        var turnFirst = Vector3F.Cross(anchorFirst, direction);
        var turnSecond = Vector3F.Cross(anchorSecond, direction);
        var mass = first.InverseMass + second.InverseMass
                   + Vector3F.Dot(turnFirst, InertiaMath.Apply(first.WorldInverseInertia, turnFirst))
                   + Vector3F.Dot(turnSecond, InertiaMath.Apply(second.WorldInverseInertia, turnSecond));
        return mass > 0 ? 1f / mass : 0;
    }

    private static float ComputeTurnMass(in BodyState first, in BodyState second, Vector3F axis)
    {
        var mass = (first.IsDynamic ? Vector3F.Dot(axis, InertiaMath.Apply(first.WorldInverseInertia, axis)) : 0)
                   + (second.IsDynamic ? Vector3F.Dot(axis, InertiaMath.Apply(second.WorldInverseInertia, axis)) : 0);
        return mass > 0 ? 1f / mass : 0;
    }

    private static Vector3F GetRelativeVelocity(in BodyState first, in BodyState second, Vector3F anchorFirst,
        Vector3F anchorSecond) =>
        second.LinearVelocity + Vector3F.Cross(second.AngularVelocity, anchorSecond)
        - first.LinearVelocity - Vector3F.Cross(first.AngularVelocity, anchorFirst);

    private static void ApplyImpulse(ref BodyState first, ref BodyState second, Vector3F anchorFirst,
        Vector3F anchorSecond, Vector3F impulse)
    {
        if (first.IsDynamic)
        {
            first.LinearVelocity -= impulse * first.InverseMass;
            first.AngularVelocity -= InertiaMath.Apply(first.WorldInverseInertia, Vector3F.Cross(anchorFirst, impulse));
        }

        if (second.IsDynamic)
        {
            second.LinearVelocity += impulse * second.InverseMass;
            second.AngularVelocity += InertiaMath.Apply(second.WorldInverseInertia, Vector3F.Cross(anchorSecond, impulse));
        }
    }
}
