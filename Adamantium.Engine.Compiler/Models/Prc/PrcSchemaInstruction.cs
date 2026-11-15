namespace Adamantium.Engine.Compiler.Models.Prc;

/// <summary>One compiled schema instruction: its code, a value it carries and the instruction it may go to.</summary>
internal readonly struct PrcSchemaInstruction
{
    internal PrcSchemaInstruction(PrcSchemaCode code, double operand, int target = -1)
    {
        Code = code;
        Operand = operand;
        Target = target;
    }

    internal PrcSchemaCode Code { get; }

    internal double Operand { get; }

    internal int Target { get; }
}
