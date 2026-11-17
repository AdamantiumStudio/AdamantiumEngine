using System.Collections.Generic;
using Adamantium.DarkMatter.Shapes;

namespace Adamantium.DarkMatter;

/// <summary>A world of rigid bodies stepped at a fixed rate: the simulation itself, a host's world on a client, a
/// replay, or a stand-in in tests. One thread owns a world.</summary>
public interface IPhysicsWorld
{
    /// <summary>How the world steps and solves.</summary>
    PhysicsSettings Settings { get; }

    /// <summary>The pull every dynamic body feels, in meters per second squared; a change wakes the sleeping bodies it
    /// pulls.</summary>
    Vector3F Gravity { get; set; }

    /// <summary>How many bodies the world holds.</summary>
    int BodyCount { get; }

    /// <summary>What the last step did.</summary>
    StepStatistics LastStep { get; }

    /// <summary>The length of one step, in seconds.</summary>
    float StepSeconds { get; }

    /// <summary>The fastest any body spins, in radians per second: a quarter turn per substep, as far as a step can
    /// follow a turn.</summary>
    float MaxAngularSpeed { get; }

    /// <summary>Adds a body as <paramref name="description"/> describes it and gives its handle.</summary>
    RigidBody Add(RigidBodyDescription description);

    /// <summary>Takes <paramref name="body"/> out of the world; bodies resting on it wake.</summary>
    void Remove(RigidBody body);

    /// <summary>Whether <paramref name="body"/> is in this world.</summary>
    bool Contains(RigidBody body);

    /// <summary>How <paramref name="body"/> moves.</summary>
    BodyKind GetKind(RigidBody body);

    /// <summary>Where <paramref name="body"/> stands.</summary>
    Pose GetPose(RigidBody body);

    /// <summary>Moves <paramref name="body"/> to <paramref name="pose"/> at once, as a jump, not a motion; it and the
    /// bodies it touches, there or here, wake.</summary>
    void SetPose(RigidBody body, in Pose pose);

    /// <summary>The velocity of <paramref name="body"/>'s center, in meters per second.</summary>
    Vector3F GetLinearVelocity(RigidBody body);

    /// <summary>Sets the velocity of a dynamic or kinematic body's center, cut to
    /// <see cref="PhysicsSettings.MaxLinearSpeed"/>, and wakes it.</summary>
    void SetLinearVelocity(RigidBody body, Vector3F velocity);

    /// <summary>The spin of <paramref name="body"/>, in radians per second around each world axis.</summary>
    Vector3F GetAngularVelocity(RigidBody body);

    /// <summary>Sets the spin of a dynamic or kinematic body, cut to <see cref="MaxAngularSpeed"/>, and wakes it.
    /// </summary>
    void SetAngularVelocity(RigidBody body, Vector3F velocity);

    /// <summary>Pushes a dynamic body's center with <paramref name="force"/>, in newtons, through the next step, and
    /// wakes it.</summary>
    void ApplyForce(RigidBody body, Vector3F force);

    /// <summary>Turns a dynamic body with <paramref name="torque"/>, in newton meters around each world axis, through
    /// the next step, and wakes it.</summary>
    void ApplyTorque(RigidBody body, Vector3F torque);

    /// <summary>Strikes a dynamic body at <paramref name="point"/> of the world with <paramref name="impulse"/>, in
    /// newton seconds, changing its velocity at once - cut to the speed limits - and wakes it.</summary>
    void ApplyImpulse(RigidBody body, Vector3F impulse, Vector3F point);

    /// <summary>Whether <paramref name="body"/> is awake; static bodies never are.</summary>
    bool IsAwake(RigidBody body);

    /// <summary>Wakes <paramref name="body"/> and everything asleep with it; a kinematic body standing still stays
    /// asleep.</summary>
    void WakeUp(RigidBody body);

    /// <summary>The box, aligned with the world's axes, that holds <paramref name="body"/>'s shape where it stands.
    /// </summary>
    BoundingBox<float> GetBounds(RigidBody body);

    /// <summary>Adds to <paramref name="into"/> every contact point the last step held: touching, or about to, as a
    /// positive separation says.</summary>
    void GetContactPoints(ICollection<ContactPoint> into);

    /// <summary>Moves the world by one step of <see cref="StepSeconds"/>.</summary>
    void Step();

    /// <summary>Where <paramref name="ray"/> first meets a body within <paramref name="maxDistance"/> meters; a ray
    /// starting inside a body meets it at once.</summary>
    bool RayCast(in Ray<float> ray, float maxDistance, out RayHit hit);

    /// <summary>Adds to <paramref name="into"/> every body that shares a point with <paramref name="shape"/> standing
    /// at <paramref name="pose"/>.</summary>
    void Overlap(Shape shape, in Pose pose, ICollection<RigidBody> into);
}
