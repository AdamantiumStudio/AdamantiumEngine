using System;
using Adamantium.Pdf.Objects;

namespace Adamantium.Pdf.Content;

/// <summary>An inline image (<c>BI ... ID ... EI</c>): its dictionary with the abbreviated keys and names written out in
/// full, and its bytes as the content stream holds them, still filtered.</summary>
public sealed class PdfInlineImage
{
    public PdfInlineImage(PdfDictionary dictionary, ReadOnlyMemory<byte> data)
    {
        Dictionary = dictionary;
        Data = data;
    }

    public PdfDictionary Dictionary { get; }

    public ReadOnlyMemory<byte> Data { get; }
}
