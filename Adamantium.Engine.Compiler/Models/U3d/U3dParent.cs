namespace Adamantium.Engine.Compiler.Models.U3d;

/// <summary>A parent of a node and where the node stands relative to it, as a row-vector matrix: the file's column
/// major matrix of column vectors read element by element.</summary>
internal sealed class U3dParent
{
    internal string Name { get; init; }

    internal Matrix4x4F Transform { get; init; }
}
