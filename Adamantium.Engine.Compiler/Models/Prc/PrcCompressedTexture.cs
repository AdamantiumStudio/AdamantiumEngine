namespace Adamantium.Engine.Compiler.Models.Prc;

/// <summary>The texture coordinates of a highly compressed tessellation (CompressedTextureParameter).</summary>
internal sealed class PrcCompressedTexture
{
    /// <summary>For each textured vertex the traversal meets, whether its coordinates were stored before.</summary>
    internal bool[] ReferenceFlags { get; set; } = [];

    internal int[] References { get; set; } = [];

    /// <summary>U and v of each stored texture coordinate, in turn.</summary>
    internal float[] Parameters { get; set; } = [];
}
