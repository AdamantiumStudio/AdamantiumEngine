namespace Adamantium.Engine.Compiler.Models.Prc;

/// <summary>A root product occurrence of the model file: the file structure it lies in and its index among that file
/// structure's occurrences.</summary>
internal sealed class PrcRootReference
{
    internal PrcRootReference(PrcUniqueId fileStructure, int occurrence)
    {
        FileStructure = fileStructure;
        Occurrence = occurrence;
    }

    internal PrcUniqueId FileStructure { get; }

    internal int Occurrence { get; }
}
