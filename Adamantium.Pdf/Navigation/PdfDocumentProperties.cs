using System;
using Adamantium.Pdf.Document;
using Adamantium.Pdf.Objects;

namespace Adamantium.Pdf.Navigation;

/// <summary>What the document information dictionary says of a document (ISO 32000-2, 14.3.3): its title, author,
/// the applications that made it, when; null where it says nothing.</summary>
public sealed class PdfDocumentProperties
{
    internal PdfDocumentProperties(PdfDictionary info, PdfDocument document)
    {
        string Text(string key) => (document.Resolve(info?[key]) as PdfString)?.ToText() is { Length: > 0 } text
            ? text
            : null;

        Title = Text("Title");
        Author = Text("Author");
        Subject = Text("Subject");
        Keywords = Text("Keywords");
        Creator = Text("Creator");
        Producer = Text("Producer");
        Created = PdfDate.Parse(Text("CreationDate"));
        Modified = PdfDate.Parse(Text("ModDate"));
    }

    public string Title { get; }

    public string Author { get; }

    public string Subject { get; }

    public string Keywords { get; }

    /// <summary>The application the document was made in.</summary>
    public string Creator { get; }

    /// <summary>The application that wrote it as PDF.</summary>
    public string Producer { get; }

    public DateTimeOffset? Created { get; }

    public DateTimeOffset? Modified { get; }
}
