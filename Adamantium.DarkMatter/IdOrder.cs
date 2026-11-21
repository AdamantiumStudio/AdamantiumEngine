using System.Collections.Generic;

namespace Adamantium.DarkMatter;

/// <summary>An order told over the ids in a caller's buffer, for the span's own sort: each id read as the last step
/// left it.</summary>
internal readonly struct IdOrder<TOrder>(PhysicsSimulation simulation, TOrder order) : IComparer<BodyId>
    where TOrder : IBodyOrder
{
    public int Compare(BodyId first, BodyId second) =>
        order.Compare(simulation.ViewOf(first), simulation.ViewOf(second));
}
