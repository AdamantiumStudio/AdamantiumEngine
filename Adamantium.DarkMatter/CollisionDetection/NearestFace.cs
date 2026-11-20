namespace Adamantium.DarkMatter.CollisionDetection;

/// <summary>The face of a tetrahedron nearest the origin yet, its point and the weights of its corners there; -1 for
/// none.</summary>
internal struct NearestFace
{
    public int Face;
    public double Distance;
    public Vector3<double> Nearest;
    public double FirstWeight;
    public double SecondWeight;
    public double ThirdWeight;
}
