using Adamantium.Pdf.Document;
using Adamantium.Pdf.Fonts;

namespace Adamantium.Pdf.Text;

/// <summary>The part of the graphics state text reading keeps (ISO 32000-2, 8.4 and 9.3): the transformation and the
/// text state parameters, saved and restored by <c>q</c> and <c>Q</c>.</summary>
internal struct PdfTextState
{
    public PdfMatrix Ctm;
    public double CharacterSpacing;
    public double WordSpacing;
    public double Scale;
    public double Leading;
    public double Rise;
    public double Size;
    public PdfFont Font;
}
