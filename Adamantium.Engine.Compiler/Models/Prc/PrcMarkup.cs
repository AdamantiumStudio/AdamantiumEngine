namespace Adamantium.Engine.Compiler.Models.Prc;

/// <summary>A markup or a leader (PRC_TYPE_MKP_Markup, PRC_TYPE_MKP_Leader): its kind and its markup tessellation.</summary>
internal sealed class PrcMarkup : PrcTreeEntity
{
    internal uint MarkupType { get; init; }

    internal uint SubType { get; init; }

    internal int TessellationIndex { get; set; } = -1;
}
