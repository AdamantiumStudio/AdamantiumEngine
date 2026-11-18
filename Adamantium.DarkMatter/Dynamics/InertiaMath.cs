using System;

namespace Adamantium.DarkMatter.Dynamics;

/// <summary>The inverse inertia of a body turned into the world's axes, and applied; the inertia of shapes put together.
/// </summary>
internal static class InertiaMath
{
    private const int MaxSweeps = 32;

    /// <summary>The inverse inertia around the world's axes of a body turned by <paramref name="rotation"/>, whose
    /// inverse moments around its own axes are <paramref name="inverseInertia"/>.</summary>
    public static Matrix3x3F ToWorld(QuaternionF rotation, Vector3F inverseInertia)
    {
        var axisX = Vector3F.Transform(Vector3F.UnitX, rotation);
        var axisY = Vector3F.Transform(Vector3F.UnitY, rotation);
        var axisZ = Vector3F.Transform(Vector3F.UnitZ, rotation);
        var weightedX = axisX * inverseInertia.X;
        var weightedY = axisY * inverseInertia.Y;
        var weightedZ = axisZ * inverseInertia.Z;
        var row1 = weightedX * axisX.X + weightedY * axisY.X + weightedZ * axisZ.X;
        var row2 = weightedX * axisX.Y + weightedY * axisY.Y + weightedZ * axisZ.Y;
        var row3 = weightedX * axisX.Z + weightedY * axisY.Z + weightedZ * axisZ.Z;
        return new Matrix3x3F(row1, row2, row3);
    }

    /// <summary>The symmetric <paramref name="inertia"/> applied to <paramref name="vector"/>.</summary>
    public static Vector3F Apply(in Matrix3x3F inertia, Vector3F vector) => Vector3F.Transform(vector, inertia);

    /// <summary>The inertia tensor of <paramref name="moments"/> around the axes <paramref name="axes"/> turns, carried
    /// <paramref name="offset"/> away for a mass of <paramref name="mass"/>.</summary>
    public static Matrix3x3<double> ToTensor(Vector3F moments, QuaternionF axes, double mass, Vector3<double> offset)
    {
        var axisX = Vector3F.Transform(Vector3F.UnitX, axes).As<double>();
        var axisY = Vector3F.Transform(Vector3F.UnitY, axes).As<double>();
        var axisZ = Vector3F.Transform(Vector3F.UnitZ, axes).As<double>();
        var around = moments.As<double>();
        var weightedX = axisX * around.X;
        var weightedY = axisY * around.Y;
        var weightedZ = axisZ * around.Z;
        var lengthSquared = offset.LengthSquared();
        var row1 = weightedX * axisX.X + weightedY * axisY.X + weightedZ * axisZ.X
                   + (new Vector3<double>(lengthSquared, 0, 0) - offset * offset.X) * mass;
        var row2 = weightedX * axisX.Y + weightedY * axisY.Y + weightedZ * axisZ.Y
                   + (new Vector3<double>(0, lengthSquared, 0) - offset * offset.Y) * mass;
        var row3 = weightedX * axisX.Z + weightedY * axisY.Z + weightedZ * axisZ.Z
                   + (new Vector3<double>(0, 0, lengthSquared) - offset * offset.Z) * mass;
        return new Matrix3x3<double>(row1, row2, row3);
    }

    /// <summary>The principal moments of the symmetric <paramref name="tensor"/> and the rotation of the axes they are
    /// taken around, found by Jacobi's rotations.</summary>
    public static void Diagonalize(in Matrix3x3<double> tensor, out Vector3F moments, out QuaternionF axes)
    {
        Span<double> matrix =
        [
            tensor.M11, tensor.M12, tensor.M13,
            tensor.M21, tensor.M22, tensor.M23,
            tensor.M31, tensor.M32, tensor.M33
        ];
        Span<double> vectors = [1, 0, 0, 0, 1, 0, 0, 0, 1];
        var scale = Math.Abs(matrix[0]) + Math.Abs(matrix[4]) + Math.Abs(matrix[8]);
        for (var sweep = 0; sweep < MaxSweeps; sweep++)
        {
            var off = matrix[1] * matrix[1] + matrix[2] * matrix[2] + matrix[5] * matrix[5];
            if (off <= 1e-30 * scale * scale)
                break;

            Rotate(matrix, vectors, 0, 1);
            Rotate(matrix, vectors, 0, 2);
            Rotate(matrix, vectors, 1, 2);
        }

        var first = new Vector3<double>(vectors[0], vectors[3], vectors[6]);
        var second = new Vector3<double>(vectors[1], vectors[4], vectors[7]);
        var third = Vector3<double>.Cross(first, second);
        moments = new Vector3F((float)matrix[0], (float)matrix[4], (float)matrix[8]);
        axes = QuaternionF.Normalize(Quaternion.CreateFromRotationMatrix(new Matrix3x3<double>(first, second, third)).As<float>());
    }

    private static void Rotate(Span<double> matrix, Span<double> vectors, int p, int q)
    {
        var apq = matrix[p * 3 + q];
        if (apq == 0)
            return;

        var theta = (matrix[q * 3 + q] - matrix[p * 3 + p]) / (2 * apq);
        var tangent = (theta < 0 ? -1 : 1) / (Math.Abs(theta) + Math.Sqrt(theta * theta + 1));
        var cosine = 1 / Math.Sqrt(tangent * tangent + 1);
        var sine = tangent * cosine;
        for (var k = 0; k < 3; k++)
        {
            var akp = matrix[k * 3 + p];
            var akq = matrix[k * 3 + q];
            matrix[k * 3 + p] = cosine * akp - sine * akq;
            matrix[k * 3 + q] = sine * akp + cosine * akq;
        }

        for (var k = 0; k < 3; k++)
        {
            var apk = matrix[p * 3 + k];
            var aqk = matrix[q * 3 + k];
            matrix[p * 3 + k] = cosine * apk - sine * aqk;
            matrix[q * 3 + k] = sine * apk + cosine * aqk;
        }

        for (var k = 0; k < 3; k++)
        {
            var vkp = vectors[k * 3 + p];
            var vkq = vectors[k * 3 + q];
            vectors[k * 3 + p] = cosine * vkp - sine * vkq;
            vectors[k * 3 + q] = sine * vkp + cosine * vkq;
        }
    }
}
