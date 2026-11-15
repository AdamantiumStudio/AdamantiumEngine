using Adamantium.Engine.Compiler.Models.Prc;

namespace Adamantium.Engine.Compiler.Models.Converters;

/// <summary>A product occurrence by the file structure it lies in and its index there.</summary>
internal readonly struct PrcOccurrenceSlot
{
    internal PrcOccurrenceSlot(PrcFileStructure structure, int index)
    {
        Structure = structure;
        Index = index;
    }

    internal PrcFileStructure Structure { get; }

    internal int Index { get; }
}
