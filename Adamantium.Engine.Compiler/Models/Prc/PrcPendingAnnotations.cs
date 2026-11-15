namespace Adamantium.Engine.Compiler.Models.Prc;

/// <summary>The annotation entities still to be read at one level of nesting, and whether they are a set's.</summary>
internal sealed class PrcPendingAnnotations
{
    internal PrcPendingAnnotations(int remaining, bool inSet)
    {
        Remaining = remaining;
        InSet = inSet;
    }

    internal int Remaining { get; set; }

    internal bool InSet { get; }
}
