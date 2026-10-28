using System;
using System.Numerics;
using Adamantium.Mathematics;
using Quaternion = Adamantium.Mathematics.Quaternion;

namespace Adamantium.MathTests.Golden;

/// <summary>The recorded operations of the old MathHelper, answered by <see cref="MathHelper"/>'s classes, the number
/// types' own conversions and clamps, and the vectors and quaternions where they took those over.</summary>
public static class HelpersGolden
{
    private const string H = "MathHelper";

    /// <summary>Adds the operations of MathHelper to <paramref name="map"/>.</summary>
    public static void Register(GoldenMap map)
    {
        RegisterScalars(map);
        RegisterAngles(map);
        RegisterPlane(map);
        map.NotMapped(key => key is $"{H}.SmootherStep(Single)" or $"{H}.Mod(Single,Single)" or $"{H}.Mod2PI(Single)"
                          || key.StartsWith($"{H}.Wrap(") || key == $"{H}.AngleBetween2D(Vector3F,Vector3F)"
                          || key == $"{H}.DistanceToPoint(Vector2,Vector2,Vector2)"
                          || key == $"{H}.IsPointInTriangle(Vector2,Vector2,Vector2,Vector2)",
            "used nowhere, not even by the old types themselves - not carried over");
        map.NotMapped($"{H}.Lerp(Byte,Byte,Single)", "a blend of color channels: comes with the colors");
        map.NotMapped(key => key is $"{H}.AngleBetween(Vector3F,Vector3F,Vector3F)" or $"{H}.AngleBetween(Vector3,Vector3,Vector3)",
            "the old took the normal's length into the angle, and measured clockwise; used by no one outside the old " +
            "types - TypedHelperTests checks Angle.Between");
        map.NotMapped($"{H}.AngleBetween(LineSegment2D,LineSegment2D,Boolean)",
            "takes the old LineSegment2D, moved over in Ф6; Angle.Turn does it - TypedHelperTests checks it");
        map.ChangedWhere(key => key.StartsWith($"{H}.Clamp("), inputs => inputs[1] > inputs[2],
            "a minimum above the maximum: the old gave the minimum below it and the maximum above, T.Clamp refuses it");
        map.ChangedWhere(key => key == $"{H}.AngleBetween(Vector3F,Vector3F,Boolean)", inputs => inputs[6] == 0,
            "old bug: without a full turn it took the arccosine of the dot product of vectors not of length one - NaN " +
            "past 1 - and gave degrees; Angle.Between gives the angle itself");
        map.ChangedWhere(key => key == $"{H}.GetRotationFromMatrix(Matrix4x4F)", inputs => MatricesGolden.UpperDeterminant(inputs, 4) < 0,
            "old bug: a mirror gave a non-unit rotation, as Matrix4x4F.Decompose did");
        map.ChangedWhere(key => key is $"{H}.AngleBetween(Vector2,Vector2,Vector2,Vector2)" or $"{H}.DetermineAngleInDegrees(Vector2,Vector2,Vector2,Vector2)",
            inputs => (inputs[0] == inputs[2] && inputs[1] == inputs[3]) || (inputs[4] == inputs[6] && inputs[5] == inputs[7]),
            "a segment of no length: the old divided by its length into NaN; the arctangent gives 0");
        map.Tolerance(key => key == $"{H}.DegreesToRadians(Double)", 1e-7,
            "the old gave a float for a double; the new keeps the double");
    }

    private static void RegisterScalars(GoldenMap map)
    {
        map.Returns($"{H}.NearEqual(Single,Single)", i => MathHelper.Scalar.NearEqual(i.Number<float>(), i.Number<float>()));
        map.Returns($"{H}.NearEqual(Double,Double)", i => MathHelper.Scalar.NearEqual(i.Number<double>(), i.Number<double>()));
        map.Returns($"{H}.NearEqual(Vector3,Vector3)", i => Vector3<double>.NearEqual(i.Vector3<double>(), i.Vector3<double>()));
        map.Returns($"{H}.WithinEpsilon(Single,Single,Single)",
            i => MathHelper.Scalar.WithinEpsilon(i.Number<float>(), i.Number<float>(), i.Number<float>()));
        map.Returns($"{H}.WithinEpsilon(Double,Double,Double)",
            i => MathHelper.Scalar.WithinEpsilon(i.Number<double>(), i.Number<double>(), i.Number<double>()));
        map.Returns($"{H}.WithinEpsilon(Vector3F,Vector3F,Single)", i =>
        {
            var (a, b, epsilon) = (i.Vector3<float>(), i.Vector3<float>(), i.Number<float>());
            return Vector3<float>.NearEqual(a, b, new Vector3<float>(epsilon, epsilon, epsilon));
        });
        map.Returns($"{H}.WithinEpsilon(Vector3,Vector3,Double)", i =>
        {
            var (a, b, epsilon) = (i.Vector3<double>(), i.Vector3<double>(), i.Number<double>());
            return Vector3<double>.NearEqual(a, b, new Vector3<double>(epsilon, epsilon, epsilon));
        });
        map.Returns($"{H}.WithinEpsilon(Vector2,Vector2,Double)", i =>
        {
            var (a, b, epsilon) = (i.Vector2<double>(), i.Vector2<double>(), i.Number<double>());
            return Vector2<double>.NearEqual(a, b, new Vector2<double>(epsilon, epsilon));
        });
        map.Returns($"{H}.Lerp(Double,Double,Double)", i => MathHelper.Scalar.Lerp(i.Number<double>(), i.Number<double>(), i.Number<double>()));
        map.Returns($"{H}.Lerp(Single,Single,Single)", i => MathHelper.Scalar.Lerp(i.Number<float>(), i.Number<float>(), i.Number<float>()));
        map.Returns($"{H}.SmoothStep(Single)", i => MathHelper.Scalar.SmoothStep(i.Number<float>()));
        map.Returns($"{H}.SmoothStep(Double)", i => MathHelper.Scalar.SmoothStep(i.Number<double>()));
        map.Returns($"{H}.IsZero(Single)", i => MathHelper.Scalar.IsZero(i.Number<float>()));
        map.Returns($"{H}.IsZero(Double)", i => MathHelper.Scalar.IsZero(i.Number<double>()));
        map.Returns($"{H}.IsOne(Single)", i => MathHelper.Scalar.IsZero(i.Number<float>() - 1));
        map.Returns($"{H}.IsOne(Double)", i => MathHelper.Scalar.IsZero(i.Number<double>() - 1));
        map.Returns($"{H}.Clamp(Single,Single,Single)", i => float.Clamp(i.Number<float>(), i.Number<float>(), i.Number<float>()));
        map.Returns($"{H}.Clamp(Double,Double,Double)", i => double.Clamp(i.Number<double>(), i.Number<double>(), i.Number<double>()));
    }

    private static void RegisterAngles(GoldenMap map)
    {
        map.Returns($"{H}.DegreesToRadians(Single)", i => float.DegreesToRadians(i.Number<float>()));
        map.Returns($"{H}.DegreesToRadians(Double)", i => double.DegreesToRadians(i.Number<double>()));
        map.Returns($"{H}.RadiansToDegrees(Single)", i => float.RadiansToDegrees(i.Number<float>()));
        map.Returns($"{H}.RadiansToDegrees(Double)", i => double.RadiansToDegrees(i.Number<double>()));
        map.Returns($"{H}.AngleBetween(Vector3F,Vector3F,Boolean)", i =>
        {
            var (from, to) = (i.Vector3<float>(), i.Vector3<float>());
            return i.Number<double>() != 0
                ? float.RadiansToDegrees(MathHelper.Angle.Turn(new Vector2<float>(from.X, from.Y), new Vector2<float>(to.X, to.Y)))
                : float.RadiansToDegrees(MathHelper.Angle.Between(from, to));
        });
        map.Returns($"{H}.AngleBetween(Vector2,Vector2,Vector2,Vector2)", i =>
        {
            var (start1, end1, start2, end2) = (i.Vector2<double>(), i.Vector2<double>(), i.Vector2<double>(), i.Vector2<double>());
            return double.RadiansToDegrees(MathHelper.Angle.Between(end1 - start1, end2 - start2));
        });
        map.Returns($"{H}.DetermineAngleInDegrees(Vector2,Vector2,Vector2,Vector2)", i =>
        {
            var (start1, end1, start2, end2) = (i.Vector2<double>(), i.Vector2<double>(), i.Vector2<double>(), i.Vector2<double>());
            return double.RadiansToDegrees(Math.Abs(MathHelper.Angle.Between(end1 - start1, end2 - start2)));
        });
        map.Returns($"{H}.GetRotationFromMatrix(Matrix4x4F)", i =>
        {
            i.Matrix4x4<float>().Decompose(out _, out var rotation, out _);
            return rotation;
        });
        map.Returns($"{H}.RotateToFace(Vector3F,Vector3F,Vector3F)", i =>
        {
            var (target, position, up) = (i.Vector3<float>(), i.Vector3<float>(), i.Vector3<float>());
            return Quaternion.CreateFacing(position, target, up);
        });
        map.Returns($"{H}.RelativeRotation(QuaternionF,QuaternionF)",
            i => Quaternion<float>.Invert(i.Quaternion<float>()) * i.Quaternion<float>());
    }

    private static void RegisterPlane(GoldenMap map)
    {
        map.Returns($"{H}.Cross2D(Vector3F,Vector3F)", i => Determinant(i.Vector3<float>(), i.Vector3<float>()));
        map.Returns($"{H}.Cross2D(Vector2,Vector2)", i => Vector2<double>.Determinant(i.Vector2<double>(), i.Vector2<double>()));
        map.Returns($"{H}.Determinant(Vector3F,Vector3F)", i => Determinant(i.Vector3<float>(), i.Vector3<float>()));
        map.Returns($"{H}.Determinant(Vector3,Vector3)", i => Determinant(i.Vector3<double>(), i.Vector3<double>()));
        map.Returns($"{H}.Determinant(Vector2,Vector2)", i => Vector2<double>.Determinant(i.Vector2<double>(), i.Vector2<double>()));
        map.Returns($"{H}.IsClockwise(Vector2F,Vector2F,Vector2F)",
            i => MathHelper.Polyline.IsClockwise(i.Vector2<float>(), i.Vector2<float>(), i.Vector2<float>()));
        map.Returns($"{H}.IsClockwise(Vector2,Vector2,Vector2,Vector3F)", i =>
        {
            var (first, second, third, view) = (i.Vector2<double>(), i.Vector2<double>(), i.Vector2<double>(), i.Vector3<float>());
            return Vector2<double>.Determinant(second - first, third - first) * view.Z > 0;
        });
        map.Returns($"{H}.Dot(Vector2,Vector2)", i => Vector2<double>.Dot(i.Vector2<double>(), i.Vector2<double>()));
        map.Returns($"{H}.LineSegmentToVector(Vector2,Vector2)", i =>
        {
            var start = i.Vector2<double>();
            return i.Vector2<double>() - start;
        });
        map.Returns($"{H}.Distance(Vector2,Vector2)", i => Vector2<double>.Distance(i.Vector2<double>(), i.Vector2<double>()));
        map.Returns($"{H}.Distance(Double,Double,Double,Double)", i =>
        {
            var start = new Vector2<double>(i.Number<double>(), i.Number<double>());
            return Vector2<double>.Distance(start, new Vector2<double>(i.Number<double>(), i.Number<double>()));
        });
        map.Returns($"{H}.PointToLineDistance(Vector2,Vector2,Vector2)",
            i => MathHelper.Polyline.DistanceToLine(i.Vector2<double>(), i.Vector2<double>(), i.Vector2<double>()));
    }

    private static T Determinant<T>(Vector3<T> left, Vector3<T> right) where T : unmanaged, IFloatingPointIeee754<T> =>
        Vector2<T>.Determinant(new Vector2<T>(left.X, left.Y), new Vector2<T>(right.X, right.Y));
}
