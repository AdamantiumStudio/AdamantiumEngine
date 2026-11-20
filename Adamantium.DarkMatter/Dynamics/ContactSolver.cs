using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Adamantium.DarkMatter.CollisionDetection;

namespace Adamantium.DarkMatter.Dynamics;

/// <summary>Solves contacts as soft constraints over substeps: a damped spring pushes overlaps apart, friction stays in
/// the cone of the push, a pass without the spring takes out the speed it added; bounces come at the step's end.</summary>
internal sealed class ContactSolver
{
    private readonly List<Contact> _active = [];

    /// <summary>The contacts solved this step, until the next <see cref="Prepare"/>: a span, read only, no enumerator.
    /// </summary>
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
            if (!contact.IsTouching)
                continue;

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
                var anchorFirst = (point.Position - first.Position).As<float>();
                var anchorSecond = (point.Position - second.Position).As<float>();
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
        }
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

            for (var index = 0; index < contact.PointCount; index++)
            {
                ref var point = ref contact.Points[index];
                var velocity = GetRelativeVelocity(first, second, point.AnchorFirst, point.AnchorSecond);
                var tangentImpulse = -point.TangentMass * Vector3F.Dot(velocity, contact.Tangent);
                var bitangentImpulse = -point.BitangentMass * Vector3F.Dot(velocity, contact.Bitangent);
                var previous = point.FrictionImpulse;
                var total = previous + contact.Tangent * tangentImpulse + contact.Bitangent * bitangentImpulse;
                var limit = contact.Friction * point.NormalImpulse;
                var length = total.Length();
                if (length > limit)
                    total *= length > 0 ? limit / length : 0;

                point.FrictionImpulse = total;
                ApplyImpulse(ref first, ref second, point.AnchorFirst, point.AnchorSecond, total - previous);
            }
        }
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
