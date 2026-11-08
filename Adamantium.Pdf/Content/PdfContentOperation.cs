using System.Collections.Generic;
using Adamantium.Pdf.Objects;

namespace Adamantium.Pdf.Content;

/// <summary>One operation of a content stream: the operator and the operands written before it.</summary>
public sealed class PdfContentOperation
{
    public PdfContentOperation(string @operator, IReadOnlyList<PdfObject> operands, PdfInlineImage inlineImage = null)
    {
        Operator = @operator;
        Operands = operands;
        InlineImage = inlineImage;
    }

    public string Operator { get; }

    public IReadOnlyList<PdfObject> Operands { get; }

    /// <summary>The image of a <c>BI</c> operation, null for every other.</summary>
    public PdfInlineImage InlineImage { get; }

    /// <summary>The operand at <paramref name="index"/> as a number, or <paramref name="fallback"/>.</summary>
    public double Number(int index, double fallback = 0) =>
        index < Operands.Count
            ? Operands[index] switch
            {
                PdfInteger integer => integer.Value,
                PdfReal real => real.Value,
                _ => fallback,
            }
            : fallback;

    public override string ToString() => string.Join(" ", Operands) + (Operands.Count > 0 ? " " : "") + Operator;
}
