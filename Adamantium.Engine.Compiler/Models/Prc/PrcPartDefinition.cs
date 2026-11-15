using System.Collections.Generic;

namespace Adamantium.Engine.Compiler.Models.Prc;

/// <summary>A part definition (PRC_TYPE_ASM_PartDefinition): its representation items and its markups.</summary>
internal sealed class PrcPartDefinition : PrcTreeEntity
{
    internal List<PrcRepresentationItem> Items { get; } = [];

    internal PrcMarkupData Markups { get; set; }

    internal List<PrcView> Views { get; } = [];
}
