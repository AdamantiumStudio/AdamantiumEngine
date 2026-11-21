namespace Adamantium.DarkMatter;

/// <summary>Nearest to a point first, by the center of mass.</summary>
public readonly struct NearestOrder : IBodyOrder
{
    private readonly WorldPosition _point;

    internal NearestOrder(WorldPosition point) => _point = point;

    public int Compare(in BodyView first, in BodyView second) =>
        (first.CenterOfMass - _point).LengthSquared().CompareTo((second.CenterOfMass - _point).LengthSquared());
}
