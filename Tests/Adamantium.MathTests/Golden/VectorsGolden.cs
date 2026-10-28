using System;
using System.Numerics;
using Adamantium.Mathematics.Typed;
using Quaternion = Adamantium.Mathematics.Typed.Quaternion;

namespace Adamantium.MathTests.Golden;

/// <summary>The recorded operations of the vectors and quaternions, answered by <see cref="Vector2{T}"/>,
/// <see cref="Vector3{T}"/>, <see cref="Vector4{T}"/> and <see cref="Quaternion{T}"/>.</summary>
public static class VectorsGolden
{
    /// <summary>Adds the float operations - Vector2F, QuaternionF... - and the double ones - Vector2, Quaternion... -
    /// to <paramref name="map"/>.</summary>
    public static void Register(GoldenMap map)
    {
        Register<float, double>(map, "F", "Single");
        Register<double, float>(map, string.Empty, "Double");
        map.NotMapped(key => key.Contains(".GetHashCode("), "the hash is not part of the contract");
        map.AnswersWhereOldThrew(key => key.StartsWith("Quaternion") && (key.Contains(".Slerp(") || key.Contains(".Squad(")
                                                                     || key.Contains(".Barycentric(")),
            "old bug: Math.Sign of a NaN dot product - a NaN input, or an overflowed one (4e30 squared, inf + -inf) - " +
            "threw; it gives NaN out now");
        map.ChangedWhere(key => key.StartsWith("Quaternion") && key.Contains(".Barycentric("),
            inputs => inputs[12] + inputs[13] == 0,
            "old bug: weights adding to zero divided 0 by 0 into NaN; now the first rotation comes back");
        map.ChangedWhere(key => key.StartsWith("Quaternion") && key.Contains(".Logarithm("),
            inputs => Math.Abs(inputs[0] * inputs[0] + inputs[1] * inputs[1] + inputs[2] * inputs[2] + inputs[3] * inputs[3] - 1) > 1e-3,
            "defined for unit quaternions: off them the old angle came from W alone, the new from W and the axis part " +
            "together, which also keeps it right next to W = -1; unit ones are still checked");
        map.NotMapped(key => key.Contains(".IsCollinear("),
            "old bug: compared component ratios - 0/0 for a zero component, Z dropped when one Z was zero, an absolute " +
            "tolerance in 2D; TypedVectorTests checks the new");
    }

    private static void Register<T, TOther>(GoldenMap map, string suffix, string s)
        where T : unmanaged, IFloatingPointIeee754<T> where TOther : unmanaged, IFloatingPointIeee754<TOther>
    {
        RegisterVector2<T, TOther>(map, "Vector2" + suffix, s);
        RegisterVector3<T, TOther>(map, "Vector3" + suffix, s);
        RegisterVector4<T, TOther>(map, "Vector4" + suffix, s);
        RegisterQuaternion<T, TOther>(map, "Quaternion" + suffix, "Vector3" + suffix, s);
    }

    private static void RegisterVector2<T, TOther>(GoldenMap map, string v, string s)
        where T : unmanaged, IFloatingPointIeee754<T> where TOther : unmanaged, IFloatingPointIeee754<TOther>
    {
        Operators2<T>(map, v, s);
        map.Calls($"{v}.get_IsNormalized(this )", i => Self(i.Vector2<T>(), x => x.IsNormalized));
        map.Calls($"{v}.get_IsZero(this )", i => Self(i.Vector2<T>(), x => x.IsZero));
        map.Calls($"{v}.Length(this )", i => Self(i.Vector2<T>(), x => x.Length()));
        map.Calls($"{v}.LengthSquared(this )", i => Self(i.Vector2<T>(), x => x.LengthSquared()));
        map.Calls($"{v}.Equals(this {v})", i => Self(i.Vector2<T>(), i.Vector2<T>(), (x, o) => Vector2<T>.NearEqual(x, o)));
        map.Calls($"{v}.Normalize(this )", i =>
        {
            var x = i.Vector2<T>();
            x.Normalize();
            return [x];
        });
        map.Calls($"{v}.Saturate(this )", i =>
        {
            var x = i.Vector2<T>();
            x.Saturate();
            return [x];
        });
        map.Returns($"{v}.Add({v},{v})", i => Vector2<T>.Add(i.Vector2<T>(), i.Vector2<T>()));
        map.Returns($"{v}.Add({v},{s})", i => Vector2<T>.Add(i.Vector2<T>(), i.Number<T>()));
        map.Returns($"{v}.Subtract({v},{v})", i => Vector2<T>.Subtract(i.Vector2<T>(), i.Vector2<T>()));
        map.Returns($"{v}.Subtract({v},{s})", i => Vector2<T>.Subtract(i.Vector2<T>(), i.Number<T>()));
        map.Returns($"{v}.Subtract({s},{v})", i => Vector2<T>.Subtract(i.Number<T>(), i.Vector2<T>()));
        map.Returns($"{v}.Multiply({v},{s})", i => Vector2<T>.Multiply(i.Vector2<T>(), i.Number<T>()));
        map.Returns($"{v}.Multiply({v},{v})", i => Vector2<T>.Multiply(i.Vector2<T>(), i.Vector2<T>()));
        map.Returns($"{v}.Divide({v},{s})", i => Vector2<T>.Divide(i.Vector2<T>(), i.Number<T>()));
        map.Returns($"{v}.Divide({s},{v})", i => Vector2<T>.Divide(i.Number<T>(), i.Vector2<T>()));
        map.Returns($"{v}.Divide({v},{v})", i => i.Vector2<T>() / i.Vector2<T>());
        map.Returns($"{v}.Negate({v})", i => Vector2<T>.Negate(i.Vector2<T>()));
        map.Returns($"{v}.Barycentric({v},{v},{v},{s},{s})",
            i => Vector2<T>.Barycentric(i.Vector2<T>(), i.Vector2<T>(), i.Vector2<T>(), i.Number<T>(), i.Number<T>()));
        map.Returns($"{v}.Clamp({v},{v},{v})", i => Vector2<T>.Clamp(i.Vector2<T>(), i.Vector2<T>(), i.Vector2<T>()));
        map.Returns($"{v}.Distance({v},{v})", i => Vector2<T>.Distance(i.Vector2<T>(), i.Vector2<T>()));
        map.Returns($"{v}.DistanceSquared({v},{v})", i => Vector2<T>.DistanceSquared(i.Vector2<T>(), i.Vector2<T>()));
        map.Returns($"{v}.Dot({v},{v})", i => Vector2<T>.Dot(i.Vector2<T>(), i.Vector2<T>()));
        map.Returns($"{v}.Normalize({v})", i => Vector2<T>.Normalize(i.Vector2<T>()));
        map.Returns($"{v}.Lerp({v},{v},{s})", i => Vector2<T>.Lerp(i.Vector2<T>(), i.Vector2<T>(), i.Number<T>()));
        map.Returns($"{v}.SmoothStep({v},{v},{s})", i => Vector2<T>.SmoothStep(i.Vector2<T>(), i.Vector2<T>(), i.Number<T>()));
        map.Returns($"{v}.Hermite({v},{v},{v},{v},{s})",
            i => Vector2<T>.Hermite(i.Vector2<T>(), i.Vector2<T>(), i.Vector2<T>(), i.Vector2<T>(), i.Number<T>()));
        map.Returns($"{v}.CatmullRom({v},{v},{v},{v},{s})",
            i => Vector2<T>.CatmullRom(i.Vector2<T>(), i.Vector2<T>(), i.Vector2<T>(), i.Vector2<T>(), i.Number<T>()));
        map.Returns($"{v}.Max({v},{v})", i => Vector2<T>.Max(i.Vector2<T>(), i.Vector2<T>()));
        map.Returns($"{v}.Min({v},{v})", i => Vector2<T>.Min(i.Vector2<T>(), i.Vector2<T>()));
        map.Returns($"{v}.Max({v})", i => Vector2<T>.Max(i.Vector2<T>()));
        map.Returns($"{v}.Min({v})", i => Vector2<T>.Min(i.Vector2<T>()));
        map.Returns($"{v}.Average({v})", i => Vector2<T>.Average(i.Vector2<T>()));
        map.Returns($"{v}.Reflect({v},{v})", i => Vector2<T>.Reflect(i.Vector2<T>(), i.Vector2<T>()));
        map.Returns($"{v}.Determinant({v},{v})", i => Vector2<T>.Determinant(i.Vector2<T>(), i.Vector2<T>()));
        map.Returns($"{v}.Cross({v})", i => Vector2<T>.Perpendicular(i.Vector2<T>()));
        map.Returns($"{v}.Round({v},Int32)", i => Vector2<T>.Round(i.Vector2<T>(), i.Int()));
        map.Returns($"{v}.Round({s},{s},Int32)", i => Vector2<T>.Round(new Vector2<T>(i.Number<T>(), i.Number<T>()), i.Int()));
        map.Returns($"{v}.Transform({v},Quaternion{(v.EndsWith('F') ? "F" : string.Empty)})",
            i => Vector2<T>.Transform(i.Vector2<T>(), i.Quaternion<T>()));
        if (!v.EndsWith('F'))
        {
            map.Returns($"{v}.Transform({v},QuaternionF)", i => Vector2<T>.Transform(i.Vector2<T>(), i.Quaternion<T>()));
        }
        map.Returns($"{v}.op_Explicit({v})", i => (Vector3<T>)i.Vector2<T>());
        map.Returns($"{v}.op_Explicit({v})", i => ((Vector3<T>)i.Vector2<T>()).As<TOther>());
        map.Returns($"{v}.op_Explicit({v})", i => (Vector4<T>)i.Vector2<T>());
        map.Returns($"{v}.op_Explicit({v})", i => i.Vector2<T>().As<TOther>());
        map.Returns($"{v}.op_Implicit({v})", i => i.Vector2<T>().As<TOther>());
    }

    private static void RegisterVector3<T, TOther>(GoldenMap map, string v, string s)
        where T : unmanaged, IFloatingPointIeee754<T> where TOther : unmanaged, IFloatingPointIeee754<TOther>
    {
        Operators3<T>(map, v, s);
        var other = v.EndsWith('F') ? "Vector3" : "Vector3F";
        map.Calls($"{v}.get_IsNormalized(this )", i => Self(i.Vector3<T>(), x => x.IsNormalized));
        map.Calls($"{v}.get_IsZero(this )", i => Self(i.Vector3<T>(), x => x.IsZero));
        map.Calls($"{v}.Length(this )", i => Self(i.Vector3<T>(), x => x.Length()));
        map.Calls($"{v}.LengthSquared(this )", i => Self(i.Vector3<T>(), x => x.LengthSquared()));
        map.Calls($"{v}.Equals(this {v})", i => Self(i.Vector3<T>(), i.Vector3<T>(), (x, o) => Vector3<T>.NearEqual(x, o)));
        map.Calls($"{v}.Normalize(this )", i =>
        {
            var x = i.Vector3<T>();
            x.Normalize();
            return [x];
        });
        map.Returns($"{v}.Add({v},{v})", i => Vector3<T>.Add(i.Vector3<T>(), i.Vector3<T>()));
        map.Returns($"{v}.Add({v},{s})", i => Vector3<T>.Add(i.Vector3<T>(), i.Number<T>()));
        map.Returns($"{v}.Subtract({v},{v})", i => Vector3<T>.Subtract(i.Vector3<T>(), i.Vector3<T>()));
        map.Returns($"{v}.Subtract({v},{s})", i => Vector3<T>.Subtract(i.Vector3<T>(), i.Number<T>()));
        map.Returns($"{v}.Subtract({s},{v})", i => Vector3<T>.Subtract(i.Number<T>(), i.Vector3<T>()));
        map.Returns($"{v}.Multiply({v},{s})", i => Vector3<T>.Multiply(i.Vector3<T>(), i.Number<T>()));
        map.Returns($"{v}.Multiply({v},{v})", i => Vector3<T>.Multiply(i.Vector3<T>(), i.Vector3<T>()));
        map.Returns($"{v}.Divide({v},{s})", i => Vector3<T>.Divide(i.Vector3<T>(), i.Number<T>()));
        map.Returns($"{v}.Divide({s},{v})", i => Vector3<T>.Divide(i.Number<T>(), i.Vector3<T>()));
        map.Returns($"{v}.Negate({v})", i => Vector3<T>.Negate(i.Vector3<T>()));
        map.Returns($"{v}.Barycentric({v},{v},{v},{s},{s})",
            i => Vector3<T>.Barycentric(i.Vector3<T>(), i.Vector3<T>(), i.Vector3<T>(), i.Number<T>(), i.Number<T>()));
        map.Returns($"{v}.Clamp({v},{v},{v})", i => Vector3<T>.Clamp(i.Vector3<T>(), i.Vector3<T>(), i.Vector3<T>()));
        map.Returns($"{v}.Cross({v},{v})", i => Vector3<T>.Cross(i.Vector3<T>(), i.Vector3<T>()));
        map.Returns($"{v}.Distance({v},{v})", i => Vector3<T>.Distance(i.Vector3<T>(), i.Vector3<T>()));
        map.Returns($"{v}.DistanceSquared({v},{v})", i => Vector3<T>.DistanceSquared(i.Vector3<T>(), i.Vector3<T>()));
        map.Returns($"{v}.NearEqual({v},{v},{v})", i => Vector3<T>.NearEqual(i.Vector3<T>(), i.Vector3<T>(), i.Vector3<T>()));
        map.Returns($"{v}.Dot({v},{v})", i => Vector3<T>.Dot(i.Vector3<T>(), i.Vector3<T>()));
        map.Returns($"{v}.Normalize({v})", i => Vector3<T>.Normalize(i.Vector3<T>()));
        map.Returns($"{v}.Lerp({v},{v},{s})", i => Vector3<T>.Lerp(i.Vector3<T>(), i.Vector3<T>(), i.Number<T>()));
        map.Returns($"{v}.SmoothStep({v},{v},{s})", i => Vector3<T>.SmoothStep(i.Vector3<T>(), i.Vector3<T>(), i.Number<T>()));
        map.Returns($"{v}.Hermite({v},{v},{v},{v},{s})",
            i => Vector3<T>.Hermite(i.Vector3<T>(), i.Vector3<T>(), i.Vector3<T>(), i.Vector3<T>(), i.Number<T>()));
        map.Returns($"{v}.CatmullRom({v},{v},{v},{v},{s})",
            i => Vector3<T>.CatmullRom(i.Vector3<T>(), i.Vector3<T>(), i.Vector3<T>(), i.Vector3<T>(), i.Number<T>()));
        map.Returns($"{v}.Max({v},{v})", i => Vector3<T>.Max(i.Vector3<T>(), i.Vector3<T>()));
        map.Returns($"{v}.Min({v},{v})", i => Vector3<T>.Min(i.Vector3<T>(), i.Vector3<T>()));
        map.Returns($"{v}.Max({v})", i => Vector3<T>.Max(i.Vector3<T>()));
        map.Returns($"{v}.Min({v})", i => Vector3<T>.Min(i.Vector3<T>()));
        map.Returns($"{v}.Average({v})", i => Vector3<T>.Average(i.Vector3<T>()));
        map.Returns($"{v}.Abs({v})", i => Vector3<T>.Abs(i.Vector3<T>()));
        map.Returns($"{v}.Round({v},Int32)", i => Vector3<T>.Round(i.Vector3<T>(), i.Int()));
        map.Returns($"{v}.Reflect({v},{v})", i => Vector3<T>.Reflect(i.Vector3<T>(), i.Vector3<T>()));
        map.Returns($"{v}.Transform({v},Quaternion{(v.EndsWith('F') ? "F" : string.Empty)})",
            i => Vector3<T>.Transform(i.Vector3<T>(), i.Quaternion<T>()));
        map.Returns($"{v}.op_Subtraction({other},{v})", i => i.Vector3<TOther>().As<T>() - i.Vector3<T>());
        map.Returns($"{v}.op_Addition({v},{other})", i => i.Vector3<T>() + i.Vector3<TOther>().As<T>());
        map.Returns($"{v}.op_Addition({other},{v})", i => i.Vector3<TOther>().As<T>() + i.Vector3<T>());
        map.Returns($"{v}.Add({v},{other})", i => i.Vector3<T>() + i.Vector3<TOther>().As<T>());
        map.Returns($"{v}.Add({other},{v})", i => i.Vector3<TOther>().As<T>() + i.Vector3<T>());
        map.Returns($"{v}.op_Explicit({v})", i => (Vector2<T>)i.Vector3<T>());
        map.Returns($"{v}.op_Explicit({v})", i => ((Vector2<T>)i.Vector3<T>()).As<TOther>());
        map.Returns($"{v}.op_Explicit({v})", i => (Vector4<T>)i.Vector3<T>());
        map.Returns($"{v}.op_Explicit({v})", i => i.Vector3<T>().As<TOther>());
        map.Returns($"{v}.op_Implicit({v})", i => i.Vector3<T>().As<TOther>());
    }

    private static void RegisterVector4<T, TOther>(GoldenMap map, string v, string s)
        where T : unmanaged, IFloatingPointIeee754<T> where TOther : unmanaged, IFloatingPointIeee754<TOther>
    {
        Operators4<T>(map, v, s);
        map.Calls($"{v}.get_IsNormalized(this )", i => Self(i.Vector4<T>(), x => x.IsNormalized));
        map.Calls($"{v}.get_IsZero(this )", i => Self(i.Vector4<T>(), x => x.IsZero));
        map.Calls($"{v}.Length(this )", i => Self(i.Vector4<T>(), x => x.Length()));
        map.Calls($"{v}.LengthSquared(this )", i => Self(i.Vector4<T>(), x => x.LengthSquared()));
        map.Calls($"{v}.Equals(this {v})", i => Self(i.Vector4<T>(), i.Vector4<T>(), (x, o) => Vector4<T>.NearEqual(x, o)));
        map.Calls($"{v}.Normalize(this )", i =>
        {
            var x = i.Vector4<T>();
            x.Normalize();
            return [x];
        });
        map.Returns($"{v}.Add({v},{v})", i => Vector4<T>.Add(i.Vector4<T>(), i.Vector4<T>()));
        map.Returns($"{v}.Add({v},{s})", i => Vector4<T>.Add(i.Vector4<T>(), i.Number<T>()));
        map.Returns($"{v}.Subtract({v},{v})", i => Vector4<T>.Subtract(i.Vector4<T>(), i.Vector4<T>()));
        map.Returns($"{v}.Subtract({v},{s})", i => Vector4<T>.Subtract(i.Vector4<T>(), i.Number<T>()));
        map.Returns($"{v}.Subtract({s},{v})", i => Vector4<T>.Subtract(i.Number<T>(), i.Vector4<T>()));
        map.Returns($"{v}.Multiply({v},{s})", i => Vector4<T>.Multiply(i.Vector4<T>(), i.Number<T>()));
        map.Returns($"{v}.Multiply({v},{v})", i => Vector4<T>.Multiply(i.Vector4<T>(), i.Vector4<T>()));
        map.Returns($"{v}.Divide({v},{s})", i => Vector4<T>.Divide(i.Vector4<T>(), i.Number<T>()));
        map.Returns($"{v}.Divide({s},{v})", i => Vector4<T>.Divide(i.Number<T>(), i.Vector4<T>()));
        map.Returns($"{v}.Negate({v})", i => Vector4<T>.Negate(i.Vector4<T>()));
        map.Returns($"{v}.Barycentric({v},{v},{v},{s},{s})",
            i => Vector4<T>.Barycentric(i.Vector4<T>(), i.Vector4<T>(), i.Vector4<T>(), i.Number<T>(), i.Number<T>()));
        map.Returns($"{v}.Clamp({v},{v},{v})", i => Vector4<T>.Clamp(i.Vector4<T>(), i.Vector4<T>(), i.Vector4<T>()));
        map.Returns($"{v}.Distance({v},{v})", i => Vector4<T>.Distance(i.Vector4<T>(), i.Vector4<T>()));
        map.Returns($"{v}.DistanceSquared({v},{v})", i => Vector4<T>.DistanceSquared(i.Vector4<T>(), i.Vector4<T>()));
        map.Returns($"{v}.Dot({v},{v})", i => Vector4<T>.Dot(i.Vector4<T>(), i.Vector4<T>()));
        map.Returns($"{v}.Normalize({v})", i => Vector4<T>.Normalize(i.Vector4<T>()));
        map.Returns($"{v}.Lerp({v},{v},{s})", i => Vector4<T>.Lerp(i.Vector4<T>(), i.Vector4<T>(), i.Number<T>()));
        map.Returns($"{v}.SmoothStep({v},{v},{s})", i => Vector4<T>.SmoothStep(i.Vector4<T>(), i.Vector4<T>(), i.Number<T>()));
        map.Returns($"{v}.Hermite({v},{v},{v},{v},{s})",
            i => Vector4<T>.Hermite(i.Vector4<T>(), i.Vector4<T>(), i.Vector4<T>(), i.Vector4<T>(), i.Number<T>()));
        map.Returns($"{v}.CatmullRom({v},{v},{v},{v},{s})",
            i => Vector4<T>.CatmullRom(i.Vector4<T>(), i.Vector4<T>(), i.Vector4<T>(), i.Vector4<T>(), i.Number<T>()));
        map.Returns($"{v}.Max({v},{v})", i => Vector4<T>.Max(i.Vector4<T>(), i.Vector4<T>()));
        map.Returns($"{v}.Min({v},{v})", i => Vector4<T>.Min(i.Vector4<T>(), i.Vector4<T>()));
        map.Returns($"{v}.Transform({v},QuaternionF)", i => Vector4<T>.Transform(i.Vector4<T>(), i.Quaternion<T>()));
        map.Returns($"{v}.op_Explicit({v})", i => (Vector2<T>)i.Vector4<T>());
        map.Returns($"{v}.op_Explicit({v})", i => (Vector3<T>)i.Vector4<T>());
    }

    private static void RegisterQuaternion<T, TOther>(GoldenMap map, string q, string v3, string s)
        where T : unmanaged, IFloatingPointIeee754<T> where TOther : unmanaged, IFloatingPointIeee754<TOther>
    {
        map.Calls($"{q}.get_IsIdentity(this )",
            i => Self(i.Quaternion<T>(), x => Quaternion<T>.NearEqual(x, Quaternion<T>.Identity)));
        map.Calls($"{q}.get_IsNormalized(this )", i => Self(i.Quaternion<T>(), x => x.IsNormalized));
        map.Calls($"{q}.get_Angle(this )", i => Self(i.Quaternion<T>(), x => x.Angle));
        map.Calls($"{q}.get_Axis(this )", i => Self(i.Quaternion<T>(), x => x.Axis));
        map.Calls($"{q}.Length(this )", i => Self(i.Quaternion<T>(), x => x.Length()));
        map.Calls($"{q}.LengthSquared(this )", i => Self(i.Quaternion<T>(), x => x.LengthSquared()));
        map.Calls($"{q}.Equals(this {q})",
            i => Self(i.Quaternion<T>(), i.Quaternion<T>(), (x, o) => Quaternion<T>.NearEqual(x, o)));
        map.Calls($"{q}.Conjugate(this )", i =>
        {
            var x = i.Quaternion<T>();
            x.Conjugate();
            return [x];
        });
        map.Calls($"{q}.Invert(this )", i =>
        {
            var x = i.Quaternion<T>();
            x.Invert();
            return [x];
        });
        map.Calls($"{q}.Normalize(this )", i =>
        {
            var x = i.Quaternion<T>();
            x.Normalize();
            return [x];
        });
        map.Calls($"{q}.ToYawPitchRoll(this {s}&,{s}&,{s}&)", i =>
        {
            var x = i.Quaternion<T>();
            x.ToYawPitchRoll(out var yaw, out var pitch, out var roll);
            return [yaw, pitch, roll, x];
        });
        map.Returns($"{q}.Add({q},{q})", i => Quaternion<T>.Add(i.Quaternion<T>(), i.Quaternion<T>()));
        map.Returns($"{q}.Subtract({q},{q})", i => Quaternion<T>.Subtract(i.Quaternion<T>(), i.Quaternion<T>()));
        map.Returns($"{q}.Multiply({q},{s})", i => Quaternion<T>.Multiply(i.Quaternion<T>(), i.Number<T>()));
        map.Returns($"{q}.Multiply({q},{q})", i => Quaternion<T>.Multiply(i.Quaternion<T>(), i.Quaternion<T>()));
        map.Returns($"{q}.Negate({q})", i => Quaternion<T>.Negate(i.Quaternion<T>()));
        map.Returns($"{q}.Euler({q})", i => Quaternion<T>.Euler(i.Quaternion<T>()));
        map.Returns($"{q}.Barycentric({q},{q},{q},{s},{s})",
            i => Quaternion<T>.Barycentric(i.Quaternion<T>(), i.Quaternion<T>(), i.Quaternion<T>(), i.Number<T>(), i.Number<T>()));
        map.Returns($"{q}.Conjugate({q})", i => Quaternion<T>.Conjugate(i.Quaternion<T>()));
        map.Returns($"{q}.Dot({q},{q})", i => Quaternion<T>.Dot(i.Quaternion<T>(), i.Quaternion<T>()));
        map.Returns($"{q}.Exponential({q})", i => Quaternion<T>.Exponential(i.Quaternion<T>()));
        map.Returns($"{q}.Invert({q})", i => Quaternion<T>.Invert(i.Quaternion<T>()));
        map.Returns($"{q}.Lerp({q},{q},{s})", i => Quaternion<T>.Lerp(i.Quaternion<T>(), i.Quaternion<T>(), i.Number<T>()));
        map.Returns($"{q}.Logarithm({q})", i => Quaternion<T>.Logarithm(i.Quaternion<T>()));
        map.Returns($"{q}.Normalize({q})", i => Quaternion<T>.Normalize(i.Quaternion<T>()));
        map.Returns($"{q}.RotationAxis({v3},{s})", i => Quaternion.CreateFromAxisAngle(i.Vector3<T>(), i.Number<T>()));
        map.Returns($"{q}.RotationYawPitchRoll({s},{s},{s})",
            i => Quaternion.CreateFromYawPitchRoll(i.Number<T>(), i.Number<T>(), i.Number<T>()));
        map.Returns($"{q}.Slerp({q},{q},{s})", i => Quaternion<T>.Slerp(i.Quaternion<T>(), i.Quaternion<T>(), i.Number<T>()));
        map.Returns($"{q}.Squad({q},{q},{q},{q},{s})",
            i => Quaternion<T>.Squad(i.Quaternion<T>(), i.Quaternion<T>(), i.Quaternion<T>(), i.Quaternion<T>(), i.Number<T>()));
        map.Returns($"{q}.op_Addition({q},{q})", i => i.Quaternion<T>() + i.Quaternion<T>());
        map.Returns($"{q}.op_Subtraction({q},{q})", i => i.Quaternion<T>() - i.Quaternion<T>());
        map.Returns($"{q}.op_UnaryNegation({q})", i => -i.Quaternion<T>());
        map.Returns($"{q}.op_Multiply({s},{q})", i => i.Number<T>() * i.Quaternion<T>());
        map.Returns($"{q}.op_Multiply({q},{s})", i => i.Quaternion<T>() * i.Number<T>());
        map.Returns($"{q}.op_Multiply({q},{q})", i => i.Quaternion<T>() * i.Quaternion<T>());
        map.Returns($"{q}.op_Equality({q},{q})", i => Quaternion<T>.NearEqual(i.Quaternion<T>(), i.Quaternion<T>()));
        map.Returns($"{q}.op_Inequality({q},{q})", i => !Quaternion<T>.NearEqual(i.Quaternion<T>(), i.Quaternion<T>()));
        map.Returns($"{q}.op_Implicit({q})", i => i.Quaternion<T>().As<TOther>());
    }

    private static void Operators2<T>(GoldenMap map, string v, string s) where T : unmanaged, INumber<T>
    {
        map.Returns($"{v}.op_Addition({v},{v})", i => i.Vector2<T>() + i.Vector2<T>());
        map.Returns($"{v}.op_Addition({v},{s})", i => i.Vector2<T>() + i.Number<T>());
        map.Returns($"{v}.op_Addition({s},{v})", i => i.Number<T>() + i.Vector2<T>());
        map.Returns($"{v}.op_Subtraction({v},{v})", i => i.Vector2<T>() - i.Vector2<T>());
        map.Returns($"{v}.op_Subtraction({v},{s})", i => i.Vector2<T>() - i.Number<T>());
        map.Returns($"{v}.op_Subtraction({s},{v})", i => i.Number<T>() - i.Vector2<T>());
        map.Returns($"{v}.op_Multiply({v},{v})", i => i.Vector2<T>() * i.Vector2<T>());
        map.Returns($"{v}.op_Multiply({v},{s})", i => i.Vector2<T>() * i.Number<T>());
        map.Returns($"{v}.op_Multiply({s},{v})", i => i.Number<T>() * i.Vector2<T>());
        map.Returns($"{v}.op_Division({v},{v})", i => i.Vector2<T>() / i.Vector2<T>());
        map.Returns($"{v}.op_Division({v},{s})", i => i.Vector2<T>() / i.Number<T>());
        map.Returns($"{v}.op_Division({s},{v})", i => i.Number<T>() / i.Vector2<T>());
        map.Returns($"{v}.op_UnaryPlus({v})", i => +i.Vector2<T>());
        map.Returns($"{v}.op_UnaryNegation({v})", i => -i.Vector2<T>());
        map.Returns($"{v}.op_Equality({v},{v})", i => Vector2<T>.NearEqual(i.Vector2<T>(), i.Vector2<T>()));
        map.Returns($"{v}.op_Inequality({v},{v})", i => !Vector2<T>.NearEqual(i.Vector2<T>(), i.Vector2<T>()));
    }

    private static void Operators3<T>(GoldenMap map, string v, string s) where T : unmanaged, INumber<T>
    {
        map.Returns($"{v}.op_Addition({v},{v})", i => i.Vector3<T>() + i.Vector3<T>());
        map.Returns($"{v}.op_Addition({v},{s})", i => i.Vector3<T>() + i.Number<T>());
        map.Returns($"{v}.op_Addition({s},{v})", i => i.Number<T>() + i.Vector3<T>());
        map.Returns($"{v}.op_Subtraction({v},{v})", i => i.Vector3<T>() - i.Vector3<T>());
        map.Returns($"{v}.op_Subtraction({v},{s})", i => i.Vector3<T>() - i.Number<T>());
        map.Returns($"{v}.op_Subtraction({s},{v})", i => i.Number<T>() - i.Vector3<T>());
        map.Returns($"{v}.op_Multiply({v},{v})", i => i.Vector3<T>() * i.Vector3<T>());
        map.Returns($"{v}.op_Multiply({v},{s})", i => i.Vector3<T>() * i.Number<T>());
        map.Returns($"{v}.op_Multiply({s},{v})", i => i.Number<T>() * i.Vector3<T>());
        map.Returns($"{v}.op_Division({v},{v})", i => i.Vector3<T>() / i.Vector3<T>());
        map.Returns($"{v}.op_Division({v},{s})", i => i.Vector3<T>() / i.Number<T>());
        map.Returns($"{v}.op_Division({s},{v})", i => i.Number<T>() / i.Vector3<T>());
        map.Returns($"{v}.op_UnaryPlus({v})", i => +i.Vector3<T>());
        map.Returns($"{v}.op_UnaryNegation({v})", i => -i.Vector3<T>());
        map.Returns($"{v}.op_Equality({v},{v})", i => Vector3<T>.NearEqual(i.Vector3<T>(), i.Vector3<T>()));
        map.Returns($"{v}.op_Inequality({v},{v})", i => !Vector3<T>.NearEqual(i.Vector3<T>(), i.Vector3<T>()));
    }

    private static void Operators4<T>(GoldenMap map, string v, string s) where T : unmanaged, INumber<T>
    {
        map.Returns($"{v}.op_Addition({v},{v})", i => i.Vector4<T>() + i.Vector4<T>());
        map.Returns($"{v}.op_Addition({v},{s})", i => i.Vector4<T>() + i.Number<T>());
        map.Returns($"{v}.op_Addition({s},{v})", i => i.Number<T>() + i.Vector4<T>());
        map.Returns($"{v}.op_Subtraction({v},{v})", i => i.Vector4<T>() - i.Vector4<T>());
        map.Returns($"{v}.op_Subtraction({v},{s})", i => i.Vector4<T>() - i.Number<T>());
        map.Returns($"{v}.op_Subtraction({s},{v})", i => i.Number<T>() - i.Vector4<T>());
        map.Returns($"{v}.op_Multiply({v},{v})", i => i.Vector4<T>() * i.Vector4<T>());
        map.Returns($"{v}.op_Multiply({v},{s})", i => i.Vector4<T>() * i.Number<T>());
        map.Returns($"{v}.op_Multiply({s},{v})", i => i.Number<T>() * i.Vector4<T>());
        map.Returns($"{v}.op_Division({v},{v})", i => i.Vector4<T>() / i.Vector4<T>());
        map.Returns($"{v}.op_Division({v},{s})", i => i.Vector4<T>() / i.Number<T>());
        map.Returns($"{v}.op_Division({s},{v})", i => i.Number<T>() / i.Vector4<T>());
        map.Returns($"{v}.op_UnaryPlus({v})", i => +i.Vector4<T>());
        map.Returns($"{v}.op_UnaryNegation({v})", i => -i.Vector4<T>());
        map.Returns($"{v}.op_Equality({v},{v})", i => Vector4<T>.NearEqual(i.Vector4<T>(), i.Vector4<T>()));
        map.Returns($"{v}.op_Inequality({v},{v})", i => !Vector4<T>.NearEqual(i.Vector4<T>(), i.Vector4<T>()));
    }

    private static object[] Self<TSelf>(TSelf self, Func<TSelf, object> answer) => [answer(self), self];

    private static object[] Self<TSelf>(TSelf self, TSelf other, Func<TSelf, TSelf, object> answer) =>
        [answer(self, other), self];
}
