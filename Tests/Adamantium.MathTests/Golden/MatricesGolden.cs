using System;
using System.Linq;
using System.Numerics;
using Adamantium.Mathematics;
using Matrix3x2 = Adamantium.Mathematics.Matrix3x2;
using Matrix4x4 = Adamantium.Mathematics.Matrix4x4;
using Quaternion = Adamantium.Mathematics.Quaternion;

namespace Adamantium.MathTests.Golden;

/// <summary>The recorded operations of the matrices, answered by <see cref="Matrix4x4{T}"/>, and those of the vectors
/// and quaternions that take a matrix.</summary>
public static class MatricesGolden
{
    /// <summary>Adds the float operations - Matrix4x4F... - and the double ones - Matrix4x4... - to
    /// <paramref name="map"/>.</summary>
    public static void Register(GoldenMap map)
    {
        Register<float, double>(map, "F", "Single");
        Register<double, float>(map, string.Empty, "Double");
        map.NotMapped(key => key.Contains("TriangularForm(") || key.Contains(".RowEchelonForm("),
            "old bug: eliminated only the leading column, so the answer was not triangular; TypedMatrixTests checks the new");
        map.AnswersWhereOldThrew(key => key.Contains(".ReducedRowEchelonForm("),
            "old bug: rows eliminated to all zeros, augment included, ran the pivot search past the last column");
        map.ChangedWhere(key => GoldenSignature.Parse(key).IsSinglePrecision
                                && ((key.StartsWith("Matrix") && key.Contains(".Invert(")) || key.Contains(".Unproject(")),
            inputs => inputs.All(double.IsFinite) && inputs.Max(Math.Abs) > 1e15,
            "old bug: the determinant of 4e30 elements overflowed float into NaN; now the matrix is scaled by a power of " +
            "two first and the inverse is finite");
        map.ChangedWhere(key => key.StartsWith("Matrix3x3") && IsDecompose(key),
            inputs => UpperDeterminant(inputs, 3) < 0,
            "old bug: a mirror gave a non-unit rotation and a positive scale; now the X scale is negative");
        map.ChangedWhere(key => key.StartsWith("Matrix4x4") && IsDecompose(key),
            inputs => UpperDeterminant(inputs, 4) < 0,
            "old bug: a mirror gave a non-unit rotation and a positive scale; now the X scale is negative");
        map.NotMapped("Matrix4x4F.LookToLH(Vector3F,Vector3F,Vector3F)",
            "old bug: this overload called LookAtRH; the one by reference is checked");
        map.NotMapped(key => key.StartsWith("Matrix4x4") && key.Contains(".Skew("),
            "old bug: added the parallel part instead of taking it away, did not normalize it, and sheared along the " +
            "rotation vector; TypedMatrixTests checks the new");
        map.NotMapped(key => key.StartsWith("Matrix3x2") && key.Contains(".Skew("),
            "old bug: the X angle skewed Y and the Y angle X; TypedMatrix3x2Tests checks the new");
        map.NotMapped(key => key.Contains(".IsometricProjection("),
            "old bug: M12 was set to 1/2, then over to 1; TypedMatrixTests checks the new");
    }

    private static void Register<T, TOther>(GoldenMap map, string suffix, string s)
        where T : unmanaged, IFloatingPointIeee754<T> where TOther : unmanaged, IFloatingPointIeee754<TOther>
    {
        RegisterMatrix4x4<T, TOther>(map, "Matrix4x4" + suffix, "Vector2" + suffix, "Vector3" + suffix, "Vector4" + suffix,
            "Quaternion" + suffix, s);
        RegisterMatrix3x3<T>(map, "Matrix3x3" + suffix, "Matrix4x4" + suffix, "Vector3" + suffix, "Quaternion" + suffix, s);
        RegisterMatrix3x2<T>(map, "Matrix3x2" + suffix, "Vector2" + suffix, s);
        RegisterVectorTransforms<T>(map, suffix, s);
    }

    private static void RegisterMatrix3x2<T>(GoldenMap map, string m, string v2, string s)
        where T : unmanaged, IFloatingPointIeee754<T>
    {
        map.Calls($"{m}.get_Row1(this )", i => Self(i.Matrix3x2<T>(), x => x.Row1));
        map.Calls($"{m}.get_Row2(this )", i => Self(i.Matrix3x2<T>(), x => x.Row2));
        map.Calls($"{m}.get_Row3(this )", i => Self(i.Matrix3x2<T>(), x => x.Row3));
        map.Calls($"{m}.get_Column1(this )", i => Self(i.Matrix3x2<T>(), x => x.Column1.As<float>()));
        map.Calls($"{m}.get_Column2(this )", i => Self(i.Matrix3x2<T>(), x => x.Column2.As<float>()));
        map.Calls($"{m}.get_TranslationVector(this )", i => Self(i.Matrix3x2<T>(), x => x.TranslationVector));
        map.Calls($"{m}.get_ScaleVector(this )", i => Self(i.Matrix3x2<T>(), x => x.ScaleVector));
        map.Calls($"{m}.get_IsIdentity(this )",
            i => Self(i.Matrix3x2<T>(), x => Matrix3x2<T>.NearEqual(x, Matrix3x2<T>.Identity)));
        map.Calls($"{m}.Determinant(this )", i => Self(i.Matrix3x2<T>(), x => x.Determinant()));
        map.Calls($"{m}.Equals(this {m})",
            i => Self(i.Matrix3x2<T>(), i.Matrix3x2<T>(), (x, o) => Matrix3x2<T>.NearEqual(x, o)));
        map.Calls($"{m}.Invert(this )", i =>
        {
            var x = i.Matrix3x2<T>();
            x.Invert();
            return [x];
        });

        map.Returns($"{m}.Add({m},{m})", i => Matrix3x2<T>.Add(i.Matrix3x2<T>(), i.Matrix3x2<T>()));
        map.Returns($"{m}.Subtract({m},{m})", i => Matrix3x2<T>.Subtract(i.Matrix3x2<T>(), i.Matrix3x2<T>()));
        map.Returns($"{m}.Multiply({m},{s})", i => Matrix3x2<T>.Multiply(i.Matrix3x2<T>(), i.Number<T>()));
        map.Returns($"{m}.Multiply({m},{m})", i => Matrix3x2<T>.Multiply(i.Matrix3x2<T>(), i.Matrix3x2<T>()));
        map.Returns($"{m}.Divide({m},{s})", i => Matrix3x2<T>.Divide(i.Matrix3x2<T>(), i.Number<T>()));
        map.Returns($"{m}.Divide({m},{m})", i => Matrix3x2<T>.Divide(i.Matrix3x2<T>(), i.Matrix3x2<T>()));
        map.Returns($"{m}.Negate({m})", i => Matrix3x2<T>.Negate(i.Matrix3x2<T>()));
        map.Returns($"{m}.Lerp({m},{m},{s})", i => Matrix3x2<T>.Lerp(i.Matrix3x2<T>(), i.Matrix3x2<T>(), i.Number<T>()));
        map.Returns($"{m}.SmoothStep({m},{m},{s})", i => Matrix3x2<T>.SmoothStep(i.Matrix3x2<T>(), i.Matrix3x2<T>(), i.Number<T>()));
        map.Returns($"{m}.Invert({m})", i => Matrix3x2<T>.Invert(i.Matrix3x2<T>()));
        map.Returns($"{m}.TransformPoint({m},{v2})", i =>
        {
            var transform = i.Matrix3x2<T>();
            return Vector2<T>.Transform(i.Vector2<T>(), transform);
        });

        map.Returns($"{m}.Scaling({v2})", i => Matrix3x2.CreateScale(i.Vector2<T>()));
        map.Returns($"{m}.Scaling({s},{s})", i => Matrix3x2.CreateScale(i.Number<T>(), i.Number<T>()));
        map.Returns($"{m}.Scaling({s})", i => Matrix3x2.CreateScale(i.Number<T>()));
        map.Returns($"{m}.Scaling({s},{s},{v2})", i => Matrix3x2.CreateScale(i.Number<T>(), i.Number<T>(), i.Vector2<T>()));
        map.Returns($"{m}.Rotation({s})", i => Matrix3x2.CreateRotation(i.Number<T>()));
        map.Returns($"{m}.Rotation({s},{v2})", i => Matrix3x2.CreateRotation(i.Number<T>(), i.Vector2<T>()));
        map.Returns($"{m}.Transformation({s},{s},{s},{s},{s})",
            i => Matrix3x2.CreateTransformation(i.Number<T>(), i.Number<T>(), i.Number<T>(), i.Number<T>(), i.Number<T>()));
        map.Returns($"{m}.Translation({v2})", i => Matrix3x2.CreateTranslation(i.Vector2<T>()));
        map.Returns($"{m}.Translation({s},{s})", i => Matrix3x2.CreateTranslation(i.Number<T>(), i.Number<T>()));

        map.Returns($"{m}.op_Addition({m},{m})", i => i.Matrix3x2<T>() + i.Matrix3x2<T>());
        map.Returns($"{m}.op_UnaryPlus({m})", i => +i.Matrix3x2<T>());
        map.Returns($"{m}.op_Subtraction({m},{m})", i => i.Matrix3x2<T>() - i.Matrix3x2<T>());
        map.Returns($"{m}.op_UnaryNegation({m})", i => -i.Matrix3x2<T>());
        map.Returns($"{m}.op_Multiply({s},{m})", i => i.Number<T>() * i.Matrix3x2<T>());
        map.Returns($"{m}.op_Multiply({m},{s})", i => i.Matrix3x2<T>() * i.Number<T>());
        map.Returns($"{m}.op_Multiply({m},{m})", i => i.Matrix3x2<T>() * i.Matrix3x2<T>());
        map.Returns($"{m}.op_Division({m},{s})", i => i.Matrix3x2<T>() / i.Number<T>());
        map.Returns($"{m}.op_Division({m},{m})", i => i.Matrix3x2<T>() / i.Matrix3x2<T>());
        map.Returns($"{m}.op_Equality({m},{m})", i => Matrix3x2<T>.NearEqual(i.Matrix3x2<T>(), i.Matrix3x2<T>()));
        map.Returns($"{m}.op_Inequality({m},{m})", i => !Matrix3x2<T>.NearEqual(i.Matrix3x2<T>(), i.Matrix3x2<T>()));
        map.Returns($"{m}.op_Implicit(Matrix4x4F)", i => (Matrix3x2<T>)i.Matrix4x4<T>());
    }

    private static void RegisterMatrix3x3<T>(GoldenMap map, string m, string m4, string v3, string q, string s)
        where T : unmanaged, IFloatingPointIeee754<T>
    {
        map.Calls($"{m}.get_Row1(this )", i => Self(i.Matrix3x3<T>(), x => x.Row1));
        map.Calls($"{m}.get_Row2(this )", i => Self(i.Matrix3x3<T>(), x => x.Row2));
        map.Calls($"{m}.get_Row3(this )", i => Self(i.Matrix3x3<T>(), x => x.Row3));
        map.Calls($"{m}.get_Column1(this )", i => Self(i.Matrix3x3<T>(), x => x.Column1));
        map.Calls($"{m}.get_Column2(this )", i => Self(i.Matrix3x3<T>(), x => x.Column2));
        map.Calls($"{m}.get_Column3(this )", i => Self(i.Matrix3x3<T>(), x => x.Column3));
        map.Calls($"{m}.get_ScaleVector(this )", i => Self(i.Matrix3x3<T>(), x => x.ScaleVector));
        map.Calls($"{m}.get_IsIdentity(this )",
            i => Self(i.Matrix3x3<T>(), x => Matrix3x3<T>.NearEqual(x, Matrix3x3<T>.Identity)));
        map.Calls($"{m}.Determinant(this )", i => Self(i.Matrix3x3<T>(), x => x.Determinant()));
        map.Calls($"{m}.Equals(this {m})",
            i => Self(i.Matrix3x3<T>(), i.Matrix3x3<T>(), (x, o) => Matrix3x3<T>.NearEqual(x, o)));
        map.Returns($"{m}.Equals({m},{m})", i => Matrix3x3<T>.NearEqual(i.Matrix3x3<T>(), i.Matrix3x3<T>()));
        map.Calls($"{m}.Invert(this )", i =>
        {
            var x = i.Matrix3x3<T>();
            x.Invert();
            return [x];
        });
        map.Calls($"{m}.Transpose(this )", i =>
        {
            var x = i.Matrix3x3<T>();
            x.Transpose();
            return [x];
        });
        map.Calls($"{m}.Orthogonalize(this )", i =>
        {
            var x = i.Matrix3x3<T>();
            x.Orthogonalize();
            return [x];
        });
        map.Calls($"{m}.Orthonormalize(this )", i =>
        {
            var x = i.Matrix3x3<T>();
            x.Orthonormalize();
            return [x];
        });
        map.Calls($"{m}.ExchangeRows(this Int32,Int32)", i =>
        {
            var x = i.Matrix3x3<T>();
            x.ExchangeRows(i.Int(), i.Int());
            return [x];
        });
        map.Calls($"{m}.ExchangeColumns(this Int32,Int32)", i =>
        {
            var x = i.Matrix3x3<T>();
            x.ExchangeColumns(i.Int(), i.Int());
            return [x];
        });
        map.Calls($"{m}.DecomposeQR(this {m}&,{m}&)", i =>
        {
            var x = i.Matrix3x3<T>();
            x.DecomposeQR(out var qPart, out var rPart);
            return [qPart, rPart, x];
        });
        map.Calls($"{m}.DecomposeLQ(this {m}&,{m}&)", i =>
        {
            var x = i.Matrix3x3<T>();
            x.DecomposeLQ(out var lPart, out var qPart);
            return [lPart, qPart, x];
        });
        map.Calls($"{m}.Decompose(this {v3}&,{q}&)", i =>
        {
            var x = i.Matrix3x3<T>();
            var done = x.Decompose(out var scale, out var rotation);
            return [done, scale, rotation, x];
        });
        map.Calls($"{m}.DecomposeUniformScale(this {s}&,{q}&)", i =>
        {
            var x = i.Matrix3x3<T>();
            var done = x.DecomposeUniformScale(out var scale, out var rotation);
            return [done, scale, rotation, x];
        });

        map.Returns($"{m}.Add({m},{m})", i => Matrix3x3<T>.Add(i.Matrix3x3<T>(), i.Matrix3x3<T>()));
        map.Returns($"{m}.Subtract({m},{m})", i => Matrix3x3<T>.Subtract(i.Matrix3x3<T>(), i.Matrix3x3<T>()));
        map.Returns($"{m}.Multiply({m},{s})", i => Matrix3x3<T>.Multiply(i.Matrix3x3<T>(), i.Number<T>()));
        map.Returns($"{m}.Multiply({m},{m})", i => Matrix3x3<T>.Multiply(i.Matrix3x3<T>(), i.Matrix3x3<T>()));
        map.Returns($"{m}.Divide({m},{s})", i => Matrix3x3<T>.Divide(i.Matrix3x3<T>(), i.Number<T>()));
        map.Returns($"{m}.Divide({m},{m})", i => Matrix3x3<T>.Divide(i.Matrix3x3<T>(), i.Matrix3x3<T>()));
        map.Returns($"{m}.Negate({m})", i => Matrix3x3<T>.Negate(i.Matrix3x3<T>()));
        map.Returns($"{m}.Exponent({m},Int32)", i => Matrix3x3<T>.Exponent(i.Matrix3x3<T>(), i.Int()));
        map.Returns($"{m}.Lerp({m},{m},{s})", i => Matrix3x3<T>.Lerp(i.Matrix3x3<T>(), i.Matrix3x3<T>(), i.Number<T>()));
        map.Returns($"{m}.SmoothStep({m},{m},{s})", i => Matrix3x3<T>.SmoothStep(i.Matrix3x3<T>(), i.Matrix3x3<T>(), i.Number<T>()));
        map.Returns($"{m}.Transpose({m})", i => Matrix3x3<T>.Transpose(i.Matrix3x3<T>()));
        map.Calls($"{m}.TransposeByRef({m}&,{m}&)", i =>
        {
            var x = i.Matrix3x3<T>();
            return [x, Matrix3x3<T>.Transpose(x)];
        });
        map.Returns($"{m}.Invert({m})", i => Matrix3x3<T>.Invert(i.Matrix3x3<T>()));
        map.Returns($"{m}.Orthogonalize({m})", i => Matrix3x3<T>.Orthogonalize(i.Matrix3x3<T>()));
        map.Returns($"{m}.Orthonormalize({m})", i => Matrix3x3<T>.Orthonormalize(i.Matrix3x3<T>()));

        map.Returns($"{m}.BillboardLH({v3},{v3},{v3},{v3})",
            i => Matrix3x3.CreateBillboardLH(i.Vector3<T>(), i.Vector3<T>(), i.Vector3<T>(), i.Vector3<T>()));
        map.Returns($"{m}.BillboardRH({v3},{v3},{v3},{v3})",
            i => Matrix3x3.CreateBillboardRH(i.Vector3<T>(), i.Vector3<T>(), i.Vector3<T>(), i.Vector3<T>()));
        map.Returns($"{m}.LookAtLH({v3},{v3},{v3})", i => Matrix3x3.CreateLookAtLH(i.Vector3<T>(), i.Vector3<T>(), i.Vector3<T>()));
        map.Returns($"{m}.LookAtRH({v3},{v3},{v3})", i => Matrix3x3.CreateLookAtRH(i.Vector3<T>(), i.Vector3<T>(), i.Vector3<T>()));
        map.Returns($"{m}.Scaling({v3})", i => Matrix3x3.CreateScale(i.Vector3<T>()));
        map.Returns($"{m}.Scaling({s},{s},{s})", i => Matrix3x3.CreateScale(i.Number<T>(), i.Number<T>(), i.Number<T>()));
        map.Returns($"{m}.Scaling({s})", i => Matrix3x3.CreateScale(i.Number<T>()));
        map.Returns($"{m}.RotationX({s})", i => Matrix3x3.CreateRotationX(i.Number<T>()));
        map.Returns($"{m}.RotationY({s})", i => Matrix3x3.CreateRotationY(i.Number<T>()));
        map.Returns($"{m}.RotationZ({s})", i => Matrix3x3.CreateRotationZ(i.Number<T>()));
        map.Returns($"{m}.RotationAxis({v3},{s})", i => Matrix3x3.CreateFromAxisAngle(i.Vector3<T>(), i.Number<T>()));
        map.Returns($"{m}.RotationQuaternion({q})", i => Matrix3x3.CreateFromQuaternion(i.Quaternion<T>()));
        map.Returns($"{m}.RotationYawPitchRoll({s},{s},{s})",
            i => Matrix3x3.CreateFromYawPitchRoll(i.Number<T>(), i.Number<T>(), i.Number<T>()));

        map.Returns($"{m}.op_Addition({m},{m})", i => i.Matrix3x3<T>() + i.Matrix3x3<T>());
        map.Returns($"{m}.op_UnaryPlus({m})", i => +i.Matrix3x3<T>());
        map.Returns($"{m}.op_Subtraction({m},{m})", i => i.Matrix3x3<T>() - i.Matrix3x3<T>());
        map.Returns($"{m}.op_UnaryNegation({m})", i => -i.Matrix3x3<T>());
        map.Returns($"{m}.op_Multiply({s},{m})", i => i.Number<T>() * i.Matrix3x3<T>());
        map.Returns($"{m}.op_Multiply({m},{s})", i => i.Matrix3x3<T>() * i.Number<T>());
        map.Returns($"{m}.op_Multiply({m},{m})", i => i.Matrix3x3<T>() * i.Matrix3x3<T>());
        map.Returns($"{m}.op_Division({m},{s})", i => i.Matrix3x3<T>() / i.Number<T>());
        map.Returns($"{m}.op_Division({m},{m})", i => i.Matrix3x3<T>() / i.Matrix3x3<T>());
        map.Returns($"{m}.op_Equality({m},{m})", i => Matrix3x3<T>.NearEqual(i.Matrix3x3<T>(), i.Matrix3x3<T>()));
        map.Returns($"{m}.op_Inequality({m},{m})", i => !Matrix3x3<T>.NearEqual(i.Matrix3x3<T>(), i.Matrix3x3<T>()));
        map.Returns($"{m}.op_Explicit({m})", i => (Matrix4x4<T>)i.Matrix3x3<T>());
        map.Returns($"{m}.op_Explicit({m4})", i => (Matrix3x3<T>)i.Matrix4x4<T>());

        map.Returns($"{q}.RotationMatrix({m})", i => Quaternion.CreateFromRotationMatrix(i.Matrix3x3<T>()));
        map.Returns($"{q}.LookAtLH({v3},{v3},{v3})", i => Quaternion.CreateLookAtLH(i.Vector3<T>(), i.Vector3<T>(), i.Vector3<T>()));
        map.Returns($"{q}.LookAtRH({v3},{v3},{v3})", i => Quaternion.CreateLookAtRH(i.Vector3<T>(), i.Vector3<T>(), i.Vector3<T>()));
        map.Returns($"{q}.RotationLookAtLH({v3},{v3})", i => Quaternion.CreateLookToLH(i.Vector3<T>(), i.Vector3<T>()));
        map.Returns($"{q}.RotationLookAtRH({v3},{v3})", i => Quaternion.CreateLookToRH(i.Vector3<T>(), i.Vector3<T>()));
        map.Returns($"{q}.BillboardLH({v3},{v3},{v3},{v3})",
            i => Quaternion.CreateBillboardLH(i.Vector3<T>(), i.Vector3<T>(), i.Vector3<T>(), i.Vector3<T>()));
        map.Returns($"{q}.RotateToFace({v3},{v3},{v3})", i =>
        {
            var (target, position, up) = (i.Vector3<T>(), i.Vector3<T>(), i.Vector3<T>());
            return Quaternion.CreateFacing(position, target, up);
        });
    }

    private static void RegisterMatrix4x4<T, TOther>(GoldenMap map, string m, string v2, string v3, string v4, string q,
        string s) where T : unmanaged, IFloatingPointIeee754<T> where TOther : unmanaged, IFloatingPointIeee754<TOther>
    {
        map.Calls($"{m}.Determinant(this )", i => Self(i.Matrix4x4<T>(), x => x.Determinant()));
        map.Calls($"{m}.get_IsIdentity(this )",
            i => Self(i.Matrix4x4<T>(), x => Matrix4x4<T>.NearEqual(x, Matrix4x4<T>.Identity)));
        map.Calls($"{m}.get_Up(this )", i => Self(i.Matrix4x4<T>(), x => x.Up));
        map.Calls($"{m}.get_Down(this )", i => Self(i.Matrix4x4<T>(), x => x.Down));
        map.Calls($"{m}.get_Right(this )", i => Self(i.Matrix4x4<T>(), x => x.Right));
        map.Calls($"{m}.get_Left(this )", i => Self(i.Matrix4x4<T>(), x => x.Left));
        map.Calls($"{m}.get_Forward(this )", i => Self(i.Matrix4x4<T>(), x => x.Forward));
        map.Calls($"{m}.get_Backward(this )", i => Self(i.Matrix4x4<T>(), x => x.Backward));
        map.Calls($"{m}.get_Row1(this )", i => Self(i.Matrix4x4<T>(), x => x.Row1));
        map.Calls($"{m}.get_Row2(this )", i => Self(i.Matrix4x4<T>(), x => x.Row2));
        map.Calls($"{m}.get_Row3(this )", i => Self(i.Matrix4x4<T>(), x => x.Row3));
        map.Calls($"{m}.get_Row4(this )", i => Self(i.Matrix4x4<T>(), x => x.Row4));
        map.Calls($"{m}.get_Column1(this )", i => Self(i.Matrix4x4<T>(), x => x.Column1));
        map.Calls($"{m}.get_Column2(this )", i => Self(i.Matrix4x4<T>(), x => x.Column2));
        map.Calls($"{m}.get_Column3(this )", i => Self(i.Matrix4x4<T>(), x => x.Column3));
        map.Calls($"{m}.get_Column4(this )", i => Self(i.Matrix4x4<T>(), x => x.Column4));
        map.Calls($"{m}.get_TranslationVector(this )", i => Self(i.Matrix4x4<T>(), x => x.TranslationVector));
        map.Calls($"{m}.get_ScaleVector(this )", i => Self(i.Matrix4x4<T>(), x => x.ScaleVector));
        map.Calls($"{m}.Equals(this {m})",
            i => Self(i.Matrix4x4<T>(), i.Matrix4x4<T>(), (x, o) => Matrix4x4<T>.NearEqual(x, o)));
        map.Calls($"{m}.Invert(this )", i =>
        {
            var x = i.Matrix4x4<T>();
            x.Invert();
            return [x];
        });
        map.Calls($"{m}.Transpose(this )", i =>
        {
            var x = i.Matrix4x4<T>();
            x.Transpose();
            return [x];
        });
        map.Calls($"{m}.Orthogonalize(this )", i =>
        {
            var x = i.Matrix4x4<T>();
            x.Orthogonalize();
            return [x];
        });
        map.Calls($"{m}.Orthonormalize(this )", i =>
        {
            var x = i.Matrix4x4<T>();
            x.Orthonormalize();
            return [x];
        });
        map.Calls($"{m}.ExchangeRows(this Int32,Int32)", i =>
        {
            var x = i.Matrix4x4<T>();
            x.ExchangeRows(i.Int(), i.Int());
            return [x];
        });
        map.Calls($"{m}.ExchangeColumns(this Int32,Int32)", i =>
        {
            var x = i.Matrix4x4<T>();
            x.ExchangeColumns(i.Int(), i.Int());
            return [x];
        });
        map.Calls($"{m}.DecomposeQR(this {m}&,{m}&)", i =>
        {
            var x = i.Matrix4x4<T>();
            x.DecomposeQR(out var qPart, out var rPart);
            return [qPart, rPart, x];
        });
        map.Calls($"{m}.DecomposeLQ(this {m}&,{m}&)", i =>
        {
            var x = i.Matrix4x4<T>();
            x.DecomposeLQ(out var lPart, out var qPart);
            return [lPart, qPart, x];
        });
        map.Calls($"{m}.Decompose(this {v3}&,{q}&,{v3}&)", i =>
        {
            var x = i.Matrix4x4<T>();
            var done = x.Decompose(out var scale, out var rotation, out var translation);
            return [done, scale, rotation, translation, x];
        });
        map.Calls($"{m}.DecomposeUniformScale(this {s}&,{q}&,{v3}&)", i =>
        {
            var x = i.Matrix4x4<T>();
            var done = x.DecomposeUniformScale(out var scale, out var rotation, out var translation);
            return [done, scale, rotation, translation, x];
        });
        map.Calls($"{m}.ReducedRowEchelonForm({m}&,{v4}&,{m}&,{v4}&)", i =>
        {
            var x = i.Matrix4x4<T>();
            var augment = i.Vector4<T>();
            var (matrix, augmentResult) = Matrix4x4<T>.ReducedRowEchelonForm(x, augment);
            return [x, augment, matrix, augmentResult];
        });

        map.Returns($"{m}.Add({m},{m})", i => Matrix4x4<T>.Add(i.Matrix4x4<T>(), i.Matrix4x4<T>()));
        map.Returns($"{m}.Subtract({m},{m})", i => Matrix4x4<T>.Subtract(i.Matrix4x4<T>(), i.Matrix4x4<T>()));
        map.Returns($"{m}.Multiply({m},{s})", i => Matrix4x4<T>.Multiply(i.Matrix4x4<T>(), i.Number<T>()));
        map.Returns($"{m}.Multiply({m},{m})", i => Matrix4x4<T>.Multiply(i.Matrix4x4<T>(), i.Matrix4x4<T>()));
        map.Returns($"{m}.Divide({m},{s})", i => Matrix4x4<T>.Divide(i.Matrix4x4<T>(), i.Number<T>()));
        map.Returns($"{m}.Divide({m},{m})", i => Matrix4x4<T>.Divide(i.Matrix4x4<T>(), i.Matrix4x4<T>()));
        map.Returns($"{m}.Negate({m})", i => Matrix4x4<T>.Negate(i.Matrix4x4<T>()));
        map.Returns($"{m}.Exponent({m},Int32)", i => Matrix4x4<T>.Exponent(i.Matrix4x4<T>(), i.Int()));
        map.Returns($"{m}.Lerp({m},{m},{s})", i => Matrix4x4<T>.Lerp(i.Matrix4x4<T>(), i.Matrix4x4<T>(), i.Number<T>()));
        map.Returns($"{m}.SmoothStep({m},{m},{s})", i => Matrix4x4<T>.SmoothStep(i.Matrix4x4<T>(), i.Matrix4x4<T>(), i.Number<T>()));
        map.Returns($"{m}.Transpose({m})", i => Matrix4x4<T>.Transpose(i.Matrix4x4<T>()));
        map.Returns($"{m}.Invert({m})", i => Matrix4x4<T>.Invert(i.Matrix4x4<T>()));
        map.Returns($"{m}.Orthogonalize({m})", i => Matrix4x4<T>.Orthogonalize(i.Matrix4x4<T>()));
        map.Returns($"{m}.Orthonormalize({m})", i => Matrix4x4<T>.Orthonormalize(i.Matrix4x4<T>()));

        map.Returns($"{m}.Scaling({v3})", i => Matrix4x4.CreateScale(i.Vector3<T>()));
        map.Returns($"{m}.Scaling({s},{s},{s})", i => Matrix4x4.CreateScale(i.Number<T>(), i.Number<T>(), i.Number<T>()));
        map.Returns($"{m}.Scaling({s})", i => Matrix4x4.CreateScale(i.Number<T>()));
        map.Returns($"{m}.Translation({v3})", i => Matrix4x4.CreateTranslation(i.Vector3<T>()));
        map.Returns($"{m}.Translation({s},{s},{s})", i => Matrix4x4.CreateTranslation(i.Number<T>(), i.Number<T>(), i.Number<T>()));
        map.Returns($"{m}.RotationX({s})", i => Matrix4x4.CreateRotationX(i.Number<T>()));
        map.Returns($"{m}.RotationY({s})", i => Matrix4x4.CreateRotationY(i.Number<T>()));
        map.Returns($"{m}.RotationZ({s})", i => Matrix4x4.CreateRotationZ(i.Number<T>()));
        map.Returns($"{m}.RotationAxis({v3},{s})", i => Matrix4x4.CreateFromAxisAngle(i.Vector3<T>(), i.Number<T>()));
        map.Returns($"{m}.RotationQuaternion({q})", i => Matrix4x4.CreateFromQuaternion(i.Quaternion<T>()));
        map.Returns($"{m}.RotationYawPitchRoll({s},{s},{s})",
            i => Matrix4x4.CreateFromYawPitchRoll(i.Number<T>(), i.Number<T>(), i.Number<T>()));
        map.Returns($"{m}.AffineTransformation({s},{q},{v3})",
            i => Matrix4x4.CreateAffineTransformation(i.Number<T>(), i.Quaternion<T>(), i.Vector3<T>()));
        map.Returns($"{m}.AffineTransformation({s},{v3},{q},{v3})",
            i => Matrix4x4.CreateAffineTransformation(i.Number<T>(), i.Vector3<T>(), i.Quaternion<T>(), i.Vector3<T>()));
        map.Returns($"{m}.AffineTransformation2D({s},{s},{v2})",
            i => Matrix4x4.CreateAffineTransformation2D(i.Number<T>(), i.Number<T>(), i.Vector2<T>()));
        map.Returns($"{m}.AffineTransformation2D({s},{v2},{s},{v2})",
            i => Matrix4x4.CreateAffineTransformation2D(i.Number<T>(), i.Vector2<T>(), i.Number<T>(), i.Vector2<T>()));
        map.Returns($"{m}.Transformation({v3},{q},{v3},{v3},{q},{v3})",
            i => Matrix4x4.CreateTransformation(i.Vector3<T>(), i.Quaternion<T>(), i.Vector3<T>(), i.Vector3<T>(),
                i.Quaternion<T>(), i.Vector3<T>()));
        map.Returns($"{m}.Transformation2D({v2},{s},{v2},{v2},{s},{v2})",
            i => Matrix4x4.CreateTransformation2D(i.Vector2<T>(), i.Number<T>(), i.Vector2<T>(), i.Vector2<T>(), i.Number<T>(),
                i.Vector2<T>()));
        map.Returns($"{m}.LookAtLH({v3},{v3},{v3})", i => Matrix4x4.CreateLookAtLH(i.Vector3<T>(), i.Vector3<T>(), i.Vector3<T>()));
        map.Returns($"{m}.LookAtRH({v3},{v3},{v3})", i => Matrix4x4.CreateLookAtRH(i.Vector3<T>(), i.Vector3<T>(), i.Vector3<T>()));
        map.Calls($"{m}.LookToLH({v3}&,{v3}&,{v3}&,{m}&)", i =>
        {
            var (eye, direction, up) = (i.Vector3<T>(), i.Vector3<T>(), i.Vector3<T>());
            return [eye, direction, up, Matrix4x4.CreateLookToLH(eye, direction, up)];
        });
        map.Returns($"{m}.BillboardLH({v3},{v3},{v3},{v3})",
            i => Matrix4x4.CreateBillboardLH(i.Vector3<T>(), i.Vector3<T>(), i.Vector3<T>(), i.Vector3<T>()));
        map.Returns($"{m}.BillboardRH({v3},{v3},{v3},{v3})",
            i => Matrix4x4.CreateBillboardRH(i.Vector3<T>(), i.Vector3<T>(), i.Vector3<T>(), i.Vector3<T>()));
        map.Returns($"{m}.BillboardForOrthoProjection({v3},{v3},{v3},{v3})",
            i => Matrix4x4.CreateBillboardForOrthoProjection(i.Vector3<T>(), i.Vector3<T>(), i.Vector3<T>(), i.Vector3<T>()));
        map.Returns($"{m}.Ortho({s},{s},{s},{s})", i => Matrix4x4.CreateOrtho(i.Number<T>(), i.Number<T>(), i.Number<T>(), i.Number<T>()));
        map.Returns($"{m}.OrthoLH({s},{s},{s},{s})", i => Matrix4x4.CreateOrthoLH(i.Number<T>(), i.Number<T>(), i.Number<T>(), i.Number<T>()));
        map.Returns($"{m}.OrthoRH({s},{s},{s},{s})", i => Matrix4x4.CreateOrthoRH(i.Number<T>(), i.Number<T>(), i.Number<T>(), i.Number<T>()));
        map.Returns($"{m}.OrthoOffCenter({s},{s},{s},{s},{s},{s})",
            i => Matrix4x4.CreateOrthoOffCenter(i.Number<T>(), i.Number<T>(), i.Number<T>(), i.Number<T>(), i.Number<T>(), i.Number<T>()));
        map.Returns($"{m}.OrthoOffCenterLH({s},{s},{s},{s},{s},{s})",
            i => Matrix4x4.CreateOrthoOffCenterLH(i.Number<T>(), i.Number<T>(), i.Number<T>(), i.Number<T>(), i.Number<T>(), i.Number<T>()));
        map.Returns($"{m}.OrthoOffCenterRH({s},{s},{s},{s},{s},{s})",
            i => Matrix4x4.CreateOrthoOffCenterRH(i.Number<T>(), i.Number<T>(), i.Number<T>(), i.Number<T>(), i.Number<T>(), i.Number<T>()));
        map.Returns($"{m}.PerspectiveLH({s},{s},{s},{s})",
            i => Matrix4x4.CreatePerspectiveLH(i.Number<T>(), i.Number<T>(), i.Number<T>(), i.Number<T>()));
        map.Returns($"{m}.PerspectiveRH({s},{s},{s},{s})",
            i => Matrix4x4.CreatePerspectiveRH(i.Number<T>(), i.Number<T>(), i.Number<T>(), i.Number<T>()));
        map.Returns($"{m}.PerspectiveFovLH({s},{s},{s},{s})",
            i => Matrix4x4.CreatePerspectiveFovLH(i.Number<T>(), i.Number<T>(), i.Number<T>(), i.Number<T>()));
        map.Returns($"{m}.PerspectiveFovRH({s},{s},{s},{s})",
            i => Matrix4x4.CreatePerspectiveFovRH(i.Number<T>(), i.Number<T>(), i.Number<T>(), i.Number<T>()));
        map.Returns($"{m}.PerspectiveFovX({s},{s},{s},{s})",
            i => Matrix4x4.CreatePerspectiveFovX(i.Number<T>() * T.Pi / T.CreateTruncating(180), i.Number<T>(), i.Number<T>(),
                i.Number<T>()));
        map.Returns($"{m}.PerspectiveFovY({s},{s},{s},{s})",
            i => Matrix4x4.CreatePerspectiveFovY(i.Number<T>(), i.Number<T>(), i.Number<T>(), i.Number<T>()));
        map.Returns($"{m}.PerspectiveOffCenter({s},{s},{s},{s},{s},{s})",
            i => Matrix4x4.CreatePerspectiveOffCenter(i.Number<T>(), i.Number<T>(), i.Number<T>(), i.Number<T>(), i.Number<T>(), i.Number<T>()));
        map.Returns($"{m}.PerspectiveOffCenterLH({s},{s},{s},{s},{s},{s})",
            i => Matrix4x4.CreatePerspectiveOffCenterLH(i.Number<T>(), i.Number<T>(), i.Number<T>(), i.Number<T>(), i.Number<T>(), i.Number<T>()));
        map.Returns($"{m}.PerspectiveOffCenterRH({s},{s},{s},{s},{s},{s})",
            i => Matrix4x4.CreatePerspectiveOffCenterRH(i.Number<T>(), i.Number<T>(), i.Number<T>(), i.Number<T>(), i.Number<T>(), i.Number<T>()));

        map.Returns($"{m}.op_Addition({m},{m})", i => i.Matrix4x4<T>() + i.Matrix4x4<T>());
        map.Returns($"{m}.op_UnaryPlus({m})", i => +i.Matrix4x4<T>());
        map.Returns($"{m}.op_Subtraction({m},{m})", i => i.Matrix4x4<T>() - i.Matrix4x4<T>());
        map.Returns($"{m}.op_UnaryNegation({m})", i => -i.Matrix4x4<T>());
        map.Returns($"{m}.op_Multiply({s},{m})", i => i.Number<T>() * i.Matrix4x4<T>());
        map.Returns($"{m}.op_Multiply({m},{s})", i => i.Matrix4x4<T>() * i.Number<T>());
        map.Returns($"{m}.op_Multiply({m},{m})", i => i.Matrix4x4<T>() * i.Matrix4x4<T>());
        map.Returns($"{m}.op_Division({m},{s})", i => i.Matrix4x4<T>() / i.Number<T>());
        map.Returns($"{m}.op_Division({m},{m})", i => i.Matrix4x4<T>() / i.Matrix4x4<T>());
        map.Returns($"{m}.op_Equality({m},{m})", i => Matrix4x4<T>.NearEqual(i.Matrix4x4<T>(), i.Matrix4x4<T>()));
        map.Returns($"{m}.op_Inequality({m},{m})", i => !Matrix4x4<T>.NearEqual(i.Matrix4x4<T>(), i.Matrix4x4<T>()));
        map.Returns($"{m}.op_Explicit({m})", i => i.Matrix4x4<T>().As<TOther>());
        map.Returns($"{m}.op_Explicit(Matrix4x4{(m.EndsWith('F') ? string.Empty : "F")})", i => i.Matrix4x4<TOther>().As<T>());
        map.Calls($"{m}.TransposeByRef({m}&,{m}&)", i =>
        {
            var x = i.Matrix4x4<T>();
            return [x, Matrix4x4<T>.Transpose(x)];
        });

        map.Returns($"{q}.RotationMatrix({m})", i => Quaternion.CreateFromRotationMatrix(i.Matrix4x4<T>()));
    }

    private static void RegisterVectorTransforms<T>(GoldenMap map, string suffix, string s)
        where T : unmanaged, IFloatingPointIeee754<T>
    {
        var (v2, v3, v4, m) = ("Vector2" + suffix, "Vector3" + suffix, "Vector4" + suffix, "Matrix4x4" + suffix);
        map.Returns($"{v2}.Transform({v2},{m})", i => Vector2<T>.Transform(i.Vector2<T>(), i.Matrix4x4<T>()));
        map.Returns($"{v2}.TransformCoordinate({v2},{m})", i => Vector2<T>.TransformCoordinate(i.Vector2<T>(), i.Matrix4x4<T>()));
        map.Returns($"{v2}.TransformNormal({v2},{m})", i => Vector2<T>.TransformNormal(i.Vector2<T>(), i.Matrix4x4<T>()));
        map.Returns($"{v3}.Transform({v3},{m})", i => Vector3<T>.Transform(i.Vector3<T>(), i.Matrix4x4<T>()));
        map.Returns($"{v3}.Transform({v3},Matrix3x3F)", i => Vector3<T>.Transform(i.Vector3<T>(), i.Matrix3x3<T>()));
        map.Calls($"{v3}.Transform({v3}&,{m}&,{v3}&)", i =>
        {
            var (vector, transform) = (i.Vector3<T>(), i.Matrix4x4<T>());
            return [vector, transform, (Vector3<T>)Vector3<T>.Transform(vector, transform)];
        });
        map.Returns($"{v3}.TransformCoordinate({v3},{m})", i => Vector3<T>.TransformCoordinate(i.Vector3<T>(), i.Matrix4x4<T>()));
        map.Returns($"{v3}.TransformNormal({v3},{m})", i => Vector3<T>.TransformNormal(i.Vector3<T>(), i.Matrix4x4<T>()));
        map.Returns($"{v3}.Project({v3},{s},{s},{s},{s},{s},{s},{m})",
            i => Vector3<T>.Project(i.Vector3<T>(), i.Number<T>(), i.Number<T>(), i.Number<T>(), i.Number<T>(), i.Number<T>(),
                i.Number<T>(), i.Matrix4x4<T>()));
        map.Returns($"{v3}.Unproject({v3},{s},{s},{s},{s},{s},{s},{m})",
            i => Vector3<T>.Unproject(i.Vector3<T>(), i.Number<T>(), i.Number<T>(), i.Number<T>(), i.Number<T>(), i.Number<T>(),
                i.Number<T>(), i.Matrix4x4<T>()));
        map.Returns($"{v4}.Transform({v4},{m})", i => Vector4<T>.Transform(i.Vector4<T>(), i.Matrix4x4<T>()));
        map.Returns($"{v4}.Transform({v4},Matrix4x4F)", i => Vector4<T>.Transform(i.Vector4<T>(), i.Matrix4x4<T>()));
    }

    private static bool IsDecompose(string key) => key.Contains(".Decompose(") || key.Contains(".DecomposeUniformScale(");

    /// <summary>The determinant of the upper left 3x3 of the matrix laid out in <paramref name="m"/> by rows of
    /// <paramref name="rowLength"/>.</summary>
    internal static double UpperDeterminant(double[] m, int rowLength) =>
        m[0] * (m[rowLength + 1] * m[2 * rowLength + 2] - m[rowLength + 2] * m[2 * rowLength + 1])
        - m[1] * (m[rowLength] * m[2 * rowLength + 2] - m[rowLength + 2] * m[2 * rowLength])
        + m[2] * (m[rowLength] * m[2 * rowLength + 1] - m[rowLength + 1] * m[2 * rowLength]);

    private static object[] Self<TSelf>(TSelf self, Func<TSelf, object> answer) => [answer(self), self];

    private static object[] Self<TSelf>(TSelf self, TSelf other, Func<TSelf, TSelf, object> answer) =>
        [answer(self, other), self];
}
