namespace Adamantium.Engine.Compiler.Models.Prc;

/// <summary>A reference to an entity with what it redefines of it (PRC_TYPE_MISC_EntityReference): its own graphics -
/// the style and visibility it gives the target - and a coordinate system of the globals it places the target by.</summary>
internal sealed class PrcEntityReference : PrcTreeEntity
{
    /// <summary>A reference coordinate system of the globals; -1 for none.</summary>
    internal int CoordinateSystemIndex { get; init; } = -1;

    /// <summary>The entity referred to; null for none.</summary>
    internal PrcReferenceTarget Target { get; init; }
}
