using System;
using System.Runtime.InteropServices;
using Adamantium.Mathematics;

namespace Adamantium.Graphics.Core.Vertices;

/// <summary>A vertex of a mesh drawn unskinned: where it is, its color and normal, four texture coordinate sets and the
/// tangent frame for normal maps.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct MeshVertex : IEquatable<MeshVertex>
{
    public MeshVertex(
        Vector3F position,
        Vector3F normal,
        Color color,
        Vector2F uv0,
        Vector2F uv1,
        Vector2F uv2,
        Vector2F uv3,
        Vector4F tangent,
        Vector3F bitangent)
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
    }

    [VertexInputElement("SV_POSITION")] public Vector3F Position;

    [VertexInputElement("COLOR")] public Vector4F Color;

    [VertexInputElement("NORMAL")] public Vector3F Normal;

    [VertexInputElement("TEXCOORD0")] public Vector2F UV0;

    [VertexInputElement("TEXCOORD1")] public Vector2F UV1;

    [VertexInputElement("TEXCOORD2")] public Vector2F UV2;

    [VertexInputElement("TEXCOORD3")] public Vector2F UV3;

    /// <summary>The tangent, its W the side the bitangent is on: +1 or -1.</summary>
    [VertexInputElement("TANGENT")] public Vector4F Tangent;

    [VertexInputElement("BINORMAL")] public Vector3F BiTangent;

    /// <summary>The size of a vertex in bytes.</summary>
    public static readonly int Size = Marshal.SizeOf<MeshVertex>();

    public bool Equals(MeshVertex other) =>
        Position.Equals(other.Position) && Color.Equals(other.Color) && Normal.Equals(other.Normal)
        && UV0.Equals(other.UV0) && UV1.Equals(other.UV1) && UV2.Equals(other.UV2) && UV3.Equals(other.UV3)
        && Tangent.Equals(other.Tangent) && BiTangent.Equals(other.BiTangent);

    public override bool Equals(object obj) => obj is MeshVertex other && Equals(other);

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
        return hash.ToHashCode();
    }

    public static bool operator ==(MeshVertex left, MeshVertex right) => left.Equals(right);

    public static bool operator !=(MeshVertex left, MeshVertex right) => !left.Equals(right);

    public override string ToString() =>
        $"Position: {Position}, Color: {Color}, Normal: {Normal}, UV0: {UV0}, UV1: {UV1}, UV2: {UV2}, UV3: {UV3}, " +
        $"Tangent: {Tangent}, BiTangent: {BiTangent}";
}
