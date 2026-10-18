using System.Collections.Generic;
using Adamantium.Fonts;
using Adamantium.Fonts.Text;
using Adamantium.Mathematics;

namespace Adamantium.Graphics.Fonts.Layout;

internal sealed record LaidOutText
{
    private bool[] _graphemes;

    public string Text { get; init; }

    public AttributedText Attributed { get; init; }

    // The attributed text's runs as the text was laid out by them: a style changed since does not reach what is drawn.
    public IReadOnlyList<AttributeRun> AttributeRuns { get; init; }

    public IReadOnlyList<ParagraphRun> ParagraphRuns { get; init; }

    public double FontSize { get; init; }

    public IFont Font { get; init; }

    public TextLayoutOptions Options { get; init; }

    public TextRenderingParameters RenderingParameters { get; init; }

    public List<GlyphWordData> Glyphs { get; init; } = [];

    public double[] LineTops { get; init; }

    public double[] LineHeights { get; init; }

    public double[] LineBaselines { get; init; }

    // The middle of each vertical line's own text below its top, when an annotation's room beside it moved it off the
    // line's middle; null otherwise.
    public double[] LineAxes { get; init; }

    public double VerticalShift { get; init; }

    public double LineSpacing { get; init; }

    public double ColumnsRight { get; init; }

    public int[] LineFrames { get; init; }

    public RectangleF[] FrameRects { get; init; }

    public TextParagraphs Paragraphs { get; init; }

    public int OversetIndex { get; init; }

    // The characters a drop cap took and the lines it stands across; 0 without one.
    public int DropCapEnd { get; init; }

    public int DropCapLines { get; init; }

    public Size Size { get; init; }

    public Size? RealTextDimensions { get; init; }

    public Glyph DotGlyph { get; init; }

    public bool HasPendingFonts { get; init; }

    public bool NoRoom { get; init; }

    public bool Vertical => Options?.Vertical == true;

    public bool[] Graphemes
    {
        get => _graphemes ??= TextBoundaries.Graphemes(Text ?? string.Empty);
        init => _graphemes = value;
    }

    public bool IsRightToLeftParagraph(int index) =>
        TextParagraphs.IsRightToLeftParagraph(Paragraphs, Text, Options?.Direction ?? TextDirection.Auto, index);

    public bool IsRightToLeftAt(int index) => TextParagraphs.IsRightToLeftAt(Paragraphs, index);
}
