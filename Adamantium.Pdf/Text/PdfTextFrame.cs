using System.Collections.Generic;
using Adamantium.Pdf.Content;
using Adamantium.Pdf.Objects;
using Adamantium.Pdf.Resources;

namespace Adamantium.Pdf.Text;

/// <summary>A content stream being read for text: its operations, its resources, how many saved states lie below
/// it, and the form it is - null for the page's own content.</summary>
internal sealed record PdfTextFrame(IEnumerator<PdfContentOperation> Operations, PdfResources Resources, int Floor,
    PdfStream Form);
