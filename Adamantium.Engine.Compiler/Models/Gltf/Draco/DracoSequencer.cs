// Ported from Draco (src/draco/compression/mesh/traverser/, src/draco/compression/attributes/linear_sequencer.h).
// Copyright 2016 The Draco Authors. Under the Apache License 2.0; translated to C# and changed, details:
// THIRD-PARTY-NOTICES.md.

using System;
using System.Collections.Generic;
using System.IO;

namespace Adamantium.Engine.Compiler.Models.Gltf.Draco;

internal sealed class DracoSequencer
{
    private const int Priorities = 3;

    private readonly bool byDegree;
    private int[] faces = [];
    private bool[] faceVisited = [];
    private bool[] vertexVisited = [];
    private int[] degrees = [];
    private List<int> points = [];
    private readonly List<int> stack = [];
    private readonly List<int>[] stacks = [[], [], []];
    private int bestPriority;

    public DracoSequencer(DracoCorners corners, DracoEncodingData encoding, bool byDegree)
    {
        Corners = corners;
        Encoding = encoding;
        this.byDegree = byDegree;
    }

    public DracoCorners Corners { get; }

    public DracoEncodingData Encoding { get; }

    public int[] Generate(int[] meshFaces, int pointCount)
    {
        if (Corners == null)
        {
            var sequence = new int[pointCount];
            for (var i = 0; i < pointCount; i++)
            {
                sequence[i] = i;
            }

            return sequence;
        }

        faces = meshFaces;
        faceVisited = new bool[Corners.FaceCount];
        vertexVisited = new bool[Corners.VertexCount];
        degrees = new int[Corners.VertexCount];
        points = new List<int>(Corners.VertexCount);
        for (var f = 0; f < Corners.FaceCount; f++)
        {
            if (byDegree)
            {
                TraverseByDegree(3 * f);
            }
            else
            {
                TraverseDepthFirst(3 * f);
            }
        }

        return [.. points];
    }

    public int[] PointToValue(int[] meshFaces, int pointCount)
    {
        if (Corners == null)
        {
            return null;
        }

        var map = new int[pointCount];
        Array.Fill(map, -1);
        for (var corner = 0; corner < Corners.CornerCount; corner++)
        {
            var point = meshFaces[corner];
            var value = Encoding.VertexToValue[VertexOf(corner)];
            if (point >= pointCount || value >= pointCount)
            {
                throw new InvalidDataException("a Draco attribute maps past its points");
            }

            map[point] = value;
        }

        return map;
    }

    private bool IsFaceVisited(int corner) => corner < 0 || faceVisited[corner / 3];

    private void Visit(int vertex, int corner)
    {
        vertexVisited[vertex] = true;
        points.Add(faces[corner]);
        Encoding.ValueToCorner.Add(corner);
        Encoding.VertexToValue[vertex] = Encoding.ValueToCorner.Count - 1;
    }

    private int VertexOf(int corner)
    {
        var vertex = Corners.Vertex(corner);
        if (vertex == DracoCorners.Invalid)
        {
            throw new InvalidDataException("a Draco face has a corner of no vertex");
        }

        return vertex;
    }

    private void VisitIfNew(int corner)
    {
        var vertex = VertexOf(corner);
        if (!vertexVisited[vertex])
        {
            Visit(vertex, corner);
        }
    }

    private void TraverseDepthFirst(int corner)
    {
        if (IsFaceVisited(corner))
        {
            return;
        }

        stack.Clear();
        stack.Add(corner);
        VisitIfNew(DracoCorners.Next(corner));
        VisitIfNew(DracoCorners.Previous(corner));
        while (stack.Count > 0)
        {
            corner = stack[^1];
            if (IsFaceVisited(corner))
            {
                stack.RemoveAt(stack.Count - 1);
                continue;
            }

            while (true)
            {
                var vertex = VertexOf(corner);
                faceVisited[corner / 3] = true;
                if (!vertexVisited[vertex])
                {
                    var onBoundary = Corners.IsOnBoundary(vertex);
                    Visit(vertex, corner);
                    if (!onBoundary)
                    {
                        corner = Corners.RightCorner(corner);
                        continue;
                    }
                }

                var right = Corners.RightCorner(corner);
                var left = Corners.LeftCorner(corner);
                if (IsFaceVisited(right))
                {
                    if (IsFaceVisited(left))
                    {
                        stack.RemoveAt(stack.Count - 1);
                        break;
                    }

                    corner = left;
                }
                else if (IsFaceVisited(left))
                {
                    corner = right;
                }
                else
                {
                    stack[^1] = left;
                    stack.Add(right);
                    break;
                }
            }
        }
    }

    private void TraverseByDegree(int corner)
    {
        if (degrees.Length == 0)
        {
            return;
        }

        stacks[0].Add(corner);
        bestPriority = 0;
        VisitIfNew(DracoCorners.Next(corner));
        VisitIfNew(DracoCorners.Previous(corner));
        VisitIfNew(corner);
        while ((corner = PopNextCorner()) != DracoCorners.Invalid)
        {
            if (IsFaceVisited(corner))
            {
                continue;
            }

            while (true)
            {
                faceVisited[corner / 3] = true;
                VisitIfNew(corner);
                var right = Corners.RightCorner(corner);
                var left = Corners.LeftCorner(corner);
                var rightVisited = IsFaceVisited(right);
                if (!IsFaceVisited(left))
                {
                    var priority = Priority(left);
                    if (rightVisited && priority <= bestPriority)
                    {
                        corner = left;
                        continue;
                    }

                    Push(left, priority);
                }

                if (!rightVisited)
                {
                    var priority = Priority(right);
                    if (priority <= bestPriority)
                    {
                        corner = right;
                        continue;
                    }

                    Push(right, priority);
                }

                break;
            }
        }
    }

    private int PopNextCorner()
    {
        for (var i = bestPriority; i < Priorities; i++)
        {
            if (stacks[i].Count > 0)
            {
                var corner = stacks[i][^1];
                stacks[i].RemoveAt(stacks[i].Count - 1);
                bestPriority = i;
                return corner;
            }
        }

        return DracoCorners.Invalid;
    }

    private void Push(int corner, int priority)
    {
        stacks[priority].Add(corner);
        bestPriority = Math.Min(bestPriority, priority);
    }

    private int Priority(int corner)
    {
        var tip = Corners.Vertex(corner);
        if (vertexVisited[tip])
        {
            return 0;
        }

        return ++degrees[tip] > 1 ? 1 : 2;
    }
}
