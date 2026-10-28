using System;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;

namespace Adamantium.Engine.Rendering;

/// <summary>
/// Turns a mesh of lines into quads, one per segment, for lines drawn a given number of pixels wide with smooth edges.
/// Each corner carries its own end as the position, the segment's other end as the normal, and in UV0 the side it
/// spreads to (-1 or 1) and which end it is (0 the start, 1 the end); the shader spreads the corners on screen.
/// </summary>
public static class LineRibbon
{
    public static Mesh Build(Mesh lines)
    {
        var points = lines.Points;
        var indices = lines.Indices;
        var count = lines.HasIndices ? indices.Length : points.Length;
        var step = lines.MeshTopology == PrimitiveType.LineStrip ? 1 : 2;
        var segments = count < 2 ? 0 : (count - 2) / step + 1;

        var positions = new Vector3<double>[segments * 4];
        var others = new Vector3F[segments * 4];
        var corners = new Vector2F[segments * 4];
        var triangles = new int[segments * 6];
        var used = 0;

        for (int i = 0; i + 1 < count; i += step)
        {
            var a = lines.HasIndices ? indices[i] : i;
            var b = lines.HasIndices ? indices[i + 1] : i + 1;
            if (a < 0 || b < 0)
            {
                continue;
            }

            var first = used * 4;
            Corner(first, points[a], points[b], -1, 0);
            Corner(first + 1, points[a], points[b], 1, 0);
            Corner(first + 2, points[b], points[a], -1, 1);
            Corner(first + 3, points[b], points[a], 1, 1);

            var triangle = used * 6;
            triangles[triangle] = first;
            triangles[triangle + 1] = first + 1;
            triangles[triangle + 2] = first + 2;
            triangles[triangle + 3] = first + 2;
            triangles[triangle + 4] = first + 1;
            triangles[triangle + 5] = first + 3;
            used++;
        }

        if (used < segments)
        {
            Array.Resize(ref positions, used * 4);
            Array.Resize(ref others, used * 4);
            Array.Resize(ref corners, used * 4);
            Array.Resize(ref triangles, used * 6);
        }

        return new Mesh(PrimitiveType.TriangleList)
            .SetPoints(positions)
            .SetNormals(others)
            .SetUVs(0, corners)
            .SetIndices(triangles);

        void Corner(int at, Vector3<double> own, Vector3<double> other, float side, float end)
        {
            positions[at] = own;
            others[at] = other.As<float>();
            corners[at] = new Vector2F(side, end);
        }
    }
}
