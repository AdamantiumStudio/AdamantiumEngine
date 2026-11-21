using System;
using Adamantium.DarkMatter;
using Adamantium.DarkMatter.Shapes;
using Adamantium.Mathematics;

namespace Adamantium.ECS.Physics;

/// <summary>The physics of a game: moves the entities with a <see cref="ECS.Components.RigidBody"/> while it plays, on a
/// thread of its own, and takes the game's pushes, queries and readings - the one way the game's code works with the
/// physics. Down is +Y in the engine's world, so gravity is too.</summary>
public interface IPhysicsService : IGameService
{
    /// <summary>How the next game's physics steps and solves; a game playing keeps those it started with, and ones set
    /// during it go back when it stops. Their gravity is not used: <see cref="Gravity"/> is.</summary>
    PhysicsSettings Settings { get; set; }

    /// <summary>The pull every dynamic body feels while the game plays, in meters per second squared; set during a game,
    /// it goes back when the game stops.</summary>
    Vector3F Gravity { get; set; }

    /// <summary>What the last step of the game's physics did.</summary>
    StepStatistics LastStep { get; }

    /// <summary>The colliders that began or stopped touching, and the triggers entered or left, by the steps of this
    /// frame; empty while no game plays.</summary>
    ReadOnlySpan<EntityContactEventArgs> Contacts { get; }

    /// <summary>Whether the physics moves <paramref name="entity"/> in the game playing.</summary>
    bool Moves(Entity entity);

    /// <summary><paramref name="entity"/>'s body as a chain of its actions and readings.</summary>
    EntityBodyActions Body(Entity entity) => new(this, entity);

    /// <summary>A query told link by link: its filter, then the ray, cast or overlap that answers it.</summary>
    EntityQuery Query() => new(this, default);

    /// <summary>The physics world's own actions as one chain.</summary>
    PhysicsWorldActions World() => new(this);

    /// <summary>The bodies the last step read, found by what they are and where they stand, each's entity by
    /// <c>body.Entity</c> (<see cref="BodyViewEntity"/>). The search only reads: an entity found is acted on through
    /// <see cref="Body"/>. Refused while no game plays.</summary>
    BodyFinder<AnyBody> Find();

    /// <summary>The velocity of <paramref name="entity"/>'s body as the last step left it, in meters per second.</summary>
    Vector3F GetLinearVelocity(Entity entity);

    /// <summary>The spin of <paramref name="entity"/>'s body as the last step left it, in radians per second.</summary>
    Vector3F GetAngularVelocity(Entity entity);

    /// <summary>The mass of <paramref name="entity"/>'s body as the physics weighed it, in kilograms.</summary>
    float GetMass(Entity entity);

    /// <summary>Whether <paramref name="entity"/>'s body was asleep after the last step.</summary>
    bool IsSleeping(Entity entity);

    /// <summary>Sets the velocity of <paramref name="entity"/>'s dynamic or kinematic body from the next step.</summary>
    void SetLinearVelocity(Entity entity, Vector3F velocity);

    /// <summary>Sets the spin of <paramref name="entity"/>'s dynamic or kinematic body from the next step.</summary>
    void SetAngularVelocity(Entity entity, Vector3F velocity);

    /// <summary>Sets the force, in newtons, pushing the center of <paramref name="entity"/>'s dynamic body at every
    /// step until it is set again; zero ends it. One too strong for the body ends it too, logged once per body.</summary>
    void SetForce(Entity entity, Vector3F force);

    /// <summary>Sets the torque, in newton meters, turning <paramref name="entity"/>'s dynamic body at every step until
    /// it is set again; zero ends it.</summary>
    void SetTorque(Entity entity, Vector3F torque);

    /// <summary>Strikes the center of <paramref name="entity"/>'s dynamic body at the next step, in newton seconds.
    /// </summary>
    void ApplyImpulse(Entity entity, Vector3F impulse);

    /// <summary>Strikes <paramref name="entity"/>'s dynamic body at <paramref name="point"/> of the world at the next
    /// step, in newton seconds.</summary>
    void ApplyImpulse(Entity entity, Vector3F impulse, WorldPosition point);

    /// <summary>Spins <paramref name="entity"/>'s dynamic body at the next step, in newton meter seconds.</summary>
    void ApplyAngularImpulse(Entity entity, Vector3F impulse);

    /// <summary>Moves <paramref name="entity"/>'s kinematic body to <paramref name="pose"/> over the next step, pushing
    /// what it meets; it stops there unless moved again.</summary>
    void MoveKinematic(Entity entity, in Pose pose);

    /// <summary>Wakes <paramref name="entity"/>'s body and everything asleep with it.</summary>
    void WakeUp(Entity entity);

    /// <summary>Wakes every body asleep whose center of mass is within <paramref name="area"/>, and what sleeps with
    /// it.</summary>
    void WakeUp(in BoundingBox<double> area);

    /// <summary>An explosion: <see cref="PhysicsSimulation.ApplyRadialImpulse"/>.</summary>
    void ApplyRadialImpulse(WorldPosition center, float radius, float strength);

    /// <summary>Where a ray from <paramref name="origin"/> along <paramref name="direction"/> first meets a body within
    /// <paramref name="maxDistance"/> meters, as the last step left them; none while no game plays.</summary>
    bool RayCast(WorldPosition origin, Vector3F direction, float maxDistance, out EntityRayHit hit,
        in EntityQueryFilter filter = default);

    /// <summary>Writes into <paramref name="hits"/> every body the ray meets, nearest first - the nearest as many as fit
    /// - and gives how many it wrote.</summary>
    int RayCast(WorldPosition origin, Vector3F direction, float maxDistance, Span<EntityRayHit> hits,
        in EntityQueryFilter filter = default);

    /// <summary>The first body <paramref name="shape"/> meets moved by <paramref name="translation"/> without turning;
    /// none while no game plays.</summary>
    bool CastShape(CollisionShape shape, in Pose pose, Vector3F translation, out EntityShapeHit hit,
        in EntityQueryFilter filter = default);

    /// <summary>Writes into <paramref name="entities"/> those whose bodies share a point with <paramref name="shape"/>
    /// standing at <paramref name="pose"/> - as many as fit - and gives how many it wrote.</summary>
    int Overlap(CollisionShape shape, in Pose pose, Span<Entity> entities, in EntityQueryFilter filter = default);
}
