using System.Collections.Generic;

namespace Adamantium.Engine.Compiler.Models.Prc;

/// <summary>A regular 3D tessellation (PRC_TYPE_TESS_3D): points, normals, the triangulated indices of its faces.</summary>
internal sealed class Prc3DTessellation : PrcTessellation
{
    internal bool HasFaces { get; set; }

    internal bool MustRecalculateNormals { get; set; }

    /// <summary>When normals are recalculated, the angle between two triangles, in degrees, up to which they share
    /// their normals.</summary>
    internal double CreaseAngle { get; set; }

    internal double[] Normals { get; set; } = [];

    internal uint[] WireIndices { get; set; } = [];

    internal uint[] TriangulatedIndices { get; set; } = [];

    internal List<PrcFaceTessellation> Faces { get; } = [];

    internal double[] TextureCoordinates { get; set; } = [];
}
