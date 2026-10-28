using System;
using System.Numerics;

namespace Adamantium.Mathematics;

/// <summary>Makes <see cref="Matrix4x4{T}"/>s: transforms, views and projections. Projections map depth to 0..1;
/// those without LH or RH in the name are for Vulkan's clip space, Y down.</summary>
public static class Matrix4x4
{
    /// <summary>A matrix of the four rows.</summary>
    public static Matrix4x4<T> Create<T>(Vector4<T> row1, Vector4<T> row2, Vector4<T> row3, Vector4<T> row4)
        where T : unmanaged, IFloatingPointIeee754<T> => new(row1, row2, row3, row4);

    /// <summary>A scale by <paramref name="scale"/> along each axis.</summary>
    public static Matrix4x4<T> CreateScale<T>(Vector3<T> scale) where T : unmanaged, IFloatingPointIeee754<T> =>
        CreateScale(scale.X, scale.Y, scale.Z);

    /// <summary>A scale by <paramref name="x"/>, <paramref name="y"/> and <paramref name="z"/> along the axes.</summary>
    public static Matrix4x4<T> CreateScale<T>(T x, T y, T z) where T : unmanaged, IFloatingPointIeee754<T>
    {
        var result = Matrix4x4<T>.Identity;
        result.ScaleVector = new Vector3<T>(x, y, z);
        return result;
    }

    /// <summary>A scale by <paramref name="scale"/> along every axis.</summary>
    public static Matrix4x4<T> CreateScale<T>(T scale) where T : unmanaged, IFloatingPointIeee754<T> =>
        CreateScale(scale, scale, scale);

    /// <summary>A move by <paramref name="value"/>.</summary>
    public static Matrix4x4<T> CreateTranslation<T>(Vector3<T> value) where T : unmanaged, IFloatingPointIeee754<T>
    {
        var result = Matrix4x4<T>.Identity;
        result.TranslationVector = value;
        return result;
    }

    /// <summary>A move by <paramref name="x"/>, <paramref name="y"/> and <paramref name="z"/>.</summary>
    public static Matrix4x4<T> CreateTranslation<T>(T x, T y, T z) where T : unmanaged, IFloatingPointIeee754<T> =>
        CreateTranslation(new Vector3<T>(x, y, z));

    /// <summary>A turn by <paramref name="angle"/> radians around the X axis.</summary>
    public static Matrix4x4<T> CreateRotationX<T>(T angle) where T : unmanaged, IFloatingPointIeee754<T> =>
        (Matrix4x4<T>)Matrix3x3.CreateRotationX(angle);

    /// <summary>A turn by <paramref name="angle"/> radians around the Y axis.</summary>
    public static Matrix4x4<T> CreateRotationY<T>(T angle) where T : unmanaged, IFloatingPointIeee754<T> =>
        (Matrix4x4<T>)Matrix3x3.CreateRotationY(angle);

    /// <summary>A turn by <paramref name="angle"/> radians around the Z axis.</summary>
    public static Matrix4x4<T> CreateRotationZ<T>(T angle) where T : unmanaged, IFloatingPointIeee754<T> =>
        (Matrix4x4<T>)Matrix3x3.CreateRotationZ(angle);

    /// <summary>A turn by <paramref name="angle"/> radians around <paramref name="axis"/>, which must be of length
    /// one.</summary>
    public static Matrix4x4<T> CreateFromAxisAngle<T>(Vector3<T> axis, T angle) where T : unmanaged, IFloatingPointIeee754<T> =>
        (Matrix4x4<T>)Matrix3x3.CreateFromAxisAngle(axis, angle);

    /// <summary>The turn <paramref name="rotation"/>, which must be of length one.</summary>
    public static Matrix4x4<T> CreateFromQuaternion<T>(Quaternion<T> rotation) where T : unmanaged, IFloatingPointIeee754<T> =>
        (Matrix4x4<T>)Matrix3x3.CreateFromQuaternion(rotation);

    /// <summary>The turn by <paramref name="roll"/> around Z, then <paramref name="pitch"/> around X, then
    /// <paramref name="yaw"/> around Y, all in radians.</summary>
    public static Matrix4x4<T> CreateFromYawPitchRoll<T>(T yaw, T pitch, T roll) where T : unmanaged, IFloatingPointIeee754<T> =>
        CreateFromQuaternion(Quaternion.CreateFromYawPitchRoll(yaw, pitch, roll));

    /// <summary>A shear along <paramref name="translationVector"/> that turns <paramref name="rotationVector"/> by
    /// <paramref name="angle"/> radians towards it; the two span the shearing plane.</summary>
    /// <exception cref="ArgumentException">A vector is zero, the two are parallel, or the angle turns the vector too far
    /// for a shear.</exception>
    public static Matrix4x4<T> CreateSkew<T>(T angle, Vector3<T> rotationVector, Vector3<T> translationVector)
        where T : unmanaged, IFloatingPointIeee754<T>
    {
        var e1 = Direction(translationVector);
        var direction = Direction(rotationVector);
        if (e1.IsZero || direction.IsZero)
        {
            throw new ArgumentException("The rotation and translation vectors must not be zero.");
        }

        var rv1 = Vector3<T>.Dot(direction, e1);
        var e0 = Vector3<T>.Normalize(direction - rv1 * e1);
        var rv0 = Vector3<T>.Dot(direction, e0);
        if (MathHelper.Scalar.IsZero(rv0))
        {
            throw new ArgumentException("The rotation vector is parallel to the translation vector.", nameof(rotationVector));
        }

        var (sin, cos) = T.SinCos(angle);
        var rr0 = rv0 * cos - rv1 * sin;
        var rr1 = rv0 * sin + rv1 * cos;
        if (rr0 < T.CreateTruncating(0.000001))
        {
            throw new ArgumentException("The skew angle turns the vector too far.", nameof(angle));
        }

        var d = rr1 / rr0 - rv1 / rv0;
        var result = Matrix4x4<T>.Identity;
        result.Row1 = new Vector4<T>(d * e0.X * e1 + Vector3<T>.UnitX, T.Zero);
        result.Row2 = new Vector4<T>(d * e0.Y * e1 + Vector3<T>.UnitY, T.Zero);
        result.Row3 = new Vector4<T>(d * e0.Z * e1 + Vector3<T>.UnitZ, T.Zero);
        return result;
    }

    /// <summary>A scale by <paramref name="scaling"/>, then the turn <paramref name="rotation"/>, then the move
    /// <paramref name="translation"/>.</summary>
    public static Matrix4x4<T> CreateAffineTransformation<T>(T scaling, Quaternion<T> rotation, Vector3<T> translation)
        where T : unmanaged, IFloatingPointIeee754<T> =>
        CreateScale(scaling) * CreateFromQuaternion(rotation) * CreateTranslation(translation);

    /// <summary>A scale by <paramref name="scaling"/>, then the turn <paramref name="rotation"/> around
    /// <paramref name="rotationCenter"/>, then the move <paramref name="translation"/>.</summary>
    public static Matrix4x4<T> CreateAffineTransformation<T>(T scaling, Vector3<T> rotationCenter, Quaternion<T> rotation,
        Vector3<T> translation) where T : unmanaged, IFloatingPointIeee754<T> =>
        CreateScale(scaling) * CreateTranslation(-rotationCenter) * CreateFromQuaternion(rotation) *
        CreateTranslation(rotationCenter) * CreateTranslation(translation);

    /// <summary>In the XY plane: a scale by <paramref name="scaling"/>, then a turn by <paramref name="rotation"/>
    /// radians, then the move <paramref name="translation"/>.</summary>
    public static Matrix4x4<T> CreateAffineTransformation2D<T>(T scaling, T rotation, Vector2<T> translation)
        where T : unmanaged, IFloatingPointIeee754<T> =>
        CreateScale(scaling, scaling, T.One) * CreateRotationZ(rotation) * CreateTranslation((Vector3<T>)translation);

    /// <summary>In the XY plane: a scale by <paramref name="scaling"/>, then a turn by <paramref name="rotation"/>
    /// radians around <paramref name="rotationCenter"/>, then the move <paramref name="translation"/>.</summary>
    public static Matrix4x4<T> CreateAffineTransformation2D<T>(T scaling, Vector2<T> rotationCenter, T rotation,
        Vector2<T> translation) where T : unmanaged, IFloatingPointIeee754<T> =>
        CreateScale(scaling, scaling, T.One) * CreateTranslation((Vector3<T>)(-rotationCenter)) * CreateRotationZ(rotation) *
        CreateTranslation((Vector3<T>)rotationCenter) * CreateTranslation((Vector3<T>)translation);

    /// <summary>A scale by <paramref name="scaling"/> along axes turned by <paramref name="scalingRotation"/> around
    /// <paramref name="scalingCenter"/>, then the turn <paramref name="rotation"/> around
    /// <paramref name="rotationCenter"/>, then the move <paramref name="translation"/>.</summary>
    public static Matrix4x4<T> CreateTransformation<T>(Vector3<T> scalingCenter, Quaternion<T> scalingRotation,
        Vector3<T> scaling, Vector3<T> rotationCenter, Quaternion<T> rotation, Vector3<T> translation)
        where T : unmanaged, IFloatingPointIeee754<T>
    {
        var scalingAxes = CreateFromQuaternion(scalingRotation);
        return CreateTranslation(-scalingCenter) * Matrix4x4<T>.Transpose(scalingAxes) * CreateScale(scaling) * scalingAxes *
               CreateTranslation(scalingCenter) * CreateTranslation(-rotationCenter) * CreateFromQuaternion(rotation) *
               CreateTranslation(rotationCenter) * CreateTranslation(translation);
    }

    /// <summary>In the XY plane: a scale by <paramref name="scaling"/> along axes turned by
    /// <paramref name="scalingRotation"/> radians around <paramref name="scalingCenter"/>, then a turn by
    /// <paramref name="rotation"/> radians around <paramref name="rotationCenter"/>, then the move
    /// <paramref name="translation"/>.</summary>
    public static Matrix4x4<T> CreateTransformation2D<T>(Vector2<T> scalingCenter, T scalingRotation, Vector2<T> scaling,
        Vector2<T> rotationCenter, T rotation, Vector2<T> translation) where T : unmanaged, IFloatingPointIeee754<T>
    {
        var result = CreateTranslation((Vector3<T>)(-scalingCenter)) * CreateRotationZ(-scalingRotation) *
                     CreateScale((Vector3<T>)scaling) * CreateRotationZ(scalingRotation) *
                     CreateTranslation((Vector3<T>)scalingCenter) * CreateTranslation((Vector3<T>)(-rotationCenter)) *
                     CreateRotationZ(rotation) * CreateTranslation((Vector3<T>)rotationCenter) *
                     CreateTranslation((Vector3<T>)translation);
        result.M33 = T.One;
        result.M44 = T.One;
        return result;
    }

    /// <summary>A left-handed view from <paramref name="eye"/> towards <paramref name="target"/>.</summary>
    public static Matrix4x4<T> CreateLookAtLH<T>(Vector3<T> eye, Vector3<T> target, Vector3<T> up)
        where T : unmanaged, IFloatingPointIeee754<T> => CreateView(eye, Vector3<T>.Normalize(target - eye), up);

    /// <summary>A right-handed view from <paramref name="eye"/> towards <paramref name="target"/>.</summary>
    public static Matrix4x4<T> CreateLookAtRH<T>(Vector3<T> eye, Vector3<T> target, Vector3<T> up)
        where T : unmanaged, IFloatingPointIeee754<T> => CreateView(eye, Vector3<T>.Normalize(eye - target), up);

    /// <summary>A left-handed view from <paramref name="eye"/> along <paramref name="lookDirection"/>.</summary>
    public static Matrix4x4<T> CreateLookToLH<T>(Vector3<T> eye, Vector3<T> lookDirection, Vector3<T> up)
        where T : unmanaged, IFloatingPointIeee754<T> => CreateView(eye, Vector3<T>.Normalize(lookDirection), up);

    /// <summary>A left-handed turn of an object at <paramref name="objectPosition"/> to face the camera.</summary>
    public static Matrix4x4<T> CreateBillboardLH<T>(Vector3<T> objectPosition, Vector3<T> cameraPosition,
        Vector3<T> cameraUpVector, Vector3<T> cameraForwardVector) where T : unmanaged, IFloatingPointIeee754<T> =>
        CreateBillboard(objectPosition, cameraPosition - objectPosition, cameraUpVector, cameraForwardVector);

    /// <summary>A right-handed turn of an object at <paramref name="objectPosition"/> to face the camera.</summary>
    public static Matrix4x4<T> CreateBillboardRH<T>(Vector3<T> objectPosition, Vector3<T> cameraPosition,
        Vector3<T> cameraUpVector, Vector3<T> cameraForwardVector) where T : unmanaged, IFloatingPointIeee754<T> =>
        CreateBillboard(objectPosition, objectPosition - cameraPosition, cameraUpVector, cameraForwardVector);

    /// <summary>An object at <paramref name="position"/> turned to the camera's axes, for an orthographic camera.</summary>
    public static Matrix4x4<T> CreateBillboardForOrthoProjection<T>(Vector3<T> position, Vector3<T> cameraUp,
        Vector3<T> cameraRight, Vector3<T> cameraForward) where T : unmanaged, IFloatingPointIeee754<T> =>
        new(new Vector4<T>(cameraRight, T.Zero), new Vector4<T>(cameraUp, T.Zero), new Vector4<T>(cameraForward, T.Zero),
            new Vector4<T>(position, T.One));

    /// <summary>An orthographic projection of a <paramref name="width"/> by <paramref name="height"/> view centered
    /// on the axis, for Vulkan's clip space: Y down, depth <paramref name="znear"/>..<paramref name="zfar"/> to 0..1
    /// along -Z.</summary>
    public static Matrix4x4<T> CreateOrtho<T>(T width, T height, T znear, T zfar) where T : unmanaged, IFloatingPointIeee754<T>
    {
        var halfWidth = width * Half<T>();
        var halfHeight = height * Half<T>();
        var result = CreateOrthoOffCenter(-halfWidth, halfWidth, -halfHeight, halfHeight, znear, zfar);
        result.M33 = -result.M33;
        return result;
    }

    /// <summary>A left-handed orthographic projection of a <paramref name="width"/> by <paramref name="height"/> view
    /// centered on the axis.</summary>
    public static Matrix4x4<T> CreateOrthoLH<T>(T width, T height, T znear, T zfar) where T : unmanaged, IFloatingPointIeee754<T>
    {
        var halfWidth = width * Half<T>();
        var halfHeight = height * Half<T>();
        return CreateOrthoOffCenterLH(-halfWidth, halfWidth, -halfHeight, halfHeight, znear, zfar);
    }

    /// <summary>A right-handed orthographic projection of a <paramref name="width"/> by <paramref name="height"/> view
    /// centered on the axis.</summary>
    public static Matrix4x4<T> CreateOrthoRH<T>(T width, T height, T znear, T zfar) where T : unmanaged, IFloatingPointIeee754<T>
    {
        var halfWidth = width * Half<T>();
        var halfHeight = height * Half<T>();
        return CreateOrthoOffCenterRH(-halfWidth, halfWidth, -halfHeight, halfHeight, znear, zfar);
    }

    /// <summary>An orthographic projection of the view from <paramref name="left"/> to <paramref name="right"/> and
    /// <paramref name="top"/> to <paramref name="bottom"/>, for Vulkan's clip space: Y down.</summary>
    public static Matrix4x4<T> CreateOrthoOffCenter<T>(T left, T right, T top, T bottom, T znear, T zfar)
        where T : unmanaged, IFloatingPointIeee754<T>
    {
        var two = T.CreateTruncating(2);
        var result = Matrix4x4<T>.Identity;
        result.M11 = two / (right - left);
        result.M22 = two / (bottom - top);
        result.M33 = T.One / (zfar - znear);
        result.M41 = (left + right) / (left - right);
        result.M42 = (top + bottom) / (top - bottom);
        result.M43 = znear / (znear - zfar);
        return result;
    }

    /// <summary>A left-handed orthographic projection of the view from <paramref name="left"/> to
    /// <paramref name="right"/> and <paramref name="bottom"/> to <paramref name="top"/>.</summary>
    public static Matrix4x4<T> CreateOrthoOffCenterLH<T>(T left, T right, T bottom, T top, T znear, T zfar)
        where T : unmanaged, IFloatingPointIeee754<T>
    {
        var two = T.CreateTruncating(2);
        var zRange = T.One / (zfar - znear);
        var result = Matrix4x4<T>.Identity;
        result.M11 = two / (right - left);
        result.M22 = two / (top - bottom);
        result.M33 = zRange;
        result.M41 = (left + right) / (left - right);
        result.M42 = (top + bottom) / (bottom - top);
        result.M43 = -znear * zRange;
        return result;
    }

    /// <summary>A right-handed orthographic projection of the view from <paramref name="left"/> to
    /// <paramref name="right"/> and <paramref name="bottom"/> to <paramref name="top"/>.</summary>
    public static Matrix4x4<T> CreateOrthoOffCenterRH<T>(T left, T right, T bottom, T top, T znear, T zfar)
        where T : unmanaged, IFloatingPointIeee754<T>
    {
        var result = CreateOrthoOffCenterLH(left, right, bottom, top, znear, zfar);
        result.M33 = -result.M33;
        return result;
    }

    /// <summary>A left-handed perspective projection whose near plane is <paramref name="width"/> by
    /// <paramref name="height"/>.</summary>
    public static Matrix4x4<T> CreatePerspectiveLH<T>(T width, T height, T znear, T zfar)
        where T : unmanaged, IFloatingPointIeee754<T>
    {
        var halfWidth = width * Half<T>();
        var halfHeight = height * Half<T>();
        return CreatePerspectiveOffCenterLH(-halfWidth, halfWidth, -halfHeight, halfHeight, znear, zfar);
    }

    /// <summary>A right-handed perspective projection whose near plane is <paramref name="width"/> by
    /// <paramref name="height"/>.</summary>
    public static Matrix4x4<T> CreatePerspectiveRH<T>(T width, T height, T znear, T zfar)
        where T : unmanaged, IFloatingPointIeee754<T>
    {
        var halfWidth = width * Half<T>();
        var halfHeight = height * Half<T>();
        return CreatePerspectiveOffCenterRH(-halfWidth, halfWidth, -halfHeight, halfHeight, znear, zfar);
    }

    /// <summary>A left-handed perspective projection of the vertical field of view <paramref name="fov"/> in
    /// radians.</summary>
    public static Matrix4x4<T> CreatePerspectiveFovLH<T>(T fov, T aspect, T znear, T zfar)
        where T : unmanaged, IFloatingPointIeee754<T>
    {
        var yScale = T.One / T.Tan(fov * Half<T>());
        var q = zfar / (zfar - znear);
        var result = Matrix4x4<T>.Zero;
        result.M11 = yScale / aspect;
        result.M22 = yScale;
        result.M33 = q;
        result.M34 = T.One;
        result.M43 = -q * znear;
        return result;
    }

    /// <summary>A right-handed perspective projection of the vertical field of view <paramref name="fov"/> in
    /// radians.</summary>
    public static Matrix4x4<T> CreatePerspectiveFovRH<T>(T fov, T aspect, T znear, T zfar)
        where T : unmanaged, IFloatingPointIeee754<T>
    {
        var yScale = T.One / T.Tan(fov * Half<T>());
        var q = zfar / (znear - zfar);
        var result = Matrix4x4<T>.Zero;
        result.M11 = yScale / aspect;
        result.M22 = yScale;
        result.M33 = q;
        result.M34 = -T.One;
        result.M43 = q * znear;
        return result;
    }

    /// <summary>A left-handed perspective projection of the horizontal field of view <paramref name="fov"/> in
    /// radians; <paramref name="aspect"/> is width over height.</summary>
    public static Matrix4x4<T> CreatePerspectiveFovX<T>(T fov, T aspect, T znear, T zfar)
        where T : unmanaged, IFloatingPointIeee754<T>
    {
        var xScale = T.One / T.Tan(fov * Half<T>());
        var result = Matrix4x4<T>.Zero;
        result.M11 = xScale;
        result.M22 = xScale * aspect;
        result.M33 = zfar / (zfar - znear);
        result.M34 = T.One;
        result.M43 = -(znear * zfar) / (zfar - znear);
        return result;
    }

    /// <summary>A left-handed perspective projection of the vertical field of view <paramref name="fov"/> in
    /// radians.</summary>
    public static Matrix4x4<T> CreatePerspectiveFovY<T>(T fov, T aspect, T znear, T zfar)
        where T : unmanaged, IFloatingPointIeee754<T>
    {
        var yScale = T.One / T.Tan(fov * Half<T>());
        var result = Matrix4x4<T>.Zero;
        result.M11 = yScale / aspect;
        result.M22 = yScale;
        result.M33 = zfar / (zfar - znear);
        result.M34 = T.One;
        result.M43 = -(znear * zfar) / (zfar - znear);
        return result;
    }

    /// <summary>A perspective projection of the near-plane rectangle from <paramref name="left"/> to
    /// <paramref name="right"/> and <paramref name="top"/> to <paramref name="bottom"/>, for Vulkan's clip space: Y
    /// down.</summary>
    public static Matrix4x4<T> CreatePerspectiveOffCenter<T>(T left, T right, T top, T bottom, T znear, T zfar)
        where T : unmanaged, IFloatingPointIeee754<T>
    {
        var two = T.CreateTruncating(2);
        var zRange = zfar / (zfar - znear);
        var result = Matrix4x4<T>.Zero;
        result.M11 = two * znear / (right - left);
        result.M22 = two * znear / (bottom - top);
        result.M31 = (left + right) / (left - right);
        result.M32 = (top + bottom) / (top - bottom);
        result.M33 = zRange;
        result.M34 = T.One;
        result.M43 = -znear * zRange;
        return result;
    }

    /// <summary>A left-handed perspective projection of the near-plane rectangle from <paramref name="left"/> to
    /// <paramref name="right"/> and <paramref name="bottom"/> to <paramref name="top"/>.</summary>
    public static Matrix4x4<T> CreatePerspectiveOffCenterLH<T>(T left, T right, T bottom, T top, T znear, T zfar)
        where T : unmanaged, IFloatingPointIeee754<T>
    {
        var two = T.CreateTruncating(2);
        var zRange = zfar / (zfar - znear);
        var result = Matrix4x4<T>.Zero;
        result.M11 = two * znear / (right - left);
        result.M22 = two * znear / (top - bottom);
        result.M31 = (left + right) / (left - right);
        result.M32 = (top + bottom) / (bottom - top);
        result.M33 = zRange;
        result.M34 = T.One;
        result.M43 = -znear * zRange;
        return result;
    }

    /// <summary>A right-handed perspective projection of the near-plane rectangle from <paramref name="left"/> to
    /// <paramref name="right"/> and <paramref name="bottom"/> to <paramref name="top"/>.</summary>
    public static Matrix4x4<T> CreatePerspectiveOffCenterRH<T>(T left, T right, T bottom, T top, T znear, T zfar)
        where T : unmanaged, IFloatingPointIeee754<T>
    {
        var result = CreatePerspectiveOffCenterLH(left, right, bottom, top, znear, zfar);
        result.Row3 = -result.Row3;
        return result;
    }

    /// <summary>The mirror image in <paramref name="plane"/>, whose normal is taken to be of length one.</summary>
    public static Matrix4x4<T> CreateReflection<T>(Plane<T> plane) where T : unmanaged, IFloatingPointIeee754<T>
    {
        var result = (Matrix4x4<T>)Matrix3x3.CreateReflection(plane);
        result.TranslationVector = plane.Normal * (T.CreateTruncating(-2) * plane.D);
        return result;
    }

    /// <summary>The flattening of points onto <paramref name="plane"/> along the light: towards the point
    /// <paramref name="light"/> with a W of one, along the direction with a W of zero.</summary>
    public static Matrix4x4<T> CreateShadow<T>(Vector4<T> light, Plane<T> plane) where T : unmanaged, IFloatingPointIeee754<T>
    {
        var coefficients = new Vector4<T>(plane.Normal, plane.D);
        var dot = Vector4<T>.Dot(coefficients, light);
        return new Matrix4x4<T>(-coefficients.X * light + dot * Vector4<T>.UnitX, -coefficients.Y * light + dot * Vector4<T>.UnitY,
            -coefficients.Z * light + dot * Vector4<T>.UnitZ, -coefficients.W * light + dot * Vector4<T>.UnitW);
    }

    /// <summary>A 2:1 isometric projection showing <paramref name="isometricScale"/> units each way from the center: a
    /// point lands at (x - z, x / 2 + y + z / 2), its depth dropped.</summary>
    public static Matrix4x4<T> CreateIsometricProjection<T>(T isometricScale) where T : unmanaged, IFloatingPointIeee754<T>
    {
        var two = T.CreateTruncating(2);
        var projection = Matrix4x4<T>.Identity;
        projection.M12 = Half<T>();
        projection.M31 = -T.One;
        projection.M32 = Half<T>();
        projection.M33 = T.Zero;
        return projection * CreateOrthoLH(two * isometricScale, two * isometricScale, -two * isometricScale, two * isometricScale);
    }

    private static Matrix4x4<T> CreateView<T>(Vector3<T> eye, Vector3<T> zAxis, Vector3<T> up)
        where T : unmanaged, IFloatingPointIeee754<T>
    {
        var turn = Matrix3x3.CreateView(zAxis, up);
        var result = (Matrix4x4<T>)turn;
        result.TranslationVector = -Vector3<T>.Transform(eye, turn);
        return result;
    }

    private static Matrix4x4<T> CreateBillboard<T>(Vector3<T> position, Vector3<T> toCamera, Vector3<T> cameraUp,
        Vector3<T> cameraForward) where T : unmanaged, IFloatingPointIeee754<T>
    {
        var result = (Matrix4x4<T>)Matrix3x3.CreateBillboard(toCamera, cameraUp, cameraForward);
        result.TranslationVector = position;
        return result;
    }

    private static Vector3<T> Direction<T>(Vector3<T> value) where T : unmanaged, IFloatingPointIeee754<T>
    {
        var largest = T.Max(T.Abs(value.X), T.Max(T.Abs(value.Y), T.Abs(value.Z)));
        if (largest == T.Zero)
        {
            return Vector3<T>.Zero;
        }

        var scaled = value / largest;
        return scaled / scaled.Length();
    }

    private static T Half<T>() where T : unmanaged, IFloatingPointIeee754<T> => T.CreateTruncating(0.5);
}
