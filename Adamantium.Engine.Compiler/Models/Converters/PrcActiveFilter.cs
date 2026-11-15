using System.Collections.Generic;
using Adamantium.Engine.Compiler.Models.Prc;

namespace Adamantium.Engine.Compiler.Models.Converters;

/// <summary>A filter a product occurrence applies, with the layers and entities it names made ready to look up.</summary>
internal sealed class PrcActiveFilter
{
    internal PrcActiveFilter(PrcFilter filter, PrcFileStructure structure)
    {
        Filter = filter;
        Layers = [.. filter.Layers];
        foreach (var reference in filter.Entities)
        {
            if (reference.Target is { IsTopology: false } target)
            {
                Entities.Add(new PrcEntityKey(target.FileStructure ?? structure.Id, target.UniqueId));
            }
        }
    }

    internal PrcFilter Filter { get; }

    internal HashSet<uint> Layers { get; }

    internal HashSet<PrcEntityKey> Entities { get; } = [];
}
