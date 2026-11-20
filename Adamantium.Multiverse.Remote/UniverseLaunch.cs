namespace Adamantium.Multiverse.Remote;

/// <summary>What a universe host plays: a scene, the folders of its content, the game's assemblies - loaded by path, the
/// PDB beside each, in the order they depend on each other - and the size it draws at first.</summary>
public sealed class UniverseLaunch
{
    public string ScenePath { get; set; }

    public string[] ContentFolders { get; set; } = [];

    public string[] CodeAssemblies { get; set; } = [];

    public uint Width { get; set; } = 1280;

    public uint Height { get; set; } = 720;
}
