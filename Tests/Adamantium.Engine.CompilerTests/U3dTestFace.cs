namespace Adamantium.Engine.CompilerTests;

/// <summary>A face of a test mesh: indices into each pool at its three corners.</summary>
internal sealed class U3dTestFace
{
    public int ShadingId { get; init; }

    public int[] Positions { get; init; }

    public int[] Normals { get; init; }

    public int[] DiffuseColors { get; init; }

    public int[] TextureCoordinates { get; init; }
}
