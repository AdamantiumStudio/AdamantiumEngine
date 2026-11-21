using Adamantium.DarkMatter;
using Adamantium.ECS.Components;
using Adamantium.Mathematics;

namespace Adamantium.ECS.Physics;

/// <summary>Where a ray met an entity the physics moves.</summary>
public readonly struct EntityRayHit
{
    public EntityRayHit(Entity entity, Collider collider, WorldPosition point, Vector3F normal, float distance)
    {
        Entity = entity;
        Collider = collider;
        Point = point;
        Normal = normal;
        Distance = distance;
    }

    /// <summary>The entity with the rigid body met; null for a body the main processor made of its own.</summary>
    public Entity Entity { get; }

    /// <summary>Which of its colliders, on it or on an entity below it; null for a part not made of one.</summary>
    public Collider Collider { get; }

    public WorldPosition Point { get; }

    /// <summary>The collider's outward normal there; against the ray for a ray starting inside.</summary>
    public Vector3F Normal { get; }

    /// <summary>How far along the ray, in meters.</summary>
    public float Distance { get; }
}
