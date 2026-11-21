namespace Adamantium.DarkMatter;

/// <summary>By one order, and by another where the first finds two alike: <c>ThenBy</c>.</summary>
public readonly struct ThenOrder<TFirst, TSecond> : IBodyOrder
    where TFirst : IBodyOrder
    where TSecond : IBodyOrder
{
    private readonly TFirst _first;
    private readonly TSecond _second;

    internal ThenOrder(TFirst first, TSecond second)
    {
        _first = first;
        _second = second;
    }

    public int Compare(in BodyView first, in BodyView second)
    {
        var order = _first.Compare(first, second);
        return order != 0 ? order : _second.Compare(first, second);
    }
}
