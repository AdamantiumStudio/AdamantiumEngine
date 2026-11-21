using Adamantium.DarkMatter;

namespace Adamantium.ECS.Physics;

/// <summary>A joint the physics holds, as the service keeps it: the request last sent for it and its joint in the
/// simulation.</summary>
internal sealed class JointLink
{
    /// <summary>What the physics was last asked for, null for nothing; a joint asking for other is asked for anew.
    /// </summary>
    public JointRequest? Sent { get; set; }

    /// <summary>The joint in the simulation the last request made; none for none.</summary>
    public JointId Id { get; set; }

    /// <summary>Whether the log was told the simulation would not take it, so it is told once.</summary>
    public bool ToldOfRefusal { get; set; }

    /// <summary>The number of the last look over the scene's joints that found this one.</summary>
    public long SeenInLook { get; set; }
}
