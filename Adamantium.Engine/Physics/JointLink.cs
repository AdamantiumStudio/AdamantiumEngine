namespace Adamantium.Engine.Physics;

/// <summary>A joint the physics holds, as the game's side keeps it: its key and the request last sent for it.</summary>
internal sealed class JointLink
{
    public JointLink(int key)
    {
        Key = key;
    }

    public int Key { get; }

    /// <summary>What the physics was last asked for, null for nothing; a joint asking for other is asked for anew.
    /// </summary>
    public JointRequest? Sent { get; set; }

    /// <summary>The number of the last look over the scene's joints that found this one.</summary>
    public long SeenInLook { get; set; }
}
