// Ported from Draco (src/draco/mesh/corner_table.h, corner_table.cc).
// Copyright 2016 The Draco Authors. Under the Apache License 2.0; translated to C# and changed, details:
// THIRD-PARTY-NOTICES.md.

using System;
using System.Collections.Generic;

namespace Adamantium.Engine.Compiler.Models.Gltf.Draco;

internal sealed class DracoCornerTable : DracoCorners
{
    private readonly int[] cornerToVertex;
    private readonly int[] opposites;
    private readonly List<int> vertexCorners = [];

    public DracoCornerTable(int faces)
    {
        cornerToVertex = new int[faces * 3];
        opposites = new int[faces * 3];
        Array.Fill(cornerToVertex, Invalid);
        Array.Fill(opposites, Invalid);
    }

    public override int VertexCount => vertexCorners.Count;

    public override int CornerCount => cornerToVertex.Length;

    public override int Opposite(int corner) => corner < 0 ? Invalid : opposites[corner];

    public override int Vertex(int corner) => corner < 0 ? Invalid : cornerToVertex[corner];

    public override int LeftMostCorner(int vertex) => vertex < 0 ? Invalid : vertexCorners[vertex];

    public void SetOpposites(int corner, int other)
    {
        opposites[corner] = other;
        opposites[other] = corner;
    }

    public void MapCornerToVertex(int corner, int vertex) => cornerToVertex[corner] = vertex;

    public int AddVertex()
    {
        vertexCorners.Add(Invalid);
        return vertexCorners.Count - 1;
    }

    public void SetLeftMostCorner(int vertex, int corner)
    {
        if (vertex >= 0)
        {
            vertexCorners[vertex] = corner;
        }
    }
}
