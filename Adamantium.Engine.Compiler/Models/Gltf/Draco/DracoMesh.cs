// Ported from Draco (src/draco/mesh/mesh.h).
// Copyright 2016 The Draco Authors. Under the Apache License 2.0; translated to C# and changed, details:
// THIRD-PARTY-NOTICES.md.

using System.Collections.Generic;

namespace Adamantium.Engine.Compiler.Models.Gltf.Draco;

/// <summary>A decoded Draco mesh: triangles over its points and the attributes those points carry.</summary>
public sealed class DracoMesh
{
    internal DracoMesh(int[] indices, int pointCount, IReadOnlyList<DracoAttribute> attributes)
    {
        Indices = indices;
        PointCount = pointCount;
        Attributes = attributes;
    }

    /// <summary>Three points for each triangle.</summary>
    public int[] Indices { get; }

    /// <summary>How many points the mesh has.</summary>
    public int PointCount { get; }

    /// <summary>Every attribute, in the order the data has them.</summary>
    public IReadOnlyList<DracoAttribute> Attributes { get; }

    /// <summary>The attribute of <paramref name="uniqueId"/>; null if there is none.</summary>
    public DracoAttribute Attribute(uint uniqueId)
    {
        foreach (var attribute in Attributes)
        {
            if (attribute.UniqueId == uniqueId)
            {
                return attribute;
            }
        }

        return null;
    }
}
