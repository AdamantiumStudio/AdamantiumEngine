using System;
using System.Numerics;

namespace Adamantium.Mathematics.Typed;

/// <summary>Makes <see cref="Vector2{T}"/>s and gives those of fractional numbers - float, double, Half - length,
/// normalizing and blending.</summary>
public static class Vector2
{
    /// <summary>A vector of <paramref name="x"/> and <paramref name="y"/>.</summary>
    public static Vector2<T> Create<T>(T x, T y) where T : unmanaged, INumber<T> => new(x, y);

    /// <summary>A vector of <paramref name="value"/> in every component.</summary>
    public static Vector2<T> Create<T>(T value) where T : unmanaged, INumber<T> => new(value);

    extension<T>(Vector2<T> vector) where T : unmanaged, IFloatingPointIeee754<T>
    {
        /// <summary>Whether the length is one within <see cref="Scalar.ZeroTolerance{T}"/>.</summary>
        public bool IsNormalized => Scalar.IsZero(vector.LengthSquared() - T.One);

        /// <summary>The length.</summary>
        public T Length() => T.Sqrt(vector.LengthSquared());

        /// <summary>Whether the two lie along one line, either way: the sine of the angle between them is within
        /// <see cref="Scalar.ZeroTolerance{T}"/>. A zero vector lies along every line.</summary>
        public bool IsCollinear(Vector2<T> other)
        {
            var (scale, otherScale) = (T.Max(T.Abs(vector.X), T.Abs(vector.Y)), T.Max(T.Abs(other.X), T.Abs(other.Y)));
            if (scale == T.Zero || otherScale == T.Zero)
            {
                return true;
            }

            var (a, b) = (DividedByLargest(vector, scale), DividedByLargest(other, otherScale));
            var determinant = Vector2<T>.Determinant(a, b);
            var tolerance = Scalar.ZeroTolerance<T>();
            return determinant * determinant <= tolerance * tolerance * a.LengthSquared() * b.LengthSquared();
        }
    }

    extension<T>(ref Vector2<T> vector) where T : unmanaged, IFloatingPointIeee754<T>
    {
        /// <summary>Scales the vector to a length of one; leaves it as it is when its length is within
        /// <see cref="Scalar.ZeroTolerance{T}"/> of zero.</summary>
        public void Normalize()
        {
            var length = vector.Length();
            if (!Scalar.IsZero(length))
            {
                var inverse = T.One / length;
                vector.X *= inverse;
                vector.Y *= inverse;
            }
        }
    }

    extension<T>(Vector2<T>) where T : unmanaged, IFloatingPointIeee754<T>
    {
        /// <summary><paramref name="value"/> scaled to a length of one, or as it is when its length is within
        /// <see cref="Scalar.ZeroTolerance{T}"/> of zero.</summary>
        public static Vector2<T> Normalize(Vector2<T> value)
        {
            value.Normalize();
            return value;
        }

        /// <summary>The distance between two points.</summary>
        public static T Distance(Vector2<T> value1, Vector2<T> value2) => (value1 - value2).Length();

        /// <summary>The average of the components.</summary>
        public static T Average(Vector2<T> value) => (value.X + value.Y) / T.CreateTruncating(2);

        /// <summary>Every component rounded to <paramref name="digits"/> fractional digits, halves away from zero.</summary>
        public static Vector2<T> Round(Vector2<T> value, int digits) =>
            new(T.Round(value.X, digits, MidpointRounding.AwayFromZero), T.Round(value.Y, digits, MidpointRounding.AwayFromZero));

        /// <summary>The blend of <paramref name="start"/> and <paramref name="end"/> at <paramref name="amount"/>:
        /// start at 0, end at 1.</summary>
        public static Vector2<T> Lerp(Vector2<T> start, Vector2<T> end, T amount) =>
            new(Scalar.Lerp(start.X, end.X, amount), Scalar.Lerp(start.Y, end.Y, amount));

        /// <summary>The blend of <paramref name="start"/> and <paramref name="end"/> at <paramref name="amount"/> eased
        /// in and out by <see cref="Scalar.SmoothStep{T}"/>.</summary>
        public static Vector2<T> SmoothStep(Vector2<T> start, Vector2<T> end, T amount) =>
            Lerp(start, end, Scalar.SmoothStep(amount));

        /// <summary>The point at <paramref name="amount"/> on the Hermite spline from <paramref name="value1"/> leaving
        /// along <paramref name="tangent1"/> to <paramref name="value2"/> arriving along <paramref name="tangent2"/>.</summary>
        public static Vector2<T> Hermite(Vector2<T> value1, Vector2<T> tangent1, Vector2<T> value2, Vector2<T> tangent2, T amount)
        {
            var weights = Scalar.HermiteWeights(amount);
            return value1 * weights.Start + value2 * weights.End + tangent1 * weights.StartTangent
                   + tangent2 * weights.EndTangent;
        }

        /// <summary>The point at <paramref name="amount"/> between <paramref name="value2"/> and
        /// <paramref name="value3"/> on the Catmull-Rom spline through all four.</summary>
        public static Vector2<T> CatmullRom(Vector2<T> value1, Vector2<T> value2, Vector2<T> value3, Vector2<T> value4, T amount)
        {
            var squared = amount * amount;
            var cubed = amount * squared;
            var two = T.CreateTruncating(2);
            var three = T.CreateTruncating(3);
            return T.CreateTruncating(0.5) * (two * value2 + (-value1 + value3) * amount
                                              + (two * value1 - T.CreateTruncating(5) * value2 + T.CreateTruncating(4) * value3 - value4) * squared
                                              + (-value1 + three * value2 - three * value3 + value4) * cubed);
        }

        /// <summary><paramref name="vector"/> reflected off a surface facing <paramref name="normal"/>.</summary>
        public static Vector2<T> Reflect(Vector2<T> vector, Vector2<T> normal) =>
            vector - T.CreateTruncating(2) * Vector2<T>.Dot(vector, normal) * normal;

        /// <summary><paramref name="vector"/>, as a point of the Z = 0 plane, turned by <paramref name="rotation"/>, which
        /// is taken to be of length one; the Z it turns to is dropped.</summary>
        public static Vector2<T> Transform(Vector2<T> vector, Quaternion<T> rotation)
        {
            var x = rotation.X + rotation.X;
            var y = rotation.Y + rotation.Y;
            var z = rotation.Z + rotation.Z;
            var wz = rotation.W * z;
            var xx = rotation.X * x;
            var xy = rotation.X * y;
            var yy = rotation.Y * y;
            var zz = rotation.Z * z;
            return new Vector2<T>(vector.X * (T.One - yy - zz) + vector.Y * (xy - wz),
                vector.X * (xy + wz) + vector.Y * (T.One - xx - zz));
        }

        /// <summary>The point <paramref name="point"/> turned, scaled and moved by <paramref name="transform"/>.</summary>
        public static Vector2<T> Transform(Vector2<T> point, Matrix3x2<T> transform) =>
            point.X * transform.Row1 + point.Y * transform.Row2 + transform.Row3;

        /// <summary>The point <paramref name="vector"/>, with a Z of zero and a W of one, times
        /// <paramref name="transform"/>.</summary>
        public static Vector4<T> Transform(Vector2<T> vector, Matrix4x4<T> transform) =>
            Matrix4x4Simd.Transform(new Vector4<T>(vector, T.Zero, T.One), transform);

        /// <summary>The point <paramref name="coordinate"/> times <paramref name="transform"/>, divided by the W of the
        /// answer: a projected point.</summary>
        public static Vector2<T> TransformCoordinate(Vector2<T> coordinate, Matrix4x4<T> transform)
        {
            var result = Matrix4x4Simd.Transform(new Vector4<T>(coordinate, T.Zero, T.One), transform);
            return (Vector2<T>)result * (T.One / result.W);
        }

        /// <summary>The direction <paramref name="normal"/> times <paramref name="transform"/>: no translation.</summary>
        public static Vector2<T> TransformNormal(Vector2<T> normal, Matrix4x4<T> transform) =>
            normal.X * (Vector2<T>)transform.Row1 + normal.Y * (Vector2<T>)transform.Row2;

        /// <summary>Writes each of <paramref name="source"/> turned by <paramref name="rotation"/> into
        /// <paramref name="destination"/>, which may be the same span.</summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="destination"/> is shorter than
        /// <paramref name="source"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="destination"/> partly overlaps <paramref name="source"/>.</exception>
        public static void Transform(ReadOnlySpan<Vector2<T>> source, Quaternion<T> rotation, Span<Vector2<T>> destination)
        {
            SpanArguments.CheckDestination(source, destination);
            for (var i = 0; i < source.Length; i++)
            {
                destination[i] = Transform(source[i], rotation);
            }
        }

        /// <summary>Writes each point of <paramref name="source"/> times <paramref name="transform"/>, W kept, into
        /// <paramref name="destination"/>.</summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="destination"/> is shorter than
        /// <paramref name="source"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="destination"/> partly overlaps <paramref name="source"/>.</exception>
        public static void Transform(ReadOnlySpan<Vector2<T>> source, Matrix4x4<T> transform, Span<Vector4<T>> destination)
        {
            SpanArguments.CheckDestination(source, destination);
            for (var i = 0; i < source.Length; i++)
            {
                destination[i] = Transform(source[i], transform);
            }
        }

        /// <summary>Writes each point of <paramref name="source"/> projected by <paramref name="transform"/> into
        /// <paramref name="destination"/>, which may be the same span.</summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="destination"/> is shorter than
        /// <paramref name="source"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="destination"/> partly overlaps <paramref name="source"/>.</exception>
        public static void TransformCoordinate(ReadOnlySpan<Vector2<T>> source, Matrix4x4<T> transform, Span<Vector2<T>> destination)
        {
            SpanArguments.CheckDestination(source, destination);
            for (var i = 0; i < source.Length; i++)
            {
                destination[i] = TransformCoordinate(source[i], transform);
            }
        }

        /// <summary>Writes each direction of <paramref name="source"/> times <paramref name="transform"/>, without
        /// translation, into <paramref name="destination"/>, which may be the same span.</summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="destination"/> is shorter than
        /// <paramref name="source"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="destination"/> partly overlaps <paramref name="source"/>.</exception>
        public static void TransformNormal(ReadOnlySpan<Vector2<T>> source, Matrix4x4<T> transform, Span<Vector2<T>> destination)
        {
            SpanArguments.CheckDestination(source, destination);
            for (var i = 0; i < source.Length; i++)
            {
                destination[i] = TransformNormal(source[i], transform);
            }
        }

        /// <summary>Writes <paramref name="source"/> made perpendicular to one another, in order, into
        /// <paramref name="destination"/> (modified Gram-Schmidt); lengths are kept.</summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="destination"/> is shorter than
        /// <paramref name="source"/>.</exception>
        public static void Orthogonalize(Span<Vector2<T>> destination, params ReadOnlySpan<Vector2<T>> source)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(destination.Length, source.Length, nameof(destination));
            for (var i = 0; i < source.Length; i++)
            {
                var vector = source[i];
                for (var r = 0; r < i; r++)
                {
                    vector -= Vector2<T>.Dot(destination[r], vector) / Vector2<T>.Dot(destination[r], destination[r]) * destination[r];
                }

                destination[i] = vector;
            }
        }

        /// <summary>Writes <paramref name="source"/> made perpendicular to one another and of length one, in order,
        /// into <paramref name="destination"/> (modified Gram-Schmidt).</summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="destination"/> is shorter than
        /// <paramref name="source"/>.</exception>
        public static void Orthonormalize(Span<Vector2<T>> destination, params ReadOnlySpan<Vector2<T>> source)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(destination.Length, source.Length, nameof(destination));
            for (var i = 0; i < source.Length; i++)
            {
                var vector = source[i];
                for (var r = 0; r < i; r++)
                {
                    vector -= Vector2<T>.Dot(destination[r], vector) * destination[r];
                }

                vector.Normalize();
                destination[i] = vector;
            }
        }
    }

    private static Vector2<T> DividedByLargest<T>(Vector2<T> value, T largest) where T : unmanaged, IFloatingPointIeee754<T> =>
        T.IsInfinity(largest)
            ? new Vector2<T>(T.IsInfinity(value.X) ? T.CopySign(T.One, value.X) : T.Zero,
                T.IsInfinity(value.Y) ? T.CopySign(T.One, value.Y) : T.Zero)
            : value / largest;
}
