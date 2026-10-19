using System;
using System.Collections.Generic;
using Adamantium.Mathematics;

namespace Adamantium.Graphics.Core.Models;

/// <summary>
/// A mesh's triangles sorted into a bounding volume hierarchy, so a ray visits the few boxes it passes through instead
/// of every triangle. Split by the surface area heuristic over binned centers.
/// </summary>
public sealed class TriangleTree
{
    private const int LeafSize = 4;
    private const int MaxLeafSize = 16;
    private const int Bins = 12;
    private const int MaxDepth = 48;
    private const int StackSize = MaxDepth + 16;
    private const float Padding = 1e-5f;

    private readonly Vector3[] points;
    private readonly int[] indices;
    private readonly PrimitiveType topology;
    private readonly Vector3F[] corners;
    private readonly int[] cornerPoints;
    private readonly Node[] nodes;
    private int nodeCount;

    public TriangleTree(Vector3[] points, int[] indices, PrimitiveType topology)
    {
        this.points = points;
        this.indices = indices;
        this.topology = topology;

        var triangles = Gather(points.Length, indices, topology);
        var gathered = new Vector3F[triangles.Length];
        for (int i = 0; i < triangles.Length; i++)
        {
            gathered[i] = (Vector3F)points[triangles[i]];
        }

        var count = triangles.Length / 3;
        var scratch = new Scratch(gathered, count);
        nodes = new Node[Math.Max(1, 2 * count - 1)];
        if (count > 0)
        {
            Build(scratch, 0, count, 0);
        }

        corners = new Vector3F[count * 3];
        cornerPoints = new int[count * 3];
        for (int i = 0; i < count; i++)
        {
            var triangle = scratch.Order[i];
            for (int corner = 0; corner < 3; corner++)
            {
                corners[i * 3 + corner] = gathered[triangle * 3 + corner];
                cornerPoints[i * 3 + corner] = triangles[triangle * 3 + corner];
            }
        }
    }

    public int TriangleCount => corners.Length / 3;

    /// <summary>Whether the tree was built from these very arrays in this topology.</summary>
    public bool Describes(Vector3[] points, int[] indices, PrimitiveType topology)
    {
        return ReferenceEquals(this.points, points) && ReferenceEquals(this.indices, indices) && this.topology == topology;
    }

    /// <summary>The nearest triangle along the ray, as a distance in lengths of the ray's direction.</summary>
    public bool Intersect(Ray ray, out float distance)
    {
        distance = float.MaxValue;
        var found = false;
        if (corners.Length == 0)
        {
            distance = 0;
            return false;
        }

        var inverse = new Vector3F(Inverse(ray.Direction.X), Inverse(ray.Direction.Y), Inverse(ray.Direction.Z));
        Span<int> stack = stackalloc int[StackSize];
        Span<float> entries = stackalloc float[StackSize];
        var top = 0;

        var rootEntry = Entry(in nodes[0], ray.Position, inverse);
        if (rootEntry < float.MaxValue)
        {
            stack[top] = 0;
            entries[top++] = rootEntry;
        }

        while (top > 0)
        {
            var current = stack[--top];
            if (entries[top] >= distance)
            {
                continue;
            }

            ref readonly var node = ref nodes[current];
            if (node.Count > 0)
            {
                for (int i = node.First; i < node.First + node.Count; i++)
                {
                    if (Collision.RayIntersectsTriangle(ref ray, ref corners[i * 3], ref corners[i * 3 + 1], ref corners[i * 3 + 2], out float hit)
                        && hit < distance)
                    {
                        distance = hit;
                        found = true;
                    }
                }

                continue;
            }

            var near = current + 1;
            var far = node.First;
            var nearEntry = Entry(in nodes[near], ray.Position, inverse);
            var farEntry = Entry(in nodes[far], ray.Position, inverse);
            if (farEntry < nearEntry)
            {
                (near, far) = (far, near);
                (nearEntry, farEntry) = (farEntry, nearEntry);
            }

            if (farEntry < distance)
            {
                stack[top] = far;
                entries[top++] = farEntry;
            }

            if (nearEntry < distance)
            {
                stack[top] = near;
                entries[top++] = nearEntry;
            }
        }

        if (!found)
        {
            distance = 0;
        }

        return found;
    }

    /// <summary>
    /// Lays the triangles on <paramref name="moved"/> - the points the tree was built from, standing elsewhere, as a skin
    /// bends them - and fits its boxes round them again. The tree keeps its shape, so a ray finds a triangle as fast as
    /// before while the points keep close to where they were.
    /// </summary>
    public void Refit(Vector3F[] moved)
    {
        for (int i = 0; i < corners.Length; i++)
        {
            corners[i] = moved[cornerPoints[i]];
        }

        for (int i = nodeCount - 1; i >= 0; i--)
        {
            ref var node = ref nodes[i];
            if (node.Count > 0)
            {
                var min = new Vector3F(float.MaxValue);
                var max = new Vector3F(float.MinValue);
                for (int corner = node.First * 3; corner < (node.First + node.Count) * 3; corner++)
                {
                    min = Vector3F.Min(min, corners[corner]);
                    max = Vector3F.Max(max, corners[corner]);
                }

                var extent = max - min;
                var pad = new Vector3F(Math.Max(extent.X, Math.Max(extent.Y, extent.Z)) * Padding);
                node.Min = min - pad;
                node.Max = max + pad;
            }
            else
            {
                node.Min = Vector3F.Min(nodes[i + 1].Min, nodes[node.First].Min);
                node.Max = Vector3F.Max(nodes[i + 1].Max, nodes[node.First].Max);
            }
        }
    }

    private static int StepOf(PrimitiveType topology)
    {
        if (topology == PrimitiveType.TriangleList)
        {
            return 3;
        }

        return topology == PrimitiveType.TriangleStrip ? 1 : 0;
    }

    private static int[] Gather(int pointCount, int[] indices, PrimitiveType topology)
    {
        var step = StepOf(topology);
        if (step == 0)
        {
            return [];
        }

        List<int> gathered = [];
        var indexed = indices.Length > 0;
        var count = indexed ? indices.Length : pointCount;
        for (int i = 0; i + 2 < count; i += step)
        {
            var a = indexed ? indices[i] : i;
            var b = indexed ? indices[i + 1] : i + 1;
            var c = indexed ? indices[i + 2] : i + 2;
            if (a < 0 || b < 0 || c < 0)
            {
                continue;
            }

            gathered.Add(a);
            gathered.Add(b);
            gathered.Add(c);
        }

        return gathered.ToArray();
    }

    private static float Inverse(float value)
    {
        return 1f / (value == 0 ? 1e-30f : value);
    }

    private static float Entry(in Node node, Vector3F origin, Vector3F inverse)
    {
        var x1 = (node.Min.X - origin.X) * inverse.X;
        var x2 = (node.Max.X - origin.X) * inverse.X;
        var y1 = (node.Min.Y - origin.Y) * inverse.Y;
        var y2 = (node.Max.Y - origin.Y) * inverse.Y;
        var z1 = (node.Min.Z - origin.Z) * inverse.Z;
        var z2 = (node.Max.Z - origin.Z) * inverse.Z;
        var enter = Math.Max(Math.Max(Math.Min(x1, x2), Math.Min(y1, y2)), Math.Min(z1, z2));
        var exit = Math.Min(Math.Min(Math.Max(x1, x2), Math.Max(y1, y2)), Math.Max(z1, z2));
        if (exit < 0 || enter > exit)
        {
            return float.MaxValue;
        }

        return Math.Max(enter, 0);
    }

    private static float Area(Vector3F min, Vector3F max)
    {
        var size = max - min;
        return size.X * size.Y + size.Y * size.Z + size.Z * size.X;
    }

    private static float Along(in Vector3F value, int axis)
    {
        return axis == 0 ? value.X : axis == 1 ? value.Y : value.Z;
    }

    private int Build(Scratch scratch, int start, int count, int depth)
    {
        var lows = scratch.Lows;
        var highs = scratch.Highs;
        var centers = scratch.Centers;
        var order = scratch.Order;
        var binOf = scratch.BinOf;

        var index = nodeCount++;
        var min = new Vector3F(float.MaxValue);
        var max = new Vector3F(float.MinValue);
        var centerMin = new Vector3F(float.MaxValue);
        var centerMax = new Vector3F(float.MinValue);
        for (int i = start; i < start + count; i++)
        {
            var triangle = order[i];
            min = Vector3F.Min(min, lows[triangle]);
            max = Vector3F.Max(max, highs[triangle]);
            centerMin = Vector3F.Min(centerMin, centers[triangle]);
            centerMax = Vector3F.Max(centerMax, centers[triangle]);
        }

        var extent = max - min;
        var pad = new Vector3F(Math.Max(extent.X, Math.Max(extent.Y, extent.Z)) * Padding);
        nodes[index] = new Node { Min = min - pad, Max = max + pad, First = start, Count = count };

        if (count <= LeafSize || depth >= MaxDepth)
        {
            return index;
        }

        var spread = centerMax - centerMin;
        var axis = spread.X >= spread.Y && spread.X >= spread.Z ? 0 : spread.Y >= spread.Z ? 1 : 2;
        var width = Along(in spread, axis);
        if (width <= 0)
        {
            return index;
        }

        var from = Along(in centerMin, axis);
        var scale = Bins / width;
        Span<int> binCounts = stackalloc int[Bins];
        Span<Vector3F> binMin = stackalloc Vector3F[Bins];
        Span<Vector3F> binMax = stackalloc Vector3F[Bins];
        for (int b = 0; b < Bins; b++)
        {
            binMin[b] = new Vector3F(float.MaxValue);
            binMax[b] = new Vector3F(float.MinValue);
        }

        for (int i = start; i < start + count; i++)
        {
            var triangle = order[i];
            var bin = Math.Min(Bins - 1, (int)((Along(in centers[triangle], axis) - from) * scale));
            binOf[triangle] = bin;
            binCounts[bin]++;
            binMin[bin] = Vector3F.Min(binMin[bin], lows[triangle]);
            binMax[bin] = Vector3F.Max(binMax[bin], highs[triangle]);
        }

        Span<float> rightCost = stackalloc float[Bins];
        var sweepMin = new Vector3F(float.MaxValue);
        var sweepMax = new Vector3F(float.MinValue);
        var sweepCount = 0;
        for (int b = Bins - 1; b > 0; b--)
        {
            sweepMin = Vector3F.Min(sweepMin, binMin[b]);
            sweepMax = Vector3F.Max(sweepMax, binMax[b]);
            sweepCount += binCounts[b];
            rightCost[b] = sweepCount == 0 ? float.MaxValue : sweepCount * Area(sweepMin, sweepMax);
        }

        var bestSplit = -1;
        var bestCost = float.MaxValue;
        sweepMin = new Vector3F(float.MaxValue);
        sweepMax = new Vector3F(float.MinValue);
        sweepCount = 0;
        for (int b = 0; b < Bins - 1; b++)
        {
            sweepMin = Vector3F.Min(sweepMin, binMin[b]);
            sweepMax = Vector3F.Max(sweepMax, binMax[b]);
            sweepCount += binCounts[b];
            if (sweepCount == 0 || sweepCount == count)
            {
                continue;
            }

            var cost = sweepCount * Area(sweepMin, sweepMax) + rightCost[b + 1];
            if (cost < bestCost)
            {
                bestCost = cost;
                bestSplit = b;
            }
        }

        if (bestSplit < 0 || (bestCost >= count * Area(min, max) && count <= MaxLeafSize))
        {
            return index;
        }

        var left = start;
        var right = start + count - 1;
        while (left <= right)
        {
            if (binOf[order[left]] <= bestSplit)
            {
                left++;
            }
            else
            {
                (order[left], order[right]) = (order[right], order[left]);
                right--;
            }
        }

        Build(scratch, start, left - start, depth + 1);
        nodes[index].First = Build(scratch, left, start + count - left, depth + 1);
        nodes[index].Count = 0;
        return index;
    }

    private struct Node
    {
        public Vector3F Min;
        public Vector3F Max;
        public int First;
        public int Count;
    }

    private sealed class Scratch
    {
        public readonly Vector3F[] Lows;
        public readonly Vector3F[] Highs;
        public readonly Vector3F[] Centers;
        public readonly int[] Order;
        public readonly int[] BinOf;

        public Scratch(Vector3F[] corners, int count)
        {
            Lows = new Vector3F[count];
            Highs = new Vector3F[count];
            Centers = new Vector3F[count];
            Order = new int[count];
            BinOf = new int[count];
            for (int i = 0; i < count; i++)
            {
                var a = corners[i * 3];
                var b = corners[i * 3 + 1];
                var c = corners[i * 3 + 2];
                Lows[i] = Vector3F.Min(Vector3F.Min(a, b), c);
                Highs[i] = Vector3F.Max(Vector3F.Max(a, b), c);
                Centers[i] = (Lows[i] + Highs[i]) * 0.5f;
                Order[i] = i;
            }
        }
    }
}
