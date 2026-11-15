using Adamantium.Engine.Compiler.Models.Prc;
using Adamantium.Graphics.Core.Models;

namespace Adamantium.Engine.Compiler.Models.Converters;

/// <summary>A node waiting to be placed: a product occurrence or a representation item, the file structure it lies
/// in, the model it goes under, the style it inherits, the occurrence it hangs under, where its parent stands in the
/// world and the inclusive filters that kept an entity above it.</summary>
internal sealed class PrcPendingNode
{
    private PrcPendingNode(PrcFileStructure structure, int occurrence, PrcRepresentationItem item, SceneData.Model parent,
        PrcStyleChoice style, PrcPendingNode owner, Matrix4x4F parentWorld, PrcFilterPass admitted)
    {
        Structure = structure;
        Occurrence = occurrence;
        Item = item;
        Parent = parent;
        Style = style;
        Owner = owner;
        ParentWorld = parentWorld;
        Admitted = admitted;
    }

    internal PrcFileStructure Structure { get; }

    /// <summary>The occurrence's index in its file structure, as the file gives it; -1 for a representation item.</summary>
    internal int Occurrence { get; }

    internal PrcRepresentationItem Item { get; }

    internal SceneData.Model Parent { get; }

    /// <summary>The style the node inherits; null for none.</summary>
    internal PrcStyleChoice Style { get; }

    /// <summary>The occurrence node this one was found under; null at the root.</summary>
    internal PrcPendingNode Owner { get; }

    /// <summary>Where the model the node goes under stands in the world, as a row-vector matrix.</summary>
    internal Matrix4x4F ParentWorld { get; }

    /// <summary>The inclusive filters an entity above the node is named by; null for none.</summary>
    internal PrcFilterPass Admitted { get; }

    /// <summary>For an occurrence node once expanded, what it redefines and filters under it; null before.</summary>
    internal PrcOccurrenceContext Context { get; set; }

    internal static PrcPendingNode CreateForOccurrence(PrcFileStructure structure, int occurrence, SceneData.Model parent,
        PrcStyleChoice style, PrcPendingNode owner, Matrix4x4F parentWorld, PrcFilterPass admitted) =>
        new(structure, occurrence, null, parent, style, owner, parentWorld, admitted);

    internal static PrcPendingNode CreateForItem(PrcFileStructure structure, PrcRepresentationItem item, SceneData.Model parent,
        PrcStyleChoice style, PrcPendingNode owner, Matrix4x4F parentWorld, PrcFilterPass admitted) =>
        new(structure, -1, item, parent, style, owner, parentWorld, admitted);

    /// <summary>Whether an occurrence above this node is <paramref name="occurrence"/> of <paramref name="structure"/>:
    /// the tree would then be its own ancestor.</summary>
    internal bool HasAncestor(PrcFileStructure structure, int occurrence)
    {
        for (var node = Owner; node != null; node = node.Owner)
        {
            if (node.Structure == structure && node.Occurrence == occurrence)
            {
                return true;
            }
        }

        return false;
    }
}
