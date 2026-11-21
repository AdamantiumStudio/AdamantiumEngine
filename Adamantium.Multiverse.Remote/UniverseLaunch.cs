namespace Adamantium.Multiverse.Remote;

/// <summary>What a universe host plays: a scene, the folders of its content, the game's assemblies - loaded by path, the
/// PDB beside each, in the order they depend on each other - and the size it draws at first.</summary>
public sealed class UniverseLaunch
{
    public string ScenePath { get; set; }

    /// <summary>The game project's physics file (<c>.aphysics</c>): its layers, gravity and steps a second; null for the
    /// defaults.</summary>
    public string ProjectPhysicsPath { get; set; }

    public string[] ContentFolders { get; set; } = [];

    public string[] CodeAssemblies { get; set; } = [];

    public uint Width { get; set; } = 1280;

    public uint Height { get; set; } = 720;

    public FrameSharing FrameSharing { get; set; } = FrameSharing.Auto;

    /// <summary>The GPU the viewer draws on (<c>GraphicsAdapter.DeviceUuid</c>); the host shares GPU memory only with
    /// the same.</summary>
    public Guid ViewerDeviceUuid { get; set; }
}
