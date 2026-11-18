using System;
using Adamantium.DarkMatter.Dynamics;
using Adamantium.DarkMatter.Shapes;

namespace Adamantium.DarkMatter;

// A body changed where it stands: its kind, shape, weight and material, without taking it out and adding it again.
public sealed partial class PhysicsWorld
{
    /// <inheritdoc/>
    public void Change(BodyHandle body, BodyDescription description, bool changesShape)
    {
        var index = Resolve(body);
        ValidateDescription(description);
        ref var state = ref _bodies[index];
        var shape = changesShape ? description.Shape : state.Shape;
        // Weighed as it will be, before anything changes: a change it cannot take leaves it as it was.
        var isDynamic = description.Kind == BodyKind.Dynamic;
        var changesKind = state.Kind != description.Kind;
        if (isDynamic)
            RequireMassAndDensity(description.Mass, description.Density);

        // A weight that is not dynamic moves nothing and is not looked at until it is; a dynamic body is weighed anew
        // only when what it weighs changes.
        var isWeighedAnew = changesShape || changesKind || isDynamic
            && !BodyDescription.AreWeighedAlike(state.Mass, state.Density, description.Mass, description.Density);
        var mass = isDynamic && isWeighedAnew ? ComputeMass(shape, description.Mass, description.Density) : default;
        if (changesShape)
            TakeShape(index, shape);

        if (changesKind)
            TakeKind(index, description.Kind);

        state.Mass = description.Mass;
        state.Density = description.Density;
        if (isWeighedAnew)
        {
            // A new shape or kind woke it and what it touched already.
            if (!changesShape && !changesKind)
            {
                WakeIsland(index);
                WakeTouching(index);
            }

            Weigh(index, mass);
        }

        if (changesShape)
            LayParts(index);

        TakeMaterial(index, description.Friction, description.Restitution);
        state.LinearDamping = description.LinearDamping;
        state.AngularDamping = description.AngularDamping;
        TakeGravityScale(index, description.GravityScale);
        TakeCanSleep(index, description.CanSleep);
    }

    /// <inheritdoc/>
    public void SetKind(BodyHandle body, BodyKind kind)
    {
        var index = Resolve(body);
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown kind of body.");

        ref var state = ref _bodies[index];
        if (state.Kind == kind)
            return;

        var mass = kind == BodyKind.Dynamic ? ComputeMass(state.Shape, state.Mass, state.Density) : default;
        TakeKind(index, kind);
        Weigh(index, mass);
    }

    /// <inheritdoc/>
    public void SetShape(BodyHandle body, CollisionShape shape)
    {
        var index = Resolve(body);
        ArgumentNullException.ThrowIfNull(shape);
        ref var state = ref _bodies[index];
        var mass = state.IsDynamic ? ComputeMass(shape, state.Mass, state.Density) : default;
        TakeShape(index, shape);
        Weigh(index, mass);
        LayParts(index);
    }

    /// <inheritdoc/>
    public void SetMass(BodyHandle body, float mass, float density)
    {
        var index = Resolve(body);
        ref var state = ref _bodies[index];
        if (state.IsDynamic)
            RequireMassAndDensity(mass, density);

        var isWeighedAlike = BodyDescription.AreWeighedAlike(state.Mass, state.Density, mass, density);
        var properties = state.IsDynamic && !isWeighedAlike ? ComputeMass(state.Shape, mass, density) : default;
        state.Mass = mass;
        state.Density = density;
        if (!state.IsDynamic || isWeighedAlike)
            return;

        WakeIsland(index);
        WakeTouching(index);
        Weigh(index, properties);
    }

    /// <inheritdoc/>
    public void SetMaterial(BodyHandle body, float friction, float restitution)
    {
        var index = Resolve(body);
        RequireFiniteNotNegative(friction, nameof(friction));
        RequireFiniteNotNegative(restitution, nameof(restitution));
        TakeMaterial(index, friction, restitution);
    }

    /// <inheritdoc/>
    public void SetDamping(BodyHandle body, float linearDamping, float angularDamping)
    {
        var index = Resolve(body);
        RequireFiniteNotNegative(linearDamping, nameof(linearDamping));
        RequireFiniteNotNegative(angularDamping, nameof(angularDamping));
        _bodies[index].LinearDamping = linearDamping;
        _bodies[index].AngularDamping = angularDamping;
    }

    /// <inheritdoc/>
    public void SetGravityScale(BodyHandle body, float gravityScale)
    {
        var index = Resolve(body);
        if (!float.IsFinite(gravityScale))
            throw new ArgumentOutOfRangeException(nameof(gravityScale), "The gravity scale must be finite.");

        TakeGravityScale(index, gravityScale);
    }

    /// <inheritdoc/>
    public void SetCanSleep(BodyHandle body, bool canSleep) => TakeCanSleep(Resolve(body), canSleep);

    // A kind taken from rest: it and what touches it wake; two bodies neither of which is dynamic stop meeting, and its
    // pairs are found again - one dynamic now meets the static bodies it passed over.
    private void TakeKind(int index, BodyKind kind)
    {
        WakeIsland(index);
        WakeTouching(index);
        ref var state = ref _bodies[index];
        state.Kind = kind;
        state.LinearVelocity = Vector3F.Zero;
        state.AngularVelocity = Vector3F.Zero;
        state.Force = Vector3F.Zero;
        state.Torque = Vector3F.Zero;
        state.IsPushed = false;
        state.SleepTime = 0;
        state.IslandHead = NoBody;
        state.IslandNext = NoBody;
        state.IsAwake = kind == BodyKind.Dynamic;
        if (kind != BodyKind.Dynamic)
            DropContactsWithoutDynamic(index);

        Mark(index, BodyMarks.Moved | BodyMarks.Placed);
    }

    // The old shape's parts and contacts let go, waking what they touched; the new parts are laid once weighed.
    private void TakeShape(int index, CollisionShape shape)
    {
        TearOffContacts(index);
        RemoveParts(index);
        _bodies[index].Shape = shape;
    }

    // The new shape's parts laid around the center of mass it was weighed for, its pairs found again.
    private void LayParts(int index)
    {
        _bodies[index].Reach = 0;
        AddParts(index, _bodies[index].Shape);
        Mark(index, BodyMarks.Moved | BodyMarks.Placed);
    }

    private void TakeMaterial(int index, float friction, float restitution)
    {
        ref var state = ref _bodies[index];
        if (state.Friction == friction && state.Restitution == restitution)
            return;

        state.Friction = friction;
        state.Restitution = restitution;
        for (var contact = state.Contacts; contact != null; contact = contact.GetNext(index))
        {
            contact.Friction = MathF.Sqrt(_bodies[contact.First].Friction * _bodies[contact.Second].Friction);
            contact.Restitution = MathF.Max(_bodies[contact.First].Restitution, _bodies[contact.Second].Restitution);
        }

        // Resting by its friction, it may slide now.
        WakeIsland(index);
        WakeTouching(index);
    }

    private void TakeGravityScale(int index, float gravityScale)
    {
        if (_bodies[index].GravityScale == gravityScale)
            return;

        _bodies[index].GravityScale = gravityScale;
        WakeIsland(index);
    }

    private void TakeCanSleep(int index, bool canSleep)
    {
        _bodies[index].CanSleep = canSleep;
        if (!canSleep)
            WakeIsland(index);
    }

    // Gives the body mass - none for one not dynamic - its origin standing where it stood and its center of mass moving
    // as that point of it moved. The parts it has stay in place, kept where they are from the new center; their
    // contacts stay theirs, and their boxes in the tree are measured again.
    private void Weigh(int index, in MassProperties mass)
    {
        ref var state = ref _bodies[index];
        var origin = state.OriginPose;
        var isDynamic = state.IsDynamic;
        var shift = state.CenterOfMass - (isDynamic ? mass.Center : Vector3F.Zero);
        state.CenterOfMass -= shift;
        state.InertiaRotation = isDynamic ? mass.InertiaRotation : QuaternionF.Identity;
        state.InverseMass = isDynamic ? 1f / mass.Mass : 0;
        state.InverseInertia = isDynamic
            ? new Vector3F(1f / mass.Inertia.X, 1f / mass.Inertia.Y, 1f / mass.Inertia.Z)
            : Vector3F.Zero;
        state.Position = origin.Position + Vector3F.Transform(state.CenterOfMass, origin.Rotation);
        state.WorldInverseInertia = isDynamic ? state.MeasureWorldInverseInertia() : default;
        if (shift != Vector3F.Zero)
        {
            // The new center moves as its point of the spinning body did.
            state.LinearVelocity += Vector3F.Cross(state.AngularVelocity, Vector3F.Transform(-shift, origin.Rotation));
            state.Reach = 0;
            for (var part = state.FirstPart; part != NoPart; part = _parts[part].Next)
            {
                ref var slot = ref _parts[part];
                slot.Pose = new Pose(slot.Pose.Position + shift, slot.Pose.Rotation);
                slot.IsCentered = slot.Pose.Position == Vector3F.Zero;
                slot.HasBodyPose = slot.IsCentered && slot.Pose.Rotation == QuaternionF.Identity;
                state.Reach = MathF.Max(state.Reach, slot.Pose.Position.Length() + slot.Shape.BoundingRadius);
            }
        }

        MoveParts(index);
    }

    private void DropContactsWithoutDynamic(int index)
    {
        var contact = _bodies[index].Contacts;
        while (contact != null)
        {
            var next = contact.GetNext(index);
            if (!_bodies[contact.GetOther(index)].IsDynamic)
                DestroyContact(contact);

            contact = next;
        }
    }
}
