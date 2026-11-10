using Adamantium.Pdf.Objects;

namespace Adamantium.Pdf.Graphics;

/// <summary>One visibility expression being evaluated (ISO 32000-2, 8.11.2.3): its operator, how far through its
/// operands it is, and what they have made so far.</summary>
internal sealed class PdfVisibilityFrame(PdfArray expression, string @operator)
{
    public PdfArray Expression { get; } = expression;

    public int Next { get; set; } = 1;

    public bool Result { get; private set; } = @operator != PdfOptionalContentKeys.Or;

    /// <summary>Whether the operands read so far settle it: a false one of an And, a true one of an Or, the one of a
    /// Not.</summary>
    public bool Decided { get; private set; }

    public void Take(bool operand)
    {
        switch (@operator)
        {
            case PdfOptionalContentKeys.Not:
                Result = !operand;
                Decided = true;
                break;
            case PdfOptionalContentKeys.And when !operand:
                Result = false;
                Decided = true;
                break;
            case PdfOptionalContentKeys.Or when operand:
                Result = true;
                Decided = true;
                break;
        }
    }
}
