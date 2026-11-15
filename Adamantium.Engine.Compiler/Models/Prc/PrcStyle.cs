namespace Adamantium.Engine.Compiler.Models.Prc;

/// <summary>A line style of the globals (PRC_TYPE_GRAPH_Style): a color or a material, a transparency, a line width.</summary>
internal sealed class PrcStyle
{
    internal string Name { get; init; }

    internal double LineWidth { get; init; }

    internal bool IsMaterial { get; init; }

    /// <summary>The index of the color, or of the material when <see cref="IsMaterial"/>; -1 for none.</summary>
    internal int ColorOrMaterialIndex { get; init; }

    /// <summary>0 transparent to 255 opaque; null when the style says nothing of it.</summary>
    internal byte? Transparency { get; init; }
}
