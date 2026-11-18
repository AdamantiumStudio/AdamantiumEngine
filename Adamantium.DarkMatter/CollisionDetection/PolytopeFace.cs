namespace Adamantium.DarkMatter.CollisionDetection;

/// <summary>A triangle of the polytope EPA grows: its corners, wound outward, its outward normal and its distance from
/// the origin.</summary>
internal readonly struct PolytopeFace
{
    public PolytopeFace(int a, int b, int c, Vector3<double> normal, double distance)
    {
        A = a;
        B = b;
        C = c;
        Normal = normal;
        Distance = distance;
    }

    public int A { get; }

    public int B { get; }

    public int C { get; }

    public Vector3<double> Normal { get; }

    public double Distance { get; }
}
