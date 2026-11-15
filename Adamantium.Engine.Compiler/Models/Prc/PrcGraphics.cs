namespace Adamantium.Engine.Compiler.Models.Prc;

/// <summary>Where an entity lies on and how it is drawn (PRC GraphicsContent): a layer, a style of the globals and the
/// behavior bits, each -1 or 0 when the entity has none.</summary>
internal sealed class PrcGraphics
{
    /// <summary>The entity is shown.</summary>
    internal const ushort Show = 0x0001;

    /// <summary>The entity has been removed from the tree.</summary>
    internal const ushort Removed = 0x2000;

    /// <summary>Every behavior bit set: the behavior is not given.</summary>
    internal const ushort UnsetBehavior = 0xFFFF;

    /// <summary>The graphics an entity has before any are read: no layer, no style, no behavior.</summary>
    internal static PrcGraphics Default { get; } = new() { LayerIndex = -1, StyleIndex = -1, Behavior = UnsetBehavior };

    internal int LayerIndex { get; init; }

    internal int StyleIndex { get; init; }

    internal ushort Behavior { get; init; }

    /// <summary>Whether the behavior bits are given.</summary>
    internal bool HasBehavior => Behavior != UnsetBehavior;

    internal bool IsShown => (Behavior & Show) != 0;

    internal bool IsRemoved => (Behavior & Removed) != 0 && HasBehavior;
}
