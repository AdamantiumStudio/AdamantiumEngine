using System.Collections.Generic;

namespace Adamantium.Engine.CompilerTests;

/// <summary>A CLOD mesh for the tests, written whole as its base mesh.</summary>
internal sealed class U3dTestMesh
{
    public string Name { get; init; }

    public List<Vector3F> Positions { get; init; } = [];

    public List<Vector3F> Normals { get; init; } = [];

    public List<Vector4F> DiffuseColors { get; init; } = [];

    public List<Vector4F> TextureCoordinates { get; init; } = [];

    public List<U3dTestFace> Faces { get; init; } = [];

    /// <summary>One entry a shading ID: its attributes, 0x1 diffuse colors.</summary>
    public List<uint> ShadingAttributes { get; init; } = [0];

    /// <summary>One entry a shading ID: how many texture layers it has.</summary>
    public List<int> ShadingLayers { get; init; } = [0];

    public bool ExcludeNormals { get; init; }
}
