namespace Adamantium.ProceduralGeometry.Shapes;

public partial class Shapes
{
    public static readonly int PrimitiveRestartValue = -1;

    // Triangles listed point by point, each wound to face -Z, toward a camera looking along +Z: the triangulator lays
    // them out the other way round.
    private static List<int> FacingTheEye(int pointCount)
    {
        var indices = new List<int>(pointCount);
        for (int i = 0; i + 2 < pointCount; i += 3)
        {
            indices.Add(i);
            indices.Add(i + 2);
            indices.Add(i + 1);
        }

        return indices;
    }
}