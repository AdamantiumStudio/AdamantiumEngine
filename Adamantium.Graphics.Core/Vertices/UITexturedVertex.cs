using System;
using System.Runtime.InteropServices;
using Adamantium.Mathematics;

namespace Adamantium.Graphics.Core.Vertices;

/// <summary>A vertex of a UI picture: where it is and where it samples its texture - a picture names the part of its
/// source it shows. 20 bytes.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct UITexturedVertex : IEquatable<UITexturedVertex>
{
    public UITexturedVertex(Vector3F position, Vector2F uv)
    {
        Position = position;
        UV = uv;
    }

    [VertexInputElement("SV_POSITION")] public Vector3F Position;

    [VertexInputElement("TEXCOORD0")] public Vector2F UV;

    public bool Equals(UITexturedVertex other) => Position.Equals(other.Position) && UV.Equals(other.UV);

    public override bool Equals(object obj) => obj is UITexturedVertex other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Position, UV);
}
