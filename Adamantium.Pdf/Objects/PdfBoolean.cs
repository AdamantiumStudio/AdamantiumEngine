namespace Adamantium.Pdf.Objects;

/// <summary>A boolean.</summary>
public sealed class PdfBoolean : PdfObject
{
    private PdfBoolean(bool value)
    {
        Value = value;
    }

    public static PdfBoolean True { get; } = new(true);

    public static PdfBoolean False { get; } = new(false);

    public bool Value { get; }

    public static PdfBoolean Of(bool value) => value ? True : False;

    public override string ToString() => Value ? "true" : "false";
}
