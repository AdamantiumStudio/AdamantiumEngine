using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Adamantium.DarkMatter.Dynamics;

namespace Adamantium.DarkMatter;

// Touches begun and ended, of a pair of parts - one of a surface with all its triangles. A pair met ahead of time is
// not yet touching: it touches once a point of it stands within the speculative distance, or the solver pushed at one
// in the step - as Box2D v3 tells a touch. Each pair met in a step is looked at once it is solved; a beginning is told
// with how fast they closed before it, an end - a pair parted, or let go of - when it happens.
internal sealed partial class PhysicsWorld
{
    private readonly List<WorldContactEvent> _contactEvents = [];
    // The pairs met in this step, looked at once it is solved.
    private readonly List<Contact> _metThisStep = [];

    /// <inheritdoc/>
    public ReadOnlySpan<WorldContactEvent> ContactEvents => CollectionsMarshal.AsSpan(_contactEvents);

    /// <inheritdoc/>
    public void ForgetContactEvents() => _contactEvents.Clear();

    private void TellTouches()
    {
        foreach (var contact in CollectionsMarshal.AsSpan(_metThisStep))
        {
            var touching = FirstTouching(contact);
            if ((touching != null) == contact.IsToldTouching)
                continue;

            if (touching == null)
            {
                TellTouchEnded(contact);
                continue;
            }

            contact.IsToldTouching = true;
            _contactEvents.Add(Describe(contact, ContactChange.Began) with
            {
                Point = touching.Points[0].Position, Normal = touching.Normal,
                ApproachSpeed = contact.IsTrigger ? 0 : MeasureApproachSpeed(contact)
            });
        }

        _metThisStep.Clear();
    }

    private void TellTouchEnded(Contact contact)
    {
        contact.IsToldTouching = false;
        _contactEvents.Add(Describe(contact, ContactChange.Ended));
    }

    private WorldContactEvent Describe(Contact contact, ContactChange change) =>
        new()
        {
            First = GetHandle(contact.First),
            Second = GetHandle(contact.Second),
            FirstPart = PartIndexOf(contact.FirstPart),
            SecondPart = PartIndexOf(contact.SecondPart),
            Change = change,
            IsTrigger = contact.IsTrigger
        };

    // The pair itself, or the first of its triangles' contacts, that touches; null for none.
    private Contact FirstTouching(Contact contact)
    {
        if (Touches(contact))
            return contact;

        if (contact.Pieces == null)
            return null;

        foreach (var piece in contact.Pieces)
        {
            if (Touches(piece))
                return piece;
        }

        return null;
    }

    private bool Touches(Contact contact)
    {
        for (var index = 0; index < contact.PointCount; index++)
        {
            ref readonly var point = ref contact.Points[index];
            if (point.Separation <= Settings.SpeculativeDistance || point.MaxNormalImpulse > 0)
                return true;
        }

        return false;
    }

    // The fastest its points closed along the normal as the solver took them; the speed apart counts for nothing.
    private static float MeasureApproachSpeed(Contact contact)
    {
        var speed = MeasureOwnApproachSpeed(contact);
        if (contact.Pieces == null)
            return speed;

        foreach (var piece in contact.Pieces)
        {
            speed = MathF.Max(speed, MeasureOwnApproachSpeed(piece));
        }

        return speed;
    }

    private static float MeasureOwnApproachSpeed(Contact contact)
    {
        var speed = 0f;
        for (var index = 0; index < contact.PointCount; index++)
        {
            speed = MathF.Max(speed, -contact.Points[index].RelativeVelocity);
        }

        return speed;
    }
}
