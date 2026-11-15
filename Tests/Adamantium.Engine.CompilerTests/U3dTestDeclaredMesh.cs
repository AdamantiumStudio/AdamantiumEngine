namespace Adamantium.Engine.CompilerTests;

/// <summary>What a CLOD Mesh Declaration says of its mesh.</summary>
internal sealed class U3dTestDeclaredMesh
{
    public uint Attributes { get; init; }

    public uint FaceCount { get; init; }

    public uint PositionCount { get; set; }

    public uint MinimumResolution { get; set; }

    public uint FinalMaximumResolution { get; set; }
}
