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
    BodyHandle Add(BodyDescription description);

    /// <summary>Takes <paramref name="body"/> out of the world; bodies resting on it wake.</summary>
    void Remove(BodyHandle body);

    /// <summary>Whether <paramref name="body"/> is in this world.</summary>
    bool Contains(BodyHandle body);

    /// <summary>How many joints the world holds.</summary>
    int JointCount { get; }

    /// <summary>Adds a joint as <paramref name="description"/> describes it and gives its handle; its bodies wake. A
    /// body taken out takes its joints with it.</summary>
    JointHandle Add(JointDescription description);

    /// <summary>Takes <paramref name="joint"/> out of the world; its bodies wake, and collide again if it kept them
    /// from it.</summary>
    void Remove(JointHandle joint);

    /// <summary>Whether <paramref name="joint"/> is in this world.</summary>
    bool Contains(JointHandle joint);

    /// <summary>Drives a hinge's or a slider's motor at <paramref name="speed"/> with no more than
    /// <paramref name="maxForce"/> - zero stops it - and wakes its bodies; another kind of joint has none.</summary>
    void SetMotor(JointHandle joint, float speed, float maxForce);

    /// <summary>Where <paramref name="joint"/> stands: a hinge's angle in radians or a slider's offset in meters from
    /// where it was added, a distance joint's length; zero for a weld or a ball.</summary>
    float GetJointPosition(JointHandle joint);

    /// <summary>How <paramref name="body"/> moves.</summary>
    BodyKind GetKind(BodyHandle body);

    /// <summary>Where <paramref name="body"/>'s origin stands, the point its shape is given around, and how it is turned.
    /// </summary>
    Pose GetPose(BodyHandle body);

    /// <summary>Where <paramref name="body"/>'s center of mass stands; the origin for a body that is not dynamic.
    /// </summary>
    Vector3F GetCenterOfMass(BodyHandle body);

    /// <summary>Where <paramref name="body"/>'s center of mass is from its origin, in the body's own axes; zero for a
    /// body that is not dynamic.</summary>
    Vector3F GetLocalCenterOfMass(BodyHandle body);

    /// <summary>Moves <paramref name="body"/>'s origin to <paramref name="pose"/> at once, as a jump, not a motion; it
    /// and the bodies it touches, there or here, wake.</summary>
    void SetPose(BodyHandle body, in Pose pose);

    /// <summary>Gives <paramref name="body"/>, where it stands, everything <paramref name="description"/> holds but its
    /// pose and velocity - and its shape only when <paramref name="changesShape"/> - as the setters below would, all at
    /// once: checked as a whole first, a change it cannot take throws and leaves it as it was.</summary>
    void Change(BodyHandle body, BodyDescription description, bool changesShape);

    /// <summary>Makes <paramref name="body"/> move as <paramref name="kind"/> says, where it stands, from rest; it and
    /// what touches it wake.</summary>
    void SetKind(BodyHandle body, BodyKind kind);

    /// <summary>Gives <paramref name="body"/> <paramref name="shape"/> in place of its own, its origin where it stands;
    /// its contacts are found anew, and it and what touched it wake.</summary>
    void SetShape(BodyHandle body, CollisionShape shape);

    /// <summary>Weighs <paramref name="body"/> by <paramref name="mass"/> in kilograms, or by
    /// <paramref name="density"/> when the mass is zero, as <see cref="BodyDescription"/> does; kept, and not looked
    /// at, for a body that is not dynamic until it is. A dynamic body and what touches it wake.</summary>
    void SetMass(BodyHandle body, float mass, float density);

    /// <summary>Sets how <paramref name="body"/> rubs and bounces, its contacts at once; a change wakes it and what
    /// touches it.</summary>
    void SetMaterial(BodyHandle body, float friction, float restitution);

    /// <summary>Sets how quickly <paramref name="body"/>'s motion and spin die away by themselves.</summary>
    void SetDamping(BodyHandle body, float linearDamping, float angularDamping);

    /// <summary>Sets how much of <see cref="Gravity"/> <paramref name="body"/> feels; a change wakes it.</summary>
    void SetGravityScale(BodyHandle body, float gravityScale);

    /// <summary>Sets whether <paramref name="body"/> may fall asleep; forbidden, it wakes.</summary>
    void SetCanSleep(BodyHandle body, bool canSleep);

    /// <summary>The velocity of <paramref name="body"/>'s center of mass, in meters per second.</summary>
    Vector3F GetLinearVelocity(BodyHandle body);

    /// <summary>Sets the velocity of a dynamic or kinematic body's center of mass, cut to
    /// <see cref="PhysicsSettings.MaxLinearSpeed"/>, and wakes it.</summary>
    void SetLinearVelocity(BodyHandle body, Vector3F velocity);

    /// <summary>The spin of <paramref name="body"/>, in radians per second around each world axis.</summary>
    Vector3F GetAngularVelocity(BodyHandle body);

    /// <summary>Sets the spin of a dynamic or kinematic body, cut to <see cref="MaxAngularSpeed"/>, and wakes it.
    /// </summary>
    void SetAngularVelocity(BodyHandle body, Vector3F velocity);

    /// <summary>Pushes a dynamic body's center of mass with <paramref name="force"/>, in newtons, through the next step; a force
    /// not zero wakes it and keeps it from counting that step toward sleep.</summary>
    void ApplyForce(BodyHandle body, Vector3F force);

    /// <summary>Turns a dynamic body with <paramref name="torque"/>, in newton meters around each world axis, through
    /// the next step; a torque not zero wakes it and keeps it from counting that step toward sleep.</summary>
    void ApplyTorque(BodyHandle body, Vector3F torque);

    /// <summary>Strikes a dynamic body at <paramref name="point"/> of the world with <paramref name="impulse"/>, in
    /// newton seconds, changing its velocity at once - cut to the speed limits - and wakes it.</summary>
    void ApplyImpulse(BodyHandle body, Vector3F impulse, Vector3F point);

    /// <summary>Whether <paramref name="body"/> is awake; static bodies never are.</summary>
    bool IsAwake(BodyHandle body);

    /// <summary>Wakes <paramref name="body"/> and everything asleep with it; a kinematic body standing still stays
    /// asleep.</summary>
    void WakeUp(BodyHandle body);

    /// <summary>The box, aligned with the world's axes, that holds <paramref name="body"/>'s shape where it stands.
    /// </summary>
    BoundingBox<float> GetBounds(BodyHandle body);

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
    void Overlap(CollisionShape shape, in Pose pose, ICollection<BodyHandle> into);

    /// <summary>The first body <paramref name="shape"/> meets moved by <paramref name="translation"/> without turning -
    /// within half a millimeter - and where it stopped, first within a millimeter of it; meshes and fields cannot be cast.
    /// </summary>
    bool CastShape(CollisionShape shape, in Pose pose, Vector3F translation, out ShapeHit hit);
}
