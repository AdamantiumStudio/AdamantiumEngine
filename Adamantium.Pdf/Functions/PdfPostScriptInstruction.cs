namespace Adamantium.Pdf.Functions;

/// <summary>One step of a compiled type 4 function: an operator, the number a push pushes, the blocks an
/// <c>if</c> or <c>ifelse</c> runs.</summary>
internal readonly record struct PdfPostScriptInstruction(
    PdfPostScriptOperator Operator,
    double Value = 0,
    int Block = -1,
    int ElseBlock = -1);
