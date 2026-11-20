using Adamantium.Mathematics;

namespace Adamantium.Engine.Physics;

/// <summary>One request of the game, sent to the physics thread and done before its next step.</summary>
internal readonly struct PhysicsCommand
{
    public PhysicsCommand(PhysicsCommandKind kind, int key, Vector3F value, Vector3<double> point,
        BodyRequest body = null, JointRequest joint = default)
    {
        Kind = kind;
        Key = key;
        Value = value;
        Point = point;
        Body = body;
        Joint = joint;
    }

    public PhysicsCommandKind Kind { get; }

    /// <summary>The body asked about, as the service numbers them.</summary>
    public int Key { get; }

    public Vector3F Value { get; }

    /// <summary>Where an impulse strikes, in the world.</summary>
    public Vector3<double> Point { get; }

    /// <summary>The body to add, or to put in place of the key's one.</summary>
    public BodyRequest Body { get; }

    /// <summary>The joint to add under the key, a joint's own key.</summary>
    public JointRequest Joint { get; }
}
