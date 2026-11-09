using System;
using Adamantium.Pdf.Document;
using Adamantium.Pdf.Objects;

namespace Adamantium.Pdf.Navigation;

/// <summary>A file embedded in a document (ISO 32000-2, 7.11.4): its name, what it is, and its bytes.</summary>
public sealed class PdfAttachment
{
    private readonly PdfDocument _document;

    private PdfAttachment(PdfDocument document, string key, string fileName, PdfStream stream)
    {
        _document = document;
        Key = key;
        FileName = fileName;
        Stream = stream;
    }

    /// <summary>Its name in the document's list of embedded files; null for one a page's annotation holds.</summary>
    public string Key { get; }

    /// <summary>The file's name.</summary>
    public string FileName { get; }

    public string Description { get; private init; }

    /// <summary>Its media type (<c>Subtype</c>), as "text/plain"; null when not given.</summary>
    public string MimeType { get; private init; }

    /// <summary>Its size in bytes as the file says (<c>Params</c> <c>Size</c>); null when not given.</summary>
    public long? Size { get; private init; }

    public DateTimeOffset? Created { get; private init; }

    public DateTimeOffset? Modified { get; private init; }

    /// <summary>The embedded file stream.</summary>
    public PdfStream Stream { get; }

    /// <summary>The file's bytes, decoded.</summary>
    public ReadOnlyMemory<byte> GetData() => _document.Decode(Stream).Data;

    /// <summary>The attachment of the file specification <paramref name="value"/>; null when it embeds no file.
    /// </summary>
    internal static PdfAttachment From(string key, PdfObject value, PdfDocument document)
    {
        if (document.Resolve(value) is not PdfDictionary specification ||
            document.Resolve(specification["EF"]) is not PdfDictionary files ||
            (document.Resolve(files["UF"]) ?? document.Resolve(files["F"])) is not PdfStream stream)
        {
            return null;
        }

        var parameters = document.Resolve(stream.Dictionary["Params"]) as PdfDictionary;
        var size = document.Resolve(parameters?["Size"]) is PdfInteger declared && declared.Value >= 0
            ? declared.Value
            : (long?)null;
        var mime = PdfValues.Name(stream.Dictionary[PdfKeys.Subtype], document);
        return new PdfAttachment(document, key, PdfFileSpecification.NameOf(specification, document) ?? key, stream)
        {
            Description = (document.Resolve(specification["Desc"]) as PdfString)?.ToText(),
            MimeType = string.IsNullOrEmpty(mime) ? null : mime,
            Size = size,
            Created = PdfDate.Parse((document.Resolve(parameters?["CreationDate"]) as PdfString)?.ToText()),
            Modified = PdfDate.Parse((document.Resolve(parameters?["ModDate"]) as PdfString)?.ToText()),
        };
    }
}
