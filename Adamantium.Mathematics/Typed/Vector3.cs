using System;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Adamantium.Mathematics.Typed;

/// <summary>Makes <see cref="Vector3{T}"/>s and gives those of fractional numbers - float, double, Half - length,
/// normalizing and blending.</summary>
public static class Vector3
{
    /// <summary>A vector of <paramref name="x"/>, <paramref name="y"/> and <paramref name="z"/>.</summary>
    public static Vector3<T> Create<T>(T x, T y, T z) where T : unmanaged, INumber<T> => new(x, y, z);

    /// <summary>A vector of <paramref name="value"/> in every component.</summary>
    public static Vector3<T> Create<T>(T value) where T : unmanaged, INumber<T> => new(value);

    /// <summary>A vector of <paramref name="value"/>'s X and Y and <paramref name="z"/>.</summary>
    public static Vector3<T> Create<T>(Vector2<T> value, T z) where T : unmanaged, INumber<T> => new(value, z);

    /// <summary>Writes each of <paramref name="points"/> less <paramref name="origin"/>, rounded to float, into
    /// <paramref name="destination"/>: a world kept in double brought near a camera for the GPU, the subtraction done
    /// in double so nothing near the camera is lost. Many at a time in SIMD, each as the subtraction and the cast one
    /// by one give it.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="destination"/> is shorter than
    /// <paramref name="points"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="destination"/> overlaps <paramref name="points"/>.</exception>
    public static void Narrow(ReadOnlySpan<Vector3<double>> points, Vector3<double> origin, Span<Vector3<float>> destination)
    {
        SpanArguments.CheckDestination(points, destination);
        Matrix4x4Batch.Narrow(MemoryMarshal.Cast<Vector3<double>, double>(points), origin,
            MemoryMarshal.Cast<Vector3<float>, float>(destination[..points.Length]));
    }

    /// <summary>Writes each point kept as <paramref name="x"/>, <paramref name="y"/>, <paramref name="z"/> less
    /// <paramref name="origin"/>, rounded to float, into <paramref name="destinationX"/>,
    /// <paramref name="destinationY"/>, <paramref name="destinationZ"/>, as <see cref="Narrow(ReadOnlySpan{Vector3{double}}, Vector3{double}, Span{Vector3{float}})"/>
    /// does.</summary>
    /// <exception cref="ArgumentException">The sources differ in length, or the spans overlap.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A destination is shorter than the sources.</exception>
    public static void Narrow(ReadOnlySpan<double> x, ReadOnlySpan<double> y, ReadOnlySpan<double> z, Vector3<double> origin,
        Span<float> destinationX, Span<float> destinationY, Span<float> destinationZ)
    {
        SpanArguments.CheckComponents(x, y, z, destinationX, destinationY, destinationZ);
        Matrix4x4Batch.Narrow(x, origin.X, destinationX);
        Matrix4x4Batch.Narrow(y, origin.Y, destinationY);
        Matrix4x4Batch.Narrow(z, origin.Z, destinationZ);
    }

    extension<T>(Vector3<T>) where T : unmanaged, INumber<T>, ISignedNumber<T>
    {
        /// <summary>Down: negative Y.</summary>
        public static Vector3<T> Down => new(T.Zero, -T.One, T.Zero);

        /// <summary>Left: negative X.</summary>
        public static Vector3<T> Left => new(-T.One, T.Zero, T.Zero);

        /// <summary>Forward in a right-handed system: negative Z.</summary>
        public static Vector3<T> ForwardRH => new(T.Zero, T.Zero, -T.One);

        /// <summary>Backward in a left-handed system: negative Z.</summary>
        public static Vector3<T> BackwardLH => new(T.Zero, T.Zero, -T.One);
    }

    extension<T>(Vector3<T> vector) where T : unmanaged, IFloatingPointIeee754<T>
    {
        /// <summary>Whether the length is one within <see cref="MathHelper.Scalar.ZeroTolerance{T}"/>.</summary>
        public bool IsNormalized => MathHelper.Scalar.IsZero(vector.LengthSquared() - T.One);

        /// <summary>The length.</summary>
        public T Length() => T.Sqrt(vector.LengthSquared());

        /// <summary>Whether the two lie along one line, either way: the sine of the angle between them is within
        /// <see cref="MathHelper.Scalar.ZeroTolerance{T}"/>. A zero vector lies along every line.</summary>
        public bool IsCollinear(Vector3<T> other)
        {
            var (scale, otherScale) = (Largest(vector), Largest(other));
            if (scale == T.Zero || otherScale == T.Zero)
            {
                return true;
            }

            var (a, b) = (DividedByLargest(vector, scale), DividedByLargest(other, otherScale));
            var tolerance = MathHelper.Scalar.ZeroTolerance<T>();
            return Vector3<T>.Cross(a, b).LengthSquared() <= tolerance * tolerance * a.LengthSquared() * b.LengthSquared();
        }
    }

    extension<T>(ref Vector3<T> vector) where T : unmanaged, IFloatingPointIeee754<T>
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
            }
        }
    }

    extension<T>(Vector3<T>) where T : unmanaged, IFloatingPointIeee754<T>
    {
        /// <summary><paramref name="value"/> scaled to a length of one, or as it is when its length is within
        /// <see cref="MathHelper.Scalar.ZeroTolerance{T}"/> of zero.</summary>
        public static Vector3<T> Normalize(Vector3<T> value)
        {
            value.Normalize();
            return value;
        }

        /// <summary>The distance between two points.</summary>
        public static T Distance(Vector3<T> value1, Vector3<T> value2) => (value1 - value2).Length();

        /// <summary>The average of the components.</summary>
        public static T Average(Vector3<T> value) => (value.X + value.Y + value.Z) / T.CreateTruncating(3);

        /// <summary>Every component rounded to <paramref name="digits"/> fractional digits, halves away from zero.</summary>
        public static Vector3<T> Round(Vector3<T> value, int digits) =>
            new(T.Round(value.X, digits, MidpointRounding.AwayFromZero), T.Round(value.Y, digits, MidpointRounding.AwayFromZero),
                T.Round(value.Z, digits, MidpointRounding.AwayFromZero));

        /// <summary>The blend of <paramref name="start"/> and <paramref name="end"/> at <paramref name="amount"/>:
        /// start at 0, end at 1.</summary>
        public static Vector3<T> Lerp(Vector3<T> start, Vector3<T> end, T amount) =>
            new(MathHelper.Scalar.Lerp(start.X, end.X, amount), MathHelper.Scalar.Lerp(start.Y, end.Y, amount), MathHelper.Scalar.Lerp(start.Z, end.Z, amount));

        /// <summary>The blend of <paramref name="start"/> and <paramref name="end"/> at <paramref name="amount"/> eased
        /// in and out by <see cref="MathHelper.Scalar.SmoothStep{T}"/>.</summary>
        public static Vector3<T> SmoothStep(Vector3<T> start, Vector3<T> end, T amount) =>
            Lerp(start, end, MathHelper.Scalar.SmoothStep(amount));

        /// <summary>The point at <paramref name="amount"/> on the Hermite spline from <paramref name="value1"/> leaving
        /// along <paramref name="tangent1"/> to <paramref name="value2"/> arriving along <paramref name="tangent2"/>.</summary>
        public static Vector3<T> Hermite(Vector3<T> value1, Vector3<T> tangent1, Vector3<T> value2, Vector3<T> tangent2, T amount)
        {
            var weights = MathHelper.Scalar.HermiteWeights(amount);
            return value1 * weights.Start + value2 * weights.End + tangent1 * weights.StartTangent
                   + tangent2 * weights.EndTangent;
        }

        /// <summary>The point at <paramref name="amount"/> between <paramref name="value2"/> and
        /// <paramref name="value3"/> on the Catmull-Rom spline through all four.</summary>
        public static Vector3<T> CatmullRom(Vector3<T> value1, Vector3<T> value2, Vector3<T> value3, Vector3<T> value4, T amount)
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
        public static Vector3<T> Reflect(Vector3<T> vector, Vector3<T> normal) =>
            vector - T.CreateTruncating(2) * Vector3<T>.Dot(vector, normal) * normal;

        /// <summary><paramref name="vector"/> turned by <paramref name="rotation"/>, which is taken to be of length one.</summary>
        public static Vector3<T> Transform(Vector3<T> vector, Quaternion<T> rotation)
        {
            var x = rotation.X + rotation.X;
            var y = rotation.Y + rotation.Y;
            var z = rotation.Z + rotation.Z;
            var wx = rotation.W * x;
            var wy = rotation.W * y;
            var wz = rotation.W * z;
            var xx = rotation.X * x;
            var xy = rotation.X * y;
            var xz = rotation.X * z;
            var yy = rotation.Y * y;
            var yz = rotation.Y * z;
            var zz = rotation.Z * z;
            return new Vector3<T>(vector.X * (T.One - yy - zz) + vector.Y * (xy - wz) + vector.Z * (xz + wy),
                vector.X * (xy + wz) + vector.Y * (T.One - xx - zz) + vector.Z * (yz - wx),
                vector.X * (xz - wy) + vector.Y * (yz + wx) + vector.Z * (T.One - xx - yy));
        }

        /// <summary><paramref name="vector"/> times <paramref name="transform"/>.</summary>
        public static Vector3<T> Transform(Vector3<T> vector, Matrix3x3<T> transform) =>
            vector.X * transform.Row1 + vector.Y * transform.Row2 + vector.Z * transform.Row3;

        /// <summary>The point <paramref name="vector"/>, with a W of one, times <paramref name="transform"/>, the W of the
        /// answer kept.</summary>
        public static Vector4<T> Transform(Vector3<T> vector, Matrix4x4<T> transform) => Matrix4x4Simd.TransformPoint(vector, transform);

        /// <summary>The point <paramref name="coordinate"/> times <paramref name="transform"/>, divided by the W of the
        /// answer: a projected point.</summary>
        public static Vector3<T> TransformCoordinate(Vector3<T> coordinate, Matrix4x4<T> transform)
        {
            var result = Matrix4x4Simd.TransformPoint(coordinate, transform);
            return (Vector3<T>)result * (T.One / result.W);
        }

        /// <summary>The direction <paramref name="normal"/> times <paramref name="transform"/>: no translation.</summary>
        public static Vector3<T> TransformNormal(Vector3<T> normal, Matrix4x4<T> transform) =>
            normal.X * (Vector3<T>)transform.Row1 + normal.Y * (Vector3<T>)transform.Row2 + normal.Z * (Vector3<T>)transform.Row3;

        /// <summary>Writes each of <paramref name="source"/> turned by <paramref name="rotation"/> into
        /// <paramref name="destination"/>, which may be the same span.</summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="destination"/> is shorter than
        /// <paramref name="source"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="destination"/> partly overlaps <paramref name="source"/>.</exception>
        public static void Transform(ReadOnlySpan<Vector3<T>> source, Quaternion<T> rotation, Span<Vector3<T>> destination)
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
        public static void Transform(ReadOnlySpan<Vector3<T>> source, Matrix4x4<T> transform, Span<Vector4<T>> destination)
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
        public static void TransformCoordinate(ReadOnlySpan<Vector3<T>> source, Matrix4x4<T> transform, Span<Vector3<T>> destination)
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
        public static void TransformNormal(ReadOnlySpan<Vector3<T>> source, Matrix4x4<T> transform, Span<Vector3<T>> destination)
        {
            SpanArguments.CheckDestination(source, destination);
            for (var i = 0; i < source.Length; i++)
            {
                destination[i] = TransformNormal(source[i], transform);
            }
        }

        /// <summary>Writes each point kept as <paramref name="x"/>, <paramref name="y"/>, <paramref name="z"/> projected
        /// by <paramref name="transform"/> into <paramref name="destinationX"/>, <paramref name="destinationY"/>,
        /// <paramref name="destinationZ"/> - many points at a time in SIMD, each the same number as
        /// <see cref="TransformCoordinate(Vector3{T}, Matrix4x4{T})"/> gives it, only a NaN maybe another NaN. A
        /// destination may be any of the very same spans as the sources.</summary>
        /// <exception cref="ArgumentException">The sources differ in length, or the spans overlap other than in place.</exception>
        /// <exception cref="ArgumentOutOfRangeException">A destination is shorter than the sources.</exception>
        public static void TransformCoordinate(ReadOnlySpan<T> x, ReadOnlySpan<T> y, ReadOnlySpan<T> z, Matrix4x4<T> transform,
            Span<T> destinationX, Span<T> destinationY, Span<T> destinationZ)
        {
            SpanArguments.CheckComponents(x, y, z, destinationX, destinationY, destinationZ);
            Matrix4x4Batch.TransformCoordinate(x, y, z, transform, destinationX, destinationY, destinationZ);
        }

        /// <summary>Writes each direction kept as <paramref name="x"/>, <paramref name="y"/>, <paramref name="z"/> times
        /// <paramref name="transform"/>, without translation, into <paramref name="destinationX"/>,
        /// <paramref name="destinationY"/>, <paramref name="destinationZ"/> - many at a time in SIMD, each the same
        /// number as <see cref="TransformNormal(Vector3{T}, Matrix4x4{T})"/> gives it, only a NaN maybe another NaN. A
        /// destination may be any of the very same spans as the sources.</summary>
        /// <exception cref="ArgumentException">The sources differ in length, or the spans overlap other than in place.</exception>
        /// <exception cref="ArgumentOutOfRangeException">A destination is shorter than the sources.</exception>
        public static void TransformNormal(ReadOnlySpan<T> x, ReadOnlySpan<T> y, ReadOnlySpan<T> z, Matrix4x4<T> transform,
            Span<T> destinationX, Span<T> destinationY, Span<T> destinationZ)
        {
            SpanArguments.CheckComponents(x, y, z, destinationX, destinationY, destinationZ);
            Matrix4x4Batch.TransformNormal(x, y, z, transform, destinationX, destinationY, destinationZ);
        }

        /// <summary>The point <paramref name="vector"/> projected by <paramref name="worldViewProjection"/> into the
        /// viewport at <paramref name="x"/>, <paramref name="y"/> of <paramref name="width"/> by
        /// <paramref name="height"/> and depth <paramref name="minZ"/>..<paramref name="maxZ"/>, Y down.</summary>
        public static Vector3<T> Project(Vector3<T> vector, T x, T y, T width, T height, T minZ, T maxZ,
            Matrix4x4<T> worldViewProjection)
        {
            var half = T.CreateTruncating(0.5);
            var v = TransformCoordinate(vector, worldViewProjection);
            return new Vector3<T>((T.One + v.X) * half * width + x, (T.One + v.Y) * half * height + y,
                v.Z * (maxZ - minZ) + minZ);
        }

        /// <summary>The point of the viewport at <paramref name="x"/>, <paramref name="y"/> of <paramref name="width"/>
        /// by <paramref name="height"/> and depth <paramref name="minZ"/>..<paramref name="maxZ"/> taken back through
        /// <paramref name="worldViewProjection"/>: the inverse of <c>Project</c>. Y is not negated: clip space runs Y
        /// down here.</summary>
        public static Vector3<T> Unproject(Vector3<T> vector, T x, T y, T width, T height, T minZ, T maxZ,
            Matrix4x4<T> worldViewProjection)
        {
            var two = T.CreateTruncating(2);
            var clip = new Vector3<T>((vector.X - x) / width * two - T.One, (vector.Y - y) / height * two - T.One,
                (vector.Z - minZ) / (maxZ - minZ));
            return TransformCoordinate(clip, Matrix4x4<T>.Invert(worldViewProjection));
        }

        /// <summary>Writes <paramref name="source"/> made perpendicular to one another, in order, into
        /// <paramref name="destination"/> (modified Gram-Schmidt); lengths are kept.</summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="destination"/> is shorter than
        /// <paramref name="source"/>.</exception>
        public static void Orthogonalize(Span<Vector3<T>> destination, params ReadOnlySpan<Vector3<T>> source)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(destination.Length, source.Length, nameof(destination));
            for (var i = 0; i < source.Length; i++)
            {
                var vector = source[i];
                for (var r = 0; r < i; r++)
                {
                    vector -= Vector3<T>.Dot(destination[r], vector) / Vector3<T>.Dot(destination[r], destination[r]) * destination[r];
                }

                destination[i] = vector;
            }
        }

        /// <summary>Writes <paramref name="source"/> made perpendicular to one another and of length one, in order,
        /// into <paramref name="destination"/> (modified Gram-Schmidt).</summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="destination"/> is shorter than
        /// <paramref name="source"/>.</exception>
        public static void Orthonormalize(Span<Vector3<T>> destination, params ReadOnlySpan<Vector3<T>> source)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(destination.Length, source.Length, nameof(destination));
            for (var i = 0; i < source.Length; i++)
            {
                var vector = source[i];
                for (var r = 0; r < i; r++)
                {
                    vector -= Vector3<T>.Dot(destination[r], vector) * destination[r];
                }

                vector.Normalize();
                destination[i] = vector;
            }
        }
    }

    private static T Largest<T>(Vector3<T> value) where T : unmanaged, IFloatingPointIeee754<T> =>
        T.Max(T.Abs(value.X), T.Max(T.Abs(value.Y), T.Abs(value.Z)));

    private static Vector3<T> DividedByLargest<T>(Vector3<T> value, T largest) where T : unmanaged, IFloatingPointIeee754<T> =>
        T.IsInfinity(largest)
            ? new Vector3<T>(T.IsInfinity(value.X) ? T.CopySign(T.One, value.X) : T.Zero,
                T.IsInfinity(value.Y) ? T.CopySign(T.One, value.Y) : T.Zero,
                T.IsInfinity(value.Z) ? T.CopySign(T.One, value.Z) : T.Zero)
            : value / largest;
}
