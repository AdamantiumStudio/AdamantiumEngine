using Adamantium.DarkMatter.Shapes;

namespace Adamantium.DarkMatter.Tests;

/// <summary>Builds the reference scenes: a ground and bodies on it, stepped for a while.</summary>
internal sealed class SceneBuilder
{
    public SceneBuilder(PhysicsSettings settings = null)
    {
        World = new PhysicsWorld(settings);
    }

    public IPhysicsWorld World { get; }

    /// <summary>A static slab whose top face lies at Y = 0, or turned by <paramref name="rotation"/> around its top
    /// face's center.</summary>
    public BodyHandle AddGround(float friction = 0.6f, QuaternionF? rotation = null)
    {
        var turn = rotation ?? QuaternionF.Identity;
        var center = Vector3F.Transform(new Vector3F(0, -1, 0), turn);
        return World.Add(new BodyDescription
        {
            Kind = BodyKind.Static,
            Shape = new BoxCollisionShape(50, 1, 50),
            Position = new WorldPosition(center),
            Rotation = turn,
            Material = new PhysicsMaterial { StaticFriction = friction, DynamicFriction = friction }
        });
    }

    public BodyHandle AddBox(Vector3F position, Vector3F halfExtents, float friction = 0.6f, float restitution = 0,
        QuaternionF? rotation = null, Vector3F velocity = default)
    {
        return World.Add(new BodyDescription
        {
            Shape = new BoxCollisionShape(halfExtents),
            Position = new WorldPosition(position),
            Rotation = rotation ?? QuaternionF.Identity,
            Material = new PhysicsMaterial
            {
                StaticFriction = friction, DynamicFriction = friction, Restitution = restitution
            },
            LinearVelocity = velocity
        });
    }

    public BodyHandle AddSphere(Vector3F position, float radius, float restitution = 0,
        CombineRule restitutionCombine = CombineRule.Average)
    {
        return World.Add(new BodyDescription
        {
            Shape = new SphereCollisionShape(radius),
            Position = new WorldPosition(position),
            Material = new PhysicsMaterial
            {
                StaticFriction = 0.6f, DynamicFriction = 0.6f, Restitution = restitution,
                RestitutionCombine = restitutionCombine
            }
        });
    }

    public BodyHandle AddCapsule(Vector3F position, float halfLength, float radius, QuaternionF? rotation = null)
    {
        return World.Add(new BodyDescription
        {
            Shape = new CapsuleCollisionShape(halfLength, radius),
            Position = new WorldPosition(position),
            Rotation = rotation ?? QuaternionF.Identity
        });
    }

    /// <summary>Steps the world for <paramref name="seconds"/>.</summary>
    public void Run(float seconds)
    {
        var steps = (int)(seconds * World.Settings.StepRate + 0.5f);
        for (var step = 0; step < steps; step++)
        {
            World.Step();
        }
    }
}
