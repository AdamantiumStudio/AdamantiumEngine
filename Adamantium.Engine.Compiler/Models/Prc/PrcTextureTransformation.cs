using Adamantium.Mathematics;

namespace Adamantium.Engine.Compiler.Models.Prc;

/// <summary>A texture transformation (PRC_TYPE_GRAPH_TextureTransformation): a 2D transformation of the texture
/// coordinates, which s and t it inverts first.</summary>
internal sealed class PrcTextureTransformation
{
    internal bool FlipS { get; init; }

    internal bool FlipT { get; init; }

    /// <summary>The 2D transformation in a row-vector matrix: s and t axes in its first two rows, the translation in its
    /// last, the homogeneous part in its last column; (s, t, 0, 1) times it.</summary>
    internal Matrix4x4<double> Matrix { get; init; } = Matrix4x4<double>.Identity;
}
