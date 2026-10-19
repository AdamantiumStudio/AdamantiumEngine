// Ported from Draco (src/draco/mesh/mesh_attribute_corner_table.cc).
// Copyright 2016 The Draco Authors. Under the Apache License 2.0; translated to C# and changed, details:
// THIRD-PARTY-NOTICES.md.

using System;
using System.Collections.Generic;
using System.IO;

namespace Adamantium.Engine.Compiler.Models.Gltf.Draco;

internal sealed class DracoSeamCornerTable : DracoCorners
{
    private readonly DracoCornerTable table;
    private readonly bool[] edgeOnSeam;
    private readonly bool[] vertexOnSeam;
    private readonly int[] cornerToVertex;
    private readonly List<int> leftMostCorners = [];

    public DracoSeamCornerTable(DracoCornerTable table)
    {
        this.table = table;
        edgeOnSeam = new bool[table.CornerCount];
        vertexOnSeam = new bool[table.VertexCount];
        cornerToVertex = new int[table.CornerCount];
        Array.Fill(cornerToVertex, Invalid);
    }

    public override int VertexCount => leftMostCorners.Count;

    public override int CornerCount => table.CornerCount;

    public override int Opposite(int corner) => corner < 0 || edgeOnSeam[corner] ? Invalid : table.Opposite(corner);

    public override int Vertex(int corner) => corner < 0 ? Invalid : cornerToVertex[corner];

    public override int LeftMostCorner(int vertex) => vertex < 0 ? Invalid : leftMostCorners[vertex];

    public bool IsCornerOnSeam(int corner) => vertexOnSeam[table.Vertex(corner)];

    public void AddSeamEdge(int corner)
    {
        MarkSeam(corner);
        var opposite = table.Opposite(corner);
        if (opposite != Invalid)
        {
            MarkSeam(opposite);
        }
    }

    public void RecomputeVertices()
    {
        leftMostCorners.Clear();
        for (var v = 0; v < table.VertexCount; v++)
        {
            var start = table.LeftMostCorner(v);
            if (start == Invalid)
            {
                continue;
            }

            var vertex = leftMostCorners.Count;
            var first = start;
            if (vertexOnSeam[v])
            {
                var corner = SwingLeft(first);
                while (corner != Invalid)
                {
                    first = corner;
                    corner = SwingLeft(corner);
                    if (corner == start)
                    {
                        throw new InvalidDataException("a Draco attribute seam goes round a vertex");
                    }
                }
            }

            cornerToVertex[first] = vertex;
            leftMostCorners.Add(first);
            var act = table.SwingRight(first);
            while (act != Invalid && act != first)
            {
                if (edgeOnSeam[Next(act)])
                {
                    vertex = leftMostCorners.Count;
                    leftMostCorners.Add(act);
                }

                cornerToVertex[act] = vertex;
                act = table.SwingRight(act);
            }
        }
    }

    private void MarkSeam(int corner)
    {
        edgeOnSeam[corner] = true;
        vertexOnSeam[table.Vertex(Next(corner))] = true;
        vertexOnSeam[table.Vertex(Previous(corner))] = true;
    }
}
