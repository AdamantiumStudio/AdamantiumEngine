using System;
using System.Runtime.InteropServices;
using Adamantium.Vulkan.Core;

namespace Adamantium.Graphics.Core;

/// <summary>How vertices are joined into primitives - a Vulkan <see cref="PrimitiveTopology"/>, to and from which it
/// converts.</summary>
[StructLayout(LayoutKind.Sequential, Size = 4)]
public readonly struct PrimitiveType : IEquatable<PrimitiveType>
{
    private readonly PrimitiveTopology _topology;

    private PrimitiveType(PrimitiveTopology type)
    {
        _topology = type;
    }

    public static readonly PrimitiveType Undefined = new PrimitiveType((PrimitiveTopology)(-1)); 
        
    public static readonly PrimitiveType PointList = new PrimitiveType(PrimitiveTopology.PointList);
    public static readonly PrimitiveType LineList = new PrimitiveType(PrimitiveTopology.LineList);
    public static readonly PrimitiveType LineStrip = new PrimitiveType(PrimitiveTopology.LineStrip);
    public static readonly PrimitiveType TriangleList = new PrimitiveType(PrimitiveTopology.TriangleList);
    public static readonly PrimitiveType TriangleFan = new PrimitiveType(PrimitiveTopology.TriangleFan);
    public static readonly PrimitiveType TriangleStrip = new PrimitiveType(PrimitiveTopology.TriangleStrip);
    public static readonly PrimitiveType LineListWithAdjacency = new PrimitiveType(PrimitiveTopology.LineListWithAdjacency);
    public static readonly PrimitiveType LineStripWithAdjacency = new PrimitiveType(PrimitiveTopology.LineStripWithAdjacency);
    public static readonly PrimitiveType TriangleListWithAdjacency = new PrimitiveType(PrimitiveTopology.TriangleListWithAdjacency);
    public static readonly PrimitiveType TriangleStripWithAdjacency = new PrimitiveType(PrimitiveTopology.TriangleStripWithAdjacency);
    public static readonly PrimitiveType PatchList = new PrimitiveType(PrimitiveTopology.PatchList);

    public static implicit operator PrimitiveTopology(PrimitiveType from) => from._topology;
    public static implicit operator PrimitiveType(PrimitiveTopology from) => new PrimitiveType(from);

    public bool Equals(PrimitiveType other) => _topology == other._topology;

    public override bool Equals(object obj) => obj is PrimitiveType other && Equals(other);

    public override int GetHashCode() => (int)_topology;

    public static bool operator ==(PrimitiveType left, PrimitiveType right) => left.Equals(right);
    public static bool operator !=(PrimitiveType left, PrimitiveType right) => !left.Equals(right);

    public override string ToString() => _topology.ToString();
}