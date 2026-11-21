using System;
using System.Collections.Generic;
using Adamantium.DarkMatter.Shapes;

namespace Adamantium.DarkMatter;

/// <summary>A world of rigid bodies stepped at a fixed rate: the simulation itself, a host's world on a client, a
/// replay, or a stand-in in tests. One thread owns a world.</summary>
internal interface IPhysicsWorld
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

    /// <summary>Holds a hinge's angle, a slider's offset or a distance joint's length between
    /// <paramref name="lowerLimit"/> and <paramref name="upperLimit"/> when <paramref name="hasLimits"/>, freely when
    /// not; wakes its bodies.</summary>
    void SetLimits(JointHandle joint, bool hasLimits, float lowerLimit, float upperLimit);

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
    WorldPosition GetCenterOfMass(BodyHandle body);

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
    /// its contacts are found anew, and it and what touched it wake. A compound's parts each take their own material
    /// and layer from <paramref name="partTraits"/>, or the body's for none or no array.</summary>
    void SetShape(BodyHandle body, CollisionShape shape, PartTraits[] partTraits = null);

    /// <summary>Weighs <paramref name="body"/> by <paramref name="mass"/> in kilograms, or by
    /// <paramref name="density"/> when the mass is zero, as <see cref="BodyDescription"/> does; kept, and not looked
    /// at, for a body that is not dynamic until it is. A dynamic body and what touches it wake.</summary>
    void SetMass(BodyHandle body, float mass, float density);

    /// <summary>Sets how <paramref name="body"/> rubs and bounces, and its parts with no material of their own, its
    /// contacts at once; a change wakes it and what touches it.</summary>
    void SetMaterial(BodyHandle body, in PhysicsMaterial material);

    /// <summary>Puts <paramref name="body"/>, and its parts with no layer of their own, on <paramref name="layer"/>:
    /// its pairs that no longer collide let go at once, those that now do are found with the next step.</summary>
    void SetLayer(BodyHandle body, Layer layer);

    /// <summary>Makes bodies on <paramref name="first"/> and on <paramref name="second"/> collide, or pass through
    /// each other: every layer collides with every other until told.</summary>
    void SetLayersCollide(Layer first, Layer second, bool collides);

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
    void ApplyImpulse(BodyHandle body, Vector3F impulse, WorldPosition point);

    /// <summary>Turns a dynamic body by <paramref name="impulse"/>, in newton meter seconds around each world axis, at
    /// once - cut to the spin limit - and wakes it.</summary>
    void ApplyAngularImpulse(BodyHandle body, Vector3F impulse);

    /// <summary>What <paramref name="body"/> weighs, in kilograms, as its mass or density gives it; zero for a body that
    /// is not dynamic.</summary>
    float GetMass(BodyHandle body);

    /// <summary>Whether <paramref name="body"/> is awake; static bodies never are.</summary>
    bool IsAwake(BodyHandle body);

    /// <summary>Wakes <paramref name="body"/> and everything asleep with it; a kinematic body standing still stays
    /// asleep.</summary>
    void WakeUp(BodyHandle body);

    /// <summary>The box, aligned with the world's axes, that holds <paramref name="body"/>'s shape where it stands.
    /// </summary>
    BoundingBox<double> GetBounds(BodyHandle body);

    /// <summary>Adds to <paramref name="into"/> every contact point the last step held: touching, or about to, as a
    /// positive separation says.</summary>
    void GetContactPoints(ICollection<ContactPoint> into);

    /// <summary>Moves the world by one step of <see cref="StepSeconds"/>.</summary>
    void Step();

    /// <summary>The touches of two parts begun and ended - by the last step, and by changes since such as a body taken
    /// out - in order; forgotten as the next step begins, or by <see cref="ForgetContactEvents"/>.</summary>
    ReadOnlySpan<WorldContactEventArgs> ContactEvents { get; }

    void ForgetContactEvents();

    /// <summary>Where a ray from <paramref name="origin"/> along <paramref name="direction"/> first meets a body within
    /// <paramref name="maxDistance"/> meters, <paramref name="except"/> passed by; a ray starting inside a body meets it
    /// at once. Every query meets only parts on <paramref name="layers"/>, a bit for each: all for the default; and
    /// triggers only when it <paramref name="meetsTriggers"/>.</summary>
    bool RayCast(WorldPosition origin, Vector3F direction, float maxDistance, out WorldRayHit hit,
        BodyHandle except = default, ulong layers = ulong.MaxValue, bool meetsTriggers = false);

    /// <summary>Writes into <paramref name="hits"/> every body the ray meets within <paramref name="maxDistance"/>,
    /// nearest first, each where it first meets it - the nearest as many as fit - and gives how many it wrote.</summary>
    int RayCast(WorldPosition origin, Vector3F direction, float maxDistance, Span<WorldRayHit> hits,
        BodyHandle except = default, ulong layers = ulong.MaxValue, bool meetsTriggers = false);

    /// <summary>Adds to <paramref name="into"/> every body but <paramref name="except"/> that shares a point with
    /// <paramref name="shape"/> standing at <paramref name="pose"/>.</summary>
    void Overlap(CollisionShape shape, in Pose pose, ICollection<BodyHandle> into, BodyHandle except = default,
        ulong layers = ulong.MaxValue, bool meetsTriggers = false);

    /// <summary>The first body but <paramref name="except"/> <paramref name="shape"/> meets moved by
    /// <paramref name="translation"/> without turning - within half a millimeter - and where it stopped, first within a
    /// millimeter of it; meshes and fields cannot be cast.</summary>
    bool CastShape(CollisionShape shape, in Pose pose, Vector3F translation, out WorldShapeHit hit,
        BodyHandle except = default, ulong layers = ulong.MaxValue, bool meetsTriggers = false);
}
