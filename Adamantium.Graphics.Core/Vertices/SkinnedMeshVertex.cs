using System;
using System.Runtime.InteropServices;
using Adamantium.Mathematics;

namespace Adamantium.Graphics.Core.Vertices;

/// <summary>
/// Vertex struct containing position, color, normal, texture, tangent, butangent, jointIndices and JointWeights
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct SkinnedMeshVertex : IEquatable<SkinnedMeshVertex>
{
    /// <summary>
    /// Initializes a new <see cref="SkinnedMeshVertex"/> instance.
    /// </summary>
    /// <param name="position">The position of this vertex.</param>
    /// <param name="color">The vertex color.</param>
    /// <param name="normal">The vertex normal.</param>
    /// <param name="uv0">UV0 texture coordinates.</param>
    /// <param name="uv1">UV1 texture coordinates.</param>
    /// <param name="uv2">UV2 texture coordinates.</param>
    /// <param name="uv3">UV3 texture coordinates.</param>
    /// <param name="tangent">tangent.</param>
    /// <param name="bitangent">tangent.</param>
    /// <param name="jointIndices">Joint indices.</param>
    /// <param name="jointWeights">Joint weights.</param>
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
        Vector4F jointWeights) : this()
    {
        Position = position;
        Normal = normal;
        UV0 = uv0;
        UV1 = uv1;
        UV2 = uv2;
        UV3 = uv3;
        Color = color.ToVector4();
        Tangent = tangent;
        BiTangent = bitangent;
        JointIndices = jointIndices;
        JointWeights = jointWeights;
    }

    /// <summary>
    /// XYZ position.
    /// </summary>
    [VertexInputElement("SV_Position")] public Vector3F Position;

    /// <summary>
    /// The vertex color.
    /// </summary>
    [VertexInputElement("COLOR")] public Vector4F Color;

    /// <summary>
    /// The vertex normal.
    /// </summary>
    [VertexInputElement("NORMAL")] public Vector3F Normal;

    /// <summary>
    /// UV texture coordinates.
    /// </summary>
    [VertexInputElement("TEXCOORD0")] public Vector2F UV0;

    /// <summary>
    /// UV texture coordinates.
    /// </summary>
    [VertexInputElement("TEXCOORD1")] public Vector2F UV1;

    /// <summary>
    /// UV texture coordinates.
    /// </summary>
    [VertexInputElement("TEXCOORD2")] public Vector2F UV2;

    /// <summary>
    /// UV texture coordinates.
    /// </summary>
    [VertexInputElement("TEXCOORD3")] public Vector2F UV3;

    /// <summary>
    /// Tangent.
    /// </summary>
    [VertexInputElement("TANGENT")] public Vector4F Tangent;

    /// <summary>
    /// Tangent.
    /// </summary>
    [VertexInputElement("BINORMAL")] public Vector3F BiTangent;

    /// <summary>
    /// JointIndices
    /// </summary>
    [VertexInputElement("BLENDINDICES")] public Vector4F JointIndices;

    /// <summary>
    /// JointWeights
    /// </summary>
    [VertexInputElement("BLENDWEIGHT")] public Vector4F JointWeights;

    /// <summary>
    /// Defines structure byte size.
    /// </summary>
    public static readonly int Size = Marshal.SizeOf<SkinnedMeshVertex>();

    public bool Equals(SkinnedMeshVertex other)
    {
        return Position.Equals(other.Position) && Normal.Equals(other.Normal) && UV0.Equals(other.UV0) && UV1.Equals(other.UV1) && UV2.Equals(other.UV2)
               && UV3.Equals(other.UV3) && Color.Equals(other.Color) && Tangent.Equals(other.Tangent) && BiTangent.Equals(other.BiTangent) &&
               JointIndices.Equals(other.JointIndices) && JointWeights.Equals(other.JointWeights);
    }

    public override bool Equals(object obj)
    {
        if (ReferenceEquals(null, obj)) return false;
        return obj is SkinnedMeshVertex && Equals((SkinnedMeshVertex) obj);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            int hashCode = Position.GetHashCode();
            hashCode = (hashCode*397) ^ Normal.GetHashCode();
            hashCode = (hashCode*397) ^ UV0.GetHashCode();
            hashCode = (hashCode * 397) ^ UV1.GetHashCode();
            hashCode = (hashCode * 397) ^ UV2.GetHashCode();
            hashCode = (hashCode * 397) ^ UV3.GetHashCode();
            hashCode = (hashCode*397) ^ Color.GetHashCode();
            hashCode = (hashCode*397) ^ Tangent.GetHashCode();
            hashCode = (hashCode*397) ^ BiTangent.GetHashCode();
            hashCode = (hashCode*397) ^ JointIndices.GetHashCode();
            hashCode = (hashCode*397) ^ JointWeights.GetHashCode();
            return hashCode;
        }
    }

    /// <summary>
    /// Comparing operator for two FullVertex structs
    /// </summary>
    /// <param name="left"></param>
    /// <param name="right"></param>
    /// <returns></returns>
    public static bool operator ==(SkinnedMeshVertex left, SkinnedMeshVertex right)
    {
        return left.Equals(right);
    }

    /// <summary>
    /// Comparing operator for two FullVertex structs
    /// </summary>
    /// <param name="left"></param>
    /// <param name="right"></param>
    /// <returns></returns>
    public static bool operator !=(SkinnedMeshVertex left, SkinnedMeshVertex right)
    {
        return !left.Equals(right);
    }

    /// <summary>
    /// Returns the fully qualified type name of this instance.
    /// </summary>
    /// <returns>
    /// A <see cref="T:System.String"/> containing a fully qualified type name.
    /// </returns>
    /// <filterpriority>2</filterpriority>
    public override string ToString()
    {
        return
            $"Position: {Position}, Normal: {Normal}, UV0: {UV0}, UV1: {UV1}, UV2: {UV2}, UV3: {UV3}, Color: {Color}, Tangent: {Tangent}, BiTangent: {BiTangent}, JointIndices: {JointIndices}, JointWeights: {JointWeights}";
    }
}