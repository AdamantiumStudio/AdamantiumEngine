using System;
using System.Collections.Generic;
using Adamantium.ECS.Components;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;
using Serilog;

namespace Adamantium.ECS.Physics;

/// <summary>An entity's collider and rigid body as a model's edits and a scene's entries keep them: a kind of collider,
/// fitted to the mesh, and a body's settings - put on an entity, and read back from one.</summary>
public static class PhysicsParts
{
    /// <summary>Gives <paramref name="entity"/> a collider of <paramref name="kind"/>, fitted to its mesh, in place of the
    /// ones it has; <see cref="ColliderKind.None"/> takes them off. A kind this engine does not know, or one for an entity
    /// with no mesh to fit it to, leaves what it has - logged.</summary>
    public static void SetCollider(Entity entity, ColliderKind kind)
    {
        ArgumentNullException.ThrowIfNull(entity);
        if (!Enum.IsDefined(kind))
        {
            Log.Logger.Warning("{Entity} is given a collider this engine does not know, {Kind}; it keeps what it has",
                entity.Name, kind);
            return;
        }

        if (kind != ColliderKind.None && entity.GetComponent<MeshData>() is not { Mesh.HasPoints: true })
        {
            Log.Logger.Warning("{Entity} has no mesh to fit a collider to; it keeps what it has", entity.Name);
            return;
        }

        PutCollider(entity, Make(kind));
    }

    /// <summary>Gives <paramref name="entity"/> <paramref name="collider"/> in place of the ones it has; null takes them
    /// off.</summary>
    public static void PutCollider(Entity entity, Collider collider)
    {
        ArgumentNullException.ThrowIfNull(entity);
        foreach (var each in entity.GetComponents<Collider>())
        {
            entity.RemoveComponent(each);
        }

        if (collider != null)
        {
            entity.AddComponent(collider);
        }
    }

    /// <summary>A new collider of the kind <paramref name="shape"/>'s template gives it; null - logged - when the shape's
    /// code fails to make one.</summary>
    public static Collider TemplateColliderOf(ProceduralShape shape)
    {
        ArgumentNullException.ThrowIfNull(shape);
        try
        {
            return shape.CreateCollider();
        }
        catch (Exception exception)
        {
            Log.Logger.Error(exception, "The shape {Type} could not make its collider", shape.GetType().FullName);
            return null;
        }
    }

    /// <summary>Gives <paramref name="collider"/> <paramref name="shape"/>'s sizes and placement, as a model's edits or a
    /// scene keep them - fitted to its mesh for null; false, the collider left as it was and logged, for values this
    /// engine cannot read.</summary>
    public static bool SetShape(Collider collider, ColliderShapeEdits shape)
    {
        ArgumentNullException.ThrowIfNull(collider);
        if (shape != null && !IsSound(shape))
        {
            Log.Logger.Warning("The collider shape given to {Entity} is not one this engine reads; it keeps what it has",
                collider.Owner?.Name);
            return false;
        }

        collider.SetShape(shape);
        return true;
    }

    /// <summary>Whether <paramref name="body"/> has <paramref name="settings"/>.</summary>
    public static bool IsAlike(RigidBody body, BodyEdits settings)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(settings);
        return body.Kind == settings.Kind && body.Mass == settings.Mass && body.Density == settings.Density
               && body.Friction == settings.Friction && body.Restitution == settings.Restitution
               && body.LinearDamping == settings.LinearDamping && body.AngularDamping == settings.AngularDamping
               && body.GravityScale == settings.GravityScale && body.CanSleep == settings.CanSleep;
    }

    /// <summary>The kind of <paramref name="collider"/>; <see cref="ColliderKind.None"/> for none.</summary>
    public static ColliderKind KindOf(Collider collider) => collider switch
    {
        BoxCollider => ColliderKind.Box,
        SphereCollider => ColliderKind.Sphere,
        CapsuleCollider => ColliderKind.Capsule,
        CylinderCollider => ColliderKind.Cylinder,
        ConvexHullCollider => ColliderKind.ConvexHull,
        MeshCollider => ColliderKind.Mesh,
        _ => ColliderKind.None
    };

    /// <summary>Gives <paramref name="entity"/>'s rigid body <paramref name="body"/>'s settings, a new body when it has
    /// none; false, the entity left as it was and logged, for settings this engine cannot read - a kind it does not know,
    /// an amount not finite or below zero.</summary>
    public static bool SetBody(Entity entity, BodyEdits body)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(body);
        if (!IsSound(body))
        {
            Log.Logger.Warning("The body given to {Entity} is not one this engine reads; it keeps what it has", entity.Name);
            return false;
        }

        // The body it has keeps its place, so a moving one keeps its speed.
        var own = entity.GetComponent<RigidBody>();
        var target = own ?? new RigidBody();
        target.Kind = body.Kind;
        target.Mass = body.Mass;
        target.Density = body.Density;
        target.Friction = body.Friction;
        target.Restitution = body.Restitution;
        target.LinearDamping = body.LinearDamping;
        target.AngularDamping = body.AngularDamping;
        target.GravityScale = body.GravityScale;
        target.CanSleep = body.CanSleep;
        if (own == null)
        {
            entity.AddComponent(target);
        }

        return true;
    }

    /// <summary>The settings of <paramref name="body"/>.</summary>
    public static BodyEdits BodyOf(RigidBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        return new BodyEdits
        {
            Kind = body.Kind,
            Mass = body.Mass,
            Density = body.Density,
            Friction = body.Friction,
            Restitution = body.Restitution,
            LinearDamping = body.LinearDamping,
            AngularDamping = body.AngularDamping,
            GravityScale = body.GravityScale,
            CanSleep = body.CanSleep
        };
    }

    /// <summary>Gives <paramref name="entity"/> the joint <paramref name="joint"/> keeps, held to
    /// <paramref name="connected"/>'s body or to the world for none: the joint of that kind it has, or a new one; its
    /// joints of other kinds stay. False, the entity left as it was and logged, for edits this engine cannot read.
    /// </summary>
    public static bool SetJoint(Entity entity, JointEdits joint, Entity connected)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(joint);
        if (!IsSound(joint))
        {
            Log.Logger.Warning("The joint given to {Entity} is not one this engine reads; it keeps what it has", entity.Name);
            return false;
        }

        var own = JointOfKind(entity, joint.Kind);
        var target = own ?? MakeJoint(joint.Kind);
        // Given as kept, it works: one a game turned off is turned on again.
        target.IsEnabled = true;
        target.ConnectedBody = connected;
        target.Anchor = joint.Anchor;
        target.CollideConnected = joint.CollideConnected;
        switch (target)
        {
            case HingeJoint hinge:
                hinge.Axis = joint.Axis;
                hinge.UsesLimits = joint.UsesLimits;
                hinge.LowerAngle = joint.LowerLimit;
                hinge.UpperAngle = joint.UpperLimit;
                hinge.UsesMotor = joint.UsesMotor;
                hinge.MotorSpeed = joint.MotorSpeed;
                hinge.MaxMotorTorque = joint.MaxMotorForce;
                break;
            case SliderJoint slider:
                slider.Axis = joint.Axis;
                slider.UsesLimits = joint.UsesLimits;
                slider.LowerLimit = joint.LowerLimit;
                slider.UpperLimit = joint.UpperLimit;
                slider.UsesMotor = joint.UsesMotor;
                slider.MotorSpeed = joint.MotorSpeed;
                slider.MaxMotorForce = joint.MaxMotorForce;
                break;
            case DistanceJoint distance:
                distance.ConnectedAnchor = joint.ConnectedAnchor.As<double>();
                distance.UsesLimits = joint.UsesLimits;
                distance.MinDistance = joint.LowerLimit;
                distance.MaxDistance = joint.UpperLimit;
                distance.SpringHertz = joint.SpringHertz;
                distance.SpringDampingRatio = joint.SpringDampingRatio;
                break;
        }

        if (own == null)
            entity.AddComponent(target);

        return true;
    }

    /// <summary>Takes off <paramref name="entity"/>'s joints of kinds no edits of <paramref name="kept"/> name - all of
    /// them for none.</summary>
    public static void RemoveJointsNotIn(Entity entity, IReadOnlyList<JointEdits> kept)
    {
        ArgumentNullException.ThrowIfNull(entity);
        foreach (var joint in entity.GetComponents<Joint>())
        {
            if (!Names(kept, joint.Kind))
                entity.RemoveComponent(joint);
        }
    }

    private static bool Names(IReadOnlyList<JointEdits> kept, DarkMatter.JointKind kind)
    {
        for (var index = 0; kept != null && index < kept.Count; index++)
        {
            if (kept[index]?.Kind == kind)
                return true;
        }

        return false;
    }

    private static Joint JointOfKind(Entity entity, DarkMatter.JointKind kind)
    {
        foreach (var component in entity.Components.AsSpan())
        {
            if (component is Joint joint && joint.Kind == kind)
                return joint;
        }

        return null;
    }

    /// <summary>The edits of <paramref name="joint"/>, its connected body named by <paramref name="connectedPart"/> -
    /// a part's path in a model, or null for the world.</summary>
    public static JointEdits JointOf(Joint joint, string connectedPart)
    {
        ArgumentNullException.ThrowIfNull(joint);
        var edits = new JointEdits
        {
            Kind = joint.Kind,
            ConnectedPart = connectedPart,
            Anchor = joint.Anchor,
            CollideConnected = joint.CollideConnected
        };

        return joint switch
        {
            HingeJoint hinge => edits with
            {
                Axis = hinge.Axis, UsesLimits = hinge.UsesLimits, LowerLimit = hinge.LowerAngle,
                UpperLimit = hinge.UpperAngle, UsesMotor = hinge.UsesMotor, MotorSpeed = hinge.MotorSpeed,
                MaxMotorForce = hinge.MaxMotorTorque
            },
            SliderJoint slider => edits with
            {
                Axis = slider.Axis, UsesLimits = slider.UsesLimits, LowerLimit = slider.LowerLimit,
                UpperLimit = slider.UpperLimit, UsesMotor = slider.UsesMotor, MotorSpeed = slider.MotorSpeed,
                MaxMotorForce = slider.MaxMotorForce
            },
            DistanceJoint distance => edits with
            {
                ConnectedAnchor = distance.ConnectedAnchor.As<float>(), UsesLimits = distance.UsesLimits,
                LowerLimit = distance.MinDistance, UpperLimit = distance.MaxDistance,
                SpringHertz = distance.SpringHertz, SpringDampingRatio = distance.SpringDampingRatio
            },
            _ => edits
        };
    }

    private static Joint MakeJoint(DarkMatter.JointKind kind) => kind switch
    {
        DarkMatter.JointKind.Fixed => new FixedJoint(),
        DarkMatter.JointKind.Hinge => new HingeJoint(),
        DarkMatter.JointKind.Slider => new SliderJoint(),
        DarkMatter.JointKind.Distance => new DistanceJoint(),
        _ => new BallJoint()
    };

    // A kind this engine knows, points finite, amounts not below zero; limits in order and a motor finite only when
    // they are used, as the physics reads them.
    private static bool IsSound(JointEdits joint) =>
        Enum.IsDefined(joint.Kind) && IsFinite(joint.Anchor) && IsFinite(joint.Axis) && IsFinite(joint.ConnectedAnchor)
        && float.IsFinite(joint.LowerLimit) && float.IsFinite(joint.UpperLimit)
        && (!joint.UsesLimits || joint.LowerLimit <= joint.UpperLimit)
        && (!joint.UsesMotor || (float.IsFinite(joint.MotorSpeed) && IsAmount(joint.MaxMotorForce)))
        && IsAmount(joint.SpringHertz) && IsAmount(joint.SpringDampingRatio);

    private static bool IsFinite(Vector3F value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static Collider Make(ColliderKind kind) => kind switch
    {
        ColliderKind.Box => new BoxCollider(),
        ColliderKind.Sphere => new SphereCollider(),
        ColliderKind.Capsule => new CapsuleCollider(),
        ColliderKind.Cylinder => new CylinderCollider(),
        ColliderKind.ConvexHull => new ConvexHullCollider(),
        ColliderKind.Mesh => new MeshCollider(),
        _ => null
    };

    // A kind this engine knows, and amounts that are finite and not negative; gravity may pull either way.
    private static bool IsSound(BodyEdits body) =>
        Enum.IsDefined(body.Kind) && IsAmount(body.Mass) && IsAmount(body.Density) && IsAmount(body.Friction)
        && IsAmount(body.Restitution) && IsAmount(body.LinearDamping) && IsAmount(body.AngularDamping)
        && float.IsFinite(body.GravityScale);

    private static bool IsAmount(float value) => float.IsFinite(value) && value >= 0;

    // Numbers all finite, sizes not negative, an axis this engine knows and a rotation that turns.
    private static bool IsSound(ColliderShapeEdits shape) =>
        IsPoint(shape.Position) && IsPoint(shape.Scale) && IsPoint(shape.Size) && shape.Size.X >= 0 && shape.Size.Y >= 0
        && shape.Size.Z >= 0 && IsAmount(shape.Radius) && IsAmount(shape.Height) && Enum.IsDefined(shape.Axis)
        && float.IsFinite(shape.Rotation.LengthSquared()) && shape.Rotation.LengthSquared() > 1e-12f;

    private static bool IsPoint(Vector3F value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
