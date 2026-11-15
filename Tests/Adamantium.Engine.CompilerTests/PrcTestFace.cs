using System.Collections.Generic;

namespace Adamantium.Engine.CompilerTests;

/// <summary>A face of a test 3D tessellation: its kinds of triangulation, where they start, their counts.</summary>
internal sealed class PrcTestFace
{
    public uint UsedEntities { get; init; }

    public uint Start { get; init; }

    public uint[] Sizes { get; init; } = [];

    public uint TextureIndexes { get; init; }

    public int Style { get; init; } = -1;

    public List<byte[]> Colors { get; init; }
}
