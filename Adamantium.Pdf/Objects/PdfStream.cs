using System;

namespace Adamantium.Pdf.Objects;

/// <summary>A stream: its dictionary and its bytes as the file holds them, still encrypted and encoded. The document
/// decodes it.</summary>
public sealed class PdfStream : PdfObject
{
    public PdfStream(PdfDictionary dictionary, ReadOnlyMemory<byte> data)
    {
        Dictionary = dictionary;
        Data = data;
    }

    public PdfDictionary Dictionary { get; }

    /// <summary>The bytes between <c>stream</c> and <c>endstream</c>.</summary>
    public ReadOnlyMemory<byte> Data { get; }

    /// <summary>The indirect object it is, which its decryption key depends on.</summary>
    public PdfObjectId Id { get; internal set; }
}
