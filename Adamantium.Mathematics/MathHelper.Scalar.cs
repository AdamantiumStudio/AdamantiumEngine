using System.Numerics;
using System.Runtime.CompilerServices;

namespace Adamantium.Mathematics;

public static partial class MathHelper
{
    /// <summary>Single numbers of any type: comparison within a tolerance - 1e-6 for float, 1e-9 for double, 4e-3 for
    /// Half, none for an integer - and the blends the vectors share.</summary>
    public static class Scalar
    {
        /// <summary>How far from zero a number may be and still count as zero.</summary>
        public static T ZeroTolerance<T>() where T : INumber<T>
        {
            if (typeof(T) == typeof(float))
            {
                return T.CreateTruncating(1e-6f);
            }

            if (typeof(T) == typeof(double))
            {
                return T.CreateTruncating(1e-9);
            }

            return FractionBytes<T>() switch
            {
                0 => T.Zero,
                2 => T.CreateTruncating(4e-3),
                4 => T.CreateTruncating(1e-6),
                _ => T.CreateTruncating(1e-9)
            };
        }

        /// <summary>Whether <paramref name="value"/> is within <see cref="ZeroTolerance{T}"/> of zero.</summary>
        public static bool IsZero<T>(T value) where T : INumber<T>
        {
            var tolerance = ZeroTolerance<T>();
            return tolerance == T.Zero ? value == T.Zero : T.Abs(value) < tolerance;
        }

        /// <summary>Whether <paramref name="a"/> and <paramref name="b"/> are equal within
        /// <see cref="ZeroTolerance{T}"/>, relative to their size; exactly equal for integers.</summary>
        public static bool NearEqual<T>(T a, T b) where T : INumber<T> => WithinEpsilon(a, b, ZeroTolerance<T>());

        /// <summary>Whether <paramref name="a"/> and <paramref name="b"/> differ by less than <paramref name="epsilon"/>
        /// of their size; by at most <paramref name="epsilon"/> when either is zero, both are below the smallest normal
        /// number, or they are integers.</summary>
        public static bool WithinEpsilon<T>(T a, T b, T epsilon) where T : INumber<T>
        {
            if (a == b)
            {
                return true;
            }

            if (FractionBytes<T>() == 0)
            {
                var distance = a > b ? a - b : b - a;
                return !T.IsNegative(distance) && distance <= epsilon;
            }

            var difference = T.Abs(a - b);
            var size = T.Abs(a) + T.Abs(b);
            if (a == T.Zero || b == T.Zero || size < SmallestNormal<T>())
            {
                return difference <= epsilon;
            }

            return difference / T.Min(size, Largest<T>()) < epsilon;
        }

        /// <summary>The blend of <paramref name="from"/> and <paramref name="to"/> at <paramref name="amount"/>: from
        /// at 0, to at 1.</summary>
        public static T Lerp<T>(T from, T to, T amount) where T : INumber<T> => (T.One - amount) * from + amount * to;

        /// <summary><paramref name="amount"/> eased in and out: 0 below 0, 1 above 1, the smooth cubic between.</summary>
        public static T SmoothStep<T>(T amount) where T : INumber<T>
        {
            if (amount <= T.Zero)
            {
                return T.Zero;
            }

            if (amount >= T.One)
            {
                return T.One;
            }

            var three = T.CreateTruncating(3);
            var two = T.CreateTruncating(2);
            return amount * amount * (three - two * amount);
        }

        /// <summary>The weights at <paramref name="amount"/> of a Hermite spline's start, end, start tangent and end
        /// tangent.</summary>
        internal static (T Start, T End, T StartTangent, T EndTangent) HermiteWeights<T>(T amount) where T : INumber<T>
        {
            var two = T.CreateTruncating(2);
            var three = T.CreateTruncating(3);
            var squared = amount * amount;
            var cubed = amount * squared;
            return (two * cubed - three * squared + T.One, -two * cubed + three * squared, cubed - two * squared + amount,
                cubed - squared);
        }

        private static int FractionBytes<T>() where T : INumber<T>
        {
            if (typeof(T) == typeof(float))
            {
                return 4;
            }

            if (typeof(T) == typeof(double))
            {
                return 8;
            }

            return T.IsInteger(T.CreateTruncating(0.5)) ? 0 : Unsafe.SizeOf<T>();
        }

        private static T SmallestNormal<T>() where T : INumber<T> =>
            FractionBytes<T>() switch
            {
                2 => T.CreateTruncating(6.103515625e-5),
                4 => T.CreateTruncating(float.Epsilon * (1 << 23)),
                8 => T.CreateTruncating(double.Epsilon * (1L << 52)),
                _ => T.Zero
            };

        private static T Largest<T>() where T : INumber<T> =>
            FractionBytes<T>() switch
            {
                2 => T.CreateTruncating(65504.0),
                4 => T.CreateTruncating(float.MaxValue),
                _ => T.CreateSaturating(double.MaxValue)
            };
    }
}
