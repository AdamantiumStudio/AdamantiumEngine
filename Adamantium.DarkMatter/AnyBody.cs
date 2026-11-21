namespace Adamantium.DarkMatter;

/// <summary>Every body: the search before any <c>Where</c>.</summary>
public readonly struct AnyBody : IBodyFilter
{
    public bool Matches(in BodyView body) => true;
}
