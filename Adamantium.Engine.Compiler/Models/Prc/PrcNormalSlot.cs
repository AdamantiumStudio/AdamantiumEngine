using System.Collections.Generic;

namespace Adamantium.Engine.Compiler.Models.Prc;

/// <summary>The normals stored for one vertex: whether it has been met, whether it has several, and the indices of
/// those stored.</summary>
internal sealed class PrcNormalSlot
{
    internal bool IsMet { get; set; }

    internal bool HasSeveral { get; set; }

    internal List<int> Normals { get; } = [];
}
