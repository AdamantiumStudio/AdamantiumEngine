using Adamantium.Mathematics;

namespace Adamantium.Engine.Compiler.Models.Prc;

/// <summary>A corner of a triangle as the crease normals see it: its triangle's unit normal - zero for a triangle of no
/// area - and the angle the triangle makes at it, in radians.</summary>
internal readonly struct PrcCreaseCorner
{
    internal PrcCreaseCorner(Vector3<double> normal, double angle)
    {
        Normal = normal;
        Angle = angle;
    }

    internal Vector3<double> Normal { get; }

    internal double Angle { get; }
}
