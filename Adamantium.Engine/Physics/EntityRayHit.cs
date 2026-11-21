using Adamantium.DarkMatter;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.Mathematics;

namespace Adamantium.Engine.Physics;

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

    /// <summary>The entity with the rigid body met.</summary>
    public Entity Entity { get; }

    /// <summary>Which of its colliders, on it or on an entity below it.</summary>
    public Collider Collider { get; }

    public WorldPosition Point { get; }

    /// <summary>The collider's outward normal there; against the ray for a ray starting inside.</summary>
    public Vector3F Normal { get; }

    /// <summary>How far along the ray, in meters.</summary>
    public float Distance { get; }
}
