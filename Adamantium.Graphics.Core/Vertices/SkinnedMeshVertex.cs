using System;
using System.Runtime.InteropServices;
using Adamantium.Mathematics;

namespace Adamantium.Graphics.Core.Vertices;

/// <summary>A vertex of a skinned mesh: a <see cref="MeshVertex"/> with the four joints that carry it and their
/// weights.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct SkinnedMeshVertex : IEquatable<SkinnedMeshVertex>
{
    public SkinnedMeshVertex(
        Vector3F position,
        Vector3F normal,
        Color color,
        Vector2F uv0,
        Vector2F uv1,
        Vector2F uv2,
        Vector2F uv3,
        Vector4F tangent,
        Vector3F bitangent,
        Vector4F jointIndices,
        Vector4F jointWeights)
    {
        Position = position;
        Color = color.ToVector4();
        Normal = normal;
        UV0 = uv0;
        UV1 = uv1;
        UV2 = uv2;
        UV3 = uv3;
        Tangent = tangent;
        BiTangent = bitangent;
        JointIndices = jointIndices;
        JointWeights = jointWeights;
    }

    [VertexInputElement("SV_Position")] public Vector3F Position;

    [VertexInputElement("COLOR")] public Vector4F Color;

    [VertexInputElement("NORMAL")] public Vector3F Normal;

    [VertexInputElement("TEXCOORD0")] public Vector2F UV0;

    [VertexInputElement("TEXCOORD1")] public Vector2F UV1;

    [VertexInputElement("TEXCOORD2")] public Vector2F UV2;

    [VertexInputElement("TEXCOORD3")] public Vector2F UV3;

    /// <summary>The tangent, its W the side the bitangent is on: +1 or -1.</summary>
    [VertexInputElement("TANGENT")] public Vector4F Tangent;

    [VertexInputElement("BINORMAL")] public Vector3F BiTangent;

    /// <summary>The places of the four joints in the skin's palette.</summary>
    [VertexInputElement("BLENDINDICES")] public Vector4F JointIndices;

    /// <summary>How much each of the four joints carries the vertex; they add up to one.</summary>
    [VertexInputElement("BLENDWEIGHT")] public Vector4F JointWeights;

    /// <summary>The size of a vertex in bytes.</summary>
    public static readonly int Size = Marshal.SizeOf<SkinnedMeshVertex>();

    public bool Equals(SkinnedMeshVertex other) =>
        Position.Equals(other.Position) && Color.Equals(other.Color) && Normal.Equals(other.Normal)
        && UV0.Equals(other.UV0) && UV1.Equals(other.UV1) && UV2.Equals(other.UV2) && UV3.Equals(other.UV3)
        && Tangent.Equals(other.Tangent) && BiTangent.Equals(other.BiTangent)
        && JointIndices.Equals(other.JointIndices) && JointWeights.Equals(other.JointWeights);

    public override bool Equals(object obj) => obj is SkinnedMeshVertex other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Position);
        hash.Add(Color);
        hash.Add(Normal);
        hash.Add(UV0);
        hash.Add(UV1);
        hash.Add(UV2);
        hash.Add(UV3);
        hash.Add(Tangent);
        hash.Add(BiTangent);
        hash.Add(JointIndices);
        hash.Add(JointWeights);
        return hash.ToHashCode();
    }

    public static bool operator ==(SkinnedMeshVertex left, SkinnedMeshVertex right) => left.Equals(right);

    public static bool operator !=(SkinnedMeshVertex left, SkinnedMeshVertex right) => !left.Equals(right);

    public override string ToString() =>
        $"Position: {Position}, Color: {Color}, Normal: {Normal}, UV0: {UV0}, UV1: {UV1}, UV2: {UV2}, UV3: {UV3}, " +
        $"Tangent: {Tangent}, BiTangent: {BiTangent}, JointIndices: {JointIndices}, JointWeights: {JointWeights}";
}
