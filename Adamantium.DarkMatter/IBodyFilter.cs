namespace Adamantium.DarkMatter;

/// <summary>A condition a search of <see cref="PhysicsSimulation.Find"/> keeps bodies by. A struct of the caller's
/// own is called straight, with no delegate: the JIT builds the search for it and inlines it.</summary>
public interface IBodyFilter
{
    bool Matches(in BodyView body);
}
