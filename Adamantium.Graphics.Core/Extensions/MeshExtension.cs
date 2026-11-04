using Adamantium.Graphics.Core.Models;
using Adamantium.Graphics.Core.Vertices;

namespace Adamantium.Graphics.Core.Extensions;

public static class MeshExtension
{
    public static MeshVertex[] ToMeshVertices(this Mesh mesh)
    {
        if (!mesh.Semantic.HasFlag(VertexSemantic.Position))
        {
            return null;
        }

        int length = mesh.Points.Length;

        var normals = mesh.Semantic & VertexSemantic.Normal;
        var colors = mesh.Semantic.HasFlag(VertexSemantic.Color);
        var texcoords0 = mesh.Semantic & VertexSemantic.UV0;
        var texcoords1 = mesh.Semantic & VertexSemantic.UV1;
        var texcoords2 = mesh.Semantic & VertexSemantic.UV2;
        var texcoords3 = mesh.Semantic & VertexSemantic.UV3;
        var tanBitan = mesh.Semantic & VertexSemantic.TangentBiNormal;

        var vertices = new MeshVertex[length];

        for (int i = 0; i < length; i++)
        {
            var vertex = new MeshVertex() { Position = mesh.Points[i].As<float>() };
            if (normals > 0)
            {
                vertex.Normal = mesh.Normals[i];
            }
            if (texcoords0 > 0)
            {
                vertex.UV0 = mesh.UV0[i];
            }
            if (texcoords1 > 0)
            {
                vertex.UV1 = mesh.UV1[i];
            }
            if (texcoords2 > 0)
            {
                vertex.UV2 = mesh.UV2[i];
            }
            if (texcoords3 > 0)
            {
                vertex.UV3 = mesh.UV3[i];
            }

            if (tanBitan > 0)
            {
                vertex.Tangent = mesh.Tangents[i];
                vertex.BiTangent = mesh.BiTangents[i];
            }

            if (colors)
            {
                vertex.Color = mesh.Colors[i].ToVector4();
            }

            vertices[i] = vertex;
        }

        return vertices;
    }

    public static SkinnedMeshVertex[] ToSkinnedMeshVertices(this Mesh mesh)
    {
        if (!mesh.Semantic.HasFlag(VertexSemantic.Position))
        {
            return null;
        }

        int length = mesh.Points.Length;

        var normals = mesh.Semantic.HasFlag(VertexSemantic.Normal);
        var texcoords0 = mesh.Semantic.HasFlag(VertexSemantic.UV0);
        var texcoords1 = mesh.Semantic.HasFlag(VertexSemantic.UV1);
        var texcoords2 = mesh.Semantic.HasFlag(VertexSemantic.UV2);
        var texcoords3 = mesh.Semantic.HasFlag(VertexSemantic.UV3);
        var tanBitan = mesh.Semantic.HasFlag(VertexSemantic.TangentBiNormal);

        var jointindices = mesh.Semantic.HasFlag(VertexSemantic.JointIndices);
        var jointWeights = mesh.Semantic.HasFlag(VertexSemantic.JointWeights);

        var vertices = new SkinnedMeshVertex[length];

        for (int i = 0; i < length; ++i)
        {
            var vertex = new SkinnedMeshVertex() { Position = mesh.Points[i].As<float>() };
            if (normals)
            {
                vertex.Normal = mesh.Normals[i];
            }
            if (texcoords0)
            {
                vertex.UV0 = mesh.UV0[i];
            }
            if (texcoords1)
            {
                vertex.UV1 = mesh.UV1[i];
            }
            if (texcoords2)
            {
                vertex.UV2 = mesh.UV2[i];
            }
            if (texcoords3)
            {
                vertex.UV3 = mesh.UV3[i];
            }

            if (tanBitan)
            {
                vertex.Tangent = mesh.Tangents[i];
                vertex.BiTangent = mesh.BiTangents[i];
            }

            if (jointindices && jointWeights)
            {
                vertex.JointIndices = mesh.JointIndices[i];
                vertex.JointWeights = mesh.JointWeights[i];
            }
            vertices[i] = vertex;
        }

        return vertices;
    }
    
    /// <summary>The mesh's points on the plane, as the UI's flat geometry draws them; null for a mesh without points.</summary>
    public static UIVertex[] ToUIVertices(this Mesh mesh)
    {
        if (!mesh.Semantic.HasFlag(VertexSemantic.Position))
        {
            return null;
        }

        var points = mesh.Points;
        var vertices = new UIVertex[points.Length];
        for (var i = 0; i < points.Length; i++)
        {
            vertices[i] = new UIVertex(new Vector2F((float)points[i].X, (float)points[i].Y));
        }

        return vertices;
    }

    /// <summary>The mesh's points on the plane with its first texture coordinates, as a UI picture draws them - zero
    /// where it has none; null for a mesh without points.</summary>
    public static UITexturedVertex[] ToUITexturedVertices(this Mesh mesh)
    {
        if (!mesh.Semantic.HasFlag(VertexSemantic.Position))
        {
            return null;
        }

        var points = mesh.Points;
        var uvs = mesh.Semantic.HasFlag(VertexSemantic.UV0) ? mesh.UV0 : null;
        var vertices = new UITexturedVertex[points.Length];
        for (var i = 0; i < points.Length; i++)
        {
            vertices[i] = new UITexturedVertex(new Vector2F((float)points[i].X, (float)points[i].Y),
                uvs != null && i < uvs.Length ? uvs[i] : default);
        }

        return vertices;
    }
}