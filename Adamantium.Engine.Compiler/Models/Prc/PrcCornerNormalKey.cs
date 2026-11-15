using System;
using Adamantium.Mathematics;

namespace Adamantium.Engine.Compiler.Models.Prc;

/// <summary>A vertex of a mesh with one of the normals its corners take: what tells the vertices it splits into.</summary>
internal readonly struct PrcCornerNormalKey : IEquatable<PrcCornerNormalKey>
{
    internal PrcCornerNormalKey(int vertex, Vector3F normal)
    {
        Vertex = vertex;
        Normal = normal;
    }

    internal int Vertex { get; }

    internal Vector3F Normal { get; }

    public bool Equals(PrcCornerNormalKey other) =>
        Vertex == other.Vertex && Normal.X.Equals(other.Normal.X) && Normal.Y.Equals(other.Normal.Y) && Normal.Z.Equals(other.Normal.Z);

    public override bool Equals(object other) => other is PrcCornerNormalKey key && Equals(key);

    public override int GetHashCode() => HashCode.Combine(Vertex, Normal.X, Normal.Y, Normal.Z);
}
