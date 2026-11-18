namespace Adamantium.DarkMatter.CollisionDetection;

/// <summary>A convex polygon cut by a plane (Sutherland and Hodgman): what of it lies behind.</summary>
internal static class PolygonClipping
{
    /// <summary>Writes into <paramref name="output"/> the part of the polygon behind the plane whose normal is
    /// <paramref name="normal"/> and offset <paramref name="limit"/>, and gives its corner count.</summary>
    public static int ClipBehind(Vector3F[] input, int count, Vector3F[] output, Vector3F normal, float limit)
    {
        var kept = 0;
        for (var index = 0; index < count; index++)
        {
            var start = input[index];
            var end = input[(index + 1) % count];
            var startDistance = Vector3F.Dot(normal, start) - limit;
            var endDistance = Vector3F.Dot(normal, end) - limit;
            if (startDistance <= 0)
                output[kept++] = start;

            if ((startDistance < 0 && endDistance > 0) || (startDistance > 0 && endDistance < 0))
                output[kept++] = start + (end - start) * (startDistance / (startDistance - endDistance));
        }

        return kept;
    }
}
