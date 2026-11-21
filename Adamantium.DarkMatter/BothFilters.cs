namespace Adamantium.DarkMatter;

/// <summary>Bodies that both conditions keep: one <c>Where</c> after another.</summary>
public readonly struct BothFilters<TFirst, TSecond> : IBodyFilter
    where TFirst : IBodyFilter
    where TSecond : IBodyFilter
{
    private readonly TFirst _first;
    private readonly TSecond _second;

    internal BothFilters(TFirst first, TSecond second)
    {
        _first = first;
        _second = second;
    }

    public bool Matches(in BodyView body) => _first.Matches(body) && _second.Matches(body);
}
