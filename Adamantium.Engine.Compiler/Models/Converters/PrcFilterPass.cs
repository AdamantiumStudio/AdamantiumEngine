using Adamantium.Engine.Compiler.Models.Prc;

namespace Adamantium.Engine.Compiler.Models.Converters;

/// <summary>An inclusive filter an entity above a node is named by, and the ones before it.</summary>
internal sealed class PrcFilterPass
{
    internal PrcFilterPass(PrcFilter filter, PrcFilterPass previous)
    {
        Filter = filter;
        Previous = previous;
    }

    internal PrcFilter Filter { get; }

    internal PrcFilterPass Previous { get; }

    /// <summary>Whether <paramref name="filter"/> is among the filters of <paramref name="pass"/> and the ones before it.</summary>
    internal static bool HasFilter(PrcFilterPass pass, PrcFilter filter)
    {
        for (var current = pass; current != null; current = current.Previous)
        {
            if (current.Filter == filter)
            {
                return true;
            }
        }

        return false;
    }
}
