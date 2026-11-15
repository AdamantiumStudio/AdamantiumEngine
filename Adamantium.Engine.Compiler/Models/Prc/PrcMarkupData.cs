using System.Collections.Generic;

namespace Adamantium.Engine.Compiler.Models.Prc;

/// <summary>The markups of a part or a product (PRC MarkupData): the markups themselves, each with its tessellation,
/// and how many leaders, linked items and annotations go with them.</summary>
internal sealed class PrcMarkupData
{
    internal List<PrcMarkup> Markups { get; } = [];

    internal List<PrcMarkup> Leaders { get; } = [];

    internal int LinkedItemCount { get; set; }

    internal int AnnotationCount { get; set; }
}
