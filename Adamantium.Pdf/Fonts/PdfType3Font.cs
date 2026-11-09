using Adamantium.Pdf.Document;
using Adamantium.Pdf.Objects;
using Adamantium.Pdf.Resources;

namespace Adamantium.Pdf.Fonts;

/// <summary>A Type 3 font (ISO 32000-2, 9.6.4): each glyph is a content stream, drawn in glyph space that
/// <see cref="FontMatrix"/> maps to text space.</summary>
public sealed class PdfType3Font : PdfSimpleFont
{
    private static readonly double[] DefaultMatrix = [0.001, 0, 0, 0.001, 0, 0];

    private readonly PdfDictionary _charProcs;
    private readonly PdfDocument _document;

    internal PdfType3Font(PdfDictionary dictionary, PdfDocument document) : base(dictionary, document)
    {
        _document = document;
        var matrix = PdfValues.Numbers(dictionary[PdfFontKeys.FontMatrix], document);
        FontMatrix = matrix is { Length: 6 } ? matrix : DefaultMatrix;
        _charProcs = document.Resolve(dictionary[PdfFontKeys.CharProcs]) as PdfDictionary;
        var resources = document.Resolve(dictionary[PdfFontKeys.Resources]) as PdfDictionary;
        Resources = resources != null ? new PdfResources(resources, document) : null;
    }

    /// <summary>The matrix from glyph space to text space.</summary>
    public double[] FontMatrix { get; }

    /// <summary>The resources the glyphs' streams use; null when they use the page's.</summary>
    public PdfResources Resources { get; }

    /// <summary>A width scaled by the font matrix, as glyph space gives it.</summary>
    public override double WidthOf(PdfCharacter character) => base.WidthOf(character) * 1000 * FontMatrix[0];

    /// <summary>The content stream drawing the character's glyph, or null.</summary>
    public PdfStream CharProcOf(PdfCharacter character)
    {
        var name = GlyphNameOf(character.Code);
        return name == null || _charProcs == null ? null : _document.Resolve(_charProcs[name]) as PdfStream;
    }
}
