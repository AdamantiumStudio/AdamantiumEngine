namespace Adamantium.DarkMatter.CollisionDetection;

/// <summary>Where two shapes touch: a normal from the first to the second and points with their separation, negative
/// while overlapping. A world refills one for every pair, so finding contacts allocates nothing.</summary>
internal sealed class ManifoldBuffer
{
    /// <summary>The most points a world may keep for a pair: its <see cref="PhysicsSettings.MaxContactPointsPerPair"/>
    /// is at most this.</summary>
    public const int Capacity = 16;

    /// <summary>The most corners a face turned toward the other shape gives, a hull's included.</summary>
    public const int FaceCapacity = 64;

    /// <summary>The most points a face clipped by a face gives: its corners and a crossing for each edge of the other.
    /// </summary>
    private const int ClipCapacity = 2 * FaceCapacity + 8;

    /// <summary>The most points it holds before <see cref="ReduceTo"/>.</summary>
    public const int PointCapacity = ClipCapacity;

    private readonly float[] _nearestChosen = new float[ClipCapacity];

    public Vector3F Normal;

    public int Count;

    /// <summary>The points found, all of a clipped face, before <see cref="ReduceTo"/> keeps the spread of them.</summary>
    public readonly Vector3F[] Positions = new Vector3F[ClipCapacity];

    public readonly float[] Separations = new float[ClipCapacity];

    /// <summary>Scratch polygons the clipping of a face works between.</summary>
    public readonly Vector3F[] ClipFirst = new Vector3F[ClipCapacity];

    public readonly Vector3F[] ClipSecond = new Vector3F[ClipCapacity];

    /// <summary>The faces of two shapes turned toward each other.</summary>
    public readonly Vector3F[] FaceFirst = new Vector3F[FaceCapacity];

    public readonly Vector3F[] FaceSecond = new Vector3F[FaceCapacity];

    public readonly GjkSimplex Simplex = new();

    public readonly ExpandingPolytope Polytope = new();

    public void Clear()
    {
        Count = 0;
        Normal = default;
    }

    /// <summary>Adds the point midway between where the first shape's surface stands and where the second's does.
    /// </summary>
    public void Add(Vector3F position, float separation)
    {
        if (Count == Positions.Length)
            return;

        Positions[Count] = position;
        Separations[Count] = separation;
        Count++;
    }

    /// <summary>The index of the point of least separation - the deepest; the first for none.</summary>
    public int FindDeepest()
    {
        var deepest = 0;
        for (var index = 1; index < Count; index++)
        {
            if (Separations[index] < Separations[deepest])
                deepest = index;
        }

        return deepest;
    }

    /// <summary>Keeps at most <paramref name="limit"/> points spread as widely as they can be: the deepest first, then
    /// each time the one farthest from all kept.</summary>
    public void ReduceTo(int limit)
    {
        if (Count <= limit)
            return;

        for (var index = 0; index < Count; index++)
        {
            _nearestChosen[index] = float.MaxValue;
        }

        var deepest = FindDeepest();

        var kept = 0;
        var next = deepest;
        while (true)
        {
            Swap(kept, next);
            kept++;
            if (kept == limit)
                break;

            var farthest = -1;
            var farthestDistance = -1f;
            for (var index = kept; index < Count; index++)
            {
                var distance = (Positions[index] - Positions[kept - 1]).LengthSquared();
                if (distance < _nearestChosen[index])
                    _nearestChosen[index] = distance;

                if (_nearestChosen[index] > farthestDistance)
                {
                    farthestDistance = _nearestChosen[index];
                    farthest = index;
                }
            }

            next = farthest;
        }

        Count = limit;
    }

    private void Swap(int first, int second)
    {
        if (first == second)
            return;

        var position = Positions[first];
        Positions[first] = Positions[second];
        Positions[second] = position;

        var separation = Separations[first];
        Separations[first] = Separations[second];
        Separations[second] = separation;

        var nearest = _nearestChosen[first];
        _nearestChosen[first] = _nearestChosen[second];
        _nearestChosen[second] = nearest;
    }
}
