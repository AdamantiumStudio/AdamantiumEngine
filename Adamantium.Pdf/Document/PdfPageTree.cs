using System.Collections.Generic;
using Adamantium.Pdf.Objects;

namespace Adamantium.Pdf.Document;

/// <summary>The pages in order, walked out of the page tree on an explicit stack. A node met twice - a loop, or a page
/// referenced from two places - is taken the first time only.</summary>
internal sealed class PdfPageTree
{
    public PdfPageTree(PdfDocument document)
    {
        var pages = new List<PdfPage>();
        Pages = pages;
        if (document.Resolve(document.Catalog?[PdfKeys.Pages]) is not PdfDictionary root)
        {
            return;
        }

        var visited = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<(PdfDictionary Node, PdfInheritedAttributes Inherited)>();
        pending.Push((root, default));
        visited.Add(root);
        while (pending.Count > 0)
        {
            var (node, above) = pending.Pop();
            var inherited = above.Below(node);
            var kids = document.Resolve(node[PdfKeys.Kids]) as PdfArray;
            var type = (document.Resolve(node[PdfKeys.Type]) as PdfName)?.Value;
            if (type == PdfObjectTypes.Page || kids == null)
            {
                pages.Add(new PdfPage(document, pages.Count, node, inherited));
                continue;
            }

            for (var i = kids.Count - 1; i >= 0; i--)
            {
                if (document.Resolve(kids[i]) is PdfDictionary kid && visited.Add(kid))
                {
                    pending.Push((kid, inherited));
                }
            }
        }
    }

    public IReadOnlyList<PdfPage> Pages { get; }
}
