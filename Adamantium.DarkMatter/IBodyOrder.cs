namespace Adamantium.DarkMatter;

/// <summary>An order a search of <see cref="PhysicsSimulation.Find"/> answers in: which of two bodies comes first. A
/// struct, so the sort is built for it and calls it straight.</summary>
public interface IBodyOrder
{
    /// <summary>Below zero when <paramref name="first"/> comes before <paramref name="second"/>, above when after,
    /// zero when either.</summary>
    int Compare(in BodyView first, in BodyView second);
}
