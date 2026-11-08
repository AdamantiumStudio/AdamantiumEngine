namespace Adamantium.Pdf.Parsing;

/// <summary>The character classes of PDF syntax (ISO 32000-2, 7.2.3).</summary>
internal static class PdfCharacters
{
    public static bool IsWhiteSpace(byte value) =>
        value is 0x00 or 0x09 or 0x0A or 0x0C or 0x0D or 0x20;

    public static bool IsDelimiter(byte value) =>
        value is (byte)'(' or (byte)')' or (byte)'<' or (byte)'>' or (byte)'[' or (byte)']' or (byte)'{' or (byte)'}'
            or (byte)'/' or (byte)'%';

    public static bool IsRegular(byte value) => !IsWhiteSpace(value) && !IsDelimiter(value);

    public static bool IsDigit(byte value) => value >= (byte)'0' && value <= (byte)'9';

    /// <summary>The value of a hexadecimal digit, or -1.</summary>
    public static int HexValue(byte value)
    {
        if (value >= (byte)'0' && value <= (byte)'9')
        {
            return value - '0';
        }

        if (value >= (byte)'a' && value <= (byte)'f')
        {
            return value - 'a' + 10;
        }

        if (value >= (byte)'A' && value <= (byte)'F')
        {
            return value - 'A' + 10;
        }

        return -1;
    }
}
