using Adamantium.Engine.Compiler.Models.Prc;

namespace Adamantium.Engine.Compiler.Models.Converters;

/// <summary>An entity reference of a product occurrence with the file structure whose globals its style and
/// coordinate system index.</summary>
internal sealed class PrcRedefinition
{
    internal PrcRedefinition(PrcEntityReference reference, PrcFileStructure structure)
    {
        Reference = reference;
        Structure = structure;
    }

    internal PrcEntityReference Reference { get; }

    internal PrcFileStructure Structure { get; }
}
