using System;
using System.Runtime.InteropServices;
using Adamantium.Mathematics;

namespace Adamantium.Graphics.Core.Vertices;

/// <summary>A vertex of the UI's geometry: where it is, its normal and two texture coordinate sets.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct UIVertex : IEquatable<UIVertex>
{
    public UIVertex(
        Vector3F position,
        Vector3F normal,
        Vector2F uv0,
        Vector2F uv1)
    {
        Position = position;
        Normal = normal;
        UV0 = uv0;
        UV1 = uv1;
    }

    [VertexInputElement("SV_POSITION")] public Vector3F Position;

    [VertexInputElement("NORMAL")] public Vector3F Normal;

    [VertexInputElement("TEXCOORD0")] public Vector2F UV0;

    [VertexInputElement("TEXCOORD1")] public Vector2F UV1;

    public bool Equals(UIVertex other)
    {
        return Position.Equals(other.Position) && Normal.Equals(other.Normal) && UV0.Equals(other.UV0) && UV1.Equals(other.UV1);
    }

    public override bool Equals(object obj)
    {
        return obj is UIVertex other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Position, Normal, UV0, UV1);
    }
}