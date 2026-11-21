namespace Adamantium.DarkMatter;

/// <summary>No order: the bodies alike, as a grouping with no order told keeps them.</summary>
public readonly struct NoOrder : IBodyOrder
{
    public int Compare(in BodyView first, in BodyView second) => 0;
}
