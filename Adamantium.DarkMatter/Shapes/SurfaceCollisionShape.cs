using System;
using System.Collections.Generic;

namespace Adamantium.DarkMatter.Shapes;

/// <summary>A surface of triangles bodies collide with, one triangle at a time: a mesh or a height field. It holds no
/// volume, so only a static or kinematic body holds one.</summary>
public abstract class SurfaceCollisionShape : CollisionShape
{
    /// <summary>The cosine of the least turn between two triangles that makes the convex edge between them one a body
    /// can catch on: five degrees, as Jolt has it. Flatter and concave edges are inside the surface.</summary>
    internal const float ActiveEdgeCosine = 0.996195f;

    /// <summary>How many triangles the surface numbers; a hole's numbers have none.</summary>
    internal abstract int TriangleCount { get; }

    /// <summary>Adds to <paramref name="into"/> the numbers of the triangles whose boxes meet <paramref name="box"/>,
    /// in the surface's own axes.</summary>
    internal abstract void FindTriangles(in BoundingBox<float> box, List<int> into);

    /// <summary>The corners of triangle <paramref name="triangle"/>, in the surface's own axes; false for a hole.
    /// </summary>
    internal abstract bool TryGetTriangle(int triangle, out Vector3F first, out Vector3F second, out Vector3F third);

    /// <summary>The normal of triangle <paramref name="triangle"/>, whose corners are <paramref name="first"/>,
    /// <paramref name="second"/> and <paramref name="third"/>; a surface that keeps its normals gives the one it keeps.
    /// </summary>
    internal virtual Vector3F GetTriangleNormal(int triangle, Vector3F first, Vector3F second, Vector3F third) =>
        NormalOf(first, second, third);

    /// <summary>Which edges of triangle <paramref name="triangle"/> a body can catch on: bit 0 the edge from the
    /// first corner to the second, bit 1 from the second to the third, bit 2 from the third to the first.</summary>
    internal abstract int GetActiveEdges(int triangle);

    /// <summary>The box, along the world's axes, round the box from <paramref name="minimum"/> to
    /// <paramref name="maximum"/> in the surface's own axes standing at <paramref name="pose"/>.</summary>
    protected static BoundingBox<float> PlaceBox(Vector3F minimum, Vector3F maximum, in Pose pose)
    {
        var center = pose.TransformPoint((minimum + maximum) * 0.5f);
        var half = (maximum - minimum) * 0.5f;
        // Each world axis reaches as far as the box's own axes, turned, reach along it.
        var axisX = Vector3F.Abs(pose.TransformDirection(new Vector3F(half.X, 0, 0)));
        var axisY = Vector3F.Abs(pose.TransformDirection(new Vector3F(0, half.Y, 0)));
        var axisZ = Vector3F.Abs(pose.TransformDirection(new Vector3F(0, 0, half.Z)));
        var reach = axisX + axisY + axisZ;
        return new BoundingBox<float>(center - reach, center + reach);
    }

    /// <summary>A surface has no volume to weigh: its body moves only as it is told.</summary>
    public override MassProperties ComputeMass(float density) =>
        throw new InvalidOperationException("A surface has no volume to weigh; its body must be static or kinematic.");

    /// <summary>Whether the edge between two triangles with normals <paramref name="normal"/> and
    /// <paramref name="otherNormal"/>, running along <paramref name="edge"/> as the first triangle winds, can catch a
    /// body: convex and turned past <see cref="ActiveEdgeCosine"/>.</summary>
    internal static bool IsEdgeActive(Vector3F normal, Vector3F otherNormal, Vector3F edge)
    {
        if (Vector3F.Dot(Vector3F.Cross(normal, otherNormal), edge) < 0)
            return false;

        return Vector3F.Dot(normal, otherNormal) < ActiveEdgeCosine;
    }

    /// <summary>The outward normal of a triangle wound counterclockwise seen from it, of length one; zero for one with
    /// no area.</summary>
    internal static Vector3F NormalOf(Vector3F first, Vector3F second, Vector3F third)
    {
        var normal = Vector3F.Cross(second - first, third - first);
        var length = normal.Length();
        return length > 0 && float.IsFinite(length) ? normal / length : Vector3F.Zero;
    }

    /// <summary>Whether <paramref name="shape"/> is a surface or a compound holding one.</summary>
    internal static bool HoldsSurface(CollisionShape shape)
    {
        if (shape is SurfaceCollisionShape)
            return true;

        if (shape is not CompoundCollisionShape compound)
            return false;

        var parts = compound.Parts;
        for (var index = 0; index < parts.Count; index++)
        {
            if (parts[index].Shape is SurfaceCollisionShape)
                return true;
        }

        return false;
    }
}
