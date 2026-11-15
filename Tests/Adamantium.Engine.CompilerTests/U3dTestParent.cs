namespace Adamantium.Engine.CompilerTests;

/// <summary>A parent of a test node and where the node stands relative to it.</summary>
internal sealed class U3dTestParent
{
    public string Name { get; init; } = string.Empty;

    public Matrix4x4F Transform { get; init; } = Matrix4x4F.Identity;
}
