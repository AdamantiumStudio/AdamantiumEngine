namespace Adamantium.Pdf.Functions;

/// <summary>A value on a type 4 function's stack: a number, or a boolean, which the language keeps apart - <c>not</c>
/// of a boolean is logical, of an integer bitwise.</summary>
internal readonly record struct PdfPostScriptValue(double Number, bool IsBoolean = false)
{
    public static PdfPostScriptValue Of(bool value) => new(value ? 1 : 0, true);

    public bool IsTrue => Number != 0;
}
