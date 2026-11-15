using System.Collections.Generic;
using Adamantium.Mathematics;

namespace Adamantium.Engine.Compiler.Models.Prc;

/// <summary>A face of a 3D tessellation (PRC_TYPE_TESS_Face): which kinds of triangulation it has, where they start
/// in the triangulated indices and how many there are, and its graphics.</summary>
internal sealed class PrcFaceTessellation
{
    internal uint[] LineAttributes { get; set; } = [];

    internal uint StartWire { get; set; }

    internal uint[] SizesWire { get; set; } = [];

    internal uint UsedEntities { get; set; }

    internal uint StartTriangulated { get; set; }

    internal uint[] SizesTriangulated { get; set; } = [];

    internal uint TextureCoordinateIndexes { get; set; }

    /// <summary>One RGBA color a vertex of its triangulation, in order; null for none.</summary>
    internal List<Color> VertexColors { get; set; }
}
