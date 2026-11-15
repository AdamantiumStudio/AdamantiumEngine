using System;

namespace Adamantium.Engine.Compiler.Models.Prc;

/// <summary>What makes a vertex of a PRC mesh: the indices of its point, normal and texture coordinates, and for a
/// vertex colored on its own, which corner it is.</summary>
internal readonly struct PrcVertexKey : IEquatable<PrcVertexKey>
{
    internal PrcVertexKey(long point, long normal, long texture, long corner)
    {
        Point = point;
        Normal = normal;
        Texture = texture;
        Corner = corner;
    }

    internal long Point { get; }

    internal long Normal { get; }

    internal long Texture { get; }

    internal long Corner { get; }

    public bool Equals(PrcVertexKey other) =>
        Point == other.Point && Normal == other.Normal && Texture == other.Texture && Corner == other.Corner;

    public override bool Equals(object other) => other is PrcVertexKey key && Equals(key);

    public override int GetHashCode() => HashCode.Combine(Point, Normal, Texture, Corner);
}
