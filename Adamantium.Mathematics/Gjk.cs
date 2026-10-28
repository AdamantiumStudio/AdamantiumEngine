using System;

namespace Adamantium.Mathematics;

/// <summary>
/// The Gilbert-Johnson-Keerthi test: whether two convex shapes overlap, from nothing but their support functions.
/// Shapes that only touch may come out either way.
/// </summary>
public static class Gjk
{
    private const int MaxIterations = 64;
    private const float Progress = 1e-6f;

    /// <summary>Whether the two shapes share a point.</summary>
    public static bool Intersects<TFirst, TSecond>(TFirst first, TSecond second)
        where TFirst : IConvexShape<float>
        where TSecond : IConvexShape<float>
    {
        var simplex = new Vector3F[4];
        var count = 1;
        simplex[0] = Support(first, second, Vector3F.UnitX);
        var direction = -simplex[0];

        for (int i = 0; i < MaxIterations; i++)
        {
            var length = direction.Length();
            if (length < 1e-12f)
            {
                return true;
            }

            direction /= length;
            var point = Support(first, second, direction);
            var reached = Vector3F.Dot(point, direction);
            if (reached < 0)
            {
                return false;
            }

            if (reached - Vector3F.Dot(simplex[count - 1], direction) <= Progress * Math.Max(1f, point.Length()))
            {
                return false;
            }

            simplex[count++] = point;
            if (Enclose(simplex, ref count, ref direction))
            {
                return true;
            }
        }

        return true;
    }

    private static Vector3F Support<TFirst, TSecond>(TFirst first, TSecond second, Vector3F direction)
        where TFirst : IConvexShape<float>
        where TSecond : IConvexShape<float>
    {
        return first.Support(direction) - second.Support(-direction);
    }

    private static bool Enclose(Vector3F[] simplex, ref int count, ref Vector3F direction)
    {
        switch (count)
        {
            case 2:
                Line(simplex, ref count, ref direction);
                return false;
            case 3:
                Triangle(simplex, ref count, ref direction);
                return false;
            default:
                return Tetrahedron(simplex, ref count, ref direction);
        }
    }

    private static void Line(Vector3F[] simplex, ref int count, ref Vector3F direction)
    {
        var a = simplex[1];
        var b = simplex[0];
        var ab = b - a;
        var ao = -a;
        if (Vector3F.Dot(ab, ao) > 0)
        {
            direction = Vector3F.Cross(Vector3F.Cross(ab, ao), ab);
            return;
        }

        simplex[0] = a;
        count = 1;
        direction = ao;
    }

    private static void Triangle(Vector3F[] simplex, ref int count, ref Vector3F direction)
    {
        var a = simplex[2];
        var b = simplex[1];
        var c = simplex[0];
        var ab = b - a;
        var ac = c - a;
        var ao = -a;
        var abc = Vector3F.Cross(ab, ac);

        if (Vector3F.Dot(Vector3F.Cross(abc, ac), ao) > 0)
        {
            if (Vector3F.Dot(ac, ao) > 0)
            {
                simplex[0] = c;
                simplex[1] = a;
                count = 2;
                direction = Vector3F.Cross(Vector3F.Cross(ac, ao), ac);
                return;
            }

            SetLine(simplex, ref count, ref direction, b, a);
            return;
        }

        if (Vector3F.Dot(Vector3F.Cross(ab, abc), ao) > 0)
        {
            SetLine(simplex, ref count, ref direction, b, a);
            return;
        }

        if (Vector3F.Dot(abc, ao) > 0)
        {
            direction = abc;
            return;
        }

        simplex[0] = b;
        simplex[1] = c;
        direction = -abc;
    }

    private static bool Tetrahedron(Vector3F[] simplex, ref int count, ref Vector3F direction)
    {
        var a = simplex[3];
        var b = simplex[2];
        var c = simplex[1];
        var d = simplex[0];
        var ab = b - a;
        var ac = c - a;
        var ad = d - a;
        var ao = -a;

        var abc = Outward(Vector3F.Cross(ab, ac), ad);
        var acd = Outward(Vector3F.Cross(ac, ad), ab);
        var adb = Outward(Vector3F.Cross(ad, ab), ac);

        if (Vector3F.Dot(abc, ao) > 0)
        {
            SetTriangle(simplex, ref count, ref direction, c, b, a);
            return false;
        }

        if (Vector3F.Dot(acd, ao) > 0)
        {
            SetTriangle(simplex, ref count, ref direction, d, c, a);
            return false;
        }

        if (Vector3F.Dot(adb, ao) > 0)
        {
            SetTriangle(simplex, ref count, ref direction, b, d, a);
            return false;
        }

        return true;
    }

    private static Vector3F Outward(Vector3F normal, Vector3F towardOpposite)
    {
        return Vector3F.Dot(normal, towardOpposite) > 0 ? -normal : normal;
    }

    private static void SetLine(Vector3F[] simplex, ref int count, ref Vector3F direction, Vector3F b, Vector3F a)
    {
        simplex[0] = b;
        simplex[1] = a;
        count = 2;
        Line(simplex, ref count, ref direction);
    }

    private static void SetTriangle(Vector3F[] simplex, ref int count, ref Vector3F direction, Vector3F c, Vector3F b, Vector3F a)
    {
        simplex[0] = c;
        simplex[1] = b;
        simplex[2] = a;
        count = 3;
        Triangle(simplex, ref count, ref direction);
    }
}
