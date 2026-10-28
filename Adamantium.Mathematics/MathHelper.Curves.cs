using System;
using System.Collections.Generic;
using System.Numerics;

namespace Adamantium.Mathematics;

public static partial class MathHelper
{
    /// <summary>Curves in the plane sampled into points, appended to a list the caller keeps: Bézier spans of any degree,
    /// quadratic B-splines, NURBS and circular arcs. A curve sampled in n segments gives n + 1 points at its parameter's
    /// i / n, its ends exactly where they are.</summary>
    public static class Curves
    {
        private const int StackLimit = 32;

        /// <summary>Appends the Bézier span through <paramref name="controls"/> - its start, its control points and its
        /// end: three for a quadratic, four for a cubic, more for higher degrees - sampled in
        /// <paramref name="segments"/>.</summary>
        /// <exception cref="ArgumentException">There are fewer than two controls.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="segments"/> is below one.</exception>
        public static void AppendBezier<T>(ReadOnlySpan<Vector2<T>> controls, int segments, List<Vector2<T>> points)
            where T : unmanaged, IFloatingPointIeee754<T>
        {
            if (controls.Length < 2)
            {
                throw new ArgumentException("A Bézier span needs at least its start and its end.", nameof(controls));
            }

            ArgumentOutOfRangeException.ThrowIfLessThan(segments, 1);
            Span<Vector2<T>> work = controls.Length <= StackLimit ? stackalloc Vector2<T>[controls.Length] : new Vector2<T>[controls.Length];
            points.Add(controls[0]);
            for (var i = 1; i < segments; i++)
            {
                var amount = T.CreateTruncating(i) / T.CreateTruncating(segments);
                controls.CopyTo(work);
                for (var level = work.Length - 1; level > 0; level--)
                {
                    for (var k = 0; k < level; k++)
                    {
                        work[k] = Vector2<T>.Lerp(work[k], work[k + 1], amount);
                    }
                }

                points.Add(work[0]);
            }

            points.Add(controls[^1]);
        }

        /// <summary>Appends the quadratic Bézier span from <paramref name="start"/> past <paramref name="control"/> to
        /// <paramref name="end"/>, sampled in <paramref name="segments"/>.</summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="segments"/> is below one.</exception>
        public static void AppendQuadraticBezier<T>(Vector2<T> start, Vector2<T> control, Vector2<T> end, int segments,
            List<Vector2<T>> points) where T : unmanaged, IFloatingPointIeee754<T>
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(segments, 1);
            var two = T.CreateTruncating(2);
            points.Add(start);
            for (var i = 1; i < segments; i++)
            {
                var amount = T.CreateTruncating(i) / T.CreateTruncating(segments);
                var rest = T.One - amount;
                points.Add(rest * rest * start + two * amount * rest * control + amount * amount * end);
            }

            points.Add(end);
        }

        /// <summary>Appends the cubic Bézier span from <paramref name="start"/> past <paramref name="control1"/> and
        /// <paramref name="control2"/> to <paramref name="end"/>, sampled in <paramref name="segments"/>.</summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="segments"/> is below one.</exception>
        public static void AppendCubicBezier<T>(Vector2<T> start, Vector2<T> control1, Vector2<T> control2, Vector2<T> end,
            int segments, List<Vector2<T>> points) where T : unmanaged, IFloatingPointIeee754<T>
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(segments, 1);
            var three = T.CreateTruncating(3);
            points.Add(start);
            for (var i = 1; i < segments; i++)
            {
                var amount = T.CreateTruncating(i) / T.CreateTruncating(segments);
                var rest = T.One - amount;
                points.Add(rest * rest * rest * start + three * amount * rest * rest * control1 + three * amount * amount * rest * control2
                           + amount * amount * amount * end);
            }

            points.Add(end);
        }

        /// <summary>Appends the uniform quadratic B-spline of <paramref name="controls"/>, each of its pieces - one fewer
        /// than the controls less one - sampled in <paramref name="segments"/>: it starts halfway along the first control
        /// leg, ends halfway along the last and passes near the controls between. Fewer than three controls append
        /// nothing.</summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="segments"/> is below one.</exception>
        public static void AppendQuadraticBSpline<T>(ReadOnlySpan<Vector2<T>> controls, int segments, List<Vector2<T>> points)
            where T : unmanaged, IFloatingPointIeee754<T>
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(segments, 1);
            if (controls.Length < 3)
            {
                return;
            }

            var half = T.One / T.CreateTruncating(2);
            for (var piece = 1; piece < controls.Length - 1; piece++)
            {
                var last = piece == controls.Length - 2 ? segments : segments - 1;
                for (var i = 0; i <= last; i++)
                {
                    var u = T.CreateTruncating(i) / T.CreateTruncating(segments);
                    var rest = T.One - u;
                    points.Add(half * rest * rest * controls[piece - 1] + (half + u - u * u) * controls[piece] + half * u * u * controls[piece + 1]);
                }
            }
        }

        /// <summary>Appends the NURBS of <paramref name="controls"/> of <paramref name="degree"/>, its parameter from 0 to 1
        /// sampled in <paramref name="segments"/>, a point that repeats the one before left out. Each control pulls the
        /// curve by its weight - <paramref name="weights"/>, one for each control, the missing ones 1; equal weights give a
        /// plain B-spline. <paramref name="uniform"/> spaces the knots evenly and samples the whole knot range, past the
        /// part a B-spline is defined on, each point divided by the weights that reach it: the curve starts at the first
        /// control, dwells there and in the end at the last. Otherwise the end knots repeat, clamping the curve to its
        /// first and last control.</summary>
        /// <exception cref="ArgumentException">There are no more controls than <paramref name="degree"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="degree"/> or <paramref name="segments"/> is below
        /// one.</exception>
        public static void AppendNurbs<T>(ReadOnlySpan<Vector2<T>> controls, ReadOnlySpan<T> weights, int degree, bool uniform,
            int segments, List<Vector2<T>> points) where T : unmanaged, IFloatingPointIeee754<T>
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(degree, 1);
            ArgumentOutOfRangeException.ThrowIfLessThan(segments, 1);
            if (controls.Length <= degree)
            {
                throw new ArgumentException("A NURBS needs more controls than its degree.", nameof(controls));
            }

            var knotCount = controls.Length + degree + 1;
            Span<T> knots = knotCount <= StackLimit ? stackalloc T[knotCount] : new T[knotCount];
            FillKnots(knots, degree, uniform);
            Span<T> basis = degree + 1 <= StackLimit ? stackalloc T[degree + 1] : new T[degree + 1];
            var first = points.Count;
            for (var step = 0; step <= segments; step++)
            {
                var point = NurbsPoint(controls, weights, degree, knots, T.CreateTruncating(step) / T.CreateTruncating(segments), basis);
                if (points.Count == first || points[^1] != point)
                {
                    points.Add(point);
                }
            }
        }

        /// <summary>Appends the circular arc of <paramref name="radius"/> from <paramref name="start"/> to
        /// <paramref name="end"/> - the shorter one, turning clockwise on a screen, Y down, when
        /// <paramref name="clockwise"/>, counterclockwise otherwise - a point every <paramref name="step"/> radians and
        /// both ends exactly - a last gap shorter than a thousandth of a step is left out - or the start alone when it is
        /// the end. A radius shorter than half the chord is taken as half the chord: a half circle.</summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="step"/> is not above zero, or so small that the
        /// points could not be counted.</exception>
        public static void AppendArc<T>(Vector2<T> start, Vector2<T> end, T radius, bool clockwise, T step, List<Vector2<T>> points)
            where T : unmanaged, IFloatingPointIeee754<T>
        {
            if (!(step > T.Zero))
            {
                throw new ArgumentOutOfRangeException(nameof(step), step, "The step between an arc's points must be above zero.");
            }

            var two = T.CreateTruncating(2);
            var halfChord = (end - start) / two;
            var half = halfChord.Length();
            points.Add(start);
            if (half == T.Zero)
            {
                return;
            }

            radius = T.Max(T.Abs(radius), half);
            var side = clockwise ? T.One : -T.One;
            var toCenter = side * T.Sqrt(T.Max(radius * radius - half * half, T.Zero)) / half;
            var center = (start + end) / two + new Vector2<T>(-halfChord.Y, halfChord.X) * toCenter;
            var startAngle = T.Atan2(start.Y - center.Y, start.X - center.X);
            var sweep = side * two * T.Asin(T.Min(half / radius, T.One));
            var steps = T.Abs(sweep) / step;
            if (!(steps < T.CreateTruncating(int.MaxValue)))
            {
                throw new ArgumentOutOfRangeException(nameof(step), step, "The step is too small for the arc's points to be counted.");
            }

            var count = int.CreateTruncating(T.Ceiling(steps));
            if (count > 1 && steps - T.CreateTruncating(count - 1) < T.CreateTruncating(1e-3))
            {
                count--;
            }

            for (var i = 1; i < count; i++)
            {
                var angle = startAngle + side * step * T.CreateTruncating(i);
                points.Add(new Vector2<T>(center.X + radius * T.Cos(angle), center.Y + radius * T.Sin(angle)));
            }

            points.Add(end);
        }

        private static void FillKnots<T>(Span<T> knots, int degree, bool uniform) where T : unmanaged, IFloatingPointIeee754<T>
        {
            var last = knots.Length - 1;
            for (var i = 0; i <= last; i++)
            {
                knots[i] = uniform
                    ? T.CreateTruncating(i) / T.CreateTruncating(last)
                    : i <= degree ? T.Zero
                    : i >= last - degree ? T.One
                    : T.CreateTruncating(i - degree) / T.CreateTruncating(last - 2 * degree);
            }
        }

        private static Vector2<T> NurbsPoint<T>(ReadOnlySpan<Vector2<T>> controls, ReadOnlySpan<T> weights, int degree, ReadOnlySpan<T> knots,
            T t, Span<T> basis) where T : unmanaged, IFloatingPointIeee754<T>
        {
            var total = T.Zero;
            for (var i = 0; i < controls.Length; i++)
            {
                total += Basis(i, degree, knots, t, basis) * (i < weights.Length ? weights[i] : T.One);
            }

            if (total == T.Zero)
            {
                return controls[0];
            }

            var sum = Vector2<T>.Zero;
            for (var i = 0; i < controls.Length; i++)
            {
                sum += controls[i] * (Basis(i, degree, knots, t, basis) * (i < weights.Length ? weights[i] : T.One) / total);
            }

            return sum;
        }

        private static T Basis<T>(int i, int degree, ReadOnlySpan<T> knots, T t, Span<T> n) where T : unmanaged, IFloatingPointIeee754<T>
        {
            var m = knots.Length - 1;
            if ((i == 0 && t == knots[0]) || (i == m - degree - 1 && t == knots[m]))
            {
                return T.One;
            }

            if (t < knots[i] || t >= knots[i + degree + 1])
            {
                return T.Zero;
            }

            for (var j = 0; j <= degree; j++)
            {
                n[j] = t >= knots[i + j] && t < knots[i + j + 1] ? T.One : T.Zero;
            }

            for (var k = 1; k <= degree; k++)
            {
                var saved = n[0] == T.Zero ? T.Zero : (t - knots[i]) * n[0] / (knots[i + k] - knots[i]);
                for (var j = 0; j < degree - k + 1; j++)
                {
                    var (left, right) = (knots[i + j + 1], knots[i + j + k + 1]);
                    if (n[j + 1] == T.Zero)
                    {
                        n[j] = saved;
                        saved = T.Zero;
                    }
                    else
                    {
                        var temp = n[j + 1] / (right - left);
                        n[j] = saved + (right - t) * temp;
                        saved = (t - left) * temp;
                    }
                }
            }

            return n[0];
        }
    }
}
