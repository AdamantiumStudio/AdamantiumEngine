using System;
using System.Threading;
using Adamantium.DarkMatter;
using Adamantium.Mathematics;
using Adamantium.MVVM;

namespace Adamantium.ECS.Components;

/// <summary>Makes its entity a body the physics moves while a game plays, colliding with the colliders on the entity and
/// on the entities below it that have no rigid body of their own: boxes, spheres, capsules and convex hulls.</summary>
[ViewModel]
public partial class RigidBody : Component
{
    public RigidBody()
    {
        Kind = BodyKind.Dynamic;
        Density = 1000f;
        GravityScale = 1f;
        CanSleep = true;
    }

    /// <summary>How the body moves: static, kinematic or dynamic.</summary>
    [Bindable]
    public partial BodyKind Kind { get; set; }

    /// <summary>The mass in kilograms; zero to have it follow from <see cref="Density"/>.</summary>
    [Bindable]
    public partial float Mass { get; set; }

    /// <summary>The density the mass follows from, in kilograms per cubic meter; water by default.</summary>
    [Bindable]
    public partial float Density { get; set; }

    /// <summary>The physics material file (<c>.aphysmat</c>) the body and its colliders with none of their own rub and
    /// bounce by; null or empty for the default material.</summary>
    [Bindable]
    public partial string MaterialPath { get; set; }

    /// <summary>The name of the project's layer the body and its colliders with none of their own are on; null or empty
    /// for the default layer.</summary>
    [Bindable]
    public partial string Layer { get; set; }

    /// <summary>How fast the body's velocity fades by itself, per second.</summary>
    [Bindable]
    public partial float LinearDamping { get; set; }

    /// <summary>How fast the body's spin fades by itself, per second.</summary>
    [Bindable]
    public partial float AngularDamping { get; set; }

    /// <summary>How much gravity pulls the body: 1 as the world's, 0 not at all.</summary>
    [Bindable]
    public partial float GravityScale { get; set; }

    /// <summary>Whether the body may fall asleep when it comes to rest.</summary>
    [Bindable]
    public partial bool CanSleep { get; set; }

    private float computedMass;
    private bool isWeighed;
    private int weighings;
    private int weighedAt;

    /// <summary>The mass the physics gives the body once it is dynamic, in kilograms: <see cref="Mass"/> when above zero,
    /// else <see cref="Density"/> times the volume of its colliders - on its entity and on those below it with no body of
    /// their own, scaled as each stands. A change to one of these says it changed; it is weighed once, when next read,
    /// however many changes came - a model of many parts put together is weighed once.</summary>
    public float ComputedMass
    {
        get
        {
            var seen = Volatile.Read(ref weighings);
            if (Volatile.Read(ref isWeighed) && weighedAt == seen)
                return computedMass;

            var mass = Mass > 0 ? Mass : (float)(Density * MeasureVolume());
            computedMass = mass;
            weighedAt = seen;
            Volatile.Write(ref isWeighed, true);
            // A change that came while it was weighed found nothing to tell: it is told now, to be read again.
            if (Volatile.Read(ref weighings) != seen)
            {
                Volatile.Write(ref isWeighed, false);
                RaiseWorkedOutChanged(nameof(ComputedMass));
            }

            return mass;
        }
    }

    partial void OnMassChanged(float value) => Reweigh();

    partial void OnDensityChanged(float value) => Reweigh();

    /// <summary>Has the body at <paramref name="entity"/>, or the nearest above it, weighed anew when next read.</summary>
    public static void ReweighAbove(Entity entity)
    {
        for (var at = entity; at != null; at = at.Owner)
        {
            if (at.GetComponent<RigidBody>() is { } body)
            {
                body.Reweigh();
                return;
            }
        }
    }

    /// <summary>Has the body at or above <paramref name="entity"/> and every body below it weighed anew when next read:
    /// its scale, turn or place changed, and so the size of all under it.</summary>
    public static void ReweighAround(Entity entity)
    {
        if (entity == null)
            return;

        ReweighAbove(entity);
        ReweighBelow(entity);
    }

    /// <summary>Has the body at <paramref name="entity"/> and every body below it weighed anew when next read: the part
    /// was hung elsewhere.</summary>
    public static void ReweighWithin(Entity entity)
    {
        if (entity == null)
            return;

        entity.GetComponent<RigidBody>()?.Reweigh();
        ReweighBelow(entity);
    }

    private static void ReweighBelow(Entity entity)
    {
        var pending = Entity.StackPool.Get();
        try
        {
            pending.Push(entity);
            while (pending.Count > 0)
            {
                var parts = pending.Pop().Dependencies;
                for (var index = 0; index < parts.Count; index++)
                {
                    if (parts[index] is not { } part)
                        continue;

                    part.GetComponent<RigidBody>()?.Reweigh();
                    pending.Push(part);
                }
            }
        }
        finally
        {
            Entity.StackPool.Return(pending);
        }
    }

    // Weighed already, it is told once that its weight changed; not weighed since, it has nothing new to tell. Not
    // counted as the entity's change: what changed already was.
    private void Reweigh()
    {
        Interlocked.Increment(ref weighings);
        if (!Volatile.Read(ref isWeighed))
            return;

        Volatile.Write(ref isWeighed, false);
        RaiseWorkedOutChanged(nameof(ComputedMass));
    }

    // Each collider is measured at the scale its entity's world matrix has along its own axes, as the physics reads it.
    private double MeasureVolume()
    {
        if (Owner is not { } body)
            return 0;

        var parentWorld = Matrix4x4F.Identity;
        for (var at = body.Owner; at != null; at = at.Owner)
        {
            parentWorld *= LocalOf(at);
        }

        return MeasurePart(body, parentWorld, true);
    }

    private static double MeasurePart(Entity entity, Matrix4x4F parentWorld, bool isBody)
    {
        if (entity == null || (!isBody && (!entity.IsEnabled || entity.ContainsComponent<RigidBody>())))
            return 0;

        // A flattened part is left out by the physics, its own colliders; a skewed one's are carried into the body by its
        // whole matrix, which stretches their space by its determinant. Those below stand on their own.
        var world = LocalOf(entity) * parentWorld;
        // A body skewed or flattened itself the physics does not take at all.
        var stretch = (double)world.Determinant();
        var volume = 0.0;
        var isSkewed = stretch != 0 && Matrix4x4.IsSkewed(world);
        if (isBody && (isSkewed || stretch == 0))
            return 0;

        var scale = Vector3F.Zero;
        if (isSkewed || (stretch != 0 && world.Decompose(out scale, out _, out _)))
        {
            foreach (var component in entity.Components.AsSpan())
            {
                if (component is Collider { IsEnabled: true, IsTrigger: false } collider)
                    volume += isSkewed
                        ? collider.MeasureCarriedVolume(Math.Abs(stretch))
                        : collider.MeasureVolume(Vector3F.Abs(scale));
            }
        }

        var parts = entity.Dependencies;
        for (var index = 0; index < parts.Count; index++)
        {
            volume += MeasurePart(parts[index], world, false);
        }

        return volume;
    }

    // An entity whose transform was taken off stands as it is.
    private static Matrix4x4F LocalOf(Entity entity) =>
        entity.Transform is { } transform ? transform.GetLocalMatrixF() : Matrix4x4F.Identity;
}
