using Adamantium.Pdf.Document;
using Adamantium.Pdf.Objects;

namespace Adamantium.Pdf.Navigation;

/// <summary>What following a link or an outline item does (ISO 32000-2, 12.6): go to a destination here or in another
/// file, open an address or a file, take a viewer's own step.</summary>
public sealed class PdfAction
{
    internal PdfAction(PdfActionKind kind)
    {
        Kind = kind;
    }

    public PdfActionKind Kind { get; }

    /// <summary>Where a <see cref="PdfActionKind.GoTo"/> goes, or the page of another file a
    /// <see cref="PdfActionKind.GoToRemote"/> opens at; null when it is not given or not found.</summary>
    public PdfDestination Destination { get; private init; }

    /// <summary>The name of a destination in another file, which only that file can find.</summary>
    public string RemoteDestinationName { get; private init; }

    /// <summary>The address of a <see cref="PdfActionKind.Uri"/> action, the document's base address before it when
    /// it is relative.</summary>
    public string Uri { get; private init; }

    /// <summary>The file a <see cref="PdfActionKind.GoToRemote"/> or <see cref="PdfActionKind.Launch"/> opens.</summary>
    public string File { get; private init; }

    /// <summary>The step of a <see cref="PdfActionKind.Named"/> action, as "NextPage", "PrevPage", "FirstPage",
    /// "LastPage".</summary>
    public string Name { get; private init; }

    /// <summary>A go-to of <paramref name="destination"/> - what a link or an outline item with a <c>/Dest</c> does.
    /// </summary>
    internal static PdfAction ForDestination(PdfDestination destination) =>
        new(PdfActionKind.GoTo) { Destination = destination };

    /// <summary>The action of the dictionary <paramref name="value"/>; null when it is none.</summary>
    internal static PdfAction From(PdfObject value, PdfDocument document)
    {
        if (document.Resolve(value) is not PdfDictionary action)
        {
            return null;
        }

        return PdfValues.Name(action["S"], document) switch
        {
            "GoTo" => new PdfAction(PdfActionKind.GoTo) { Destination = document.ResolveDestination(action["D"]) },
            "GoToR" => new PdfAction(PdfActionKind.GoToRemote)
            {
                File = PdfFileSpecification.NameOf(action["F"], document),
                Destination = document.Resolve(action["D"]) is PdfArray remote
                    ? PdfDestination.From(remote, document, true)
                    : null,
                RemoteDestinationName = document.Resolve(action["D"]) switch
                {
                    PdfString text => text.ToText(),
                    PdfName name => name.Value,
                    _ => null,
                },
            },
            "URI" => new PdfAction(PdfActionKind.Uri)
            {
                Uri = WithBase(document.Resolve(action["URI"]) is PdfString address
                    ? System.Text.Encoding.Latin1.GetString(address.Bytes)
                    : null, document),
            },
            "Launch" => new PdfAction(PdfActionKind.Launch) { File = PdfFileSpecification.NameOf(action["F"], document) },
            "Named" => new PdfAction(PdfActionKind.Named) { Name = PdfValues.Name(action["N"], document) },
            "JavaScript" => new PdfAction(PdfActionKind.JavaScript),
            _ => new PdfAction(PdfActionKind.Other),
        };
    }

    private static string WithBase(string address, PdfDocument document)
    {
        if (address == null || HasScheme(address))
        {
            return address;
        }

        var catalogUri = document.Resolve(document.Catalog?["URI"]) as PdfDictionary;
        var root = (document.Resolve(catalogUri?["Base"]) as PdfString)?.ToText();
        return root != null && HasScheme(root) && System.Uri.TryCreate(root, System.UriKind.Absolute, out var baseUri) &&
               System.Uri.TryCreate(baseUri, address, out var combined)
            ? combined.ToString()
            : address;
    }

    private static bool HasScheme(string address)
    {
        var colon = address.IndexOf(':');
        if (colon < 2 || !char.IsAsciiLetter(address[0]))
        {
            return false;
        }

        for (var i = 1; i < colon; i++)
        {
            if (!char.IsAsciiLetterOrDigit(address[i]) && address[i] is not ('+' or '-' or '.'))
            {
                return false;
            }
        }

        return true;
    }
}
