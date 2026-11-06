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

    /// <summary>In frames, the room below each line for the bottom border of the frame it closes; null for none.</summary>
    public double[] LineClosingRooms { get; init; }

    /// <summary>Where each line in frames may run across, from its stretch's left edge to its right: lines that share a
    /// row stand side by side in it.</summary>
    public (double Left, double Right)[] LineSpans { get; init; }

    public RectangleF[] FrameRects { get; init; }

    // A line taller than the room it was given that reaches into an exclusion, and its height: laid again to fit.
    internal (int Line, double Height)? LineOverAnExclusion { get; init; }

    public TextParagraphs Paragraphs { get; init; }

    public int OversetIndex { get; init; }

    // The characters a drop cap took and the lines beside it - fewer than it spans where a paragraph after it went below
    // it; 0 without one.
    public int DropCapEnd { get; init; }

    public int DropCapLines { get; init; }

    /// <summary>The bottom of a drop cap's last line, down the layout.</summary>
    public double DropCapBottom { get; init; }

    // The frame a drop cap - with the line below its paragraphs' frame - did not fit in: its paragraph is tried in the
    // frames after, as Word moves it, and kept where the cap is set and fits.
    internal int? DropCapFrameOver { get; init; }

    public Size<double> Size { get; init; }

    /// <summary>Where the text ends down the layout, not rounded: the last line's bottom, a drop cap's, and the room of
    /// the last paragraph's line below.</summary>
    public double TextBottom { get; init; }

    /// <summary>The lines of the paragraphs' borders; null for none.</summary>
    public List<TextAdornment> BorderLines { get; init; }

    /// <summary>The paragraphs' shading, as backgrounds under everything else; null for none.</summary>
    public List<TextAdornment> Shadings { get; init; }

    public Size<double>? RealTextDimensions { get; init; }

    public Glyph DotGlyph { get; init; }

    public bool HasPendingFonts { get; init; }

    public bool NoRoom { get; init; }

    // A word first on its line was wider than the line's room, as wrapping measures it (Overflows) and as trimming does
    // (InkOverflows, without the room justification may take back): it stood past the room.
    public bool Overflows { get; init; }

    public bool InkOverflows { get; init; }

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
