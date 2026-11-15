using System.Collections.Generic;

namespace Adamantium.Engine.Compiler.Models.U3d;

/// <summary>A node of the node palette (ECMA-363 9.5).</summary>
internal sealed class U3dNode
{
    internal U3dNodeKind Kind { get; init; }

    internal string Name { get; init; }

    internal List<U3dParent> Parents { get; } = [];

    /// <summary>The model, light or view resource; null for a group.</summary>
    internal string ResourceName { get; set; }

    /// <summary>A model node's visibility: 0 none, 1 front, 2 back, 3 both.</summary>
    internal uint Visibility { get; set; } = 1;

    /// <summary>A view node's own fields; null for other nodes.</summary>
    internal U3dViewNodeParameters View { get; set; }

    internal IReadOnlyList<U3dMetaDataEntry> MetaData { get; init; }
}
