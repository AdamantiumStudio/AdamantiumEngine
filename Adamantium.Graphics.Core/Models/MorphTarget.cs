using System.Collections.Generic;
using Adamantium.Mathematics;

namespace Adamantium.Graphics.Core.Models;

/// <summary>A shape a mesh can blend toward - a smile, a blink: how far each vertex moves at its full weight. Each
/// array is empty when the target does not move that, or one value a vertex.</summary>
public class MorphTarget
{
    public string Name { get; set; }

    public Vector3F[] Positions { get; set; } = [];

    public Vector3F[] Normals { get; set; } = [];

    public Vector3F[] Tangents { get; set; } = [];

    /// <summary>How the target moves the mesh's other per-vertex data, by the same names as
    /// <see cref="Mesh.Attributes"/>; kept as the source has it.</summary>
    public Dictionary<string, VertexAttribute> Attributes { get; set; } = new();

    public MorphTarget Clone()
    {
        var copy = new MorphTarget
        {
            Name = Name,
            Positions = (Vector3F[])Positions.Clone(),
            Normals = (Vector3F[])Normals.Clone(),
            Tangents = (Vector3F[])Tangents.Clone()
        };
        foreach (var (name, attribute) in Attributes)
        {
            copy.Attributes[name] = attribute.Clone();
        }

        return copy;
    }
}
