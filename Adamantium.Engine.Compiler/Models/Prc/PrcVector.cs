using System;

namespace Adamantium.Engine.Compiler.Models.Prc;

/// <summary>A vector computed exactly as the PRC basis pseudocode (PrcPt) does, so decoded points match the
/// encoder's.</summary>
internal readonly struct PrcVector
{
    /// <summary>FLT_EPSILON: below this length a vector has no direction.</summary>
    internal const double SmallestLength = 1.1920928955078125e-7;

    private const int MantissaBits = 23;
    private const int ExponentMask = 0xFF;
    private const int ExponentBias = 127;
    private const int MostSteps = 100;

    internal PrcVector(double x, double y, double z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    internal double X { get; }

    internal double Y { get; }

    internal double Z { get; }

    internal static PrcVector Zero => new(0, 0, 0);

    public static PrcVector operator +(PrcVector left, PrcVector right) => new(left.X + right.X, left.Y + right.Y, left.Z + right.Z);

    public static PrcVector operator -(PrcVector left, PrcVector right) => new(left.X - right.X, left.Y - right.Y, left.Z - right.Z);

    public static PrcVector operator -(PrcVector vector) => new(-vector.X, -vector.Y, -vector.Z);

    public static PrcVector operator *(PrcVector vector, double factor) => new(vector.X * factor, vector.Y * factor, vector.Z * factor);

    public static PrcVector operator /(PrcVector vector, double divisor) => new(vector.X / divisor, vector.Y / divisor, vector.Z / divisor);

    internal double Dot(PrcVector other) => X * other.X + Y * other.Y + Z * other.Z;

    internal PrcVector Cross(PrcVector other) =>
        new(Y * other.Z - Z * other.Y, Z * other.X - X * other.Z, X * other.Y - Y * other.X);

    /// <summary>The length by the spec's Newton iteration on the single-precision sum of squares; -1 when it does not
    /// settle.</summary>
    internal double GetLength()
    {
        var rounded = (float)(X * X + Y * Y + Z * Z);
        var squared = (double)rounded;
        var bits = BitConverter.SingleToInt32Bits(rounded);
        var exponent = (bits >> MantissaBits) & ExponentMask;
        if (exponent > ExponentBias)
        {
            exponent = (exponent - ExponentBias) / 2 + ExponentBias;
            bits = (bits & ~(ExponentMask << MantissaBits)) | (exponent << MantissaBits);
        }

        var guess = (double)BitConverter.Int32BitsToSingle(bits);
        for (var step = 0; step < MostSteps; step++)
        {
            var next = 0.5 * (guess + squared / guess);
            if (next == guess)
            {
                return next;
            }

            guess = next;
        }

        return -1;
    }

    /// <summary>This vector divided by its length; false when the length is below the single-precision epsilon.</summary>
    internal bool TryNormalize(out PrcVector unit)
    {
        var length = GetLength();
        if (length < SmallestLength)
        {
            unit = this;
            return false;
        }

        unit = new PrcVector(X / length, Y / length, Z / length);
        return true;
    }

    /// <summary>The angle to <paramref name="other"/> in radians, the cosine clamped near ±1 (AngleBetween); false
    /// when either vector has no direction.</summary>
    internal bool TryGetAngleTo(PrcVector other, out double angle)
    {
        angle = 0;
        var length = GetLength();
        var otherLength = other.GetLength();
        if (length < SmallestLength || otherLength < SmallestLength)
        {
            return false;
        }

        var cosine = (this / length).Dot(other / otherLength);
        cosine = cosine > 1.0 - 1e-12 ? 1.0 : cosine < -1.0 + 1e-12 ? -1.0 : cosine;
        angle = Math.Acos(cosine);
        return true;
    }

    /// <summary>A unit vector at right angles to this one (MakeOrthoRep): its cross product with the y axis, or the x
    /// axis when that has no direction; false when none can be made.</summary>
    internal bool TryGetOrthogonal(out PrcVector orthogonal)
    {
        orthogonal = Zero;
        if (!TryNormalize(out var axis))
        {
            return false;
        }

        if (axis.Cross(new PrcVector(0, 1, 0)).TryNormalize(out orthogonal))
        {
            return true;
        }

        return axis.Cross(new PrcVector(1, 0, 0)).TryNormalize(out orthogonal);
    }
}
