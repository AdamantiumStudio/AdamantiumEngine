using System;
using System.Runtime.InteropServices;
using Adamantium.Mathematics;

namespace Adamantium.Graphics.Core.Vertices;

/// <summary>A vertex of the UI's flat geometry: where it is and nothing else - a fill's brush maps itself from the shape's
/// box, not from the vertex. 8 bytes.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct UIVertex : IEquatable<UIVertex>
{
    public UIVertex(Vector2F position)
    {
        Position = position;
    }

    [VertexInputElement("SV_POSITION")] public Vector2F Position;

    public bool Equals(UIVertex other) => Position.Equals(other.Position);

    public override bool Equals(object obj) => obj is UIVertex other && Equals(other);

    public override int GetHashCode() => Position.GetHashCode();
}
