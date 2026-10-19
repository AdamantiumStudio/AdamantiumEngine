namespace Adamantium.Graphics.Core.Models;

/// <summary>
/// Bridges the rich behavioral <see cref="Mesh"/> class to a flat serializable <see cref="MeshGeometry"/>
/// snapshot using only Mesh's public API (getters + Set* methods). This keeps <see cref="Mesh"/> itself
/// free of any serialization concern.
/// </summary>
public static class MeshSerializationExtensions
{
    public static MeshGeometry ToGeometry(this Mesh mesh)
    {
        return new MeshGeometry
        {
            Name = mesh.Name,
            MeshTopology = mesh.MeshTopology,
            UpAxis = mesh.UpAxis,
            MaterialID = mesh.MaterialID,
            Points = mesh.Points,
            Indices = mesh.Indices,
            Normals = mesh.Normals,
            UV0 = mesh.UV0,
            UV1 = mesh.UV1,
            UV2 = mesh.UV2,
            UV3 = mesh.UV3,
            Tangents = mesh.Tangents,
            BiTangents = mesh.BiTangents,
            Colors = mesh.Colors,
            JointIndices = mesh.JointIndices,
            JointWeights = mesh.JointWeights
        };
    }

    public static Mesh ToMesh(this MeshGeometry geometry)
    {
        var mesh = new Mesh(geometry.MeshTopology)
        {
            Name = geometry.Name,
            MaterialID = geometry.MaterialID,
            UpAxis = geometry.UpAxis
        };

        if (geometry.Points is { Length: > 0 }) mesh.SetPoints(geometry.Points);
        if (geometry.Indices is { Length: > 0 }) mesh.SetIndices(geometry.Indices);
        if (geometry.Normals is { Length: > 0 }) mesh.SetNormals(geometry.Normals);
        if (geometry.Colors is { Length: > 0 }) mesh.SetColors(geometry.Colors);
        if (geometry.UV0 is { Length: > 0 }) mesh.SetUVs(0, geometry.UV0);
        if (geometry.UV1 is { Length: > 0 }) mesh.SetUVs(1, geometry.UV1);
        if (geometry.UV2 is { Length: > 0 }) mesh.SetUVs(2, geometry.UV2);
        if (geometry.UV3 is { Length: > 0 }) mesh.SetUVs(3, geometry.UV3);
        if (geometry.Tangents is { Length: > 0 } && geometry.BiTangents is { Length: > 0 })
            mesh.SetTangentsAndBiTangents(geometry.Tangents, geometry.BiTangents);
        else if (geometry.Tangents is { Length: > 0 })
            mesh.SetTangents(geometry.Tangents);
        if (geometry.JointIndices is { Length: > 0 }) mesh.SetJointIndices(geometry.JointIndices);
        if (geometry.JointWeights is { Length: > 0 }) mesh.SetJointWeights(geometry.JointWeights);

        return mesh;
    }
}
