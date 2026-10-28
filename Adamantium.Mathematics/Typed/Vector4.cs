using System;
using System.Numerics;

namespace Adamantium.Mathematics.Typed;

/// <summary>Makes <see cref="Vector4{T}"/>s and gives those of fractional numbers - float, double, Half - length,
/// normalizing and blending.</summary>
public static class Vector4
{
    /// <summary>A vector of <paramref name="x"/>, <paramref name="y"/>, <paramref name="z"/> and <paramref name="w"/>.</summary>
    public static Vector4<T> Create<T>(T x, T y, T z, T w) where T : unmanaged, INumber<T> => new(x, y, z, w);

    /// <summary>A vector of <paramref name="value"/> in every component.</summary>
    public static Vector4<T> Create<T>(T value) where T : unmanaged, INumber<T> => new(value);

    /// <summary>A vector of <paramref name="value"/>'s X, Y and Z and <paramref name="w"/>.</summary>
    public static Vector4<T> Create<T>(Vector3<T> value, T w) where T : unmanaged, INumber<T> => new(value, w);

    /// <summary>A vector of <paramref name="value"/>'s X and Y and <paramref name="z"/> and <paramref name="w"/>.</summary>
    public static Vector4<T> Create<T>(Vector2<T> value, T z, T w) where T : unmanaged, INumber<T> => new(value, z, w);

    extension<T>(Vector4<T> vector) where T : unmanaged, IFloatingPointIeee754<T>
    {
        /// <summary>Whether the length is one within <see cref="MathHelper.Scalar.ZeroTolerance{T}"/>.</summary>
        public bool IsNormalized => MathHelper.Scalar.IsZero(vector.LengthSquared() - T.One);

        /// <summary>The length.</summary>
        public T Length() => T.Sqrt(vector.LengthSquared());
    }

    extension<T>(ref Vector4<T> vector) where T : unmanaged, IFloatingPointIeee754<T>
    {
        /// <summary>Scales the vector to a length of one; leaves it as it is when its length is within
        /// <see cref="MathHelper.Scalar.ZeroTolerance{T}"/> of zero.</summary>
        public void Normalize()
        {
            var length = vector.Length();
            if (!MathHelper.Scalar.IsZero(length))
            {
                var inverse = T.One / length;
                vector.X *= inverse;
                vector.Y *= inverse;
                vector.Z *= inverse;
                vector.W *= inverse;
            }
        }
    }

    extension<T>(Vector4<T>) where T : unmanaged, IFloatingPointIeee754<T>
    {
        /// <summary><paramref name="value"/> scaled to a length of one, or as it is when its length is within
        /// <see cref="MathHelper.Scalar.ZeroTolerance{T}"/> of zero.</summary>
        public static Vector4<T> Normalize(Vector4<T> value)
        {
            value.Normalize();
            return value;
        }

        /// <summary>The distance between two points.</summary>
        public static T Distance(Vector4<T> value1, Vector4<T> value2) => (value1 - value2).Length();

        /// <summary>The blend of <paramref name="start"/> and <paramref name="end"/> at <paramref name="amount"/>:
        /// start at 0, end at 1.</summary>
        public static Vector4<T> Lerp(Vector4<T> start, Vector4<T> end, T amount) =>
            new(MathHelper.Scalar.Lerp(start.X, end.X, amount), MathHelper.Scalar.Lerp(start.Y, end.Y, amount), MathHelper.Scalar.Lerp(start.Z, end.Z, amount),
                MathHelper.Scalar.Lerp(start.W, end.W, amount));

        /// <summary>The blend of <paramref name="start"/> and <paramref name="end"/> at <paramref name="amount"/> eased
        /// in and out by <see cref="MathHelper.Scalar.SmoothStep{T}"/>.</summary>
        public static Vector4<T> SmoothStep(Vector4<T> start, Vector4<T> end, T amount) =>
            Lerp(start, end, MathHelper.Scalar.SmoothStep(amount));

        /// <summary>The point at <paramref name="amount"/> on the Hermite spline from <paramref name="value1"/> leaving
        /// along <paramref name="tangent1"/> to <paramref name="value2"/> arriving along <paramref name="tangent2"/>.</summary>
        public static Vector4<T> Hermite(Vector4<T> value1, Vector4<T> tangent1, Vector4<T> value2, Vector4<T> tangent2, T amount)
        {
            var weights = MathHelper.Scalar.HermiteWeights(amount);
            return value1 * weights.Start + value2 * weights.End + tangent1 * weights.StartTangent
                   + tangent2 * weights.EndTangent;
        }

        /// <summary>The point at <paramref name="amount"/> between <paramref name="value2"/> and
        /// <paramref name="value3"/> on the Catmull-Rom spline through all four.</summary>
        public static Vector4<T> CatmullRom(Vector4<T> value1, Vector4<T> value2, Vector4<T> value3, Vector4<T> value4, T amount)
        {
            var squared = amount * amount;
            var cubed = amount * squared;
            var two = T.CreateTruncating(2);
            var three = T.CreateTruncating(3);
            return T.CreateTruncating(0.5) * (two * value2 + (-value1 + value3) * amount
                                              + (two * value1 - T.CreateTruncating(5) * value2 + T.CreateTruncating(4) * value3 - value4) * squared
                                              + (-value1 + three * value2 - three * value3 + value4) * cubed);
        }

        /// <summary><paramref name="vector"/>'s X, Y and Z turned by <paramref name="rotation"/>, which is taken to be of
        /// length one; W is kept.</summary>
        public static Vector4<T> Transform(Vector4<T> vector, Quaternion<T> rotation) =>
            new(Vector3<T>.Transform((Vector3<T>)vector, rotation), vector.W);

        /// <summary>The row vector <paramref name="vector"/> times <paramref name="transform"/>.</summary>
        public static Vector4<T> Transform(Vector4<T> vector, Matrix4x4<T> transform) => Matrix4x4Simd.Transform(vector, transform);

        /// <summary>Writes each of <paramref name="source"/> with X, Y and Z turned by <paramref name="rotation"/> into
        /// <paramref name="destination"/>, which may be the same span.</summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="destination"/> is shorter than
        /// <paramref name="source"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="destination"/> partly overlaps <paramref name="source"/>.</exception>
        public static void Transform(ReadOnlySpan<Vector4<T>> source, Quaternion<T> rotation, Span<Vector4<T>> destination)
        {
            SpanArguments.CheckDestination(source, destination);
            for (var i = 0; i < source.Length; i++)
            {
                destination[i] = Transform(source[i], rotation);
            }
        }

        /// <summary>Writes each row vector of <paramref name="source"/> times <paramref name="transform"/> into
        /// <paramref name="destination"/>, which may be the same span.</summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="destination"/> is shorter than
        /// <paramref name="source"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="destination"/> partly overlaps <paramref name="source"/>.</exception>
        public static void Transform(ReadOnlySpan<Vector4<T>> source, Matrix4x4<T> transform, Span<Vector4<T>> destination)
        {
            SpanArguments.CheckDestination(source, destination);
            for (var i = 0; i < source.Length; i++)
            {
                destination[i] = Transform(source[i], transform);
            }
        }

        /// <summary>Writes <paramref name="source"/> made perpendicular to one another, in order, into
        /// <paramref name="destination"/> (modified Gram-Schmidt); lengths are kept.</summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="destination"/> is shorter than
        /// <paramref name="source"/>.</exception>
        public static void Orthogonalize(Span<Vector4<T>> destination, params ReadOnlySpan<Vector4<T>> source)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(destination.Length, source.Length, nameof(destination));
            for (var i = 0; i < source.Length; i++)
            {
                var vector = source[i];
                for (var r = 0; r < i; r++)
                {
                    vector -= Vector4<T>.Dot(destination[r], vector) / Vector4<T>.Dot(destination[r], destination[r]) * destination[r];
                }

                destination[i] = vector;
            }
        }

        /// <summary>Writes <paramref name="source"/> made perpendicular to one another and of length one, in order,
        /// into <paramref name="destination"/> (modified Gram-Schmidt).</summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="destination"/> is shorter than
        /// <paramref name="source"/>.</exception>
        public static void Orthonormalize(Span<Vector4<T>> destination, params ReadOnlySpan<Vector4<T>> source)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(destination.Length, source.Length, nameof(destination));
            for (var i = 0; i < source.Length; i++)
            {
                var vector = source[i];
                for (var r = 0; r < i; r++)
                {
                    vector -= Vector4<T>.Dot(destination[r], vector) * destination[r];
                }

                vector.Normalize();
                destination[i] = vector;
            }
        }
    }
}
