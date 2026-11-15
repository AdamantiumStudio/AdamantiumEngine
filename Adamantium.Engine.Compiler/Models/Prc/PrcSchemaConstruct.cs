namespace Adamantium.Engine.Compiler.Models.Prc;

/// <summary>An open construct of the token stream while it compiles: what it is, how far it got and the instructions
/// waiting for a target.</summary>
internal sealed class PrcSchemaConstruct
{
    internal PrcSchemaConstruct(int kind, double operand)
    {
        Kind = kind;
        Operand = operand;
    }

    internal int Kind { get; }

    internal double Operand { get; }

    internal int Stage { get; set; }

    internal int FirstPending { get; set; } = -1;

    internal int SecondPending { get; set; } = -1;

    internal int LoopTop { get; set; } = -1;
}
