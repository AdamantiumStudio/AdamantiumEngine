using System;
using System.Numerics;

namespace Adamantium.Mathematics;

public static partial class MathHelper
{
    /// <summary>Points in a row in the plane - a polyline, or a polygon when its last point joins its first: length,
    /// even resampling, winding and convexity, and a point's distance to a line.</summary>
    public static class Polyline
    {
        /// <summary>The sum of the lengths of the segments between consecutive <paramref name="points"/>.</summary>
        public static T Length<T>(ReadOnlySpan<Vector2<T>> points) where T : unmanaged, IFloatingPointIeee754<T>
        {
            var length = T.Zero;
            for (var i = 1; i < points.Length; i++)
            {
                length += Vector2<T>.Distance(points[i - 1], points[i]);
            }

            return length;
        }

        /// <summary>Fills <paramref name="resampled"/> with points along <paramref name="points"/> evenly spaced by length:
        /// the first and the last where those are, the rest between - so a curve sampled by its parameter, bunched where it
        /// is slow and sparse where it is fast, comes out even. A polyline of no length gives its first point, then its
        /// last.</summary>
        /// <exception cref="ArgumentException"><paramref name="points"/> is empty, or <paramref name="resampled"/> holds
        /// fewer than two points.</exception>
        public static void Resample<T>(ReadOnlySpan<Vector2<T>> points, Span<Vector2<T>> resampled) where T : unmanaged, IFloatingPointIeee754<T>
        {
            if (points.IsEmpty)
            {
                throw new ArgumentException("There are no points to resample.", nameof(points));
            }

            if (resampled.Length < 2)
            {
                throw new ArgumentException("A resampled polyline needs at least its two ends.", nameof(resampled));
            }

            var total = Length(points);
            var last = resampled.Length - 1;
            resampled[0] = points[0];
            resampled[last] = points[^1];
            if (!(total > T.Zero))
            {
                resampled[1..last].Fill(points[^1]);
                return;
            }

            var segment = 1;
            var walked = T.Zero;
            var segmentLength = points.Length > 1 ? Vector2<T>.Distance(points[0], points[1]) : T.Zero;
            for (var i = 1; i < last; i++)
            {
                var target = total * T.CreateTruncating(i) / T.CreateTruncating(last);
                while (segment < points.Length - 1 && walked + segmentLength < target)
                {
                    walked += segmentLength;
                    segment++;
                    segmentLength = Vector2<T>.Distance(points[segment - 1], points[segment]);
                }

                var amount = segmentLength > T.Zero ? T.Clamp((target - walked) / segmentLength, T.Zero, T.One) : T.Zero;
                resampled[i] = Vector2<T>.Lerp(points[segment - 1], points[segment], amount);
            }
        }

        /// <summary>Whether <paramref name="first"/>, <paramref name="second"/>, <paramref name="third"/> turn clockwise on
        /// a screen, Y down - counterclockwise with Y up; points in a line turn neither way.</summary>
        public static bool IsClockwise<T>(Vector2<T> first, Vector2<T> second, Vector2<T> third) where T : unmanaged, IFloatingPointIeee754<T> =>
            Vector2<T>.Determinant(second - first, third - first) > T.Zero;

        /// <summary>Whether the polygon through <paramref name="points"/>, its last joined to its first, is convex: it turns
        /// one way all round, once - a star that turns one way but goes round twice is not. A spike that turns straight back
        /// makes it not convex; points in a line along a side, or repeated, do not. Fewer than three sides are no
        /// polygon.</summary>
        public static bool IsConvex<T>(ReadOnlySpan<Vector2<T>> points) where T : unmanaged, IFloatingPointIeee754<T>
        {
            var previous = Vector2<T>.Zero;
            for (var i = points.Length - 1; i >= 0 && previous == Vector2<T>.Zero; i--)
            {
                previous = points[(i + 1) % points.Length] - points[i];
            }

            var sign = 0;
            var sides = 0;
            var turned = T.Zero;
            for (var i = 0; i < points.Length; i++)
            {
                var edge = points[(i + 1) % points.Length] - points[i];
                if (edge == Vector2<T>.Zero)
                {
                    continue;
                }

                sides++;
                var cross = Vector2<T>.Determinant(previous, edge);
                var dot = Vector2<T>.Dot(previous, edge);
                previous = edge;
                if ((cross == T.Zero && dot < T.Zero) || T.IsNaN(cross) || T.IsNaN(dot))
                {
                    return false;
                }

                var side = T.Sign(cross);
                if (side != 0)
                {
                    if (sign == 0)
                    {
                        sign = side;
                    }
                    else if (side != sign)
                    {
                        return false;
                    }
                }

                turned += T.Atan2(cross, dot);
            }

            var round = T.Abs(turned);
            return sides >= 3 && round > T.Pi && round < T.Pi + T.Tau;
        }

        /// <summary>The distance from <paramref name="point"/> to the line through <paramref name="start"/> and
        /// <paramref name="end"/> - the whole line, not only the segment; to <paramref name="start"/> when the two are
        /// one point.</summary>
        public static T DistanceToLine<T>(Vector2<T> start, Vector2<T> end, Vector2<T> point) where T : unmanaged, IFloatingPointIeee754<T>
        {
            var along = end - start;
            var length = along.Length();
            return length == T.Zero ? Vector2<T>.Distance(start, point) : T.Abs(Vector2<T>.Determinant(along, point - start)) / length;
        }
    }
}
