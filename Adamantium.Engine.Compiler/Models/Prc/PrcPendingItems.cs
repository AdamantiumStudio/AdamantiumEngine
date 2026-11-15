using System.Collections.Generic;

namespace Adamantium.Engine.Compiler.Models.Prc;

/// <summary>The representation items still to be read at one level of nesting: the set they belong to (null at the
/// top), the list they go into, and how many are left.</summary>
internal sealed class PrcPendingItems
{
    internal PrcPendingItems(PrcRepresentationItem set, List<PrcRepresentationItem> into, int remaining)
    {
        Set = set;
        Into = into;
        Remaining = remaining;
    }

    internal PrcRepresentationItem Set { get; }

    internal List<PrcRepresentationItem> Into { get; }

    internal int Remaining { get; set; }
}
