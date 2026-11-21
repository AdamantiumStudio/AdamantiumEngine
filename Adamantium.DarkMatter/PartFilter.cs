using Adamantium.DarkMatter.Dynamics;

namespace Adamantium.DarkMatter;

/// <summary>The parts a query of the world meets: those on its layers, and triggers only when it asks.</summary>
internal readonly struct PartFilter(ulong layers, bool meetsTriggers)
{
    public bool Meets(in PartState part) => (layers & (1UL << part.Layer)) != 0 && (meetsTriggers || !part.IsTrigger);
}
