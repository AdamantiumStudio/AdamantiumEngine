using System.Linq;
using Adamantium.Mathematics;

namespace Adamantium.Engine.Compiler.Models.Prc;

/// <summary>The fields of a highly compressed tessellation as the file holds them, before its mesh is rebuilt.</summary>
internal sealed class PrcCompressedTessellationData
{
    internal bool IsCalculated { get; init; }

    internal bool HasFaces { get; init; }

    /// <summary>The step the point coordinates are integer multiples of.</summary>
    internal double Tolerance { get; init; }

    /// <summary>The point the first point's coordinates are measured from.</summary>
    internal Vector3<double> Origin { get; set; }

    /// <summary>Three integers for each point stored, in the coordinate system the traversal builds for it.</summary>
    internal int[] Points { get; set; } = [];

    /// <summary>Two bits a triangle, in traversal order: 1 when it has a right neighbor, 2 a left one.</summary>
    internal int[] EdgeStatus { get; set; } = [];

    /// <summary>The face of each triangle, in traversal order.</summary>
    internal int[] TriangleFaces { get; set; } = [];

    /// <summary>For each vertex the traversal meets, whether it is a point met before.</summary>
    internal bool[] PointIsReference { get; set; } = [];

    /// <summary>For each vertex met before, the index of its point, points numbered as they are stored.</summary>
    internal int[] PointReferences { get; set; } = [];

    internal bool MustRecalculateNormals { get; set; }

    /// <summary>One flag a triangle when normals are recalculated: whether its winding is the traversal's.</summary>
    internal bool[] NormalIsReversed { get; set; } = [];

    internal double CreaseAngle { get; set; }

    internal int NormalAngleBits { get; set; }

    internal bool[] NormalBinaryData { get; set; } = [];

    internal int[] NormalAngles { get; set; } = [];

    internal bool[] IsFacePlanar { get; set; } = [];

    /// <summary>For each face, whether its vertices have colors; null when no face has.</summary>
    internal bool[] IsPointColorOnFace { get; set; }

    /// <summary>Five characters a colored vertex: whether it has alpha, then red, green, blue and alpha.</summary>
    internal int[] PointColors { get; set; } = [];

    /// <summary>For each face, whether each of its triangles has a line attribute of its own; null when no face has.</summary>
    internal bool[] IsMultipleLineAttributeOnFace { get; set; }

    /// <summary>Styles of the globals, plus one: one a face, or one a triangle of a face of several.</summary>
    internal int[] LineAttributes { get; set; } = [];

    /// <summary>The texture coordinates; null for none.</summary>
    internal PrcCompressedTexture Texture { get; set; }

    internal bool[] FaceHasTexture { get; set; }

    internal int[] Behaviors { get; set; } = [];

    /// <summary>One more than the highest face a triangle names.</summary>
    internal int FaceCount => TriangleFaces.Length == 0 ? 0 : TriangleFaces.Max() + 1;
}
