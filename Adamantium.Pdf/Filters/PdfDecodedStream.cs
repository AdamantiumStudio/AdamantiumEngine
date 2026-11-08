using System;
using Adamantium.Pdf.Objects;

namespace Adamantium.Pdf.Filters;

/// <summary>A stream's bytes after its general filters. An image filter (<c>DCTDecode</c>, <c>JPXDecode</c>,
/// <c>CCITTFaxDecode</c>, <c>JBIG2Decode</c>) is left for the image decoder, with its parameters.</summary>
public sealed class PdfDecodedStream
{
    public PdfDecodedStream(ReadOnlyMemory<byte> data, string imageFilter = null,
        PdfDictionary imageFilterParameters = null)
    {
        Data = data;
        ImageFilter = imageFilter;
        ImageFilterParameters = imageFilterParameters;
    }

    /// <summary>The bytes - the decoder's own buffer, or the file's bytes for a stream with no filter. Not to be
    /// written into.</summary>
    public ReadOnlyMemory<byte> Data { get; }

    /// <summary>The image filter still to apply, or null when the data is fully decoded.</summary>
    public string ImageFilter { get; }

    public PdfDictionary ImageFilterParameters { get; }
}
