namespace Adamantium.Pdf.Fonts;

/// <summary>Reads a character code of a Unicode CMap as the text it is.</summary>
internal static class PdfCodeText
{
    /// <summary>The UTF-16 code units of a code of 2 or 4 bytes, high unit first; null for any other length or a
    /// broken surrogate pair.</summary>
    public static string Utf16(uint code, int length)
    {
        switch (length)
        {
            case 2:
                var unit = (char)code;
                return char.IsSurrogate(unit) ? null : unit.ToString();
            case 4:
                var high = (char)(code >> 16);
                var low = (char)code;
                return char.IsSurrogatePair(high, low) ? new string([high, low]) : null;
            default:
                return null;
        }
    }
}
