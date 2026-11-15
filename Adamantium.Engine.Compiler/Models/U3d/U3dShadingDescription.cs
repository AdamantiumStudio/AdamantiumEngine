namespace Adamantium.Engine.Compiler.Models.U3d;

/// <summary>Which per-vertex attributes beside the position and the normal a shading ID uses (ECMA-363 9.6.1.1.3.9).</summary>
internal sealed class U3dShadingDescription
{
    internal uint Attributes { get; init; }

    /// <summary>The dimensions of each texture layer's coordinates, one entry a layer.</summary>
    internal uint[] TextureCoordinateDimensions { get; init; } = [];

    internal bool HasDiffuseColors => (Attributes & 0x1) != 0;

    internal bool HasSpecularColors => (Attributes & 0x2) != 0;

    internal int TextureLayerCount => TextureCoordinateDimensions.Length;
}
