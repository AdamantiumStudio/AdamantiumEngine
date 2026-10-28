using System.Numerics;

namespace Adamantium.Mathematics.Typed;

/// <summary>Makes <see cref="Quaternion{T}"/>s.</summary>
public static class Quaternion
{
    /// <summary>A quaternion of <paramref name="x"/>, <paramref name="y"/>, <paramref name="z"/> and <paramref name="w"/>.</summary>
    public static Quaternion<T> Create<T>(T x, T y, T z, T w) where T : unmanaged, IFloatingPointIeee754<T> => new(x, y, z, w);

    /// <summary>A quaternion of <paramref name="value"/>'s X, Y and Z and <paramref name="w"/>.</summary>
    public static Quaternion<T> Create<T>(Vector3<T> value, T w) where T : unmanaged, IFloatingPointIeee754<T> => new(value, w);

    /// <summary>The rotation by <paramref name="angle"/> radians around <paramref name="axis"/>, which need not be of
    /// length one.</summary>
    public static Quaternion<T> CreateFromAxisAngle<T>(Vector3<T> axis, T angle) where T : unmanaged, IFloatingPointIeee754<T>
    {
        var normalized = Vector3<T>.Normalize(axis);
        var (sin, cos) = T.SinCos(angle * T.CreateTruncating(0.5));
        return new Quaternion<T>(normalized.X * sin, normalized.Y * sin, normalized.Z * sin, cos);
    }

    /// <summary>The rotation the upper 3x3 part of <paramref name="matrix"/> describes, which must be a pure
    /// rotation.</summary>
    public static Quaternion<T> CreateFromRotationMatrix<T>(Matrix4x4<T> matrix) where T : unmanaged, IFloatingPointIeee754<T> =>
        CreateFromRotationMatrix((Matrix3x3<T>)matrix);

    /// <summary>The rotation <paramref name="matrix"/> describes, which must be a pure rotation.</summary>
    public static Quaternion<T> CreateFromRotationMatrix<T>(Matrix3x3<T> matrix) where T : unmanaged, IFloatingPointIeee754<T>
    {
        var half = T.CreateTruncating(0.5);
        var (m11, m12, m13) = (matrix.M11, matrix.M12, matrix.M13);
        var (m21, m22, m23) = (matrix.M21, matrix.M22, matrix.M23);
        var (m31, m32, m33) = (matrix.M31, matrix.M32, matrix.M33);
        var trace = m11 + m22 + m33;
        if (trace > T.Zero)
        {
            var root = T.Sqrt(trace + T.One);
            var scale = half / root;
            return new Quaternion<T>((m23 - m32) * scale, (m31 - m13) * scale, (m12 - m21) * scale, root * half);
        }

        if (m11 >= m22 && m11 >= m33)
        {
            var root = T.Sqrt(T.One + m11 - m22 - m33);
            var scale = half / root;
            return new Quaternion<T>(half * root, (m12 + m21) * scale, (m13 + m31) * scale, (m23 - m32) * scale);
        }

        if (m22 > m33)
        {
            var root = T.Sqrt(T.One + m22 - m11 - m33);
            var scale = half / root;
            return new Quaternion<T>((m21 + m12) * scale, half * root, (m32 + m23) * scale, (m31 - m13) * scale);
        }

        var rootZ = T.Sqrt(T.One + m33 - m11 - m22);
        var scaleZ = half / rootZ;
        return new Quaternion<T>((m31 + m13) * scaleZ, (m32 + m23) * scaleZ, half * rootZ, (m12 - m21) * scaleZ);
    }

    /// <summary>The turn of a left-handed view from <paramref name="eye"/> towards <paramref name="target"/>, as
    /// <see cref="Matrix3x3.CreateLookAtLH{T}"/> gives it.</summary>
    public static Quaternion<T> CreateLookAtLH<T>(Vector3<T> eye, Vector3<T> target, Vector3<T> up)
        where T : unmanaged, IFloatingPointIeee754<T> => CreateFromRotationMatrix(Matrix3x3.CreateLookAtLH(eye, target, up));

    /// <summary>The turn of a right-handed view from <paramref name="eye"/> towards <paramref name="target"/>, as
    /// <see cref="Matrix3x3.CreateLookAtRH{T}"/> gives it.</summary>
    public static Quaternion<T> CreateLookAtRH<T>(Vector3<T> eye, Vector3<T> target, Vector3<T> up)
        where T : unmanaged, IFloatingPointIeee754<T> => CreateFromRotationMatrix(Matrix3x3.CreateLookAtRH(eye, target, up));

    /// <summary>The turn of a left-handed view along <paramref name="direction"/>.</summary>
    public static Quaternion<T> CreateLookToLH<T>(Vector3<T> direction, Vector3<T> up)
        where T : unmanaged, IFloatingPointIeee754<T> => CreateLookAtLH(Vector3<T>.Zero, direction, up);

    /// <summary>The turn of a right-handed view along <paramref name="direction"/>.</summary>
    public static Quaternion<T> CreateLookToRH<T>(Vector3<T> direction, Vector3<T> up)
        where T : unmanaged, IFloatingPointIeee754<T> => CreateLookAtRH(Vector3<T>.Zero, direction, up);

    /// <summary>A left-handed turn of an object at <paramref name="objectPosition"/> to face the camera.</summary>
    public static Quaternion<T> CreateBillboardLH<T>(Vector3<T> objectPosition, Vector3<T> cameraPosition,
        Vector3<T> cameraUpVector, Vector3<T> cameraForwardVector) where T : unmanaged, IFloatingPointIeee754<T> =>
        CreateFromRotationMatrix(Matrix3x3.CreateBillboardLH(objectPosition, cameraPosition, cameraUpVector, cameraForwardVector));

    /// <summary>The turn of an object at <paramref name="objectPosition"/> around <paramref name="up"/> only, so that its
    /// forward, -Z, points at <paramref name="targetPosition"/> as seen from above: how far the target lies along
    /// <paramref name="up"/> is left out and the object's up stays <paramref name="up"/>.</summary>
    public static Quaternion<T> CreateFacing<T>(Vector3<T> objectPosition, Vector3<T> targetPosition, Vector3<T> up)
        where T : unmanaged, IFloatingPointIeee754<T>
    {
        var right = Vector3<T>.Normalize(Vector3<T>.Cross(up, objectPosition - targetPosition));
        var backward = Vector3<T>.Normalize(Vector3<T>.Cross(right, up));
        return CreateFromRotationMatrix(new Matrix3x3<T>(right, Vector3<T>.Cross(backward, right), backward));
    }

    /// <summary>The rotation by <paramref name="roll"/> around Z, then <paramref name="pitch"/> around X, then
    /// <paramref name="yaw"/> around Y, all in radians.</summary>
    public static Quaternion<T> CreateFromYawPitchRoll<T>(T yaw, T pitch, T roll) where T : unmanaged, IFloatingPointIeee754<T>
    {
        var half = T.CreateTruncating(0.5);
        var (sinRoll, cosRoll) = T.SinCos(roll * half);
        var (sinPitch, cosPitch) = T.SinCos(pitch * half);
        var (sinYaw, cosYaw) = T.SinCos(yaw * half);
        return new Quaternion<T>(cosYaw * sinPitch * cosRoll + sinYaw * cosPitch * sinRoll,
            sinYaw * cosPitch * cosRoll - cosYaw * sinPitch * sinRoll,
            cosYaw * cosPitch * sinRoll - sinYaw * sinPitch * cosRoll,
            cosYaw * cosPitch * cosRoll + sinYaw * sinPitch * sinRoll);
    }
}
