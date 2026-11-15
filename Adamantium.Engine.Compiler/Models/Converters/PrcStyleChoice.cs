using Adamantium.Engine.Compiler.Models.Prc;

namespace Adamantium.Engine.Compiler.Models.Converters;

/// <summary>A style of a file structure's globals, which a mesh is drawn by.</summary>
internal sealed class PrcStyleChoice
{
    internal PrcStyleChoice(PrcFileStructure structure, int index)
    {
        Structure = structure;
        Index = index;
    }

    internal PrcFileStructure Structure { get; }

    internal int Index { get; }

    internal PrcStyle Style => Structure.Styles[Index];

    internal string MaterialId => $"style {Structure.Index}/{Index}";
}
