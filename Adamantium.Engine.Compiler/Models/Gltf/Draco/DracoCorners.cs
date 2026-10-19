// Ported from Draco (src/draco/mesh/corner_table.h, corner_table_iterators.h).
// Copyright 2016 The Draco Authors. Under the Apache License 2.0; translated to C# and changed, details:
// THIRD-PARTY-NOTICES.md.

using System.Collections.Generic;

namespace Adamantium.Engine.Compiler.Models.Gltf.Draco;

internal abstract class DracoCorners
{
    public const int Invalid = -1;

    public abstract int VertexCount { get; }

    public abstract int CornerCount { get; }

    public int FaceCount => CornerCount / 3;

    public abstract int Opposite(int corner);

    public abstract int Vertex(int corner);

    public abstract int LeftMostCorner(int vertex);

    public static int Next(int corner)
    {
        if (corner < 0)
        {
            return Invalid;
        }

        return corner % 3 == 2 ? corner - 2 : corner + 1;
    }

    public static int Previous(int corner)
    {
        if (corner < 0)
        {
            return Invalid;
        }

        return corner % 3 == 0 ? corner + 2 : corner - 1;
    }

    public int SwingRight(int corner) => Previous(Opposite(Previous(corner)));

    public int SwingLeft(int corner) => Next(Opposite(Next(corner)));

    public int LeftCorner(int corner) => Opposite(Previous(corner));

    public int RightCorner(int corner) => Opposite(Next(corner));

    public bool IsOnBoundary(int vertex) => SwingLeft(LeftMostCorner(vertex)) == Invalid;

    public IEnumerable<int> CornersAround(int start)
    {
        var corner = start;
        var left = true;
        while (corner != Invalid)
        {
            yield return corner;
            if (!left)
            {
                corner = SwingRight(corner);
                continue;
            }

            corner = SwingLeft(corner);
            if (corner == Invalid)
            {
                corner = SwingRight(start);
                left = false;
            }
            else if (corner == start)
            {
                corner = Invalid;
            }
        }
    }
}
