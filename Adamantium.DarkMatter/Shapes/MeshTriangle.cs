namespace Adamantium.DarkMatter.Shapes;

/// <summary>A triangle of a mesh by its three corners, least first, however it is wound.</summary>
internal readonly record struct MeshTriangle(int Low, int Middle, int High)
{
    public static MeshTriangle Of(int first, int second, int third)
    {
        var low = first < second ? first : second;
        var high = first < second ? second : first;
        if (third < low)
            return new MeshTriangle(third, low, high);

        return third > high ? new MeshTriangle(low, high, third) : new MeshTriangle(low, third, high);
    }
}
