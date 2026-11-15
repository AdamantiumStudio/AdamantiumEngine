using System;
using System.Collections.Generic;
using Adamantium.Engine.Compiler.Models.U3d;

namespace Adamantium.Engine.Compiler.Models.Converters;

/// <summary>Corners alike in every index are one vertex.</summary>
internal sealed class U3dCornerComparer : IEqualityComparer<U3dCorner>
{
    public bool Equals(U3dCorner left, U3dCorner right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        return left != null && right != null && left.Position == right.Position && left.Normal == right.Normal &&
               left.Diffuse == right.Diffuse && left.Specular == right.Specular &&
               left.TextureCoordinates.AsSpan().SequenceEqual(right.TextureCoordinates);
    }

    public int GetHashCode(U3dCorner corner)
    {
        var hash = new HashCode();
        hash.Add(corner.Position);
        hash.Add(corner.Normal);
        hash.Add(corner.Diffuse);
        hash.Add(corner.Specular);
        foreach (var coordinate in corner.TextureCoordinates)
        {
            hash.Add(coordinate);
        }

        return hash.ToHashCode();
    }
}
