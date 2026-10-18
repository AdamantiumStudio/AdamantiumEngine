namespace Adamantium.ProceduralGeometry.Shapes;

public partial class Shapes
{
    public static readonly int PrimitiveRestartValue = -1;

    // Triangles listed point by point as the triangulator lays them out: clockwise on screen to a camera looking along
    // +Z, the side the GPU takes for the front.
    private static List<int> FacingTheEye(int pointCount)
    {
        var indices = new List<int>(pointCount);
        for (int i = 0; i + 2 < pointCount; i += 3)
        {
            indices.Add(i);
            indices.Add(i + 1);
            indices.Add(i + 2);
        }

        return indices;
    }
}