using Adamantium.DarkMatter;
using Adamantium.ECS.Components;
using Adamantium.Mathematics;

namespace Adamantium.ECS.Physics;

/// <summary>What is told of two colliders of two entities that began or stopped touching: a trigger entered or left
/// among them.</summary>
public readonly struct EntityContactEventArgs
{
    internal EntityContactEventArgs(Entity first, Collider firstCollider, Entity second, Collider secondCollider,
        in ContactEventArgs told)
    {
        First = first;
        FirstCollider = firstCollider;
        Second = second;
        SecondCollider = secondCollider;
        Change = told.Change;
        IsTrigger = told.IsTrigger;
        Point = told.Point;
        Normal = told.Normal;
        ApproachSpeed = told.ApproachSpeed;
    }

    /// <summary>The entities with the rigid bodies that touched; null for a body the main processor made of its own.
    /// </summary>
    public Entity First { get; }
    public Entity Second { get; }

    /// <summary>Each body's collider that touched, on its entity or on one below it; null for a part not made of one.
    /// </summary>
    public Collider FirstCollider { get; }
    public Collider SecondCollider { get; }

    public ContactChange Change { get; }

    /// <summary>Whether one of the colliders is a trigger: it was entered or left, and pushed nothing.</summary>
    public bool IsTrigger { get; }

    /// <summary>Where they first touched; none for an end.</summary>
    public WorldPosition Point { get; }

    /// <summary>The normal of their touch as it began, pointing from the first to the second; none for an end.
    /// </summary>
    public Vector3F Normal { get; }

    /// <summary>How hard they met: <see cref="ContactEventArgs.ApproachSpeed"/>.</summary>
    public float ApproachSpeed { get; }

    /// <summary>Whether <paramref name="entity"/> is one of the two, and which is the other.</summary>
    public bool Involves(Entity entity, out Entity other)
    {
        var involved = entity != null && (entity == First || entity == Second);
        other = !involved ? null : entity == First ? Second : First;
        return involved;
    }
}
