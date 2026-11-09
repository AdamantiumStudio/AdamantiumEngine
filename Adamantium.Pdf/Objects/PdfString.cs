namespace Adamantium.Pdf.Objects;

/// <summary>A string: bytes, literal or hexadecimal in the file. <see cref="ToText"/> reads them as a text string.</summary>
public sealed class PdfString : PdfObject
{
    public PdfString(byte[] bytes, bool isHex = false)
    {
        Bytes = bytes;
        IsHex = isHex;
    }

    /// <summary>The bytes, decrypted when the file is encrypted.</summary>
    public byte[] Bytes { get; internal set; }

    /// <summary>Whether the file wrote it in hexadecimal.</summary>
    public bool IsHex { get; }

    /// <summary>A text string of <paramref name="text"/> (<see cref="PdfTextEncoding.Encode"/>).</summary>
    public static PdfString FromText(string text) => new(PdfTextEncoding.Encode(text));

    /// <summary>The bytes as a text string: UTF-16BE or UTF-8 after their byte order mark, PDFDocEncoding otherwise.
    /// </summary>
    public string ToText() => PdfTextEncoding.Decode(Bytes);

    public override string ToString() => ToText();
}
