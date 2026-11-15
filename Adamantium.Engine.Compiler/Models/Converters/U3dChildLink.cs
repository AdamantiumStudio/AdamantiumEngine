using Adamantium.Engine.Compiler.Models.U3d;

namespace Adamantium.Engine.Compiler.Models.Converters;

/// <summary>A node as a child of one of its parents, with where it stands relative to that parent.</summary>
internal sealed class U3dChildLink
{
    internal U3dChildLink(U3dNode node, Matrix4x4F transform)
    {
        Node = node;
        Transform = transform;
    }

    internal U3dNode Node { get; }

    internal Matrix4x4F Transform { get; }
}
