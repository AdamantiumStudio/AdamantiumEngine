namespace Adamantium.DarkMatter.Dynamics;

/// <summary>The inverse inertia of a body turned into the world's axes, and applied.</summary>
internal static class InertiaMath
{
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
}
