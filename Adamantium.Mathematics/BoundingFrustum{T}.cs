using System;
using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Adamantium.Mathematics;

/// <summary>
/// The volume a camera sees, between six planes facing inward - near, far, left, right, top, bottom - taken from a
/// view-projection matrix whose clip depth runs from 0 to 1, as Vulkan's; its surface part of it. Top and bottom go by
/// clip y: top where y reaches w, so a projection that turns y over swaps them. The far plane is checked only when
/// asked. A near or far plane the matrix puts at infinity, as an endless far plane, keeps everything in front of it,
/// and its corners lie at infinity along the frustum's edges. A matrix that leaves a plane neither a normal nor a
/// distance, as the zero matrix, sees nothing. The tests go plane by plane, so a shape just past an edge - outside, yet
/// behind no one plane - counts as meeting the frustum. A point, box or sphere with a NaN is outside. The default value
/// is the zero matrix's frustum, seeing nothing; its corners mean nothing. Made with <see cref="BoundingFrustum"/>'s
/// factory. Equality is exact; <see cref="NearEqual"/> compares within a tolerance.
/// </summary>
public readonly struct BoundingFrustum<T> : IEquatable<BoundingFrustum<T>>, IFormattable, IConvexShape<T> where T : unmanaged, IFloatingPointIeee754<T>
{
    /// <summary>How many corners a frustum has.</summary>
    public const int CornerCount = 8;

    private const int PlaneCount = 6;
    private const int FarIndex = 1;

    private readonly Planes planes;
    private readonly Corners corners;
    private readonly Corners directions;

    /// <summary>The frustum <paramref name="viewProjection"/> sees, checking its far plane when
    /// <paramref name="enableFarPlaneCheck"/>.</summary>
    public BoundingFrustum(Matrix4x4<T> viewProjection, bool enableFarPlaneCheck = false)
    {
        ViewProjection = viewProjection;
        EnableFarPlaneCheck = enableFarPlaneCheck;
        var x = Column(viewProjection, 0);
        var y = Column(viewProjection, 1);
        var z = Column(viewProjection, 2);
        var w = Column(viewProjection, 3);
        planes[0] = Inward(z, Vector4<T>.Zero);
        planes[1] = Inward(w, -z);
        planes[2] = Inward(w, x);
        planes[3] = Inward(w, -x);
        planes[4] = Inward(w, -y);
        planes[5] = Inward(w, y);
        for (var i = 0; i < CornerCount; i++)
        {
            var end = i < CornerCount / 2 ? 0 : FarIndex;
            var side = (i & 3) is 0 or 3 ? planes[2] : planes[3];
            var edge = (i & 3) < 2 ? planes[4] : planes[5];
            if (planes[end].Normal != Vector3<T>.Zero)
            {
                corners[i] = Meet(planes[end], side, edge);
                continue;
            }

            var direction = Vector3<T>.Cross(side.Normal, edge.Normal);
            directions[i] = Vector3<T>.Dot(planes[FarIndex - end].Normal, direction) < T.Zero ? -direction : direction;
            corners[i] = planes[FarIndex - end].Normal != Vector3<T>.Zero
                ? Meet(planes[FarIndex - end], side, edge)
                : OnBoth(side, edge, direction);
        }
    }

    /// <summary>The view-projection matrix the frustum is taken from.</summary>
    public Matrix4x4<T> ViewProjection { get; }

    /// <summary>Whether the tests check the far plane too.</summary>
    public bool EnableFarPlaneCheck { get; }

    /// <summary>The near plane, facing inward, its normal of length one - or none, when at infinity.</summary>
    public Plane<T> Near => planes[0];

    /// <summary>The far plane, facing inward, its normal of length one - or none, when at infinity.</summary>
    public Plane<T> Far => planes[1];

    /// <summary>The left plane, facing inward, its normal of length one.</summary>
    public Plane<T> Left => planes[2];

    /// <summary>The right plane, facing inward, its normal of length one.</summary>
    public Plane<T> Right => planes[3];

    /// <summary>The top plane, where clip y reaches w, facing inward, its normal of length one.</summary>
    public Plane<T> Top => planes[4];

    /// <summary>The bottom plane, where clip y reaches -w, facing inward, its normal of length one.</summary>
    public Plane<T> Bottom => planes[5];

    /// <summary>Copies the corners to <paramref name="destination"/>: near left top, near right top, near right bottom,
    /// near left bottom, then the far ones in the same order. A corner at infinity is the other end of its edge with
    /// the coordinates the edge runs along made infinite.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="destination"/> holds fewer than
    /// <see cref="CornerCount"/>.</exception>
    public void GetCorners(Span<Vector3<T>> destination)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(destination.Length, CornerCount, nameof(destination));
        for (var i = 0; i < CornerCount; i++)
        {
            destination[i] = Corner(i);
        }
    }

    /// <summary>The corner farthest along <paramref name="direction"/>; when an edge runs off to infinity that way, its
    /// corner at infinity.</summary>
    public Vector3<T> Support(Vector3<T> direction)
    {
        var best = 0;
        var reach = Reach(0, direction);
        for (var i = 1; i < CornerCount; i++)
        {
            var along = Reach(i, direction);
            if (along > reach)
            {
                reach = along;
                best = i;
            }
        }

        return T.IsFinite(reach) ? corners[best] : Corner(best);
    }

    /// <summary>Whether <paramref name="point"/> is inside or outside; on the surface is inside.</summary>
    public ContainmentType Contains(Vector3<T> point)
    {
        if (T.IsNaN(Collision.Largest(point)))
        {
            return ContainmentType.Disjoint;
        }

        for (var i = 0; i < PlaneCount; i++)
        {
            if (!Skips(i) && !(Distance(planes[i], point) >= T.Zero))
            {
                return ContainmentType.Disjoint;
            }
        }

        return ContainmentType.Contains;
    }

    /// <summary>Whether all of <paramref name="points"/> are inside, some, or none - by planes, so points all round
    /// the frustum but each behind none of its planes count as some; no points are none.</summary>
    public ContainmentType Contains(ReadOnlySpan<Vector3<T>> points)
    {
        var result = ContainmentType.Contains;
        for (var i = 0; i < PlaneCount; i++)
        {
            if (Skips(i))
            {
                continue;
            }

            var inside = 0;
            foreach (var point in points)
            {
                if (!T.IsNaN(Collision.Largest(point)) && Distance(planes[i], point) >= T.Zero)
                {
                    inside++;
                }
            }

            if (inside == 0)
            {
                return ContainmentType.Disjoint;
            }

            if (inside < points.Length)
            {
                result = ContainmentType.Intersects;
            }
        }

        return result;
    }

    /// <summary>Whether <paramref name="box"/> is inside, crosses the surface, or is outside.</summary>
    public ContainmentType Contains(BoundingBox<T> box)
    {
        if (T.IsNaN(Collision.Largest(box.Minimum)) || T.IsNaN(Collision.Largest(box.Maximum)))
        {
            return ContainmentType.Disjoint;
        }

        var result = ContainmentType.Contains;
        for (var i = 0; i < PlaneCount; i++)
        {
            if (Skips(i))
            {
                continue;
            }

            var normal = planes[i].Normal;
            var farthest = new Vector3<T>(normal.X >= T.Zero ? box.Maximum.X : box.Minimum.X,
                normal.Y >= T.Zero ? box.Maximum.Y : box.Minimum.Y, normal.Z >= T.Zero ? box.Maximum.Z : box.Minimum.Z);
            if (!(Distance(planes[i], farthest) >= T.Zero))
            {
                return ContainmentType.Disjoint;
            }

            var nearest = new Vector3<T>(normal.X >= T.Zero ? box.Minimum.X : box.Maximum.X,
                normal.Y >= T.Zero ? box.Minimum.Y : box.Maximum.Y, normal.Z >= T.Zero ? box.Minimum.Z : box.Maximum.Z);
            if (Distance(planes[i], nearest) < T.Zero)
            {
                result = ContainmentType.Intersects;
            }
        }

        return result;
    }

    /// <summary>Whether <paramref name="sphere"/> is inside, crosses the surface, or is outside.</summary>
    public ContainmentType Contains(BoundingSphere<T> sphere)
    {
        if (T.IsNaN(Collision.Largest(sphere.Center)))
        {
            return ContainmentType.Disjoint;
        }

        var result = ContainmentType.Contains;
        for (var i = 0; i < PlaneCount; i++)
        {
            if (Skips(i))
            {
                continue;
            }

            var distance = Distance(planes[i], sphere.Center);
            if (!(distance >= -sphere.Radius))
            {
                return ContainmentType.Disjoint;
            }

            if (distance < sphere.Radius)
            {
                result = ContainmentType.Intersects;
            }
        }

        return result;
    }

    /// <summary>Whether <paramref name="box"/> is inside, crosses the surface, or is outside.</summary>
    public ContainmentType Contains(OrientedBoundingBox<T> box)
    {
        var axes = Matrix3x3.CreateFromQuaternion(box.Orientation);
        if (box.IsEmpty || T.IsNaN(Collision.Largest(box.Center) + Collision.Largest(box.HalfExtent) + Collision.Largest(axes.Row1)
                    + Collision.Largest(axes.Row2) + Collision.Largest(axes.Row3)))
        {
            return ContainmentType.Disjoint;
        }

        var result = ContainmentType.Contains;
        for (var i = 0; i < PlaneCount; i++)
        {
            if (Skips(i))
            {
                continue;
            }

            var normal = planes[i].Normal;
            var reach = box.HalfExtent.X * T.Abs(Vector3<T>.Dot(normal, axes.Row1)) + box.HalfExtent.Y * T.Abs(Vector3<T>.Dot(normal, axes.Row2))
                        + box.HalfExtent.Z * T.Abs(Vector3<T>.Dot(normal, axes.Row3));
            var distance = Distance(planes[i], box.Center);
            if (!(distance >= -reach))
            {
                return ContainmentType.Disjoint;
            }

            if (distance < reach)
            {
                result = ContainmentType.Intersects;
            }
        }

        return result;
    }

    /// <summary>Whether <paramref name="frustum"/> is inside, crosses the surface, or is outside, by its corners - those
    /// at infinity too.</summary>
    public ContainmentType Contains(BoundingFrustum<T> frustum)
    {
        if (frustum.ViewProjection == ViewProjection)
        {
            return ContainmentType.Contains;
        }

        Span<Plane<T>> active = stackalloc Plane<T>[PlaneCount];
        var count = 0;
        for (var i = 0; i < PlaneCount; i++)
        {
            if (!Skips(i))
            {
                active[count++] = planes[i];
            }
        }

        return frustum.CornersInside(active[..count]);
    }

    /// <summary>Whether every corner of the frustum - those at infinity too - is in front of or on every one of
    /// <paramref name="inward"/>, or all are behind one of them, or neither.</summary>
    internal ContainmentType CornersInside(ReadOnlySpan<Plane<T>> inward)
    {
        var result = ContainmentType.Contains;
        foreach (var plane in inward)
        {
            var inside = 0;
            for (var corner = 0; corner < CornerCount; corner++)
            {
                if (Side(plane, corner) >= T.Zero)
                {
                    inside++;
                }
            }

            if (inside == 0)
            {
                return ContainmentType.Disjoint;
            }

            if (inside < CornerCount)
            {
                result = ContainmentType.Intersects;
            }
        }

        return result;
    }

    /// <summary>Whether <paramref name="box"/> and the frustum share a point.</summary>
    public bool Intersects(BoundingBox<T> box) => Contains(box) != ContainmentType.Disjoint;

    /// <summary>Whether <paramref name="box"/> and the frustum share a point.</summary>
    public bool Intersects(OrientedBoundingBox<T> box) => Contains(box) != ContainmentType.Disjoint;

    /// <summary>Whether <paramref name="sphere"/> and the frustum share a point.</summary>
    public bool Intersects(BoundingSphere<T> sphere) => Contains(sphere) != ContainmentType.Disjoint;

    /// <summary>Whether <paramref name="frustum"/> and this one share a point.</summary>
    public bool Intersects(BoundingFrustum<T> frustum) => Contains(frustum) != ContainmentType.Disjoint;

    /// <summary>Which side of <paramref name="plane"/> the frustum is on, by its corners - those at infinity too - or
    /// whether it crosses or touches the plane; as <see cref="Collision.PlaneIntersectsPoint{T}"/>, a NaN crosses
    /// it.</summary>
    public PlaneIntersectionType Intersects(Plane<T> plane)
    {
        var result = SideOf(plane, 0);
        for (var i = 1; i < CornerCount; i++)
        {
            if (SideOf(plane, i) != result)
            {
                result = PlaneIntersectionType.Intersecting;
            }
        }

        return result;
    }

    /// <summary>The frustum in another number type, its matrix converted as a cast would.</summary>
    public BoundingFrustum<TOther> As<TOther>() where TOther : unmanaged, IFloatingPointIeee754<TOther> =>
        new(ViewProjection.As<TOther>(), EnableFarPlaneCheck);

    /// <summary>Whether the matrices are within <see cref="MathHelper.Scalar.NearEqual{T}"/> and both check the far
    /// plane or neither.</summary>
    public static bool NearEqual(BoundingFrustum<T> left, BoundingFrustum<T> right) =>
        Matrix4x4<T>.NearEqual(left.ViewProjection, right.ViewProjection) && left.EnableFarPlaneCheck == right.EnableFarPlaneCheck;

    /// <summary>Whether the matrices are equal and both check the far plane or neither; NaN equals nothing.</summary>
    public static bool operator ==(BoundingFrustum<T> left, BoundingFrustum<T> right) =>
        left.ViewProjection == right.ViewProjection && left.EnableFarPlaneCheck == right.EnableFarPlaneCheck;

    /// <summary>Whether the matrices differ or one checks the far plane and the other not; NaN differs from
    /// everything.</summary>
    public static bool operator !=(BoundingFrustum<T> left, BoundingFrustum<T> right) => !(left == right);

    /// <summary>Whether the matrices are equal, a NaN to a NaN too, and both check the far plane or neither.</summary>
    public bool Equals(BoundingFrustum<T> other) =>
        ViewProjection.Equals(other.ViewProjection) && EnableFarPlaneCheck == other.EnableFarPlaneCheck;

    /// <inheritdoc />
    public override bool Equals(object obj) => obj is BoundingFrustum<T> other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(ViewProjection, EnableFarPlaneCheck);

    /// <inheritdoc />
    public override string ToString() => ToString(null, CultureInfo.CurrentCulture);

    /// <inheritdoc />
    public string ToString(string format, IFormatProvider formatProvider) =>
        string.Format(formatProvider, "Near:{0} Far:{1} Left:{2} Right:{3} Top:{4} Bottom:{5}",
            Near.ToString(format, formatProvider), Far.ToString(format, formatProvider), Left.ToString(format, formatProvider),
            Right.ToString(format, formatProvider), Top.ToString(format, formatProvider), Bottom.ToString(format, formatProvider));

    private bool Skips(int index) => index == FarIndex && !EnableFarPlaneCheck;

    private T Side(Plane<T> plane, int corner)
    {
        var direction = directions[corner];
        if (direction == Vector3<T>.Zero)
        {
            return Distance(plane, corners[corner]);
        }

        var along = Along(plane.Normal, direction);
        return along == T.Zero ? Distance(plane, corners[corner]) : along * T.PositiveInfinity;
    }

    private PlaneIntersectionType SideOf(Plane<T> plane, int corner)
    {
        var side = Side(plane, corner);
        return side > T.Zero ? PlaneIntersectionType.Front : side < T.Zero ? PlaneIntersectionType.Back : PlaneIntersectionType.Intersecting;
    }

    private T Reach(int corner, Vector3<T> direction)
    {
        var edge = directions[corner];
        if (edge == Vector3<T>.Zero)
        {
            return Collision.Reach(direction, corners[corner]);
        }

        var along = Along(direction, edge);
        return along == T.Zero ? Collision.Reach(direction, corners[corner]) : along * T.PositiveInfinity;
    }

    private Vector3<T> Corner(int index) =>
        directions[index] == Vector3<T>.Zero ? corners[index] : AtInfinity(corners[index], directions[index]);

    private static T Along(Vector3<T> direction, Vector3<T> edge)
    {
        var along = Vector3<T>.Dot(direction, edge);
        return T.Abs(along) <= Collision.Parallel<T>() * Collision.LengthScaled(direction) * Collision.LengthScaled(edge) ? T.Zero : along;
    }

    private static T Distance(Plane<T> plane, Vector3<T> point)
    {
        if (plane.Normal != Vector3<T>.Zero)
        {
            return Collision.Reach(plane.Normal, point) + plane.D;
        }

        return plane.D > T.Zero ? T.PositiveInfinity : plane.D <= T.Zero ? T.NegativeInfinity : plane.D;
    }

    private static Vector4<T> Column(Matrix4x4<T> matrix, int index) =>
        new(matrix.Row1[index], matrix.Row2[index], matrix.Row3[index], matrix.Row4[index]);

    private static Plane<T> Inward(Vector4<T> first, Vector4<T> second)
    {
        var largest = T.Max(Largest(first), Largest(second));
        if (largest > T.One && T.IsFinite(largest))
        {
            var scale = T.ScaleB(T.One, -T.ILogB(largest));
            first *= scale;
            second *= scale;
        }

        var coefficients = first + second;
        var normal = (Vector3<T>)coefficients;
        var length = Collision.LengthScaled(normal);
        return length == T.Zero ? new Plane<T>(normal, coefficients.W) : new Plane<T>(normal / length, coefficients.W / length);
    }

    private static T Largest(Vector4<T> value) =>
        T.Max(T.Max(T.Abs(value.X), T.Abs(value.Y)), T.Max(T.Abs(value.Z), T.Abs(value.W)));

    private static Vector3<T> OnBoth(Plane<T> first, Plane<T> second, Vector3<T> direction) =>
        Vector3<T>.Cross(first.Normal * second.D - second.Normal * first.D, direction) / direction.LengthSquared();

    private static Vector3<T> Meet(Plane<T> first, Plane<T> second, Plane<T> third)
    {
        var secondThird = Vector3<T>.Cross(second.Normal, third.Normal);
        var sum = secondThird * first.D + Vector3<T>.Cross(third.Normal, first.Normal) * second.D
                  + Vector3<T>.Cross(first.Normal, second.Normal) * third.D;
        return sum / -Vector3<T>.Dot(first.Normal, secondThird);
    }

    private static Vector3<T> AtInfinity(Vector3<T> start, Vector3<T> direction) =>
        new(Toward(start.X, Along(Vector3<T>.UnitX, direction)), Toward(start.Y, Along(Vector3<T>.UnitY, direction)),
            Toward(start.Z, Along(Vector3<T>.UnitZ, direction)));

    private static T Toward(T start, T along) =>
        along > T.Zero ? T.PositiveInfinity : along < T.Zero ? T.NegativeInfinity : along == T.Zero ? start : T.NaN;

    [InlineArray(PlaneCount)]
    private struct Planes
    {
        private Plane<T> first;
    }

    [InlineArray(CornerCount)]
    private struct Corners
    {
        private Vector3<T> first;
    }
}
