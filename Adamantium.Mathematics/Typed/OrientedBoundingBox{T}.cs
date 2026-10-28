using System;
using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Adamantium.Mathematics.Typed;

/// <summary>
/// A box of fractional numbers turned by <see cref="Orientation"/>, of a length of one, around <see cref="Center"/>; its
/// surface part of it, so touching counts as meeting. Each test brings the other shape into the box's own axes and
/// answers there as <see cref="Collision"/> does for an axis-aligned box: a NaN is outside, a ray's distances are in
/// units of its direction. A box of a half extent below zero, as one made from an axis-aligned box turned inside out,
/// holds nothing. Made with <see cref="OrientedBoundingBox"/>'s factories. Equality is exact;
/// <see cref="NearEqual"/> compares within a tolerance.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct OrientedBoundingBox<T> : IEquatable<OrientedBoundingBox<T>>, IFormattable where T : unmanaged, IFloatingPointIeee754<T>
{
    /// <summary>How many corners a box has.</summary>
    public const int CornerCount = 8;

    /// <summary>The middle of the box.</summary>
    public Vector3<T> Center;

    /// <summary>Half the box's size along each of its own axes.</summary>
    public Vector3<T> HalfExtent;

    /// <summary>The turn from the world's axes to the box's own, of length one.</summary>
    public Quaternion<T> Orientation;

    /// <summary>A box around <paramref name="center"/>, <paramref name="halfExtent"/> to each side along its own axes,
    /// turned by <paramref name="orientation"/>.</summary>
    public OrientedBoundingBox(Vector3<T> center, Vector3<T> halfExtent, Quaternion<T> orientation)
    {
        Center = center;
        HalfExtent = halfExtent;
        Orientation = orientation;
    }

    /// <summary>The box's full size along its own axes.</summary>
    public readonly Vector3<T> Size => HalfExtent + HalfExtent;

    /// <summary>Copies the corners to <paramref name="corners"/>, in the order of <see cref="BoundingBox{T}.GetCorners"/>
    /// for the box of its own axes, turned and moved into place.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="corners"/> holds fewer than
    /// <see cref="CornerCount"/>.</exception>
    public readonly void GetCorners(Span<Vector3<T>> corners)
    {
        new BoundingBox<T>(-HalfExtent, HalfExtent).GetCorners(corners);
        var axes = Axes();
        for (var i = 0; i < CornerCount; i++)
        {
            corners[i] = ToWorld(corners[i], axes);
        }
    }

    /// <summary>The corner farthest along <paramref name="direction"/>; for a direction square to an axis, the greater
    /// side.</summary>
    public readonly Vector3<T> Support(Vector3<T> direction)
    {
        var axes = Axes();
        var local = new Vector3<T>(Vector3<T>.Dot(direction, axes.Row1), Vector3<T>.Dot(direction, axes.Row2), Vector3<T>.Dot(direction, axes.Row3));
        return ToWorld(new BoundingBox<T>(-HalfExtent, HalfExtent).Support(local), axes);
    }

    /// <summary>The smallest axis-aligned box holding this one.</summary>
    public readonly BoundingBox<T> GetBoundingBox()
    {
        var axes = Axes();
        var reach = Vector3<T>.Abs(axes.Row1) * HalfExtent.X + Vector3<T>.Abs(axes.Row2) * HalfExtent.Y + Vector3<T>.Abs(axes.Row3) * HalfExtent.Z;
        return new BoundingBox<T>(Center - reach, Center + reach);
    }

    /// <summary>The box turned by <paramref name="rotation"/> around the origin, then moved by
    /// <paramref name="translation"/>.</summary>
    public readonly OrientedBoundingBox<T> Transform(Quaternion<T> rotation, Vector3<T> translation) =>
        new(Vector3<T>.Transform(Center, rotation) + translation, HalfExtent, rotation * Orientation);

    /// <summary>The box scaled by <paramref name="scale"/> about the origin, turned by <paramref name="rotation"/>
    /// around it, then moved by <paramref name="translation"/>.</summary>
    public readonly OrientedBoundingBox<T> Transform(T scale, Quaternion<T> rotation, Vector3<T> translation) =>
        new(Vector3<T>.Transform(Center * scale, rotation) + translation, HalfExtent * T.Abs(scale), rotation * Orientation);

    /// <summary>The smallest box along the carried axes holding this one scaled by <paramref name="scale"/> along the
    /// world's axes about the origin, turned by <paramref name="rotation"/>, then moved by
    /// <paramref name="translation"/>.</summary>
    public readonly OrientedBoundingBox<T> Transform(Vector3<T> scale, Quaternion<T> rotation, Vector3<T> translation) =>
        Transform(Matrix4x4.CreateScale(scale) * Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(translation));

    /// <summary>The box carried by the affine <paramref name="transform"/>: exact while it keeps the box's axes square
    /// to each other - a turn, a move, a scale along them; otherwise the smallest box holding the carried one, along
    /// axes taken from the carried ones, longest first. A box that holds nothing stays so, its center carried.</summary>
    public readonly OrientedBoundingBox<T> Transform(Matrix4x4<T> transform)
    {
        if (IsEmpty)
        {
            return new OrientedBoundingBox<T>(Vector3<T>.TransformCoordinate(Center, transform), HalfExtent, Orientation);
        }

        var axes = Axes();
        Span<Vector3<T>> carried =
        [
            Vector3<T>.TransformNormal(axes.Row1 * HalfExtent.X, transform),
            Vector3<T>.TransformNormal(axes.Row2 * HalfExtent.Y, transform),
            Vector3<T>.TransformNormal(axes.Row3 * HalfExtent.Z, transform)
        ];
        Span<Vector3<T>> basis = stackalloc Vector3<T>[3];
        Orthonormalize(carried, basis);
        var halfExtent = Vector3<T>.Zero;
        for (var j = 0; j < 3; j++)
        {
            halfExtent[j] = T.Abs(Vector3<T>.Dot(carried[0], basis[j])) + T.Abs(Vector3<T>.Dot(carried[1], basis[j]))
                            + T.Abs(Vector3<T>.Dot(carried[2], basis[j]));
        }

        var orientation = Quaternion.CreateFromRotationMatrix(new Matrix3x3<T>(basis[0], basis[1], basis[2]));
        return new OrientedBoundingBox<T>(Vector3<T>.TransformCoordinate(Center, transform), halfExtent, orientation);
    }

    /// <summary>Whether <paramref name="point"/> is inside or outside; on the surface is inside.</summary>
    public readonly ContainmentType Contains(Vector3<T> point) =>
        IsEmpty ? ContainmentType.Disjoint : Collision.BoxContainsPoint(LocalBox(), ToLocal(point, Axes()));

    /// <summary>Whether all of <paramref name="points"/> are inside, some, or none; no points are none.</summary>
    public readonly ContainmentType Contains(ReadOnlySpan<Vector3<T>> points)
    {
        if (IsEmpty)
        {
            return ContainmentType.Disjoint;
        }

        var (box, axes) = (LocalBox(), Axes());
        var inside = 0;
        foreach (var point in points)
        {
            if (Collision.BoxContainsPoint(box, ToLocal(point, axes)) == ContainmentType.Contains)
            {
                inside++;
            }
        }

        return inside == 0 ? ContainmentType.Disjoint : inside == points.Length ? ContainmentType.Contains : ContainmentType.Intersects;
    }

    /// <summary>Whether the segment from <paramref name="start"/> to <paramref name="end"/> is inside, crosses the
    /// surface, or is outside.</summary>
    public readonly ContainmentType Contains(Vector3<T> start, Vector3<T> end)
    {
        if (IsEmpty)
        {
            return ContainmentType.Disjoint;
        }

        var (box, axes) = (LocalBox(), Axes());
        var (from, to) = (ToLocal(start, axes), ToLocal(end, axes));
        var ends = (Collision.BoxContainsPoint(box, from) == ContainmentType.Contains ? 1 : 0)
                   + (Collision.BoxContainsPoint(box, to) == ContainmentType.Contains ? 1 : 0);
        if (ends > 0)
        {
            return ends == 2 ? ContainmentType.Contains : ContainmentType.Intersects;
        }

        var middle = (from + to) / (T.One + T.One);
        var half = to - middle;
        var reach = Vector3<T>.Abs(half);
        for (var i = 0; i < 3; i++)
        {
            var (i1, i2) = ((i + 1) % 3, (i + 2) % 3);
            if (!(T.Abs(middle[i]) <= HalfExtent[i] + reach[i])
                || !(T.Abs(middle[i1] * half[i2] - middle[i2] * half[i1]) <= HalfExtent[i1] * reach[i2] + HalfExtent[i2] * reach[i1]))
            {
                return ContainmentType.Disjoint;
            }
        }

        return ContainmentType.Intersects;
    }

    /// <summary>Whether <paramref name="box"/> is inside, crosses the surface, or is outside.</summary>
    public readonly ContainmentType Contains(BoundingBox<T> box) => Contains(OrientedBoundingBox.CreateFromBox(box));

    /// <summary>Whether <paramref name="box"/> is inside, crosses the surface, or is outside, by the separating axes of
    /// the two boxes.</summary>
    public readonly ContainmentType Contains(OrientedBoundingBox<T> box)
    {
        if (IsEmpty || box.IsEmpty)
        {
            return ContainmentType.Disjoint;
        }

        var (axes, other) = (Axes(), box.Axes());
        var rotation = Orientation == box.Orientation
            ? Matrix3x3<T>.Identity
            : new Matrix3x3<T>(
                new Vector3<T>(Vector3<T>.Dot(axes.Row1, other.Row1), Vector3<T>.Dot(axes.Row1, other.Row2), Vector3<T>.Dot(axes.Row1, other.Row3)),
                new Vector3<T>(Vector3<T>.Dot(axes.Row2, other.Row1), Vector3<T>.Dot(axes.Row2, other.Row2), Vector3<T>.Dot(axes.Row2, other.Row3)),
                new Vector3<T>(Vector3<T>.Dot(axes.Row3, other.Row1), Vector3<T>.Dot(axes.Row3, other.Row2), Vector3<T>.Dot(axes.Row3, other.Row3)));
        var offset = ToLocal(box.Center, axes);
        var (a, b) = (HalfExtent, box.HalfExtent);
        var magnitude = new Matrix3x3<T>(Vector3<T>.Abs(rotation.Row1), Vector3<T>.Abs(rotation.Row2), Vector3<T>.Abs(rotation.Row3));
        var contained = true;
        for (var i = 0; i < 3; i++)
        {
            contained &= T.Abs(offset[i]) + Collision.Reach(Row(magnitude, i), b) <= a[i];
        }

        if (contained)
        {
            return ContainmentType.Contains;
        }

        for (var i = 0; i < 3; i++)
        {
            if (!(T.Abs(offset[i]) <= a[i] + Collision.Reach(Row(magnitude, i), b))
                || !(T.Abs(Vector3<T>.Dot(offset, Column(rotation, i))) <= Collision.Reach(Column(magnitude, i), a) + b[i]))
            {
                return ContainmentType.Disjoint;
            }
        }

        var slack = new Vector3<T>(T.CreateTruncating(16) * (T.BitIncrement(T.One) - T.One));
        var absolute = new Matrix3x3<T>(magnitude.Row1 + slack, magnitude.Row2 + slack, magnitude.Row3 + slack);
        for (var i = 0; i < 3; i++)
        {
            var (i1, i2) = ((i + 1) % 3, (i + 2) % 3);
            for (var j = 0; j < 3; j++)
            {
                var (j1, j2) = ((j + 1) % 3, (j + 2) % 3);
                var apart = T.Abs(offset[i2] * Row(rotation, i1)[j] - offset[i1] * Row(rotation, i2)[j]);
                var reach = a[i1] * Row(absolute, i2)[j] + a[i2] * Row(absolute, i1)[j]
                            + b[j1] * Row(absolute, i)[j2] + b[j2] * Row(absolute, i)[j1];
                if (!(apart <= reach))
                {
                    return ContainmentType.Disjoint;
                }
            }
        }

        return ContainmentType.Intersects;
    }

    /// <summary>Whether <paramref name="sphere"/> is inside, crosses the surface, or is outside.</summary>
    public readonly ContainmentType Contains(BoundingSphere<T> sphere) =>
        IsEmpty ? ContainmentType.Disjoint : Collision.BoxContainsSphere(LocalBox(), ToLocal(sphere));

    /// <summary>Whether <paramref name="frustum"/> is inside, crosses the surface, or is outside: inside when all its
    /// corners are, outside when its planes or the box's own faces leave the other wholly behind one of them.</summary>
    public readonly ContainmentType Contains(BoundingFrustum<T> frustum)
    {
        if (IsEmpty || frustum.Contains(this) == ContainmentType.Disjoint)
        {
            return ContainmentType.Disjoint;
        }

        var axes = Axes();
        Span<Plane<T>> faces =
        [
            new(-axes.Row1, Vector3<T>.Dot(axes.Row1, Center) + HalfExtent.X), new(axes.Row1, HalfExtent.X - Vector3<T>.Dot(axes.Row1, Center)),
            new(-axes.Row2, Vector3<T>.Dot(axes.Row2, Center) + HalfExtent.Y), new(axes.Row2, HalfExtent.Y - Vector3<T>.Dot(axes.Row2, Center)),
            new(-axes.Row3, Vector3<T>.Dot(axes.Row3, Center) + HalfExtent.Z), new(axes.Row3, HalfExtent.Z - Vector3<T>.Dot(axes.Row3, Center))
        ];
        return frustum.CornersInside(faces);
    }

    /// <summary>Whether <paramref name="box"/> and this one share a point.</summary>
    public readonly bool Intersects(BoundingBox<T> box) => Contains(box) != ContainmentType.Disjoint;

    /// <summary>Whether <paramref name="box"/> and this one share a point.</summary>
    public readonly bool Intersects(OrientedBoundingBox<T> box) => Contains(box) != ContainmentType.Disjoint;

    /// <summary>Whether <paramref name="sphere"/> and the box share a point.</summary>
    public readonly bool Intersects(BoundingSphere<T> sphere) => Contains(sphere) != ContainmentType.Disjoint;

    /// <summary>Whether <paramref name="frustum"/> and the box share a point.</summary>
    public readonly bool Intersects(BoundingFrustum<T> frustum) => Contains(frustum) != ContainmentType.Disjoint;

    /// <summary>Which side of <paramref name="plane"/> the box is on, or whether it crosses or touches it; for a box that
    /// holds nothing, the side of its center.</summary>
    public readonly PlaneIntersectionType Intersects(Plane<T> plane)
    {
        if (IsEmpty)
        {
            return Collision.PlaneIntersectsPoint(plane, Center);
        }

        var axes = Axes();
        var normal = new Vector3<T>(Vector3<T>.Dot(plane.Normal, axes.Row1), Vector3<T>.Dot(plane.Normal, axes.Row2),
            Vector3<T>.Dot(plane.Normal, axes.Row3));
        return Collision.PlaneIntersectsBox(new Plane<T>(normal, Plane<T>.DotCoordinate(plane, Center)), LocalBox());
    }

    /// <summary>Whether <paramref name="ray"/> meets the box, from its start on.</summary>
    public readonly bool Intersects(Ray<T> ray) => Intersects(ray, out T _);

    /// <summary>Whether <paramref name="ray"/> meets the box, and how far along it; zero when it starts inside.</summary>
    public readonly bool Intersects(Ray<T> ray, out T distance)
    {
        if (IsEmpty)
        {
            distance = T.Zero;
            return false;
        }

        var axes = Axes();
        var direction = new Vector3<T>(Vector3<T>.Dot(ray.Direction, axes.Row1), Vector3<T>.Dot(ray.Direction, axes.Row2),
            Vector3<T>.Dot(ray.Direction, axes.Row3));
        return Collision.RayIntersectsBox(new Ray<T>(ToLocal(ray.Position, axes), direction), LocalBox(), out distance);
    }

    /// <summary>Whether <paramref name="ray"/> meets the box, and where; its start when it starts inside.</summary>
    public readonly bool Intersects(Ray<T> ray, out Vector3<T> point)
    {
        if (Intersects(ray, out T distance))
        {
            point = ray.Position + ray.Direction * distance;
            return true;
        }

        point = Vector3<T>.Zero;
        return false;
    }

    /// <summary>The frustum of the same volume: the box carried to the clip box from (-1, -1, 0) to (1, 1, 1), its far
    /// plane checked. A box that holds nothing gives the default frustum, which sees nothing; a box too thin along an
    /// axis for its inverse to be a number gives a matrix that is not finite.</summary>
    public readonly BoundingFrustum<T> ToFrustum()
    {
        if (IsEmpty)
        {
            return default;
        }

        var axes = Axes();
        var two = T.One + T.One;
        var scale = new Vector3<T>(T.One / HalfExtent.X, T.One / HalfExtent.Y, T.One / (two * HalfExtent.Z));
        var toClip = new Matrix4x4<T>(
            new Vector4<T>(axes.Row1.X * scale.X, axes.Row2.X * scale.Y, axes.Row3.X * scale.Z, T.Zero),
            new Vector4<T>(axes.Row1.Y * scale.X, axes.Row2.Y * scale.Y, axes.Row3.Y * scale.Z, T.Zero),
            new Vector4<T>(axes.Row1.Z * scale.X, axes.Row2.Z * scale.Y, axes.Row3.Z * scale.Z, T.Zero),
            new Vector4<T>(-Vector3<T>.Dot(Center, axes.Row1) * scale.X, -Vector3<T>.Dot(Center, axes.Row2) * scale.Y,
                -Vector3<T>.Dot(Center, axes.Row3) * scale.Z + T.One / two, T.One));
        return new BoundingFrustum<T>(toClip, true);
    }

    /// <summary>The box in another number type, each value converted as a cast would.</summary>
    public readonly OrientedBoundingBox<TOther> As<TOther>() where TOther : unmanaged, IFloatingPointIeee754<TOther> =>
        new(Center.As<TOther>(), HalfExtent.As<TOther>(), Orientation.As<TOther>());

    /// <summary>Whether the centers, half extents and orientations are within
    /// <see cref="MathHelper.Scalar.NearEqual{T}"/>.</summary>
    public static bool NearEqual(OrientedBoundingBox<T> left, OrientedBoundingBox<T> right) =>
        Vector3<T>.NearEqual(left.Center, right.Center) && Vector3<T>.NearEqual(left.HalfExtent, right.HalfExtent)
        && Quaternion<T>.NearEqual(left.Orientation, right.Orientation);

    /// <summary>Whether the centers, half extents and orientations are equal; NaN equals nothing.</summary>
    public static bool operator ==(OrientedBoundingBox<T> left, OrientedBoundingBox<T> right) =>
        left.Center == right.Center && left.HalfExtent == right.HalfExtent && left.Orientation == right.Orientation;

    /// <summary>Whether the centers, half extents or orientations differ; NaN differs from everything.</summary>
    public static bool operator !=(OrientedBoundingBox<T> left, OrientedBoundingBox<T> right) => !(left == right);

    /// <summary>Whether the centers, half extents and orientations are equal, a NaN to a NaN too.</summary>
    public readonly bool Equals(OrientedBoundingBox<T> other) =>
        Center.Equals(other.Center) && HalfExtent.Equals(other.HalfExtent) && Orientation.Equals(other.Orientation);

    /// <inheritdoc />
    public override readonly bool Equals(object obj) => obj is OrientedBoundingBox<T> other && Equals(other);

    /// <inheritdoc />
    public override readonly int GetHashCode() => HashCode.Combine(Center, HalfExtent, Orientation);

    /// <inheritdoc />
    public override readonly string ToString() => ToString(null, CultureInfo.CurrentCulture);

    /// <inheritdoc />
    public readonly string ToString(string format, IFormatProvider formatProvider) =>
        string.Format(formatProvider, "Center:{0} HalfExtent:{1} Orientation:{2}", Center.ToString(format, formatProvider),
            HalfExtent.ToString(format, formatProvider), Orientation.ToString(format, formatProvider));

    private readonly Matrix3x3<T> Axes() => Matrix3x3.CreateFromQuaternion(Orientation);

    /// <summary>Whether a half extent is below zero, or NaN: a box that holds nothing.</summary>
    internal readonly bool IsEmpty => !(HalfExtent.X >= T.Zero && HalfExtent.Y >= T.Zero && HalfExtent.Z >= T.Zero);

    /// <summary>The box in its own axes, around the origin.</summary>
    internal readonly BoundingBox<T> LocalBox() => new(-HalfExtent, HalfExtent);

    /// <summary><paramref name="sphere"/> in the box's own axes, the box's center at the origin.</summary>
    internal readonly BoundingSphere<T> ToLocal(BoundingSphere<T> sphere) => new(ToLocal(sphere.Center, Axes()), sphere.Radius);

    private readonly Vector3<T> ToLocal(Vector3<T> point, Matrix3x3<T> axes)
    {
        var offset = point - Center;
        return new Vector3<T>(Vector3<T>.Dot(offset, axes.Row1), Vector3<T>.Dot(offset, axes.Row2), Vector3<T>.Dot(offset, axes.Row3));
    }

    private readonly Vector3<T> ToWorld(Vector3<T> local, Matrix3x3<T> axes) =>
        Center + axes.Row1 * local.X + axes.Row2 * local.Y + axes.Row3 * local.Z;

    private static Vector3<T> Row(Matrix3x3<T> matrix, int index) => index switch
    {
        0 => matrix.Row1,
        1 => matrix.Row2,
        _ => matrix.Row3
    };

    private static Vector3<T> Column(Matrix3x3<T> matrix, int index) => new(matrix.Row1[index], matrix.Row2[index], matrix.Row3[index]);

    private static void Orthonormalize(ReadOnlySpan<Vector3<T>> axes, Span<Vector3<T>> basis)
    {
        Span<int> order = [0, 1, 2];
        Span<T> lengths = [Collision.LengthScaled(axes[0]), Collision.LengthScaled(axes[1]), Collision.LengthScaled(axes[2])];
        for (var i = 0; i < 2; i++)
        {
            for (var j = 2; j > i; j--)
            {
                if (lengths[order[j]] > lengths[order[j - 1]])
                {
                    (order[j], order[j - 1]) = (order[j - 1], order[j]);
                }
            }
        }

        var first = lengths[order[0]] > T.Zero ? axes[order[0]] / lengths[order[0]] : Unit(order[0]);
        var second = axes[order[1]] - first * Vector3<T>.Dot(axes[order[1]], first);
        var secondLength = Collision.LengthScaled(second);
        second = secondLength > Collision.Parallel<T>() * lengths[order[0]] ? second / secondLength : Perpendicular(first);
        var third = Vector3<T>.Cross(first, second);
        basis[order[0]] = first;
        basis[order[1]] = second;
        basis[order[2]] = third;
        if (Vector3<T>.Dot(Vector3<T>.Cross(basis[0], basis[1]), basis[2]) < T.Zero)
        {
            basis[order[2]] = -third;
        }
    }

    private static Vector3<T> Unit(int axis) => axis == 0 ? Vector3<T>.UnitX : axis == 1 ? Vector3<T>.UnitY : Vector3<T>.UnitZ;

    private static Vector3<T> Perpendicular(Vector3<T> direction)
    {
        var across = T.Abs(direction.X) <= T.Abs(direction.Y) && T.Abs(direction.X) <= T.Abs(direction.Z) ? Vector3<T>.UnitX
            : T.Abs(direction.Y) <= T.Abs(direction.Z) ? Vector3<T>.UnitY : Vector3<T>.UnitZ;
        var perpendicular = Vector3<T>.Cross(direction, across);
        return perpendicular / perpendicular.Length();
    }
}
