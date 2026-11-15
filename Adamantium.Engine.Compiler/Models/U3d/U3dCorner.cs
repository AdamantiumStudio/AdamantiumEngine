namespace Adamantium.Engine.Compiler.Models.U3d;

/// <summary>A corner of a face, an end of a line or a point: an index into each of the geometry's pools, -1 for none.</summary>
internal sealed class U3dCorner
{
    internal int Position { get; set; }

    internal int Normal { get; set; } = -1;

    internal int Diffuse { get; set; } = -1;

    internal int Specular { get; set; } = -1;

    /// <summary>One entry a texture layer of the corner's shading ID.</summary>
    internal int[] TextureCoordinates { get; set; } = [];
}
