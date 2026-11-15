namespace Adamantium.Engine.Compiler.Models.Prc;

/// <summary>A schema loop under way.</summary>
internal sealed class PrcSchemaLoop
{
    internal PrcSchemaLoop(uint count)
    {
        Count = count;
    }

    internal uint Count { get; }

    internal uint Index { get; set; }
}
