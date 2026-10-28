using System.Numerics;

namespace Adamantium.Mathematics;

/// <summary>Makes <see cref="Matrix3x3{T}"/>s: scales, turns, views and billboards without translation.</summary>
public static class Matrix3x3
{
    /// <summary>A matrix of the three rows.</summary>
    public static Matrix3x3<T> Create<T>(Vector3<T> row1, Vector3<T> row2, Vector3<T> row3)
        where T : unmanaged, IFloatingPointIeee754<T> => new(row1, row2, row3);

    /// <summary>A scale by <paramref name="scale"/> along each axis.</summary>
    public static Matrix3x3<T> CreateScale<T>(Vector3<T> scale) where T : unmanaged, IFloatingPointIeee754<T> =>
        CreateScale(scale.X, scale.Y, scale.Z);

    /// <summary>A scale by <paramref name="x"/>, <paramref name="y"/> and <paramref name="z"/> along the axes.</summary>
    public static Matrix3x3<T> CreateScale<T>(T x, T y, T z) where T : unmanaged, IFloatingPointIeee754<T> =>
        new(x, T.Zero, T.Zero, T.Zero, y, T.Zero, T.Zero, T.Zero, z);

    /// <summary>A scale by <paramref name="scale"/> along every axis.</summary>
    public static Matrix3x3<T> CreateScale<T>(T scale) where T : unmanaged, IFloatingPointIeee754<T> =>
        CreateScale(scale, scale, scale);

    /// <summary>A turn by <paramref name="angle"/> radians around the X axis.</summary>
    public static Matrix3x3<T> CreateRotationX<T>(T angle) where T : unmanaged, IFloatingPointIeee754<T>
    {
        var (sin, cos) = T.SinCos(angle);
        return new Matrix3x3<T>(T.One, T.Zero, T.Zero, T.Zero, cos, sin, T.Zero, -sin, cos);
    }

    /// <summary>A turn by <paramref name="angle"/> radians around the Y axis.</summary>
    public static Matrix3x3<T> CreateRotationY<T>(T angle) where T : unmanaged, IFloatingPointIeee754<T>
    {
        var (sin, cos) = T.SinCos(angle);
        return new Matrix3x3<T>(cos, T.Zero, -sin, T.Zero, T.One, T.Zero, sin, T.Zero, cos);
    }

    /// <summary>A turn by <paramref name="angle"/> radians around the Z axis.</summary>
    public static Matrix3x3<T> CreateRotationZ<T>(T angle) where T : unmanaged, IFloatingPointIeee754<T>
    {
        var (sin, cos) = T.SinCos(angle);
        return new Matrix3x3<T>(cos, sin, T.Zero, -sin, cos, T.Zero, T.Zero, T.Zero, T.One);
    }

    /// <summary>A turn by <paramref name="angle"/> radians around <paramref name="axis"/>, which must be of length
    /// one.</summary>
    public static Matrix3x3<T> CreateFromAxisAngle<T>(Vector3<T> axis, T angle) where T : unmanaged, IFloatingPointIeee754<T>
    {
        var (x, y, z) = (axis.X, axis.Y, axis.Z);
        var (sin, cos) = T.SinCos(angle);
        var (xx, yy, zz) = (x * x, y * y, z * z);
        var (xy, xz, yz) = (x * y, x * z, y * z);
        return new Matrix3x3<T>(xx + cos * (T.One - xx), xy - cos * xy + sin * z, xz - cos * xz - sin * y,
            xy - cos * xy - sin * z, yy + cos * (T.One - yy), yz - cos * yz + sin * x,
            xz - cos * xz + sin * y, yz - cos * yz - sin * x, zz + cos * (T.One - zz));
    }

    /// <summary>The turn <paramref name="rotation"/>, which must be of length one.</summary>
    public static Matrix3x3<T> CreateFromQuaternion<T>(Quaternion<T> rotation) where T : unmanaged, IFloatingPointIeee754<T>
    {
        var two = T.CreateTruncating(2);
        var (xx, yy, zz) = (rotation.X * rotation.X, rotation.Y * rotation.Y, rotation.Z * rotation.Z);
        var (xy, zw, zx) = (rotation.X * rotation.Y, rotation.Z * rotation.W, rotation.Z * rotation.X);
        var (yw, yz, xw) = (rotation.Y * rotation.W, rotation.Y * rotation.Z, rotation.X * rotation.W);
        return new Matrix3x3<T>(T.One - two * (yy + zz), two * (xy + zw), two * (zx - yw),
            two * (xy - zw), T.One - two * (zz + xx), two * (yz + xw),
            two * (zx + yw), two * (yz - xw), T.One - two * (yy + xx));
    }

    /// <summary>The turn by <paramref name="roll"/> around Z, then <paramref name="pitch"/> around X, then
    /// <paramref name="yaw"/> around Y, all in radians.</summary>
    public static Matrix3x3<T> CreateFromYawPitchRoll<T>(T yaw, T pitch, T roll) where T : unmanaged, IFloatingPointIeee754<T> =>
        CreateFromQuaternion(Quaternion.CreateFromYawPitchRoll(yaw, pitch, roll));

    /// <summary>The turn of a left-handed view from <paramref name="eye"/> towards <paramref name="target"/>.</summary>
    public static Matrix3x3<T> CreateLookAtLH<T>(Vector3<T> eye, Vector3<T> target, Vector3<T> up)
        where T : unmanaged, IFloatingPointIeee754<T> => CreateView(Vector3<T>.Normalize(target - eye), up);

    /// <summary>The turn of a right-handed view from <paramref name="eye"/> towards <paramref name="target"/>.</summary>
    public static Matrix3x3<T> CreateLookAtRH<T>(Vector3<T> eye, Vector3<T> target, Vector3<T> up)
        where T : unmanaged, IFloatingPointIeee754<T> => CreateView(Vector3<T>.Normalize(eye - target), up);

    /// <summary>A left-handed turn of an object at <paramref name="objectPosition"/> to face the camera.</summary>
    public static Matrix3x3<T> CreateBillboardLH<T>(Vector3<T> objectPosition, Vector3<T> cameraPosition,
        Vector3<T> cameraUpVector, Vector3<T> cameraForwardVector) where T : unmanaged, IFloatingPointIeee754<T> =>
        CreateBillboard(cameraPosition - objectPosition, cameraUpVector, cameraForwardVector);

    /// <summary>A right-handed turn of an object at <paramref name="objectPosition"/> to face the camera.</summary>
    public static Matrix3x3<T> CreateBillboardRH<T>(Vector3<T> objectPosition, Vector3<T> cameraPosition,
        Vector3<T> cameraUpVector, Vector3<T> cameraForwardVector) where T : unmanaged, IFloatingPointIeee754<T> =>
        CreateBillboard(objectPosition - cameraPosition, cameraUpVector, cameraForwardVector);

    /// <summary>The mirror image in the plane through the origin parallel to <paramref name="plane"/>, whose normal is
    /// taken to be of length one.</summary>
    public static Matrix3x3<T> CreateReflection<T>(Plane<T> plane) where T : unmanaged, IFloatingPointIeee754<T>
    {
        var twice = plane.Normal * T.CreateTruncating(-2);
        return new Matrix3x3<T>(plane.Normal.X * twice + Vector3<T>.UnitX, plane.Normal.Y * twice + Vector3<T>.UnitY,
            plane.Normal.Z * twice + Vector3<T>.UnitZ);
    }

    /// <summary>The turn-and-scale part of <see cref="Matrix4x4.CreateShadow{T}"/>: X, Y and Z of the light only.</summary>
    public static Matrix3x3<T> CreateShadow<T>(Vector4<T> light, Plane<T> plane) where T : unmanaged, IFloatingPointIeee754<T>
    {
        var dot = Vector4<T>.Dot(new Vector4<T>(plane.Normal, plane.D), light);
        var direction = (Vector3<T>)light;
        return new Matrix3x3<T>(-plane.Normal.X * direction + dot * Vector3<T>.UnitX, -plane.Normal.Y * direction + dot * Vector3<T>.UnitY,
            -plane.Normal.Z * direction + dot * Vector3<T>.UnitZ);
    }

    /// <summary>The turn of a view whose depth axis is <paramref name="zAxis"/>, of length one: the view's axes as
    /// columns, so a world direction times it is the direction seen from the view.</summary>
    internal static Matrix3x3<T> CreateView<T>(Vector3<T> zAxis, Vector3<T> up) where T : unmanaged, IFloatingPointIeee754<T>
    {
        var xAxis = Vector3<T>.Normalize(Vector3<T>.Cross(up, zAxis));
        var yAxis = Vector3<T>.Cross(zAxis, xAxis);
        return Matrix3x3<T>.Transpose(new Matrix3x3<T>(xAxis, yAxis, zAxis));
    }

    /// <summary>The turn whose depth axis points along <paramref name="toCamera"/> - or against
    /// <paramref name="cameraForward"/> when that is zero - with the axes as rows.</summary>
    internal static Matrix3x3<T> CreateBillboard<T>(Vector3<T> toCamera, Vector3<T> cameraUp, Vector3<T> cameraForward)
        where T : unmanaged, IFloatingPointIeee754<T>
    {
        var lengthSquared = toCamera.LengthSquared();
        var zAxis = MathHelper.Scalar.IsZero(lengthSquared) ? -cameraForward : toCamera * (T.One / T.Sqrt(lengthSquared));
        var xAxis = Vector3<T>.Normalize(Vector3<T>.Cross(cameraUp, zAxis));
        return new Matrix3x3<T>(xAxis, Vector3<T>.Cross(zAxis, xAxis), zAxis);
    }
}
