using System.Collections.Generic;

namespace Adamantium.Engine.Compiler.Models.Converters;

/// <summary>What a product occurrence does to the entities under it: their redefinitions by reference, by the entity
/// each is of, and the filters it shows them by.</summary>
internal sealed class PrcOccurrenceContext
{
    internal Dictionary<PrcEntityKey, PrcRedefinition> Redefinitions { get; } = new();

    internal List<PrcActiveFilter> Filters { get; } = [];
}
