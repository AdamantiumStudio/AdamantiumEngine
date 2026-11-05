using System;
using System.Runtime.InteropServices;
using Adamantium.Mathematics;

namespace Adamantium.Graphics.Core.Vertices;

/// <summary>A vertex of the UI's geometry: where it is, in 3D, and nothing else - a fill's brush maps itself from the
/// shape's box, not from the vertex. 12 bytes.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct UIVertex : IEquatable<UIVertex>
{
    public UIVertex(Vector3F position)
    {
        Position = position;
    }

    [VertexInputElement("SV_POSITION")] public Vector3F Position;

    public bool Equals(UIVertex other) => Position.Equals(other.Position);

    public override bool Equals(object obj) => obj is UIVertex other && Equals(other);

    public override int GetHashCode() => Position.GetHashCode();
}
