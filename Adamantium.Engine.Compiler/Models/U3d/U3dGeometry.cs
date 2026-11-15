using System.Collections.Generic;

namespace Adamantium.Engine.Compiler.Models.U3d;

/// <summary>A model resource's geometry as the author mesh, point set or line set has it: pools of positions, normals,
/// colors and texture coordinates and the primitives indexing them.</summary>
internal sealed class U3dGeometry
{
    internal U3dGeometryKind Kind { get; init; }

    internal string Name { get; init; }

    /// <summary>A mesh's attributes: 0x1 its faces have no normals.</summary>
    internal uint MeshAttributes { get; set; }

    internal bool ExcludesNormals => Kind == U3dGeometryKind.Mesh && (MeshAttributes & 0x1) != 0;

    internal List<U3dShadingDescription> Shadings { get; } = [];

    internal uint MinimumResolution { get; set; }

    internal uint FinalMaximumResolution { get; set; }

    internal float PositionInverseQuant { get; set; }

    internal float NormalInverseQuant { get; set; }

    internal float TextureCoordinateInverseQuant { get; set; }

    internal float DiffuseColorInverseQuant { get; set; }

    internal float SpecularColorInverseQuant { get; set; }

    internal uint BoneCount { get; set; }

    internal List<Vector3F> Positions { get; } = [];

    internal List<Vector3F> Normals { get; } = [];

    internal List<Vector4F> DiffuseColors { get; } = [];

    internal List<Vector4F> SpecularColors { get; } = [];

    /// <summary>Four coordinates each - u, v, s, t - however many a layer uses.</summary>
    internal List<Vector4F> TextureCoordinates { get; } = [];

    internal List<U3dPrimitive> Primitives { get; } = [];

    /// <summary>The points or line ends at each position, which the next position added is predicted from.</summary>
    internal List<List<U3dCorner>> CornersAtPosition { get; } = [];

    /// <summary>The faces using each position, by their index in <see cref="Primitives"/>: what a mesh's resolution
    /// updates split, move and predict from.</summary>
    internal List<List<int>> FacesAtPosition { get; } = [];

    /// <summary>What the previous new face of a mesh had at each of its corners - split, new, third - which a
    /// duplicate flag of the next one uses again: the diffuse color, the specular color, then each texture layer.</summary>
    internal Dictionary<int, int[]> PreviousNewFaceAttributes { get; } = [];

    /// <summary>The last diffuse color added, which a duplicate flag uses again; -1 before the first.</summary>
    internal int LastDiffuse { get; set; } = -1;

    internal int LastSpecular { get; set; } = -1;

    internal int LastTextureCoordinate { get; set; } = -1;
}
