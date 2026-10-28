using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Adamantium.Core.Fonts.Shaping;
using Adamantium.Fonts;
using Adamantium.Fonts.Shaping;
using Adamantium.Fonts.Text;
using Adamantium.Mathematics;
using static Adamantium.Graphics.Fonts.Layout.TextGeometry;

namespace Adamantium.Graphics.Fonts.Layout;

internal sealed class LineComposer
{
    private static readonly Dictionary<char, (double Left, double Right)> MarginHangs = new()
    {
        ['.'] = (0, 0.7), [','] = (0, 0.7), [':'] = (0, 0.5), [';'] = (0, 0.5), ['!'] = (0, 0.2), ['?'] = (0, 0.2),
        ['…'] = (0, 0.3), ['-'] = (0.7, 0.7), ['‐'] = (0.7, 0.7), ['‑'] = (0.7, 0.7),
        ['–'] = (0.5, 0.5), ['—'] = (0.3, 0.3), ['"'] = (0.5, 0.5), ['\''] = (0.5, 0.5),
        ['“'] = (0.7, 0.3), ['”'] = (0.3, 0.7), ['‘'] = (0.7, 0.3), ['’'] = (0.3, 0.7),
        ['„'] = (0.5, 0.5), ['‚'] = (0.5, 0.5), ['«'] = (0.5, 0.5), ['»'] = (0.5, 0.5),
        ['‹'] = (0.5, 0.5), ['›'] = (0.5, 0.5), ['('] = (0.1, 0), [')'] = (0, 0.1), ['['] = (0.1, 0),
        [']'] = (0, 0.1),
    };

    private readonly record struct Ruby(RubyAnnotation Annotation, double FontSize,
        List<(IFont Font, ShapedGlyph Glyph, int Cluster)> Glyphs, double Width, double Ascent, double Descent,
        int Items, bool Pending, bool Spread);

    private readonly IFont _font;
    private readonly Glyph _spaceGlyph;
    private readonly Glyph _dotGlyph;
    private readonly GlyphRunShaper _shaper;

    public LineComposer(IFont font, GlyphRunShaper shaper)
    {
        _font = font;
        _shaper = shaper;
        _spaceGlyph = font.GetGlyphByCharacter(' ');
        _dotGlyph = font.GetGlyphByCharacter('.');
    }

    public Glyph DotGlyph => _dotGlyph;

    public LaidOutText Lay(string text, AttributedText attributed, double fontSize,
        TextRenderingParameters renderingParameters, TextLayoutOptions options)
    {
        // A text trimmed and wrapped in a fixed height takes only lines that fit whole. A line raised by larger
        // text in it is known only once laid, so a text whose last line came out taller than the room is laid again up
        // to the lines that fit - until its last line fits, or only one is left.
        var wrapWidth = BalancedWidth(text, attributed, fontSize, renderingParameters, options);
        var laid = Lay(text, attributed, fontSize, renderingParameters, options, 0, null, null, wrapWidth);
        var limit = TrimmedLinesThatFit(laid, renderingParameters, options);
        while (limit > 0)
        {
            laid = Lay(text, attributed, fontSize, renderingParameters, options, limit, null, null, wrapWidth);
            var fit = TrimmedLinesThatFit(laid, renderingParameters, options);
            limit = fit < limit ? fit : 0;
        }

        // In frames a line is given the room of its paragraph's line height: raised by larger text or a taller object,
        // it can come out into an exclusion, or past the bottom of its frame. The text is laid again with that line's
        // room found for its height, or with it in the next frame on. Each pass settles the first such line, the lines
        // before it staying as they were.
        Dictionary<int, int> pushed = null;
        Dictionary<int, double> tall = null;
        var pushes = 0;
        for (var pass = 0; options.Frames is { Length: > 0 } && pass < MostFramePasses; pass++)
        {
            if (laid.LineOverAnExclusion is var (tallLine, height)
                && (tall == null || !tall.TryGetValue(tallLine, out var known) || height > known + 0.5))
            {
                (tall ??= [])[tallLine] = height;
            }
            else if (pushes++ <= options.Frames.Length && LineOverItsFrame(laid) is var (line, frame))
            {
                (pushed ??= [])[line] = frame + 1;
                // The lines from it on take other text in the next frame: their heights are found again.
                foreach (var stale in tall?.Keys.Where(index => index >= line).ToList() ?? [])
                {
                    tall.Remove(stale);
                }
            }
            else
            {
                break;
            }

            laid = Lay(text, attributed, fontSize, renderingParameters, options, 0, pushed, tall, wrapWidth);
        }

        return laid;
    }

    private const int MostFramePasses = 64;

    // Text longer than this many lines is not balanced: each width tried lays all of it out again.
    private const int MostBalancedLines = 10;

    // The narrowest whole width the text wraps in without taking more lines than MaxLines, or than wrapping at the full
    // width takes; 0 when the lines are not balanced. The lines are still aligned in the full width. A width counts only
    // when every word fits its line whole - none past the room, none broken by hyphenation - and every line fits the
    // area's height, so trimming has nothing to cut.
    private int BalancedWidth(string text, AttributedText attributed, double fontSize,
        TextRenderingParameters renderingParameters, TextLayoutOptions options)
    {
        if (!options.BalanceLines || renderingParameters.TextWrapping != TextWrapping.WrapByWords || options.Vertical
            || options.Frames is { Length: > 0 } || options.DropCap != null || string.IsNullOrEmpty(text))
        {
            return 0;
        }

        var area = renderingParameters.TextArea;
        // Tried untrimmed and unbounded below, so what does not fit shows as a word past its room or a line too many.
        var tried = new TextRenderingParameters
        {
            TextArea = new Rectangle<int>(area.X, area.Y, area.Width, Int32.MaxValue),
            TextWrapping = renderingParameters.TextWrapping,
            HorizontalTextAlignment = renderingParameters.HorizontalTextAlignment,
            VerticalTextAlignment = VerticalTextAlignment.Top,
            JustifyLastLine = renderingParameters.JustifyLastLine,
            TextTrimming = TextTrimming.None,
            Color = renderingParameters.Color,
        };
        var trims = renderingParameters.TextTrimming != TextTrimming.None;
        var breaks = TextBoundaries.LineBreaks(text);
        var full = Lay(text, attributed, fontSize, tried, options, 0, null, null, 0);
        var lines = LinesOf(full);
        var target = options.MaxLines > 0 ? options.MaxLines : lines;
        if (target < 2 || lines > MostBalancedLines || !Fits(full))
        {
            return 0;
        }

        // Unbounded, the room is the ink's plus an em: a last glyph's advance may reach past its ink.
        var high = area.Width < Int32.MaxValue
            ? area.Width
            : (int)Math.Ceiling(full.Size.Width) + (int)Math.Ceiling(fontSize);
        if (area.Width == Int32.MaxValue && !Fits(Tried(high)))
        {
            return 0;
        }

        var low = 0;
        while (high - low > 1)
        {
            var middle = low + (high - low) / 2;
            if (Fits(Tried(middle)))
            {
                high = middle;
            }
            else
            {
                low = middle;
            }
        }

        return high < area.Width ? high : 0;

        // Laid no further than a line past the most, so a width far too narrow costs what a right one does.
        LaidOutText Tried(int width) => Lay(text, attributed, fontSize, tried, options, target + 1, null, null, width);

        bool Fits(LaidOutText laid)
        {
            var count = LinesOf(laid);
            if (laid.NoRoom || laid.HasPendingFonts || laid.Overflows || trims && laid.InkOverflows
                || laid.OversetIndex < text.Length
                || count == 0 || count > target
                || area.Height != Int32.MaxValue
                && laid.LineTops[count - 1] - laid.VerticalShift + laid.LineHeights[count - 1] >= area.Height + 1.0)
            {
                return false;
            }

            var starts = new int[count];
            Array.Fill(starts, Int32.MaxValue);
            foreach (var glyph in laid.Glyphs)
            {
                if (glyph.PositionInString >= 0 && !glyph.Annotates && glyph.LineIndex < count)
                {
                    starts[glyph.LineIndex] = Math.Min(starts[glyph.LineIndex], glyph.PositionInString);
                }
            }

            // A line starting inside a word, or after a soft hyphen, was hyphenated there.
            for (var line = 1; line < count; line++)
            {
                if (starts[line] < breaks.Length
                    && (breaks[starts[line]] == LineBreakKind.None || text[starts[line] - 1] == '­'))
                {
                    return false;
                }
            }

            return true;
        }

        static int LinesOf(LaidOutText laid) => laid.LineTops?.Length ?? 0;
    }

    // The first line that ends past the bottom of its frame and is not the first in it: the next frame may hold it.
    private static (int Line, int Frame)? LineOverItsFrame(LaidOutText laid)
    {
        var tops = laid.LineTops;
        var frames = laid.FrameRects;
        var lineFrames = laid.LineFrames;
        if (tops == null || frames == null || lineFrames == null || laid.HasPendingFonts)
        {
            return null;
        }

        for (var i = 1; i < tops.Length && i < lineFrames.Length; i++)
        {
            var frame = frames[lineFrames[i]];
            if (tops[i] + laid.LineHeights[i] > frame.Bottom + 0.5 && lineFrames[i - 1] == lineFrames[i])
            {
                return (i, lineFrames[i]);
            }
        }

        return null;
    }

    private static int TrimmedLinesThatFit(LaidOutText laid, TextRenderingParameters renderingParameters,
        TextLayoutOptions options)
    {
        var height = renderingParameters.TextArea.Height;
        var tops = laid.LineTops;
        if (renderingParameters.TextTrimming == TextTrimming.None
            || renderingParameters.TextWrapping == TextWrapping.NoWrap || height == Int32.MaxValue
            || options.Vertical || options.Frames is { Length: > 0 } || laid.HasPendingFonts || tops == null
            || tops.Length < 2)
        {
            return 0;
        }

        var fit = 0;
        while (fit < tops.Length && tops[fit] - laid.VerticalShift + laid.LineHeights[fit] < height + 1.0)
        {
            fit++;
        }

        return fit < tops.Length ? Math.Max(fit, 1) : 0;
    }

    private LaidOutText Lay(string text, AttributedText attributed, double fontSize,
        TextRenderingParameters renderingParameters, TextLayoutOptions options, int lineLimit,
        IReadOnlyDictionary<int, int> pushed, IReadOnlyDictionary<int, double> tall, int wrapWidth)
    {
        var laidOutParameters = renderingParameters;

        var _b0 = System.GC.GetAllocatedBytesForCurrentThread();


        var _b1 = System.GC.GetAllocatedBytesForCurrentThread();

        var scale = fontSize / _font.UnitsPerEm;
        var vertical = options.Vertical;
        var grid = vertical ? null : options.BaselineGrid;
        var gridOffset = grid != null ? (grid.Offset % grid.Step + grid.Step) % grid.Step : 0;
        IReadOnlyList<RectangleF> frames = options.Frames;
        IReadOnlyList<RectangleF> exclusions = options.Exclusions;
        double columnsRight = 0;
        if (vertical)
        {
            var screenArea = renderingParameters.TextArea;
            columnsRight = screenArea.Width < Int32.MaxValue ? screenArea.Width
                : options.Frames is { Length: > 0 } ? options.Frames.Max(frame => frame.Right) : 0;
            frames = options.Frames?.Select(AlongColumns).ToArray();
            exclusions = options.Exclusions?.Select(AlongColumns).ToArray();
            renderingParameters = new TextRenderingParameters
            {
                TextArea = new Rectangle<int>(0, 0, screenArea.Height, screenArea.Width),
                TextWrapping = renderingParameters.TextWrapping,
                TextTrimming = renderingParameters.TextTrimming,
                HorizontalTextAlignment = renderingParameters.HorizontalTextAlignment,
                VerticalTextAlignment = renderingParameters.VerticalTextAlignment,
                JustifyLastLine = renderingParameters.JustifyLastLine,
                Color = renderingParameters.Color,
            };
        }

        var oversetIndex = text.Length;
        var overflows = false;
        var inkOverflows = false;
        var tabStops = options.TabStops.OrderBy(stop => stop.Position).ToArray();
        var paragraphs = TextParagraphs.Resolve(text, options.Direction);
        // What the attributed text says, read once: a style changed meanwhile does not reach this layout.
        var (attributeRuns, resolvedParagraphs) = attributed?.Resolved() ?? (null, null);
        var paragraphRuns = resolvedParagraphs is { Count: > 0 } ? resolvedParagraphs : null;
        var dropRightToLeft = TextParagraphs.IsRightToLeftParagraph(paragraphs, text, options.Direction, 0);
        var dropEnd = options.DropCap != null && renderingParameters.TextWrapping == TextWrapping.WrapByWords
                                              && !(vertical && dropRightToLeft)
                                              && (options.MaxLines <= 0 || options.MaxLines >= options.DropCap.Lines)
                                              && (lineLimit <= 0 || lineLimit >= options.DropCap.Lines)
            ? options.DropCap.Length(text)
            : 0;
        var graphemes = options.Fallback == null ? null : TextBoundaries.Graphemes(text);
        var items = _shaper.Shape(text, attributed, attributeRuns, fontSize, dropEnd, vertical, options, paragraphs,
            graphemes, out var hasPendingFonts);
        var tabStop = _font.GetAdvanceWidth(_spaceGlyph.Index) * scale * Math.Max(options.TabSize, 1);

        var _b2 = System.GC.GetAllocatedBytesForCurrentThread();

        var naturalHeight = LineAdvance(_font, fontSize);
        var lineHeight = options.LineHeight > 0 && double.IsFinite(options.LineHeight) ? options.LineHeight : naturalHeight;
        var baseLine = BaselineInLine(_font, scale) + (lineHeight - naturalHeight) / 2;
        var lineSpacing = double.IsFinite(options.LineSpacing) ? Math.Max(options.LineSpacing, 1 - lineHeight) : 0;


        var textArea = renderingParameters.TextArea;
        if (wrapWidth > 0 && wrapWidth < textArea.Width)
        {
            textArea.Width = wrapWidth;
        }

        double width = 0;
        double height = textArea.Y;
        double lineRawTop = textArea.Y;
        double cursorPosition = 0;
        double wordStartPosition = 0;
        var glyphsData = new List<GlyphWordData>(items.Count);
        var leaders = new Dictionary<GlyphWordData, string>();
        HashSet<GlyphWordData> placingTabs = null;
        int wordIndex = 0;
        int lineIndex = 0;
        var lineParagraph = ParagraphOf(0);
        var paragraphOpenLine = 0;
        var lineRightToLeft = paragraphRuns != null && IsRightToLeftParagraph(0);
        var plainRightToLeft = text.Length > 0 && IsRightToLeftParagraph(0);
        double pendingGap = 0;
        var lineStates = new List<(ParagraphAttributes Paragraph, bool Opens, bool RightToLeft, double Gap)>();
        HashSet<int> linesAfterBreaks = null;
        double verticalShift = 0;
        Size<double>? realTextDimensions = null;
        int[] laidOutLineFrames = null;
        (double Left, double Right)[] laidOutLineSpans = null;
        var lineBreaks = renderingParameters.TextWrapping == TextWrapping.WrapByWords
            ? TextBoundaries.LineBreaks(text)
            : null;
        var slots = lineBreaks != null && frames is { Count: > 0 }
            ? new List<(int Frame, double Left, double Right, double Top, double Unsnapped)>()
            : null;
        // Per slot: where the next one is looked for - the frame, the top of the next row and the rows tried in it; for
        // a line beside it in its row, the edge its stretch ends at, which way it reads and the row's tops - and the
        // room above and the height it was found for, so a line that turns out otherwise finds its slot again.
        var slotCursors =
            new List<(int Frame, double Top, int Rows, double Beside, bool RightToLeft, double RowTop,
                double RowUnsnapped)>();
        var slotBases = new List<(double Gap, double Height, bool Gridded, bool RightToLeft)>();
        List<(double Left, double Right)> rowSpans = [];
        List<(double Left, double Right)> spansLeft = [];
        List<bool> lineReads = [];
        var frameRects = slots != null ? frames.ToArray() : null;
        if (slots != null && !HasSlot(0))
        {
            return new LaidOutText
            {
                Text = text,
                Attributed = attributed,
                FontSize = fontSize,
                Font = _font,
                Options = options,
                RenderingParameters = laidOutParameters,
                Paragraphs = paragraphs,
                Graphemes = graphemes,
                LineSpacing = lineSpacing,
                ColumnsRight = columnsRight,
                FrameRects = frameRects,
                OversetIndex = 0,
                RealTextDimensions = Size<double>.Zero,
                HasPendingFonts = hasPendingFonts,
                NoRoom = true,
                DotGlyph = _dotGlyph,
            };
        }

        // A number keeps the ideograph or kana after it (1月) only while the two fit a line they may stand on: a longer
        // one may break there, as it may anyway.
        double[] clusterAdvances = null;
        var trimRoom = double.MaxValue;
        for (var i = 1; lineBreaks != null && i < text.Length; i++)
        {
            if (lineBreaks[i] == LineBreakKind.Allowed
                && (text[i - 1] == SoftHyphen
                    || (char.IsDigit(text[i - 1]) && IsHanOrKana(CodepointAt(text, i)) && NumberFitsWith(i))))
            {
                lineBreaks[i] = LineBreakKind.None;
            }
        }

        int[] rubyAt = null;
        bool[] boundaries = null;
        // Where a fullwidth CJK punctuation is set half-width, its blank half trimmed; the comma or full stop each line
        // hangs past its end.
        var trimmedPunctuation = new HashSet<int>();
        Dictionary<int, GlyphWordData> hungEnds = null;
        int[] rubyOther = null;
        List<Ruby> rubies = null;
        bool[] rubyShown = null;
        var turned = false;

        HashSet<int> composedStarts = null;
        HashSet<int> composedCuts = null;
        var composedLines = 0;
        double dropIndent = 0;
        var dropLines = 0;
        var dropItems = 0;
        IFont dropFont = null;
        double dropScale = 0;
        ShapedGlyph[] dropGlyphs = null;
        if (dropEnd > 0 && items.Count > 0)
        {
            SetUpDropCap();
        }

        if (attributed is { Rubies.Count: > 0 })
        {
            rubies = SetUpRubies();
        }

        var punctuated = CjkPunctuation.Appears(text);
        if (punctuated && options.SpacingTrim != TextSpacingTrim.SpaceAll)
        {
            CollapsePunctuation();
        }

        cursorPosition = LineIndent(0);
        if (lineBreaks != null && options.LineBreaking == LineBreaking.Paragraph
                               && (slots != null || textArea.Width < Int32.MaxValue))
        {
            Compose();
            lineParagraph = ParagraphOf(0);
            paragraphOpenLine = 0;
            lineRightToLeft = paragraphRuns != null && IsRightToLeftParagraph(0);
            plainRightToLeft = text.Length > 0 && IsRightToLeftParagraph(0);
            pendingGap = 0;
        }

        NoteLine(0, 0);
        lineRawTop = height;
        height = OnGrid(height, RowBaseline(0), 0);
        var wordStart = dropItems;
        while (wordStart <= items.Count)
        {
            var wordEnd = WordEnd(wordStart);
            // A word hyphenated is set again in its halves: its end moves by the items that took.
            var itemCount = items.Count;
            var processed = ProcessWord(wordStart, wordEnd);
            wordEnd += items.Count - itemCount;
            if (!processed)
            {
                break;
            }

            if (wordEnd < items.Count && !IsBlank(items[wordEnd].Symbol))
            {
                wordIndex++;
                wordStart = wordEnd;
                continue;
            }

            if (wordEnd < items.Count)
            {
                // Sub-pixel like the glyphs, so the space doesn't inflate the line bounds. A tab draws nothing and
                // reaches the next tab stop.
                var space = items[wordEnd];
                var isTab = space.Symbol == '\t';
                var stop = isTab ? NextTabStop(cursorPosition, lineIndex) : default;
                var advance = isTab ? TabAdvance(stop, wordEnd) : space.Advance;
                var rect = new RectangleF((float)cursorPosition,
                    (float)(height + baseLine),
                    (float)advance,
                    0f);
                var blank = new GlyphWordData(isTab ? _spaceGlyph : space.Glyph, space.Symbol, rect, space.Cluster, lineIndex)
                {
                    PenX = cursorPosition,
                    Advance = advance,
                    Attributes = space.Attributes,
                    Font = space.Font,
                    FontSize = space.FontSize,
                };
                glyphsData.Add(blank);
                if (isTab && stop.Leader != null)
                {
                    leaders[blank] = stop.Leader;
                }

                if (isTab && stop.Alignment != TabAlignment.Left)
                {
                    (placingTabs ??= []).Add(blank);
                }

                cursorPosition += advance;
            }

            wordIndex++;
            wordStart = wordEnd + 1;
        }

        if (punctuated)
        {
            FinishLineEnds();
        }

        paragraphs?.ReorderLines(glyphsData);

        // Not published yet: the render thread reads the laid-out text, and the alignment below still moves every glyph.
        // The height is a font metric (the bottom of the last line, its descent included), not the ink, so same-size
        // strings measure alike and a turned label keeps its descenders. A line is as tall as its largest text.
        var lineCount = Math.Max(lineIndex, glyphsData.Count > 0 ? glyphsData.Max(x => x.LineIndex) : 0) + 1;
        var lineTops = new double[lineCount];
        var lineHeights = new double[lineCount];
        var lineBaselines = new double[lineCount];
        double[] lineCoreAbove = null;
        double[] lineCoreBelow = null;
        if (rubies != null)
        {
            // Only a base that came out whole is annotated, and only its line grows for it.
            var counts = new int[rubies.Count];
            foreach (var glyph in glyphsData)
            {
                if (RubyIndex(glyph.PositionInString) is var r and >= 0)
                {
                    counts[r]++;
                }

                if (OtherRuby(glyph.PositionInString) is var o and >= 0)
                {
                    counts[o]++;
                }
            }

            rubyShown = new bool[rubies.Count];
            for (var r = 0; r < rubies.Count; r++)
            {
                rubyShown[r] = rubies[r].Annotation != null && counts[r] == rubies[r].Items && !rubies[r].Pending;
            }
        }

        PlaceLines();
        var trimsLines = !vertical && slots == null && options.LineBounds != TextLineBounds.Full;
        if (trimsLines)
        {
            TrimLineBounds();
        }

        // The middle of each vertical line's own text: an annotation's room beside it does not move it.
        double[] lineAxes = null;
        if (lineCoreAbove != null)
        {
            lineAxes = new double[lineCount];
            for (var i = 0; i < lineCount; i++)
            {
                lineAxes[i] = lineBaselines[i] + (lineCoreBelow[i] - lineCoreAbove[i]) / 2;
            }
        }
        height = lineTops[lineCount - 1] + lineHeights[lineCount - 1];
        if (dropGlyphs != null)
        {
            height = Math.Max(height, DropBottom());
        }

        var _b3 = System.GC.GetAllocatedBytesForCurrentThread();


        double inkRight = 0;
        if (glyphsData.Count > 0)
        {
            inkRight = CalculateRealTextDimensions(glyphsData);
        }

        // Each line keeps its own paragraph's room on the right.
        var reservedRight = inkRight;
        if ((paragraphRuns != null || (dropRightToLeft && dropGlyphs != null)) && glyphsData.Count > 0)
        {
            var lineRights = new double[lineCount];
            Array.Fill(lineRights, double.NegativeInfinity);
            foreach (var glyph in glyphsData)
            {
                lineRights[glyph.LineIndex] = Math.Max(lineRights[glyph.LineIndex], RightOf(glyph));
            }

            for (var i = 0; i < lineCount; i++)
            {
                if (!double.IsNegativeInfinity(lineRights[i]))
                {
                    reservedRight = Math.Max(reservedRight, lineRights[i] + RightReserve(i));
                }
            }
        }

        var maxX = Math.Max(reservedRight, dropIndent);
        var finalRect = new Size<double>(Math.Ceiling(maxX), Math.Ceiling(height));
        if (renderingParameters.TextArea.Width != Int32.MaxValue)
        {
            finalRect.Width = renderingParameters.TextArea.Width;
        }

        if (renderingParameters.TextArea.Height != Int32.MaxValue)
        {
            finalRect.Height = renderingParameters.TextArea.Height;
        }

        if (slots != null)
        {
            finalRect = new Size<double>(Math.Ceiling(frames.Max(frame => frame.Right)), Math.Ceiling(frames.Max(frame => frame.Bottom)));
        }

        if (glyphsData.Count > 0)
        {
            ArrangeText();
            PlaceBlankRightToLeftLines();
        }

        for (var i = 0; i < lineCount; i++)
        {
            lineTops[i] += verticalShift;
        }

        (int Line, double Height)? lineOverAnExclusion = null;
        if (slots != null)
        {
            PlaceInFrames();
            lineOverAnExclusion = LineOverAnExclusion();
            if (renderingParameters.TextArea.Height == Int32.MaxValue)
            {
                var bottom = dropGlyphs != null ? DropBaseline() + lineHeight - baseLine : 0.0;
                for (var i = 0; i < lineCount; i++)
                {
                    bottom = Math.Max(bottom, lineTops[i] + lineHeights[i]);
                }

                finalRect.Height = Math.Ceiling(bottom);
            }
        }

        if (dropGlyphs != null)
        {
            glyphsData.InsertRange(0, DropCapGlyphs());
            if (!vertical)
            {
                paragraphs?.ResolveApart(0, dropEnd);
            }
            CalculateRealTextDimensions(glyphsData);
        }

        if (leaders.Count > 0)
        {
            glyphsData = WithLeaders(glyphsData, leaders,
                slots == null && textArea.Width < Int32.MaxValue ? textArea.Width : double.MaxValue);
        }

        if (rubies != null)
        {
            AddRubyGlyphs();
            if (glyphsData.Count > 0)
            {
                CalculateRealTextDimensions(glyphsData);
            }
        }

        if (vertical)
        {
            TurnIntoColumns();
        }

        var _b4 = System.GC.GetAllocatedBytesForCurrentThread();

        TextLayout.TranslateBytes += _b1 - _b0;
        TextLayout.FeatureBytes += _b2 - _b1;
        TextLayout.WordLoopBytes += _b3 - _b2;
        TextLayout.TailBytes += _b4 - _b3;
        TextLayout.ProcessCount++;

        return new LaidOutText
        {
            Text = text,
            Attributed = attributed,
            AttributeRuns = attributeRuns,
            ParagraphRuns = paragraphRuns,
            FontSize = fontSize,
            Font = _font,
            Options = options,
            RenderingParameters = laidOutParameters,
            Glyphs = glyphsData,
            LineTops = lineTops,
            LineHeights = lineHeights,
            LineBaselines = lineBaselines,
            LineAxes = lineAxes,
            VerticalShift = verticalShift,
            LineSpacing = lineSpacing,
            ColumnsRight = columnsRight,
            LineFrames = laidOutLineFrames,
            LineSpans = laidOutLineSpans,
            FrameRects = frameRects,
            LineOverAnExclusion = lineOverAnExclusion,
            Paragraphs = paragraphs,
            Graphemes = graphemes,
            OversetIndex = oversetIndex,
            DropCapEnd = dropGlyphs != null ? dropEnd : 0,
            DropCapLines = dropGlyphs != null ? dropLines : 0,
            Size = finalRect,
            RealTextDimensions = realTextDimensions,
            HasPendingFonts = hasPendingFonts,
            Overflows = overflows,
            InkOverflows = inkOverflows,
            DotGlyph = _dotGlyph,
        };

        double Raise(TextAttributes attributes, IFont font, double size, double baseSize) =>
            TextGeometry.Raise(_font, options.WritingMode, attributes, font, size, baseSize);

        double LineRaise(TextAttributes attributes, char symbol, double lineBaseline, double lineBox, IFont font,
            double size) =>
            TextGeometry.LineRaise(options.WritingMode, attributes, symbol, lineBaseline, lineBox, font, size);

        bool IsRightToLeftParagraph(int index) =>
            TextParagraphs.IsRightToLeftParagraph(paragraphs, text, options.Direction, index);

        double CalculateRealTextDimensions(List<GlyphWordData> glyphs)
        {
            var minX = double.PositiveInfinity;
            var maxInk = double.NaN;
            var minY = float.PositiveInfinity;
            var maxY = float.NaN;
            foreach (var glyph in glyphs)
            {
                minX = Math.Min(minX, LeftOf(glyph));
                minY = Math.Min(minY, glyph.Rect.Top);
                var right = RightOf(glyph);
                if (double.IsNaN(maxInk) || right > maxInk)
                {
                    maxInk = right;
                }

                var bottom = glyph.Rect.Bottom;
                if (float.IsNaN(maxY) || bottom > maxY)
                {
                    maxY = bottom;
                }
            }

            realTextDimensions = new Size<double>(maxInk - minX, maxY - minY);
            return maxInk;
        }

        // Stops are measured from the edge the line starts at: the right one of a line running right to left - its frame's,
        // past an exclusion - whose pen starts at its left reserve all the same.
        TabStop NextTabStop(double x, int line)
        {
            var origin = 0.0;
            if (LineState(line).RightToLeft)
            {
                var excluded = slots != null ? frames[SlotOf(line).Frame].Width - SlotOf(line).Right : 0;
                origin = LineIndent(line) - RightReserve(line) - excluded;
            }

            foreach (var stop in tabStops)
            {
                if (stop.Position + origin > x + 1e-6)
                {
                    return origin == 0
                        ? stop
                        : new TabStop(stop.Position + origin, stop.Alignment, stop.Leader, stop.AlignOn);
                }
            }

            return new TabStop((Math.Floor((x - origin) / tabStop) + 1) * tabStop + origin);
        }

        double TabAdvance(TabStop stop, int tab) => TabAdvanceAt(stop, tab, cursorPosition);

        double TabAdvanceAt(TabStop stop, int tab, double x)
        {
            var advance = stop.Position - x;
            if (stop.Alignment == TabAlignment.Left)
            {
                return advance;
            }

            double width = 0;
            double beforeSeparator = -1;
            for (var k = tab + 1; k < items.Count && items[k].Symbol is not ('\t' or '\n'); k++)
            {
                if (stop.Alignment == TabAlignment.Decimal && beforeSeparator < 0 && items[k].Symbol == stop.AlignOn)
                {
                    beforeSeparator = width;
                }

                width += items[k].Advance;
            }

            var shift = stop.Alignment switch
            {
                TabAlignment.Center => width / 2,
                TabAlignment.Decimal when beforeSeparator >= 0 => beforeSeparator,
                _ => width,
            };
            return Math.Max(advance - shift, 0);
        }

        List<GlyphWordData> WithLeaders(List<GlyphWordData> glyphs, Dictionary<GlyphWordData, string> tabLeaders, double limit)
        {
            var result = new List<GlyphWordData>(glyphs.Count + tabLeaders.Count * 16);
            foreach (var glyph in glyphs)
            {
                result.Add(glyph);
                if (!tabLeaders.TryGetValue(glyph, out var leader))
                {
                    continue;
                }

                var glyphScale = glyph.FontSize / glyph.Font.UnitsPerEm;
                var leaderGlyphs = leader.Select(symbol => glyph.Font.GetGlyphByCharacter(symbol)).ToArray();
                var advances = leaderGlyphs.Select(g => glyph.Font.GetAdvanceWidth(g.Index) * glyphScale).ToArray();
                var unit = advances.Sum();
                var to = Math.Min(glyph.PenX + glyph.Advance, limit);
                var shift = Raise(glyph.Attributes, glyph.Font, glyph.FontSize, fontSize)
                            + LineRaise(glyph.Attributes, glyph.Symbol, lineBaselines[glyph.LineIndex],
                                lineHeights[glyph.LineIndex], glyph.Font, glyph.FontSize);
                if (unit <= 0)
                {
                    continue;
                }

                for (var x = Math.Ceiling(glyph.PenX / unit) * unit; x + unit <= to + 1e-6; x += unit)
                {
                    var pen = x;
                    for (var g = 0; g < leaderGlyphs.Length; g++)
                    {
                        var rect = CalculateGlyphPosition(glyph.Font, leaderGlyphs[g], pen,
                            glyph.Rect.Y - verticalShift - shift, glyphScale);
                        rect.Y += (float)verticalShift;
                        result.Add(new GlyphWordData(leaderGlyphs[g], leader[g], rect, -1, glyph.LineIndex)
                        {
                            PenX = pen,
                            Advance = advances[g],
                            OffsetY = shift,
                            Attributes = glyph.Attributes,
                            Font = glyph.Font,
                            FontSize = glyph.FontSize,
                        });
                        pen += advances[g];
                    }
                }
            }

            return result;
        }

        bool NumberFitsWith(int letter)
        {
            var room = frames is { Count: > 0 } ? frames.Max(frame => (double)frame.Width) : textArea.Width;
            if (room >= Int32.MaxValue)
            {
                return true;
            }

            if (clusterAdvances == null)
            {
                clusterAdvances = new double[text.Length];
                foreach (var item in items)
                {
                    if (item.Cluster >= 0 && item.Cluster < text.Length)
                    {
                        clusterAdvances[item.Cluster] += item.Advance;
                    }
                }
            }

            var start = letter - 1;
            while (start > 0 && char.IsDigit(text[start - 1]))
            {
                start--;
            }

            // Between the paragraph's margins; past its indent only where the number opens it, on its first line.
            var paragraph = AttributedText.ParagraphIn(paragraphRuns, start, text.Length);
            var attributes = paragraph?.Attributes;
            room -= Math.Max(0, Finite(attributes?.MarginLeft)) + Math.Max(0, Finite(attributes?.MarginRight))
                    + (start == (paragraph?.Start ?? 0) ? Math.Max(0, Finite(attributes?.TextIndent)) : 0);

            var end = Math.Min(letter + (char.IsHighSurrogate(text[letter]) ? 2 : 1), text.Length);
            var width = 0.0;
            for (var c = start; c < end; c++)
            {
                width += clusterAdvances[c];
            }

            return width <= room;
        }

        int WordEnd(int start)
        {
            var end = start;
            // A break opportunity is where a cluster starts: the second glyph of a letter shaped in two is not one.
            while (end < items.Count && (!IsBlank(items[end].Symbol) || InsideABase(end))
                   && (end == start || lineBreaks == null || items[end].Cluster == items[end - 1].Cluster
                       || lineBreaks[items[end].Cluster] == LineBreakKind.None))
            {
                end++;
            }

            return end;
        }

        // On the baseline of the cap's last line; one that a paragraph's room, its own line height or a frame moved
        // away does not move it.
        double DropBaseline()
        {
            var moved = false;
            for (var line = 1; line < Math.Min(dropLines, lineTops.Length); line++)
            {
                moved |= LineGap(line) > 0 || OwnLineHeight(line) != OwnLineHeight(0)
                         || (laidOutLineFrames != null && laidOutLineFrames[line] != laidOutLineFrames[0]);
            }

            if (moved)
            {
                return lineTops[0] + lineBaselines[0] + (dropLines - 1) * DropPitch();
            }

            return dropLines - 1 < lineTops.Length
                ? lineTops[dropLines - 1] + lineBaselines[dropLines - 1]
                : lineTops[lineTops.Length - 1] + lineBaselines[lineTops.Length - 1]
                                                + (dropLines - lineTops.Length) * DropPitch();
        }

        // The bottom of the cap's last line, or its baseline where the last line ends on it.
        double DropBottom() => DropBaseline()
                               + (trimsLines && options.LineBounds is TextLineBounds.TrimToBaseline or TextLineBounds.Tight
                                   ? 0
                                   : lineHeight - baseLine);

        // A drop cap spans lines of the first paragraph, as far apart as its lines are.
        double DropPitch()
        {
            var pitch = OwnLineHeight(0) + SpacingAfter(0);
            return grid is { FirstLineOnly: false } ? Math.Ceiling(pitch / grid.Step - 1e-6) * grid.Step : pitch;
        }

        List<GlyphWordData> DropCapGlyphs()
        {
            var attributes = items[0].Attributes;
            var shift = attributes?.BaselineShift ?? 0;
            var baseline = DropBaseline() - shift;
            List<GlyphWordData> glyphs = [];
            double pen = (slots != null ? frames[SlotOf(0).Frame].Left + SlotOf(0).Left : 0) + EdgeReserve(0, false);
            if (dropRightToLeft)
            {
                pen = (slots != null ? frames[SlotOf(0).Frame].Left + SlotOf(0).Right : finalRect.Width)
                      - EdgeReserve(0, true) - dropGlyphs.Sum(shaped => shaped.XAdvance) * dropScale;
            }

            // Down vertical lines its middle stands halfway from the first line's middle to the last's.
            var across = (DropBaseline() - lineTops[0] - lineBaselines[0]) / 2;
            foreach (var shaped in dropGlyphs)
            {
                var glyph = dropFont.GetGlyphByIndex(shaped.GlyphIndex);
                if (vertical)
                {
                    var down = -shaped.YAdvance * dropScale;
                    glyphs.Add(new GlyphWordData(glyph, text[shaped.Cluster], default, shaped.Cluster, 0)
                    {
                        PenX = pen,
                        Advance = down,
                        OffsetX = -shaped.YOffset * dropScale,
                        OffsetY = shaped.XOffset * dropScale - across + shift,
                        Attributes = attributes,
                        Font = dropFont,
                        FontSize = dropScale * dropFont.UnitsPerEm,
                        Upright = true,
                    });
                    pen += down;
                    continue;
                }

                var advance = shaped.XAdvance * dropScale;
                var rect = CalculateGlyphPosition(dropFont, glyph, pen + shaped.XOffset * dropScale,
                    baseline - shaped.YOffset * dropScale, dropScale);
                glyphs.Add(new GlyphWordData(glyph, text[shaped.Cluster], rect, shaped.Cluster, 0)
                {
                    PenX = pen,
                    Advance = advance,
                    OffsetX = shaped.XOffset * dropScale,
                    OffsetY = shaped.YOffset * dropScale + shift,
                    Attributes = attributes,
                    Font = dropFont,
                    FontSize = dropScale * dropFont.UnitsPerEm,
                });
                pen += advance;
            }

            return paragraphs != null ? glyphs.OrderBy(glyph => glyph.PositionInString).ToList() : glyphs;
        }

        // Each ruby's annotation shaped in its base's font (or the fallback's); a base narrower than it spread to its
        // width - the room around each of its clusters, or only around the whole of a joined script - and kept on one
        // line. A base ends at a hard break in it, and starts after the letters a drop cap took.
        List<Ruby> SetUpRubies()
        {
            rubyAt = new int[text.Length];
            Array.Fill(rubyAt, -1);
            rubyOther = new int[text.Length];
            Array.Fill(rubyOther, -1);
            var runs = attributed.Rubies;
            var capped = dropItems > 0 ? items[dropItems - 1].Cluster + 1 : 0;
            for (var r = 0; r < runs.Count; r++)
            {
                var start = Math.Max(runs[r].Start, capped);
                var end = runs[r].End;
                for (var c = start; c < end; c++)
                {
                    if (text[c] is '\n' or '\r' or LineSeparator or ParagraphSeparator)
                    {
                        end = c;
                        break;
                    }
                }

                // A base takes one annotation over it and one under it, as a double-sided ruby.
                for (var c = start; c < end; c++)
                {
                    if (rubyAt[c] < 0)
                    {
                        rubyAt[c] = r;
                    }
                    else
                    {
                        rubyOther[c] = r;
                    }
                }

                for (var c = start + 1; lineBreaks != null && c < end; c++)
                {
                    lineBreaks[c] = LineBreakKind.None;
                }
            }

            KernAcrossBaseEdges();
            var firsts = new int[runs.Count];
            var lasts = new int[runs.Count];
            var counts = new int[runs.Count];
            var units = new int[runs.Count];
            var widths = new double[runs.Count];
            var joined = new bool[runs.Count];
            Array.Fill(firsts, -1);
            for (var k = 0; k < items.Count; k++)
            {
                Note(k, RubyIndex(items[k].Cluster));
                Note(k, OtherRuby(items[k].Cluster));
            }

            void Note(int k, int r)
            {
                if (r < 0)
                {
                    return;
                }

                firsts[r] = firsts[r] < 0 ? k : firsts[r];
                lasts[r] = k;
                counts[r]++;
                widths[r] += items[k].Advance;
                units[r] += UnitEnds(k, r) ? 1 : 0;
                joined[r] |= CursiveScripts.Contains(CodepointAt(text, items[k].Cluster));
            }

            List<Ruby> set = new(runs.Count);
            for (var r = 0; r < runs.Count; r++)
            {
                if (firsts[r] < 0)
                {
                    set.Add(default);
                    continue;
                }

                var item = items[firsts[r]];
                var annotation = runs[r].Annotation;
                var size = annotation.FontSize is > 0 and var own && double.IsFinite(own) ? own : item.FontSize / 2;
                var glyphs = _shaper.ShapeAlone(annotation.Text, item.Font, item.Attributes?.Language ?? options.Language,
                    vertical, options, out var pending);
                hasPendingFonts |= pending;
                double width = 0;
                double ascent = 0;
                double descent = 0;
                foreach (var (font, glyph, _) in glyphs)
                {
                    var glyphScale = size / font.UnitsPerEm;
                    width += (vertical ? -glyph.YAdvance : glyph.XAdvance) * glyphScale;
                    ascent = Math.Max(ascent, font.LineAscent * glyphScale);
                    descent = Math.Max(descent, font.LineDescent * glyphScale);
                }

                // The base as wide as it already is: the annotation on its other side may have spread it.
                double spreadWidth = 0;
                for (var k = firsts[r]; k <= lasts[r]; k++)
                {
                    spreadWidth += InRuby(items[k].Cluster, r) ? items[k].Advance : 0;
                }

                set.Add(new Ruby(annotation, size, glyphs, width, ascent, descent, counts[r], pending,
                    width > widths[r]));
                var extra = width - spreadWidth;
                if (extra <= 0)
                {
                    continue;
                }

                // The room follows the glyph a cluster ends on as it is drawn: right to left, the first of it, which the
                // visual order puts last.
                var rightToLeft = TextParagraphs.IsRightToLeftAt(paragraphs, runs[r].Start);
                var share = joined[r] ? extra : extra / Math.Max(1, units[r]);
                for (var k = firsts[r]; k <= lasts[r]; k++)
                {
                    if (!InRuby(items[k].Cluster, r))
                    {
                        continue;
                    }

                    var spread = items[k];
                    var takes = joined[r]
                        ? k == (rightToLeft ? firsts[r] : lasts[r])
                        : rightToLeft ? UnitStarts(k, r) : UnitEnds(k, r);
                    items[k] = new ShapedItem(spread.Glyph, spread.Symbol, spread.Cluster,
                        spread.Advance + (takes ? share : 0), spread.OffsetX + share / 2,
                        spread.OffsetY, spread.Attributes, spread.Font, spread.FontSize, spread.Upright,
                        spread.HorizontalScale, spread.Tail);
                }
            }

            return set;
        }

        // Shaping is cut at a base's edge, so a ligature never joins a base to the text beside it - and the pair the font
        // kerns there lost its kerning. The pair is shaped whole and the first letter takes the kerning the font gives it,
        // between letters that do not join (shaped whole, joining letters would take other forms).
        void KernAcrossBaseEdges()
        {
            for (var k = 0; !vertical && k + 1 < items.Count; k++)
            {
                var left = items[k];
                var right = items[k + 1];
                if ((RubyIndex(left.Cluster), OtherRuby(left.Cluster)) == (RubyIndex(right.Cluster), OtherRuby(right.Cluster))
                    || !ReferenceEquals(left.Font, right.Font) || !left.FontSize.Equals(right.FontSize)
                    || left.Cluster >= right.Cluster || IsBlank(left.Symbol) || IsBlank(right.Symbol)
                    || left.Symbol == '\n' || ObjectSizeOf(left.Symbol, left.Attributes) != null
                    || ObjectSizeOf(right.Symbol, right.Attributes) != null
                    || (left.Attributes?.Kerning ?? options.Kerning) != TextKerning.Metrics
                    || CursiveScripts.Contains(CodepointAt(text, left.Cluster))
                    || CursiveScripts.Contains(CodepointAt(text, right.Cluster)))
                {
                    continue;
                }

                var rightEnd = k + 2 < items.Count && items[k + 2].Cluster > right.Cluster
                    ? items[k + 2].Cluster
                    : Math.Min(text.Length, right.Cluster + (char.IsHighSurrogate(text[right.Cluster]) ? 2 : 1));
                var language = left.Attributes?.Language ?? options.Language;
                var alone = _shaper.ShapeAlone(text[left.Cluster..right.Cluster], left.Font, language, false, options,
                    out var pendingAlone);
                var pair = _shaper.ShapeAlone(text[left.Cluster..rightEnd], left.Font, language, false, options,
                    out var pendingPair);
                // The pair comes in the order it is drawn: right to left, its first letter is the second glyph.
                var first = pair.Count == 2 && pair[0].Cluster != pair[1].Cluster ? pair[pair[0].Cluster == 0 ? 0 : 1] : default;
                if (pendingAlone || pendingPair || alone.Count != 1 || first.Font == null || first.Cluster != 0
                    || !ReferenceEquals(first.Font, left.Font))
                {
                    continue;
                }

                var scale = left.FontSize / left.Font.UnitsPerEm;
                var kerning = (first.Glyph.XAdvance - alone[0].Glyph.XAdvance) * scale;
                var moved = (first.Glyph.XOffset - alone[0].Glyph.XOffset) * scale;
                if (kerning != 0 || moved != 0)
                {
                    items[k] = new ShapedItem(left.Glyph, left.Symbol, left.Cluster, left.Advance + kerning,
                        left.OffsetX + moved, left.OffsetY, left.Attributes, left.Font, left.FontSize, left.Upright,
                        left.HorizontalScale, left.Tail);
                }
            }
        }

        // The last and the first item of a cluster of a base.
        bool UnitEnds(int k, int r) => k + 1 >= items.Count || !InRuby(items[k + 1].Cluster, r)
                                                             || items[k + 1].Cluster != items[k].Cluster;

        bool UnitStarts(int k, int r) => k == 0 || !InRuby(items[k - 1].Cluster, r)
                                                || items[k - 1].Cluster != items[k].Cluster;

        int RubyIndex(int index) => rubyAt != null && index >= 0 && index < rubyAt.Length ? rubyAt[index] : -1;

        // The annotation on the other side of a double-sided ruby's base, or -1.
        int OtherRuby(int index) => rubyOther != null && index >= 0 && index < rubyOther.Length ? rubyOther[index] : -1;

        bool InRuby(int index, int r) => r >= 0 && (RubyIndex(index) == r || OtherRuby(index) == r);

        bool SameBase(int a, int b) => InRuby(b, RubyIndex(a)) || InRuby(b, OtherRuby(a));

        bool InABase(GlyphWordData glyph) => RubyIndex(glyph.PositionInString) >= 0 || OtherRuby(glyph.PositionInString) >= 0;

        // The edges a glyph takes on its line: its ink, or for a base spread to its annotation the whole room it takes;
        // once the lines are turned into columns, the ink.
        double RightOf(GlyphWordData glyph) => Spread(glyph)
            ? Math.Max(InkRight(glyph), glyph.PenX + glyph.Advance)
            : InkRight(glyph);

        double LeftOf(GlyphWordData glyph) => Spread(glyph)
            ? Math.Min(InkLeft(glyph), glyph.PenX)
            : InkLeft(glyph);

        bool Spread(GlyphWordData glyph) => !turned
                                            && (RubyIndex(glyph.PositionInString) is var r and >= 0 && rubies[r].Spread
                                                || OtherRuby(glyph.PositionInString) is var o and >= 0 && rubies[o].Spread);

        // An item of a ruby base with more of the base after it: no line breaks there.
        bool InsideABase(int k) => rubyAt != null && k + 1 < items.Count && items[k].Symbol != '\t'
                                   && SameBase(items[k].Cluster, items[k + 1].Cluster);

        // How far a base glyph reaches from its line's baseline on the annotation's side.
        double BaseReach(GlyphWordData glyph, bool over)
        {
            var glyphScale = glyph.FontSize / glyph.Font.UnitsPerEm;
            if (ObjectSizeOf(glyph.Symbol, glyph.Attributes) is { } objectSize)
            {
                return over ? objectSize.Height + glyph.OffsetY : -glyph.OffsetY;
            }

            return over
                ? glyph.Font.LineAscent * glyphScale + glyph.OffsetY
                : glyph.Font.LineDescent * glyphScale - glyph.OffsetY;
        }

        // Each annotation centered over (or under) its whole base where the base was set, beyond the base's farthest
        // reach; drawn right after its base, which an effect over the base then reaches too. A base the trimming cut
        // takes none.
        void AddRubyGlyphs()
        {
            var bases = new (int Line, double Left, double Right, double Reach, double Em, int Count, int Last,
                GlyphWordData First)[rubies.Count];
            Array.Fill(bases, (-1, double.PositiveInfinity, double.NegativeInfinity, 0.0, 0.0, 0, -1, (GlyphWordData)null));
            for (var k = 0; k < glyphsData.Count; k++)
            {
                Reach(k, RubyIndex(glyphsData[k].PositionInString));
                Reach(k, OtherRuby(glyphsData[k].PositionInString));
            }

            void Reach(int k, int r)
            {
                if (r < 0 || rubies[r].Annotation is not { } annotation)
                {
                    return;
                }

                var glyph = glyphsData[k];
                var b = bases[r];
                bases[r] = (b.Line < 0 ? glyph.LineIndex : b.Line, Math.Min(b.Left, glyph.PenX),
                    Math.Max(b.Right, glyph.PenX + glyph.Advance),
                    Math.Max(b.Reach, BaseReach(glyph, annotation.Position == RubyPosition.Over)),
                    Math.Max(b.Em, glyph.FontSize), b.Count + 1, k, b.First ?? glyph);
            }

            // A base's annotations follow its last glyph; a double-sided base has two.
            var after = new List<int>[glyphsData.Count];
            for (var r = 0; r < rubies.Count; r++)
            {
                if (bases[r].Line >= 0 && rubyShown[r])
                {
                    (after[bases[r].Last] ??= []).Add(r);
                }
            }

            var placed = new List<GlyphWordData>(glyphsData.Count + 16);
            for (var k = 0; k < glyphsData.Count; k++)
            {
                placed.Add(glyphsData[k]);
                foreach (var r in after[k] ?? [])
                {
                    AddAnnotation(rubies[r], bases[r]);
                }
            }

            glyphsData = placed;

            void AddAnnotation(Ruby ruby, (int Line, double Left, double Right, double Reach, double Em, int Count,
                int Last, GlyphWordData First) at)
            {
                var over = ruby.Annotation.Position == RubyPosition.Over;
                var lineBase = lineTops[at.Line] + lineBaselines[at.Line];
                // Upright down a vertical line beside its base, an em of each apart from the line's middle.
                var across = (at.Em + ruby.FontSize) / 2 * (over ? 1 : -1);
                var baseline = over ? lineBase - at.Reach - ruby.Descent : lineBase + at.Reach + ruby.Ascent;
                var pen = (at.Left + at.Right) / 2 - ruby.Width / 2;
                foreach (var (font, shaped, cluster) in ruby.Glyphs)
                {
                    var scale = ruby.FontSize / font.UnitsPerEm;
                    var glyph = font.GetGlyphByIndex(shaped.GlyphIndex);
                    var advance = (vertical ? -shaped.YAdvance : shaped.XAdvance) * scale;
                    placed.Add(new GlyphWordData(glyph, ruby.Annotation.Text[cluster],
                        vertical ? default : CalculateGlyphPosition(font, glyph, pen + shaped.XOffset * scale,
                            baseline - shaped.YOffset * scale, scale), -1, at.Line)
                    {
                        PenX = pen,
                        Advance = advance,
                        OffsetX = vertical ? -shaped.YOffset * scale : shaped.XOffset * scale,
                        OffsetY = vertical ? shaped.XOffset * scale + across : lineBase - baseline + shaped.YOffset * scale,
                        Upright = vertical,
                        Attributes = at.First.Attributes,
                        Font = font,
                        FontSize = ruby.FontSize,
                        Annotates = true,
                    });
                    pen += advance;
                }
            }
        }

        void SetUpDropCap()
        {
            var dropCap = options.DropCap;
            if (slots != null)
            {
                if (!HasSlot(dropCap.Lines - 1))
                {
                    return;
                }

                for (var line = 1; line < dropCap.Lines; line++)
                {
                    if (slots[line].Frame != slots[0].Frame
                        || Math.Abs(slots[line].Top - slots[line - 1].Top - DropPitch()) > 0.5)
                    {
                        return;
                    }
                }
            }

            var first = items[0];
            var kerning = first.Attributes?.Kerning ?? options.Kerning;
            var features = first.Attributes?.Features;
            var shapingOptions = new ShapingOptions(null, first.Attributes?.Language ?? options.Language,
                kerning == TextKerning.Metrics ? features : [.. features ?? [], FontFeature.Kerning.Off], vertical: vertical);
            var font = dropCap.Font ?? first.Font;
            var shaped = Shape(font);
            if (!ReferenceEquals(font, first.Font) && shaped.Any(glyph => glyph.GlyphIndex == 0))
            {
                font = first.Font;
                shaped = Shape(font);
            }

            ShapedGlyph[] Shape(IFont capFont)
            {
                var glyphs = vertical || paragraphs == null
                    ? TextShaper.Shape(capFont, text, 0, dropEnd, shapingOptions)
                    : InVisualOrder(capFont);
                if (kerning == TextKerning.Optical && !vertical && OpticalKerning.Measures(capFont)
                    && OpticalKerning.Applies(text, 0, dropEnd))
                {
                    OpticalKerning.Apply(capFont, glyphs, 0, glyphs.Length);
                }

                return glyphs;
            }

            ShapedGlyph[] InVisualOrder(IFont capFont)
            {
                var levels = paragraphs.LevelsApart(0, dropEnd);
                List<(int Start, int End)> runs = [];
                List<byte> runLevels = [];
                for (var start = 0; start < dropEnd;)
                {
                    var end = start + 1;
                    while (end < dropEnd && levels[end] == levels[start])
                    {
                        end++;
                    }

                    runs.Add((start, end));
                    runLevels.Add(levels[start]);
                    start = end;
                }

                List<ShapedGlyph> glyphs = [];
                foreach (var run in BidiParagraph.VisualOrder(runLevels))
                {
                    var (start, end) = runs[run];
                    var direction = (runLevels[run] & 1) == 1 ? TextDirection.RightToLeft : TextDirection.LeftToRight;
                    var options = new ShapingOptions(shapingOptions.Script, shapingOptions.Language, shapingOptions.Features,
                        direction);
                    var shaped = TextShaper.Shape(capFont, text, start, end, options);
                    if (direction == TextDirection.RightToLeft)
                    {
                        shaped = GlyphRunShaper.ToLogicalOrder(shaped);
                        Array.Reverse(shaped);
                    }

                    foreach (var glyph in shaped)
                    {
                        glyphs.Add(new ShapedGlyph(glyph.GlyphIndex, start + glyph.Cluster, glyph.XAdvance, glyph.YAdvance,
                            glyph.XOffset, glyph.YOffset));
                    }
                }

                return glyphs.ToArray();
            }

            var (capScale, indent) = vertical ? DownTheLines() : AcrossTheLines();
            if (capScale <= 0)
            {
                return;
            }

            if ((slots != null || textArea.Width < Int32.MaxValue) && indent > LineRight(0) - LineIndent(0) - 2 * first.FontSize)
            {
                return;
            }

            dropFont = font;
            dropScale = capScale;
            dropGlyphs = shaped;
            dropIndent = indent;
            dropLines = dropCap.Lines;
            while (dropItems < items.Count && items[dropItems].Cluster < dropEnd)
            {
                dropItems++;
            }

            // Upright at the head of vertical lines: its em as wide as the lines it spans, from the right of the first's
            // em to the left of the last's.
            (double, double) DownTheLines()
            {
                double down = 0;
                double wide = 0;
                foreach (var glyph in shaped)
                {
                    down -= glyph.YAdvance;
                    wide = Math.Max(wide, font.GetAdvanceWidth(glyph.GlyphIndex));
                }

                if (down <= 0 || wide <= 0)
                {
                    return (0, 0);
                }

                var scale = (first.FontSize + (dropCap.Lines - 1) * DropPitch()) / wide;
                return (scale, down * scale);
            }

            static (int Reference, int Own) ReferenceLetter(string text, int end)
            {
                for (var i = 0; i < end; i++)
                {
                    if (!Rune.TryGetRuneAt(text, i, out var rune) || !Rune.IsLetter(rune))
                    {
                        continue;
                    }

                    var own = BaseLetter(rune).Value;
                    var upper = own is >= 0x10D0 and <= 0x10FF ? own : Rune.ToUpperInvariant(new Rune(own)).Value;
                    return (upper != own ? upper : own switch
                    {
                        >= 0x0590 and <= 0x05FF or >= 0xFB1D and <= 0xFB4F => 0x05D4,
                        >= 0x0600 and <= 0x06FF or >= 0x0750 and <= 0x077F or >= 0x08A0 and <= 0x08FF
                            or >= 0xFB50 and <= 0xFDFF or >= 0xFE70 and <= 0xFEFF => 0x0627,
                        >= 0x1100 and <= 0x11FF or >= 0x3130 and <= 0x318F or >= 0xA960 and <= 0xA97F
                            or >= 0xAC00 and <= 0xD7FF or >= 0xFFA0 and <= 0xFFDC => 0xD55C,
                        >= 0x2E80 and <= 0x9FFF or >= 0xF900 and <= 0xFAFF or >= 0xFF65 and <= 0xFF9F
                            or >= 0x20000 and <= 0x3FFFF => 0x56FD,
                        _ => own,
                    }, own);
                }

                return ('H', 'H');
            }

            static Rune BaseLetter(Rune letter)
            {
                var decomposed = letter.ToString().Normalize(NormalizationForm.FormD);
                var first = Rune.GetRuneAt(decomposed, 0);
                for (var i = first.Utf16SequenceLength; i < decomposed.Length;)
                {
                    var next = Rune.GetRuneAt(decomposed, i);
                    if (Rune.GetUnicodeCategory(next) is not (UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark
                        or UnicodeCategory.EnclosingMark))
                    {
                        return letter;
                    }

                    i += next.Utf16SequenceLength;
                }

                return first;
            }

            static double LetterHeight(IFont letterFont, int letter) =>
                InkTop(letterFont, letter) ?? (letterFont.CapsHeight > 0 ? letterFont.CapsHeight : letterFont.Ascender * 0.7);

            static double? InkTop(IFont letterFont, int letter) =>
                letterFont.TryGetGlyphIndex(letter, out var index) && index != 0
                && letterFont.GetGlyphByIndex(index).BoundingRectangle is var bounds && bounds.Y + bounds.Height > 0
                    ? bounds.Y + bounds.Height
                    : null;

            (double, double) AcrossTheLines()
            {
                double top = 0;
                double pen = 0;
                var inkLeft = double.MaxValue;
                double inkRight = 0;
                foreach (var glyph in shaped)
                {
                    var bounds = font.GetGlyphByIndex(glyph.GlyphIndex).BoundingRectangle;
                    top = Math.Max(top, bounds.Y + bounds.Height);
                    if (bounds.Width > 0)
                    {
                        var left = pen + glyph.XOffset + font.GetLeftSideBearing(glyph.GlyphIndex);
                        inkLeft = Math.Min(inkLeft, left);
                        inkRight = Math.Max(inkRight, left + bounds.Width);
                    }

                    pen += glyph.XAdvance;
                }

                if (dropRightToLeft)
                {
                    inkRight = inkLeft == double.MaxValue ? 0 : pen - inkLeft;
                }

                if (top <= 0)
                {
                    return (0, 0);
                }

                var (reference, own) = ReferenceLetter(text, dropEnd);
                var letter = InkTop(first.Font, reference) != null && InkTop(font, reference) != null ? reference : own;
                var cap = LetterHeight(first.Font, letter) * first.FontSize / first.Font.UnitsPerEm;
                var scale = (cap + (dropCap.Lines - 1) * DropPitch()) / LetterHeight(font, letter);
                return (scale, Math.Max(pen * scale, inkRight * scale + 0.1 * first.FontSize));
            }
        }

        void NewLine()
        {
            SettleLine(lineIndex);
            (lineRawTop, height) = NextTop(pendingGap);
            lineIndex++;
            NoteLine(lineIndex, pendingGap);
            pendingGap = 0;
            cursorPosition = LineIndent(lineIndex);
        }

        ParagraphAttributes ParagraphOf(int index) => AttributedText.ParagraphIn(paragraphRuns, index, text.Length)?.Attributes;

        void NoteLine(int line, double gap)
        {
            while (lineStates.Count <= line)
            {
                lineStates.Add((lineParagraph, lineStates.Count == paragraphOpenLine, lineRightToLeft,
                    lineStates.Count == line ? gap : 0));
                lineReads.Add(plainRightToLeft);
            }

            ForgetSlotsFrom(line, gap);
        }

        // A line in a frame turns out as tall as its text: the slots after it are found again below it.
        void SettleLine(int line)
        {
            if (slots == null || line >= slots.Count || options.LineStacking != LineStackingStrategy.MaxHeight)
            {
                return;
            }

            var (lineBaseline, lineBox) = MeasuredLine(line);
            var cursor = slotCursors[line];
            var bottom = OnGrid(slots[line].Unsnapped, lineBaseline, line) + lineBox + SpacingAfter(line);
            if (bottom <= cursor.Top + 1e-6)
            {
                return;
            }

            slotCursors[line] = cursor with { Top = bottom };
            if (slots.Count > line + 1)
            {
                slots.RemoveRange(line + 1, slots.Count - line - 1);
                slotCursors.RemoveRange(line + 1, slotCursors.Count - line - 1);
                slotBases.RemoveRange(line + 1, slotBases.Count - line - 1);
            }
        }

        // The baseline and height of a line as its glyphs stack it, as PlaceLines does.
        (double Baseline, double Height) MeasuredLine(int line)
        {
            var ownBaseline = RowBaseline(line);
            var ownHeight = OwnLineHeight(line);
            var setHeight = OwnSetHeight(line) is > 0 and var set && double.IsFinite(set)
                            || (options.LineHeight > 0 && double.IsFinite(options.LineHeight));
            var above = ownBaseline;
            var below = ownHeight - ownBaseline;
            for (var k = glyphsData.Count - 1; k >= 0 && glyphsData[k].LineIndex >= line; k--)
            {
                if (glyphsData[k].LineIndex == line)
                {
                    var extent = GlyphExtent(glyphsData[k], setHeight, ownBaseline, ownHeight);
                    above = Math.Max(above, extent.Above);
                    below = Math.Max(below, extent.Below);
                }
            }

            return (above, above + below);
        }

        // How far a glyph reaches above and below its line's baseline when lines grow to hold their text.
        (double Above, double Below) GlyphExtent(GlyphWordData glyph, bool setHeight, double ownBaseline,
            double ownHeight, bool withRuby = true)
        {
            var glyphScale = glyph.FontSize / glyph.Font.UnitsPerEm;
            var ownSize = setHeight && glyph.FontSize == fontSize;
            var above = ownSize ? ownBaseline : BaselineInLine(glyph.Font, glyphScale);
            var below = ownSize ? ownHeight - ownBaseline : LineAdvance(glyph.Font, glyph.FontSize) - above;
            if (ObjectSizeOf(glyph.Symbol, glyph.Attributes) is { } objectSize && vertical)
            {
                // Upright down a vertical line, centered across it on the middle of the text's own.
                var middle = (below - above) / 2;
                above = Math.Max(above, objectSize.Width / 2 - middle);
                below = Math.Max(below, objectSize.Width / 2 + middle);
            }
            else if (ObjectSizeOf(glyph.Symbol, glyph.Attributes) is { } standing)
            {
                above = Math.Max(above, standing.Height + glyph.OffsetY);
                below = Math.Max(below, -glyph.OffsetY);
            }

            // A ruby base's line grows to hold its annotations, as CSS's; one whose height is set does not.
            if (withRuby && !setHeight)
            {
                Grow(RubyIndex(glyph.PositionInString));
                Grow(OtherRuby(glyph.PositionInString));
            }

            return (above, below);

            void Grow(int r)
            {
                if (r < 0 || rubies[r].Annotation is not { } annotation || rubies[r].Pending
                    || (rubyShown != null && !rubyShown[r]))
                {
                    return;
                }

                var ruby = rubies[r];
                var over = annotation.Position == RubyPosition.Over;
                if (vertical)
                {
                    // An upright annotation is an em of its size across the line.
                    above += over ? ruby.FontSize : 0;
                    below += over ? 0 : ruby.FontSize;
                }
                else if (over)
                {
                    above = Math.Max(above, BaseReach(glyph, true) + ruby.Ascent + ruby.Descent);
                }
                else
                {
                    below = Math.Max(below, BaseReach(glyph, false) + ruby.Ascent + ruby.Descent);
                }
            }
        }

        // Slots found ahead for a line that turns out to keep other room above it or other heights are found again.
        void ForgetSlotsFrom(int line, double gap)
        {
            if (slots != null && line < slots.Count &&
                slotBases[line] != (gap, SlotHeight(line), GridsLine(line), ReadsRightToLeft(line)))
            {
                slots.RemoveRange(line, slots.Count - line);
                slotCursors.RemoveRange(line, slotCursors.Count - line);
                slotBases.RemoveRange(line, slotBases.Count - line);
            }
        }

        double OwnSetHeight(int line) => LineState(line).Paragraph?.LineHeight ?? double.NaN;

        // The height a line's slot is found for: its paragraph's, or what an earlier layout found the line takes.
        double SlotHeight(int line) => tall != null && tall.TryGetValue(line, out var measured)
            ? Math.Max(OwnLineHeight(line), measured)
            : OwnLineHeight(line);

        // A paragraph's own line height, or the layout's.
        double OwnLineHeight(int line)
        {
            var own = OwnSetHeight(line);
            return own > 0 && double.IsFinite(own) ? own : lineHeight;
        }

        // The spacing after a line, never so negative that the next line starts above it.
        double SpacingAfter(int line) =>
            double.IsFinite(options.LineSpacing) ? Math.Max(options.LineSpacing, 1 - OwnLineHeight(line)) : 0;

        // The top of the line after this one: below it by its height and the room given, on the grid.
        // The tops of the line after this one, before and on the grid: below this one as its text sets it, where
        // PlaceLines puts them.
        (double Raw, double Top) NextTop(double gap)
        {
            var (lineBaseline, box) = options.LineStacking == LineStackingStrategy.MaxHeight
                ? MeasuredLine(lineIndex)
                : (RowBaseline(lineIndex), OwnLineHeight(lineIndex));
            var raw = OnGrid(lineRawTop, lineBaseline, lineIndex) + box + SpacingAfter(lineIndex) + gap;
            return (raw, OnGrid(raw, RowBaseline(lineIndex + 1), lineIndex + 1));
        }

        // The top that puts the line's baseline on the first grid line at or below it.
        double OnGrid(double top, double lineBaseline, int line)
        {
            if (!GridsLine(line))
            {
                return top;
            }

            var steps = Math.Ceiling((top + lineBaseline - gridOffset) / grid.Step - 1e-6);
            return gridOffset + steps * grid.Step - lineBaseline;
        }

        bool GridsLine(int line) => grid != null && (!grid.FirstLineOnly || OpensParagraph(line));

        // The first line of the text or of a paragraph: one its paragraph opens, or one after a hard break.
        bool OpensParagraph(int line) =>
            line == 0 || LineState(line).Opens || linesAfterBreaks?.Contains(line) == true;

        // A hard break that is not a line separator starts the line after it as a first line.
        void NoteParagraphBreak(int cluster, int nextLine)
        {
            if (grid is { FirstLineOnly: true } && cluster >= 0 && cluster < text.Length && text[cluster] != LineSeparator)
            {
                (linesAfterBreaks ??= []).Add(nextLine);
            }
        }

        // A shift of the whole text that keeps its lines on the grid: whole steps of it, no further than asked.
        double GridSteps(double shift)
        {
            if (grid == null)
            {
                return shift;
            }

            var steps = shift >= 0 ? Math.Floor(shift / grid.Step + 1e-6) : Math.Ceiling(shift / grid.Step - 1e-6);
            return steps * grid.Step;
        }

        // Where a row's baseline lies in it, as the lines of its paragraph are set.
        double RowBaseline(int line) => OwnSetHeight(line) is > 0 and var set && double.IsFinite(set)
            ? BaselineInLine(_font, scale) + (set - naturalHeight) / 2
            : baseLine;

        (int Frame, double Left, double Right, double Top, double Unsnapped) SlotOf(int line)
        {
            HasSlot(line);
            return slots[Math.Min(line, slots.Count - 1)];
        }

        bool HasSlot(int line)
        {
            if (line >= lineStates.Count)
            {
                ForgetSlotsFrom(line, line == paragraphOpenLine ? pendingGap : 0);
            }

            while (slots.Count <= line)
            {
                if (!AddSlot())
                {
                    return false;
                }
            }

            return true;
        }

        // The next line's slot: in the row of the line before, in a stretch the exclusions leave past its own in the
        // order the line reads; else below it by the room its paragraph keeps (none at the top of a frame), as tall as
        // its own lines, in the first stretch wide enough.
        bool AddSlot()
        {
            var line = slots.Count;
            var gap = line < lineStates.Count ? lineStates[line].Gap : line == paragraphOpenLine ? pendingGap : 0;
            var rowHeight = SlotHeight(line);
            var rightToLeft = ReadsRightToLeft(line);
            var (f, top, rows, beside, besideRightToLeft, rowAt, rowUnsnapped) = line == 0
                ? (0, (double)frames[0].Top, 0, double.NaN, false, 0.0, 0.0)
                : slotCursors[line - 1];
            var from = -1;
            var pushedOn = pushed != null && pushed.TryGetValue(line, out from) && f < from;
            if (exclusions is { Count: > 0 } && !double.IsNaN(beside) && besideRightToLeft == rightToLeft && gap <= 0 &&
                !pushedOn && !(dropEnd > 0 && line < options.DropCap.Lines) &&
                Math.Abs(OnGrid(rowUnsnapped, RowBaseline(line), line) - rowAt) < 1e-6 &&
                rowAt + rowHeight <= frames[f].Bottom + 1e-6 &&
                SpanPast(frames[f], rowAt, rowHeight, beside, rightToLeft) is { } next)
            {
                slots.Add((f, next.Left - frames[f].Left, next.Right - frames[f].Left, rowAt, rowUnsnapped));
                slotCursors.Add((f, Math.Max(top, rowAt + rowHeight + SpacingAfter(line)), rows,
                    rightToLeft ? next.Left : next.Right, rightToLeft, rowAt, rowUnsnapped));
                slotBases.Add((gap, rowHeight, GridsLine(line), rightToLeft));
                return true;
            }

            if (rows > 0)
            {
                top += gap;
            }

            // A line an earlier layout found too tall for the rest of its frame starts in a later one.
            if (pushedOn)
            {
                f = from;
                rows = 0;
                top = f < frames.Count ? frames[f].Top : 0;
            }

            while (f < frames.Count)
            {
                var frame = frames[f];
                var rowTop = OnGrid(top, RowBaseline(line), line);
                if (((rows > 0 || (rowTop > top + 1e-6 && f + 1 < frames.Count)) && rowTop + rowHeight > frame.Bottom + 1e-6)
                    || rows >= 4 * (text.Length + 1))
                {
                    f++;
                    rows = 0;
                    top = f < frames.Count ? frames[f].Top : 0;
                    continue;
                }

                rows++;
                var unsnapped = top;
                top = rowTop + rowHeight + SpacingAfter(line);
                var edge = rightToLeft ? double.PositiveInfinity : double.NegativeInfinity;
                if (SpanPast(frame, rowTop, rowHeight, edge, rightToLeft) is { } first)
                {
                    slots.Add((f, first.Left - frame.Left, first.Right - frame.Left, rowTop, unsnapped));
                    slotCursors.Add((f, top, rows, rightToLeft ? first.Left : first.Right, rightToLeft, rowTop,
                        unsnapped));
                    slotBases.Add((gap, rowHeight, GridsLine(line), rightToLeft));
                    return true;
                }

                var cleared = ClearedFrom(frame, rowTop, rowHeight);
                if (cleared < top && OnGrid(cleared, RowBaseline(line), line) > rowTop + 1e-6)
                {
                    top = cleared;
                    if (rows > 1)
                    {
                        rows--;
                    }
                }
            }

            return false;
        }

        double ClearedFrom(RectangleF frame, double top, double rowHeight)
        {
            var cleared = double.PositiveInfinity;
            foreach (var exclusion in exclusions ?? [])
            {
                if (exclusion.Top < top + rowHeight && exclusion.Bottom > top && exclusion.Left < frame.Right &&
                    exclusion.Right > frame.Left)
                {
                    cleared = Math.Min(cleared, exclusion.Bottom);
                }
            }

            return cleared;
        }

        bool ReadsRightToLeft(int line) => paragraphRuns != null ? LineState(line).RightToLeft
            : line < lineReads.Count ? lineReads[line]
            : plainRightToLeft;

        (double Left, double Right)? SpanPast(RectangleF frame, double top, double rowHeight, double edge,
            bool rightToLeft)
        {
            var spans = Spans(frame, top, rowHeight);
            for (var i = 0; i < spans.Count; i++)
            {
                var span = spans[rightToLeft ? spans.Count - 1 - i : i];
                var past = rightToLeft ? span.Right <= edge + 1e-6 : span.Left >= edge - 1e-6;
                if (past && span.Right - span.Left >= 2 * fontSize)
                {
                    return span;
                }
            }

            return null;
        }

        List<(double Left, double Right)> Spans(RectangleF frame, double top, double rowHeight)
        {
            rowSpans.Clear();
            rowSpans.Add((frame.Left, frame.Right));
            foreach (var exclusion in exclusions ?? [])
            {
                if (exclusion.Top >= top + rowHeight || exclusion.Bottom <= top)
                {
                    continue;
                }

                spansLeft.Clear();
                foreach (var (left, right) in rowSpans)
                {
                    if (exclusion.Left > left)
                    {
                        spansLeft.Add((left, Math.Min(right, exclusion.Left)));
                    }

                    if (exclusion.Right < right)
                    {
                        spansLeft.Add((Math.Max(left, exclusion.Right), right));
                    }
                }

                (rowSpans, spansLeft) = (spansLeft, rowSpans);
            }

            return rowSpans;
        }

        (ParagraphAttributes Paragraph, bool Opens, bool RightToLeft) LineState(int line) => line < lineStates.Count
            ? (lineStates[line].Paragraph, lineStates[line].Opens, lineStates[line].RightToLeft)
            : (lineParagraph, line == paragraphOpenLine, lineRightToLeft);

        double LineGap(int line) => line < lineStates.Count ? lineStates[line].Gap : 0;

        HorizontalTextAlignment AlignmentOf(int line) =>
            LineState(line).Paragraph?.Alignment ?? renderingParameters.HorizontalTextAlignment;

        // A paragraph starting at next: the room between it and the one before, and its lines from openLine on. The
        // empty line after the text's last break starts one only after a paragraph separator.
        void BreakInto(int next, int openLine)
        {
            if (paragraphRuns == null && next < text.Length)
            {
                plainRightToLeft = IsRightToLeftParagraph(next);
            }

            var paragraph = ParagraphOf(next);
            var startsHere = AttributedText.ParagraphIn(paragraphRuns, next, text.Length)?.Start == next;
            if (paragraphRuns == null || next > text.Length
                || (!startsHere && paragraph == lineParagraph)
                || (!startsHere && next == text.Length && text[next - 1] != ParagraphSeparator))
            {
                return;
            }

            pendingGap = Math.Max(Math.Max(0, Finite(lineParagraph?.SpaceAfter ?? 0)),
                Finite(paragraph?.SpaceBefore ?? 0));
            lineParagraph = paragraph;
            paragraphOpenLine = openLine;
            lineRightToLeft = next < text.Length ? IsRightToLeftParagraph(next) : lineRightToLeft;
            ForgetSlotsFrom(openLine, pendingGap);
        }

        // The margins stand at their own sides; the first line's indent at the side its lines start from, unless a drop
        // cap takes its place. A line never starts past the block's edges.
        double EdgeReserve(int line, bool right)
        {
            var (paragraph, opens, rightToLeft) = LineState(line);
            if (paragraph == null)
            {
                return 0;
            }

            var indent = opens && rightToLeft == right && line >= dropLines ? Finite(paragraph.TextIndent) : 0;
            return Math.Max(0, Finite(right ? paragraph.MarginRight : paragraph.MarginLeft) + indent);
        }

        double RightReserve(int line) => EdgeReserve(line, true) + (line < dropLines && dropRightToLeft ? dropIndent : 0);

        double LineIndent(int line) => (slots != null ? SlotOf(line).Left : 0)
                                       + EdgeReserve(line, false) + (line < dropLines && !dropRightToLeft ? dropIndent : 0);

        double LineRight(int line) => (slots != null ? SlotOf(line).Right : textArea.Width) - RightReserve(line);

        bool LineHasContent() => cursorPosition > LineIndent(lineIndex) + 1e-9;

        // The next line's top stays inside the area: below this line by its height and the room a paragraph starting
        // there keeps. A trimmed text takes only a line that fits whole at its paragraph's line height, so its ellipsis is
        // not cut off - within the part of a pixel the area's whole height lost. A line grown by larger text in it is
        // not known yet: the text is laid again up to the lines that fit (lineLimit).
        bool HasRoomBelow()
        {
            if (options.MaxLines > 0 && lineIndex + 1 >= options.MaxLines || lineLimit > 0 && lineIndex + 1 >= lineLimit)
            {
                return false;
            }

            if (slots == null)
            {
                var top = NextTop(pendingGap).Top;
                return renderingParameters.TextTrimming == TextTrimming.None
                    ? top < textArea.Height
                    : top + OwnLineHeight(lineIndex + 1) < textArea.Height + 1.0;
            }

            SettleLine(lineIndex);
            return HasSlot(lineIndex + 1);
        }

        bool CutsOverflow() => slots != null || options.MaxLines > 0 || lineLimit > 0;

        // A word laid on the last line only to be cut to the ellipsis keeps no letters that raise the line past the
        // bottom, unless the line was that tall without it.
        void LayAndTrim(int start, int end, TextTrimming trimming, bool lastLine)
        {
            trimRoom = lastLine && slots == null && !vertical && textArea.Height < Int32.MaxValue
                       && options.LineStacking == LineStackingStrategy.MaxHeight
                ? Math.Max(textArea.Height + 1.0 - height, MeasuredLine(lineIndex).Height)
                : double.MaxValue;
            LayItems(start, end);
            EndInEllipsis(trimming);
            trimRoom = double.MaxValue;
        }

        bool FitsTrimRoom() => trimRoom == double.MaxValue || MeasuredLine(lineIndex).Height <= trimRoom;

        void EndInEllipsis(TextTrimming trimming)
        {
            var floor = LineFirstGlyph();
            if (cursorPosition + EllipsisWidth() <= LineRight(lineIndex) && FitsTrimRoom())
            {
                TrimText(0, height + baseLine);
                return;
            }

            if (trimming == TextTrimming.WordEllipses && !HasBlank(floor))
            {
                trimming = TextTrimming.CharEllipses;
            }

            PrepareDataAndTrim(glyphsData.Count, floor, 0, height + baseLine, trimming);
        }

        bool ProcessComposedWord(int start, int end)
        {
            var newline = end > start && items[end - 1].Symbol == '\n' ? end - 1 : end;
            var trims = renderingParameters.TextTrimming != TextTrimming.None;
            // The hyphens the composer chose in this word: it is set again in the pieces they cut.
            var wordFrom = CharAt(start);
            var wordTo = CharAt(newline);
            var cuts = composedCuts.Where(cut => cut > wordFrom && cut < wordTo).OrderBy(cut => cut).ToList();
            if (cuts.Count > 0)
            {
                var grown = Recut(start, newline, cuts);
                newline += grown;
                end += grown;
            }

            bool IsCut(int k) => k > start && k < newline && items[k].Cluster != items[k - 1].Cluster
                                 && composedCuts.Contains(items[k].Cluster);

            if (composedStarts.Contains(wordFrom) && LineHasContent())
            {
                if (HasRoomBelow())
                {
                    NewLine();
                }
                else if (trims)
                {
                    if (renderingParameters.TextTrimming == TextTrimming.CharEllipses)
                    {
                        LayItems(start, newline);
                    }

                    EndInEllipsis(renderingParameters.TextTrimming);
                    return false;
                }
                else if (CutsOverflow())
                {
                    return Overset(start);
                }
            }

            var firstCut = start + 1;
            while (firstCut < newline && !IsCut(firstCut))
            {
                firstCut++;
            }

            var needed = firstCut < newline
                ? Math.Max(WordWidth(start, firstCut), Advance(start, firstCut) + HyphenInk(items[firstCut - 1]))
                : WordWidth(start, newline);
            var passedCut = -1;
            if (LineHasContent() && HasRoomBelow() && cursorPosition + needed > LineRight(lineIndex) + LineShrink())
            {
                NewLine();
                passedCut = firstCut;
            }

            for (var k = start + 1; k < newline; k++)
            {
                if (!IsCut(k) || k == passedCut)
                {
                    continue;
                }

                if (!HasRoomBelow())
                {
                    if (trims)
                    {
                        LayItems(start, newline);
                        EndInEllipsis(TextTrimming.CharEllipses);
                        return false;
                    }

                    if (CutsOverflow())
                    {
                        LayItems(start, k);
                        AddHyphen(items[k - 1]);
                        return Overset(k);
                    }

                    break;
                }

                LayItems(start, k);
                AddHyphen(items[k - 1]);
                NewLine();
                start = k;
            }

            if (!LineHasContent())
            {
                overflows |= cursorPosition + WordWidth(start, newline) > LineRight(lineIndex) + LineShrink();
                inkOverflows |= cursorPosition + WordWidth(start, newline) > LineRight(lineIndex);
            }

            wordStartPosition = cursorPosition;
            if (trims && !LineHasContent() && cursorPosition + WordWidth(start, newline) > LineRight(lineIndex))
            {
                var lastLine = !HasRoomBelow();
                LayAndTrim(start, newline, TextTrimming.CharEllipses, lastLine);
                return !lastLine && LayItems(newline, end);
            }

            return LayItems(start, end);
        }

        void Compose()
        {
            composedStarts = [];
            composedCuts = [];
            var justified = renderingParameters.HorizontalTextAlignment == HorizontalTextAlignment.Justify;
            var raggedStretch = 2 * fontSize;
            List<(ComposerItem Item, int Next, bool Cut, bool Automatic)> paragraph = [];
            Dictionary<int, int> tabs = [];
            var start = dropItems;
            while (start < items.Count)
            {
                if (paragraph.Count == 0 && items[start].Cluster >= 0)
                {
                    if (start > dropItems && items[start - 1].Symbol == '\n')
                    {
                        BreakInto(items[start].Cluster, composedLines);
                        NoteParagraphBreak(items[start - 1].Cluster, composedLines);
                    }

                    justified = (lineParagraph?.Alignment ?? renderingParameters.HorizontalTextAlignment)
                                == HorizontalTextAlignment.Justify;
                }

                var end = WordEnd(start);
                var newline = end > start && items[end - 1].Symbol == '\n' ? end - 1 : end;
                List<int> points = options.Hyphens == Hyphens.None ? [] : HyphenationPoints(start, newline);
                var automatic = options.Hyphens == Hyphens.Auto && !HasSoftHyphen(start, newline);
                var piece = start;
                var wordFrom = CharAt(start);
                var wordTo = CharAt(newline);
                var reshaped = !HasRuby(start, newline);
                foreach (var point in points)
                {
                    // The word broken there is set again in its halves: the line it ends takes the first with the
                    // hyphen, the next line starts with the rest - what they take more or less than the word's pieces.
                    // A word with a ruby base is cut as it is.
                    var cut = ItemAt(piece, newline, point);
                    if (cut > piece)
                    {
                        AddWordPiece(paragraph, piece, cut, justified, false);
                    }

                    if (reshaped)
                    {
                        var before = Piece(wordFrom, point);
                        var after = Piece(point, wordTo);
                        var hyphen = before.Count > 0 ? HyphenAdvance(before[^1]) : 0;
                        paragraph.Add((ComposerItem.Penalty(Width(before) + hyphen - Advance(start, cut), 50, true,
                            Width(after) - Advance(cut, newline)), point, true, automatic));
                    }
                    else
                    {
                        paragraph.Add((ComposerItem.Penalty(cut > start ? HyphenAdvance(items[cut - 1]) : 0, 50, true),
                            point, true, automatic));
                    }

                    piece = cut;
                }

                var lastLetter = 0.0;
                if (newline > piece)
                {
                    lastLetter = AddWordPiece(paragraph, piece, newline, justified, true);
                }

                if (newline < end || end >= items.Count)
                {
                    ComposeParagraph(paragraph, justified, tabs);
                    paragraph.Clear();
                    tabs.Clear();
                    start = end;
                    continue;
                }

                var next = end;
                while (next < items.Count && IsBlank(items[next].Symbol))
                {
                    next++;
                }

                // Where the line after a break here starts, in the text: its places stay where they are when a word
                // is set again in pieces.
                var nextAt = CharAt(next);
                if (next == end)
                {
                    var hyphen = items[end - 1].Symbol is '-' or '‐';
                    // A line ending here may hang its comma or full stop, or trim its closing punctuation's blank half.
                    AddBreak(paragraph, nextAt, ComposerItem.Penalty(-EndLeeway(start, newline), hyphen ? 50 : 0, hyphen),
                        justified, raggedStretch);
                    // Words with no space between them - ideographs, a word after a hyphen - keep the letter spacing
                    // of the gap between them, which a space's glue carries elsewhere; at a break it is dropped. CJK
                    // letters widen between them when justified, as the lines are - a quarter of an em, so a line closed
                    // up is still taken before one spread, as JLREQ prefers.
                    var cjkGap = next < items.Count && (CjkPunctuation.IsCjk(CodepointAt(text, items[end - 1].Cluster))
                                                        || CjkPunctuation.IsCjk(CodepointAt(text, items[next].Cluster)));
                    if (justified && (lastLetter > 0 || cjkGap))
                    {
                        paragraph.Add((ComposerItem.Glue(0,
                            lastLetter * (options.LetterSpacing.Maximum - options.LetterSpacing.Desired)
                            + (cjkGap ? items[end - 1].FontSize / 4 : 0),
                            lastLetter * (options.LetterSpacing.Desired - options.LetterSpacing.Minimum)), nextAt, false, false));
                    }
                }
                else
                {
                    AddBreak(paragraph, nextAt, WordGlue(end, lastLetter), justified, raggedStretch);
                    NoteTab(end);
                    for (var k = end + 1; k < next; k++)
                    {
                        var glue = WordGlue(k, 0);
                        paragraph.Add((justified ? glue : ComposerItem.Glue(glue.Width, 0, 0), nextAt, false, false));
                        NoteTab(k);
                    }
                }

                start = next;
            }

            if (paragraph.Count > 0)
            {
                ComposeParagraph(paragraph, justified, tabs);
            }

            void NoteTab(int k)
            {
                if (items[k].Symbol == '\t')
                {
                    tabs[paragraph.Count - 1] = k;
                }
            }
        }

        void ComposeParagraph(List<(ComposerItem Item, int Next, bool Cut, bool Automatic)> paragraph, bool justified,
            Dictionary<int, int> tabs)
        {
            while (paragraph.Count > 0 && paragraph[paragraph.Count - 1].Item.Kind != ComposerItemKind.Box)
            {
                paragraph.RemoveAt(paragraph.Count - 1);
            }

            if (paragraph.Count == 0)
            {
                composedLines++;
                return;
            }

            // From the paragraph's first line on: it may be indented, a drop cap or a frame may narrow a few more.
            List<double> widths = [];
            var last = composedLines + paragraph.Count + 1;
            var distinct = Math.Min(Math.Max(Math.Max(dropLines, composedLines + 1), slots != null ? last : 0), last);
            if (slots != null)
            {
                HasSlot(distinct);
                distinct = Math.Min(distinct, Math.Max(slots.Count, composedLines + 1));
            }

            for (var line = composedLines; line < distinct; line++)
            {
                widths.Add(LineRight(line) - LineIndent(line));
            }

            widths.Add(LineRight(distinct) - LineIndent(distinct));
            if (!justified || !JustifiesLastLine())
            {
                paragraph.Add((ComposerItem.Penalty(0, ParagraphComposer.Never, false), -1, false, false));
                paragraph.Add((ComposerItem.Glue(0, double.PositiveInfinity, 0), -1, false, false));
            }

            paragraph.Add((ComposerItem.Penalty(0, ParagraphComposer.Forced, false), -1, false, false));
            var limit = options.HyphenLimit;
            List<(ComposerItem Item, int Next, bool Cut, bool Automatic)> chosen;
            int[] breaks;
            for (var pass = 0;; pass++)
            {
                var exactIndices = Enumerable.Range(0, paragraph.Count).Where(e => !paragraph[e].Automatic).ToList();
                var chosenIndices = exactIndices;
                chosen = exactIndices.Select(e => paragraph[e]).ToList();
                breaks = ParagraphComposer.Break(chosen.Select(entry => entry.Item).ToList(), widths, 100, limit);
                if (breaks == null)
                {
                    chosen = paragraph;
                    chosenIndices = Enumerable.Range(0, paragraph.Count).ToList();
                    var all = paragraph.Select(entry => entry.Item).ToList();
                    breaks = ParagraphComposer.Break(all, widths, 200, limit)
                             ?? ParagraphComposer.BreakAnyway(all, widths, 2 * fontSize, limit);
                }

                if (tabs.Count == 0 || pass == 2 || !MeasureTabs(chosenIndices, breaks))
                {
                    break;
                }
            }

            composedLines += breaks.Length;

            for (var k = 0; k < breaks.Length - 1; k++)
            {
                var entry = chosen[breaks[k]];
                composedStarts.Add(entry.Next);
                if (entry.Cut)
                {
                    composedCuts.Add(entry.Next);
                }
            }

            // A tab is as wide as the room to its stop from where it stands on its line, which only the breaks tell: each
            // is measured on the line it fell on, and the spaces before the last tab of a justified line take no stretch,
            // as justification widens only what follows it. Whether anything changed, for the paragraph to be broken again.
            bool MeasureTabs(List<int> chosenIndices, int[] found)
            {
                var breakAt = new HashSet<int>(found.Take(found.Length - 1).Select(b => chosenIndices[b]));
                var changed = false;
                var line = composedLines;
                var x = LineIndent(line);
                var atLineStart = true;
                var lineFirst = 0;
                for (var e = 0; e < paragraph.Count; e++)
                {
                    var item = paragraph[e].Item;
                    if (breakAt.Contains(e))
                    {
                        FreezeBeforeLastTab(lineFirst, e);
                        line++;
                        x = LineIndent(line);
                        atLineStart = true;
                        lineFirst = e + 1;
                        continue;
                    }

                    if (atLineStart && item.Kind != ComposerItemKind.Box)
                    {
                        continue;
                    }

                    atLineStart = false;
                    if (tabs.TryGetValue(e, out var tab))
                    {
                        var width = TabAdvanceAt(NextTabStop(x, line), tab, x);
                        if (Math.Abs(width - item.Width) > 0.01)
                        {
                            paragraph[e] = (ComposerItem.Glue(width, 0, 0), paragraph[e].Next, paragraph[e].Cut,
                                paragraph[e].Automatic);
                            changed = true;
                        }

                        x += width;
                        continue;
                    }

                    x += item.Kind == ComposerItemKind.Penalty ? 0 : item.Width;
                }

                FreezeBeforeLastTab(lineFirst, paragraph.Count);
                return changed;

                void FreezeBeforeLastTab(int from, int to)
                {
                    var lastTab = -1;
                    for (var e = from; e < to; e++)
                    {
                        lastTab = tabs.ContainsKey(e) ? e : lastTab;
                    }

                    for (var e = from; justified && e < lastTab; e++)
                    {
                        var item = paragraph[e].Item;
                        if (item.Kind == ComposerItemKind.Glue && (item.Stretch != 0 || item.Shrink != 0))
                        {
                            paragraph[e] = (ComposerItem.Glue(item.Width, 0, 0), paragraph[e].Next, paragraph[e].Cut,
                                paragraph[e].Automatic);
                            changed = true;
                        }
                    }
                }
            }
        }

        static void AddBreak(List<(ComposerItem Item, int Next, bool Cut, bool Automatic)> paragraph, int next,
            ComposerItem gap, bool justified, double raggedStretch)
        {
            if (justified)
            {
                paragraph.Add((gap, next, false, false));
                return;
            }

            var space = gap.Kind == ComposerItemKind.Glue ? gap.Width : 0;
            paragraph.Add((ComposerItem.Glue(0, raggedStretch, 0), next, false, false));
            paragraph.Add((ComposerItem.Penalty(gap.Kind == ComposerItemKind.Penalty ? gap.Width : 0,
                gap.Kind == ComposerItemKind.Penalty ? gap.Cost : 0,
                gap.Kind == ComposerItemKind.Penalty && gap.Flagged), next, false, false));
            paragraph.Add((ComposerItem.Glue(space, -raggedStretch, 0), next, false, false));
        }

        ComposerItem WordGlue(int index, double letterBefore)
        {
            var item = items[index];
            if (item.Symbol == '\t')
            {
                return ComposerItem.Glue(tabStop, 0, 0);
            }

            var unit = SpaceWidth(item.Font, item.FontSize);
            return ComposerItem.Glue(item.Advance,
                unit * (options.WordSpacing.Maximum - options.WordSpacing.Desired)
                + letterBefore * (options.LetterSpacing.Maximum - options.LetterSpacing.Desired),
                unit * (options.WordSpacing.Desired - options.WordSpacing.Minimum)
                + letterBefore * (options.LetterSpacing.Desired - options.LetterSpacing.Minimum));
        }

        double AddWordPiece(List<(ComposerItem Item, int Next, bool Cut, bool Automatic)> paragraph, int from, int to,
            bool justified, bool lastOfWord)
        {
            double letters = 0;
            double lastLetter = 0;
            double natural = 0;
            for (var k = from; k < to && justified; k++)
            {
                var item = items[k];
                if (!IsBlank(item.Symbol) && !item.Upright && ObjectSizeOf(item.Symbol, item.Attributes) == null)
                {
                    natural += item.Advance / item.HorizontalScale;
                }

                if ((k == from || item.Cluster != items[k - 1].Cluster) && item.Advance > 0 && !IsBlank(item.Symbol)
                    && !CursiveScripts.Contains(CodepointAt(text, item.Cluster)))
                {
                    lastLetter = SpaceWidth(item.Font, item.FontSize);
                    letters += lastLetter;
                }
            }

            if (lastOfWord)
            {
                letters -= lastLetter;
            }

            var joins = false;
            for (var k = from; k < to - 1 && justified && options.Kashidas && !joins; k++)
            {
                joins = items[k + 1].Cluster > items[k].Cluster
                        && KashidaAt(items[k].Cluster, items[k + 1].Cluster, items[k].Font, out _) != null;
            }

            var stretch = letters * (options.LetterSpacing.Maximum - options.LetterSpacing.Desired)
                          + natural * (options.GlyphScaling.Maximum - options.GlyphScaling.Desired)
                          + (joins ? items[from].FontSize : 0);
            var shrink = letters * (options.LetterSpacing.Desired - options.LetterSpacing.Minimum)
                         + natural * (options.GlyphScaling.Desired - options.GlyphScaling.Minimum);
            if (to - from < 2 || (stretch == 0 && shrink == 0))
            {
                paragraph.Add((ComposerItem.Box(Advance(from, to)), to, false, false));
                return lastLetter;
            }

            var middle = from + (to - from) / 2;
            paragraph.Add((ComposerItem.Box(Advance(from, middle)), to, false, false));
            paragraph.Add((ComposerItem.Penalty(0, ParagraphComposer.Never, false), to, false, false));
            paragraph.Add((ComposerItem.Glue(0, stretch, shrink), to, false, false));
            paragraph.Add((ComposerItem.Box(Advance(middle, to)), to, false, false));
            return lastLetter;
        }

        double Advance(int start, int end)
        {
            double advance = 0;
            for (var k = start; k < end; k++)
            {
                advance += items[k].Advance;
            }

            return advance;
        }

        bool HasSoftHyphen(int start, int end)
        {
            for (var k = start; k < end; k++)
            {
                if (items[k].Symbol == SoftHyphen)
                {
                    return true;
                }
            }

            return false;
        }

        double HyphenAdvance(ShapedItem last)
        {
            var hyphen = HyphenGlyph(last.Font);
            return last.Font.GetAdvanceWidth(hyphen.Index) * (last.FontSize / last.Font.UnitsPerEm)
                                                           * options.GlyphScaling.Desired;
        }

        bool ProcessWord(int start, int end)
        {
            if (composedStarts != null)
            {
                return ProcessComposedWord(start, end);
            }

            var wrapsByWords = renderingParameters.TextWrapping == TextWrapping.WrapByWords;
            var wordWidth = wrapsByWords ? WordWidth(start, end) : 0;
            if (wrapsByWords && options.Hyphens != Hyphens.None && Overflows(EndWidth(start, end)))
            {
                var rest = HyphenateWord(start, ref end);
                if (rest < 0)
                {
                    return false;
                }

                if (rest != start)
                {
                    start = rest;
                    wordWidth = WordWidth(start, end);
                }
            }

            // Wrapped at the word boundary, before any glyph is laid: a word that does not fit starts a new line, and
            // a word wider than the line only overflows as the first on its line.
            // A comma or full stop that may hang, or a closing punctuation that may give up its blank half, does not take
            // the word to the next line.
            if (wrapsByWords
                && wordIndex > 0
                && LineHasContent()
                && Overflows(EndWidth(start, end)))
            {
                if (HasRoomBelow())
                {
                    NewLine();
                }
                else if (CutsOverflow() && renderingParameters.TextTrimming == TextTrimming.None)
                {
                    return Overset(start);
                }
            }

            if (wrapsByWords && !LineHasContent())
            {
                overflows |= Overflows(EndWidth(start, end));
                inkOverflows |= Overflows(wordWidth);
            }

            wordStartPosition = cursorPosition;
            if (wrapsByWords
                && renderingParameters.TextTrimming != TextTrimming.None
                && Overflows(wordWidth))
            {
                var newline = end > start && items[end - 1].Symbol == '\n' ? end - 1 : end;
                var lastLine = !HasRoomBelow();
                var trimming = LineHasContent() ? renderingParameters.TextTrimming : TextTrimming.CharEllipses;
                LayAndTrim(start, newline, trimming, lastLine);
                return !lastLine && LayItems(newline, end);
            }

            return LayItems(start, end);
        }

        bool JustifiesLastLine() =>
            renderingParameters.JustifyLastLine || options.LastLineAlignment == HorizontalTextAlignment.Justify;

        bool Overflows(double width) =>
            cursorPosition + width > LineRight(lineIndex) && cursorPosition + width > LineRight(lineIndex) + LineShrink();

        double AlignRight(int line) => slots != null
            ? LineRight(line)
            : finalRect.Width - RightReserve(line);

        void PlaceInFrames()
        {
            var lineFrames = new int[lineCount];
            var shiftsX = new double[lineCount];
            var shiftsY = new double[lineCount];
            var lettersShiftsY = new double[lineCount];
            var spans = new (double Left, double Right)[lineCount];
            for (var i = 0; i < lineCount; i++)
            {
                var slot = SlotOf(i);
                lineFrames[i] = slot.Frame;
                shiftsX[i] = frames[slot.Frame].Left;
                spans[i] = (frames[slot.Frame].Left + slot.Left, frames[slot.Frame].Left + slot.Right);
                var top = OnGrid(slot.Unsnapped, lineBaselines[i], i);
                shiftsY[i] = top - lineTops[i];
                lettersShiftsY[i] = Math.Round(top + lineBaselines[i]) - Math.Round(lineTops[i] + lineBaselines[i]);
                lineTops[i] = top;
            }

            foreach (var glyph in glyphsData)
            {
                var line = glyph.LineIndex;
                glyph.Rect.X += (float)shiftsX[line];
                glyph.Rect.Y += (float)(ObjectSizeOf(glyph.Symbol, glyph.Attributes) != null
                    ? shiftsY[line]
                    : lettersShiftsY[line]);
                glyph.PenX += shiftsX[line];
            }

            laidOutLineFrames = lineFrames;
            laidOutLineSpans = spans;
        }

        // The first line that came out taller than the room its slot was found for and reaches into an exclusion beside
        // or below that room, with its height.
        (int Line, double Height)? LineOverAnExclusion()
        {
            for (var i = 0; i < lineCount && i < slots.Count; i++)
            {
                if (lineHeights[i] <= slotBases[i].Height + 0.5)
                {
                    continue;
                }

                var slot = slots[i];
                var frame = frames[slot.Frame];
                var left = frame.Left + slot.Left;
                var right = frame.Left + slot.Right;
                var top = lineTops[i];
                var bottom = top + lineHeights[i];
                foreach (var exclusion in exclusions ?? [])
                {
                    if (exclusion.Top < bottom - 0.5 && exclusion.Bottom > top + 0.5
                        && exclusion.Left < right && exclusion.Right > left)
                    {
                        return (i, lineHeights[i]);
                    }
                }
            }

            return null;
        }

        RectangleF AlongColumns(RectangleF rect) =>
            new(rect.Y, (float)(columnsRight - rect.Right), rect.Height, rect.Width);

        void TurnIntoColumns()
        {
            turned = true;
            if (slots == null)
            {
                columnsRight = finalRect.Height;
            }

            var right = columnsRight;
            foreach (var glyph in glyphsData)
            {
                var line = Math.Min(glyph.LineIndex, lineCount - 1);
                var axis = lineTops[line] + (lineAxes != null ? lineAxes[line] : lineHeights[line] / 2);
                var glyphScale = glyph.FontSize / glyph.Font.UnitsPerEm;
                if (ObjectSizeOf(glyph.Symbol, glyph.Attributes) is { } objectSize)
                {
                    // Upright, centered across its line, from where the line has come to down its height.
                    glyph.Rect = new RectangleF((float)Math.Round(right - axis - objectSize.Width / 2),
                        (float)Math.Round(glyph.PenX), (float)objectSize.Width, (float)objectSize.Height);
                    continue;
                }

                if (glyph.Upright)
                {
                    var down = glyph.PenX + glyph.OffsetX;
                    var upright = CalculateGlyphPosition(glyph.Font, glyph.Glyph, right - axis + glyph.OffsetY, down,
                        glyphScale);
                    upright.Y += (float)(down - Math.Round(down));
                    glyph.Rect = upright;
                    continue;
                }

                var shift = axis + (glyph.Font.LineAscent - glyph.Font.LineDescent) / 2.0 * glyphScale
                            - (lineTops[line] + lineBaselines[line]);
                var rect = glyph.Rect;
                glyph.Rect = new RectangleF((float)(right - rect.Bottom - shift), rect.X, rect.Height, rect.Width);
                glyph.Sideways = true;
            }

            finalRect = new Size<double>(Math.Ceiling(slots != null ? options.Frames.Max(frame => frame.Right) : right),
                finalRect.Width);
            if (glyphsData.Count > 0)
            {
                CalculateRealTextDimensions(glyphsData);
            }
        }

        bool Overset(int start)
        {
            oversetIndex = start < items.Count ? items[start].Cluster : text.Length;
            return false;
        }

        double LineShrink()
        {
            if (AlignmentOf(lineIndex) != HorizontalTextAlignment.Justify)
            {
                return 0;
            }

            double shrink = 0;
            var letters = options.LetterSpacing.Desired > options.LetterSpacing.Minimum;
            var scales = options.GlyphScaling.Desired > options.GlyphScaling.Minimum;
            var first = LineFirstGlyph();
            var started = false;
            for (var k = first; k < glyphsData.Count; k++)
            {
                var glyph = glyphsData[k];
                if (glyph.Symbol == ' ')
                {
                    shrink += started
                        ? SpaceWidth(glyph.Font, glyph.FontSize) * (options.WordSpacing.Desired - options.WordSpacing.Minimum)
                        : 0;
                    continue;
                }

                if (glyph.Symbol == '\t')
                {
                    shrink = 0;
                    started = true;
                    continue;
                }

                if (glyph.Symbol == '\n')
                {
                    continue;
                }

                started = true;
                if (scales && !glyph.Upright && ObjectSizeOf(glyph.Symbol, glyph.Attributes) == null)
                {
                    shrink += glyph.Advance / glyph.HorizontalScale
                                            * (options.GlyphScaling.Desired - options.GlyphScaling.Minimum);
                }

                if (letters && (k == first || glyph.PositionInString != glyphsData[k - 1].PositionInString)
                    && glyph.PositionInString >= 0 && glyph.Advance > 0
                    && !CursiveScripts.Contains(CodepointAt(text, glyph.PositionInString)))
                {
                    shrink += SpaceWidth(glyph.Font, glyph.FontSize)
                              * (options.LetterSpacing.Desired - options.LetterSpacing.Minimum);
                }
            }

            return shrink;
        }

        int LineFirstGlyph()
        {
            var first = glyphsData.Count;
            while (first > 0 && glyphsData[first - 1].LineIndex == lineIndex)
            {
                first--;
            }

            return first;
        }

        bool HasBlank(int from)
        {
            for (var k = from; k < glyphsData.Count; k++)
            {
                if (IsBlank(glyphsData[k].Symbol))
                {
                    return true;
                }
            }

            return false;
        }

        double WordWidth(int start, int end)
        {
            double pen = 0;
            double right = 0;
            for (var k = start; k < end; k++)
            {
                var item = items[k];
                if (item.Symbol == '\n')
                {
                    continue;
                }

                if (item.Upright || ObjectSizeOf(item.Symbol, item.Attributes) != null)
                {
                    right = Math.Max(right, pen + item.Advance);
                }
                else if (item.Glyph.BoundingRectangle.Width > 0)
                {
                    var inkRight = (item.Font.GetLeftSideBearing(item.Glyph.Index) + item.Glyph.BoundingRectangle.Width)
                                   * (item.FontSize / item.Font.UnitsPerEm) * item.HorizontalScale;
                    right = Math.Max(right, pen + item.OffsetX + inkRight);
                }

                pen += item.Advance;
            }

            return right;
        }

        // A word wider than the room hyphenated where its first part, set again by itself with the hyphen, fits: the
        // word's items are set again in the two halves, so the end of the word moves with them.
        int HyphenateWord(int start, ref int end)
        {
            var newline = start;
            while (newline < end && items[newline].Symbol != '\n')
            {
                newline++;
            }

            List<int> points = null;
            while (Overflows(EndWidth(start, newline))
                   && (HasRoomBelow() || (CutsOverflow() && renderingParameters.TextTrimming == TextTrimming.None)))
            {
                points ??= HyphenationPoints(start, newline);
                var cut = -1;
                var room = LineRight(lineIndex) + LineShrink();
                var from = CharAt(start);
                if (MayHyphenate())
                {
                    // The points measured in the word's items find the last that fits; set again by itself the half
                    // differs only where a ligature or a joining letter is cut, so it is set again from one point past
                    // that one back until a half fits.
                    var first = 0;
                    while (first < points.Count && points[first] <= from)
                    {
                        first++;
                    }

                    var estimate = first - 1;
                    for (var p = first; p < points.Count; p++)
                    {
                        var cutAt = ItemAt(start, newline, points[p]);
                        if (cutAt <= start || cursorPosition + Advance(start, cutAt) + HyphenInk(items[cutAt - 1]) > room)
                        {
                            break;
                        }

                        estimate = p;
                    }

                    for (var p = Math.Min(estimate + 1, points.Count - 1); p >= first; p--)
                    {
                        var half = Piece(from, points[p]);
                        if (half.Count > 0 && cursorPosition + Width(half) + HyphenInk(half[^1]) <= room)
                        {
                            cut = points[p];
                            break;
                        }
                    }
                }

                if (cut < 0)
                {
                    if (LineHasContent() && wordIndex > 0 && HasRoomBelow())
                    {
                        NewLine();
                        continue;
                    }

                    break;
                }

                var grown = Recut(start, newline, [cut]);
                newline += grown;
                end += grown;
                var cutIndex = ItemAt(start, newline, cut);
                LayItems(start, cutIndex);
                AddHyphen(items[cutIndex - 1]);
                if (!HasRoomBelow())
                {
                    Overset(cutIndex);
                    return -1;
                }

                NewLine();
                start = cutIndex;
            }

            return start;
        }

        bool MayHyphenate()
        {
            if (options.HyphenLimit <= 0 || !LineHasContent())
            {
                return true;
            }

            var run = 0;
            var line = lineIndex - 1;
            for (var k = glyphsData.Count - 1; k >= 0 && line >= 0 && run < options.HyphenLimit; k--)
            {
                var glyph = glyphsData[k];
                if (glyph.LineIndex > line || IsBlank(glyph.Symbol))
                {
                    continue;
                }

                if (glyph.LineIndex < line || glyph.PositionInString >= 0 || glyph.Symbol != '-')
                {
                    break;
                }

                run++;
                line--;
            }

            return run < options.HyphenLimit;
        }

        List<int> HyphenationPoints(int start, int end)
        {
            var points = new List<int>();
            if (start >= end)
            {
                return points;
            }

            var first = items[start].Cluster;
            var last = end < items.Count ? items[end].Cluster : text.Length;
            var breaks = new HashSet<int>();
            for (var c = first; c < last - 1; c++)
            {
                if (text[c] == SoftHyphen)
                {
                    breaks.Add(c + 1);
                }
            }

            if (breaks.Count == 0 && options.Hyphens == Hyphens.Auto)
            {
                var c = first;
                var languageItem = start;
                while (c < last)
                {
                    if (!IsWordLetter(text, c, first, last))
                    {
                        c++;
                        continue;
                    }

                    var runStart = c;
                    while (c < last && IsWordLetter(text, c, first, last))
                    {
                        c++;
                    }

                    while (languageItem < end - 1 && items[languageItem].Cluster < runStart)
                    {
                        languageItem++;
                    }

                    var hyphenator = Hyphenator.ForLanguage(items[languageItem].Attributes?.Language ?? options.Language);
                    foreach (var offset in hyphenator?.Hyphenate(text.Substring(runStart, c - runStart)) ?? [])
                    {
                        breaks.Add(runStart + offset);
                    }
                }
            }

            // Where the text may break, in order: between two graphemes, inside a ligature too - the halves are set
            // again by themselves - but not inside a ruby base.
            boundaries ??= graphemes ?? TextBoundaries.Graphemes(text);
            for (var c = first + 1; c < last; c++)
            {
                if (breaks.Contains(c) && boundaries[c] && !(rubyAt != null && SameBase(c - 1, c)))
                {
                    points.Add(c);
                }
            }

            return points;
        }

        // Where an item starts in the text; past the last, the text's end.
        int CharAt(int k) => k < items.Count ? items[k].Cluster : text.Length;

        // A piece of the text set by itself: the half of a word before or after a hyphen.
        List<ShapedItem> Piece(int from, int to) =>
            _shaper.ShapePiece(text, attributed, attributeRuns, fontSize, from, to, vertical, options, paragraphs, graphemes);

        // The items of a word from start to end set again in pieces cut at the given places, as the lines break it;
        // how many more items it takes. A word with a ruby base keeps its items, spread and kerned to the annotation.
        int Recut(int start, int end, IReadOnlyList<int> cuts)
        {
            if (HasRuby(start, end))
            {
                return 0;
            }

            var from = CharAt(start);
            var pieces = new List<ShapedItem>(end - start + 4);
            foreach (var cut in cuts)
            {
                pieces.AddRange(Piece(from, cut));
                from = cut;
            }

            pieces.AddRange(Piece(from, CharAt(end)));
            items.RemoveRange(start, end - start);
            items.InsertRange(start, pieces);

            // Set again full-width: the punctuation trimmed before is trimmed again.
            for (var k = start; k < start + pieces.Count; k++)
            {
                if ((k == start || items[k - 1].Cluster != items[k].Cluster) && FullWidth(items[k])
                    && trimmedPunctuation.Remove(items[k].Cluster))
                {
                    Trim(k, true);
                }
            }

            return pieces.Count - (end - start);
        }

        // The first item at or after a place in the text.
        int ItemAt(int start, int end, int at)
        {
            var k = start;
            while (k < end && items[k].Cluster < at)
            {
                k++;
            }

            return k;
        }

        // A piece's width as the line sets it: its punctuation trimmed as the word's was.
        double Width(List<ShapedItem> piece)
        {
            double width = 0;
            for (var k = 0; k < piece.Count; k++)
            {
                var item = piece[k];
                width += item.Advance;
                if ((k == 0 || piece[k - 1].Cluster != item.Cluster) && FullWidth(item)
                    && trimmedPunctuation.Contains(item.Cluster))
                {
                    width -= item.FontSize / 2;
                }
            }

            return width;
        }

        bool HasRuby(int start, int end)
        {
            for (var k = start; rubyAt != null && k < end; k++)
            {
                if (RubyIndex(items[k].Cluster) >= 0 || OtherRuby(items[k].Cluster) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        bool IsCjkCluster(GlyphWordData[] cluster) =>
            cluster[0].PositionInString >= 0 && CjkPunctuation.IsCjk(CodepointAt(text, cluster[0].PositionInString));

        // The comma or full stop a line hangs past its end, left out when the line is aligned.
        bool HungAtEnd(GlyphWordData glyph) =>
            hungEnds != null && hungEnds.TryGetValue(glyph.LineIndex, out var hung) && ReferenceEquals(hung, glyph);

        CjkPunctuationClass PunctuationOf(ShapedItem item) =>
            CjkPunctuation.Of(CodepointAt(text, item.Cluster), item.Attributes?.Language ?? options.Language);

        // A punctuation glyph set on a full em: one with half of it blank. A proportional one is never trimmed.
        static bool FullWidth(ShapedItem item) => Math.Abs(item.Advance - item.FontSize) <= item.FontSize * 0.05;

        static ShapedItem Spaced(ShapedItem item, double advance, double offsetX) => new(item.Glyph, item.Symbol,
            item.Cluster, advance, offsetX, item.OffsetY, item.Attributes, item.Font, item.FontSize, item.Upright,
            item.HorizontalScale, item.Tail);

        // The blank half of a fullwidth punctuation taken away, or given back: an opening one's is before its ink, a
        // closing one's after it, a middle dot's a quarter on either side.
        void Trim(int k, bool half)
        {
            var item = items[k];
            if (trimmedPunctuation.Contains(item.Cluster) == half)
            {
                return;
            }

            var amount = (half ? -1 : 1) * item.FontSize / 2;
            var before = PunctuationOf(item) switch
            {
                CjkPunctuationClass.Opening => amount,
                CjkPunctuationClass.MiddleDot => amount / 2,
                _ => 0,
            };
            items[k] = Spaced(item, item.Advance + amount, item.OffsetX + before);
            if (half)
            {
                trimmedPunctuation.Add(item.Cluster);
            }
            else
            {
                trimmedPunctuation.Remove(item.Cluster);
            }
        }

        // Fullwidth punctuation side by side, as CSS's text-spacing-trim collapses it: an opening one after an opening
        // one, a middle dot, an ideographic space or a closing one no smaller, half-width; a closing one before a
        // closing one, a middle dot, an ideographic space or a larger opening one, half-width. TrimAll trims them all.
        void CollapsePunctuation()
        {
            for (var k = dropItems; k < items.Count; k++)
            {
                var item = items[k];
                var kind = PunctuationOf(item);
                if (kind is CjkPunctuationClass.None or CjkPunctuationClass.IdeographicSpace || !FullWidth(item)
                    || (k > 0 && items[k - 1].Cluster == item.Cluster))
                {
                    continue;
                }

                if (options.SpacingTrim == TextSpacingTrim.TrimAll)
                {
                    Trim(k, true);
                    continue;
                }

                var previous = k > dropItems && items[k - 1].Symbol != '\n' ? items[k - 1] : (ShapedItem?)null;
                var next = k + 1 < items.Count && items[k + 1].Symbol != '\n' ? items[k + 1] : (ShapedItem?)null;
                var trim = kind switch
                {
                    CjkPunctuationClass.Opening => previous is { } before && (PunctuationOf(before) switch
                    {
                        CjkPunctuationClass.Opening or CjkPunctuationClass.MiddleDot
                            or CjkPunctuationClass.IdeographicSpace => true,
                        CjkPunctuationClass.Closing => before.FontSize >= item.FontSize,
                        _ => CharUnicodeInfo.GetUnicodeCategory(CodepointAt(text, before.Cluster))
                             == UnicodeCategory.OpenPunctuation,
                    }),
                    CjkPunctuationClass.Closing => next is { } after && (PunctuationOf(after) switch
                    {
                        CjkPunctuationClass.Closing or CjkPunctuationClass.MiddleDot
                            or CjkPunctuationClass.IdeographicSpace => true,
                        CjkPunctuationClass.Opening => after.FontSize > item.FontSize,
                        _ => CharUnicodeInfo.GetUnicodeCategory(CodepointAt(text, after.Cluster))
                             == UnicodeCategory.ClosePunctuation,
                    }),
                    _ => false,
                };
                if (trim)
                {
                    Trim(k, true);
                }
            }
        }

        // An opening punctuation starting a line: full-width, as Normal sets it, or half-width - always for TrimStart and
        // TrimBoth, past the first line of a paragraph for SpaceFirst.
        void StartLine(int k)
        {
            var item = items[k];
            if (options.SpacingTrim is TextSpacingTrim.SpaceAll or TextSpacingTrim.TrimAll
                || PunctuationOf(item) != CjkPunctuationClass.Opening || !FullWidth(item) && !trimmedPunctuation.Contains(item.Cluster))
            {
                return;
            }

            var opensParagraph = k == dropItems || (k > 0 && items[k - 1].Symbol == '\n');
            Trim(k, options.SpacingTrim switch
            {
                TextSpacingTrim.TrimStart or TextSpacingTrim.TrimBoth => true,
                TextSpacingTrim.SpaceFirst => !opensParagraph,
                _ => false,
            });
        }

        // The end of each line: a closing punctuation set half-width where the line would not fit otherwise, or at the
        // end of every line for TrimBoth; a comma or full stop hanging past the end, left out when the line is aligned.
        void FinishLineEnds()
        {
            Dictionary<int, GlyphWordData> lastOfLine = null;
            foreach (var glyph in glyphsData)
            {
                if (glyph.PositionInString >= 0 && !IsBlank(glyph.Symbol) && glyph.Symbol != '\n')
                {
                    (lastOfLine ??= [])[glyph.LineIndex] = glyph;
                }
            }

            foreach (var (line, glyph) in lastOfLine ?? [])
            {
                var codepoint = CodepointAt(text, glyph.PositionInString);
                var over = glyph.PenX + glyph.Advance > LineRight(line) + 0.01;
                if (CjkPunctuation.IsStopOrComma(codepoint)
                    && (options.HangingPunctuation == HangingPunctuation.ForceEnd
                        || (options.HangingPunctuation == HangingPunctuation.AllowEnd && over)))
                {
                    (hungEnds ??= [])[line] = glyph;
                    continue;
                }

                var trimsEnd = options.SpacingTrim is TextSpacingTrim.TrimBoth or TextSpacingTrim.TrimAll
                               || (options.SpacingTrim != TextSpacingTrim.SpaceAll && over);
                if (trimsEnd && !trimmedPunctuation.Contains(glyph.PositionInString)
                    && CjkPunctuation.Of(codepoint, glyph.Attributes?.Language ?? options.Language) == CjkPunctuationClass.Closing
                    && Math.Abs(glyph.Advance - glyph.FontSize) <= glyph.FontSize * 0.05)
                {
                    glyph.Advance -= glyph.FontSize / 2;
                    trimmedPunctuation.Add(glyph.PositionInString);
                }
            }
        }

        // What the last character of a word may give up at the end of a line that would not fit otherwise: a comma or
        // full stop hanging past it whole, a closing punctuation its blank half.
        double EndLeeway(int start, int end)
        {
            var last = end - 1;
            while (last >= start && items[last].Symbol == '\n')
            {
                last--;
            }

            if (last < start)
            {
                return 0;
            }

            var item = items[last];
            if (options.HangingPunctuation != HangingPunctuation.None && CjkPunctuation.IsStopOrComma(CodepointAt(text, item.Cluster)))
            {
                return item.Advance;
            }

            return options.SpacingTrim != TextSpacingTrim.SpaceAll && PunctuationOf(item) == CjkPunctuationClass.Closing
                   && FullWidth(item) && !trimmedPunctuation.Contains(item.Cluster)
                ? item.Advance / 2
                : 0;
        }

        // The width a word takes at the end of a line: its ink, with what its last character may give up left out. A
        // closing punctuation's ink is off its blank half already; set upright, the whole advance counts.
        double EndWidth(int start, int end)
        {
            var width = WordWidth(start, end);
            var leeway = EndLeeway(start, end);
            if (leeway <= 0)
            {
                return width;
            }

            var last = end - 1;
            while (items[last].Symbol == '\n')
            {
                last--;
            }

            return Math.Max(WordWidth(start, last), Math.Min(width, Advance(start, last) + items[last].Advance - leeway));
        }

        double HyphenInk(ShapedItem last)
        {
            var hyphen = HyphenGlyph(last.Font);
            return (last.Font.GetLeftSideBearing(hyphen.Index) + hyphen.BoundingRectangle.Width)
                   * (last.FontSize / last.Font.UnitsPerEm) * options.GlyphScaling.Desired;
        }

        void AddHyphen(ShapedItem last)
        {
            var hyphen = HyphenGlyph(last.Font);
            var hyphenScale = last.FontSize / last.Font.UnitsPerEm;
            var advance = last.Font.GetAdvanceWidth(hyphen.Index) * hyphenScale * options.GlyphScaling.Desired;
            var shift = Raise(last.Attributes, last.Font, last.FontSize, fontSize);
            var rect = Widened(CalculateGlyphPosition(last.Font, hyphen, cursorPosition, height + baseLine - shift,
                hyphenScale), cursorPosition, options.GlyphScaling.Desired);
            glyphsData.Add(new GlyphWordData(hyphen, '-', rect, -1, lineIndex)
            {
                PenX = cursorPosition,
                Advance = advance,
                OffsetY = shift,
                Attributes = last.Attributes,
                Font = last.Font,
                FontSize = last.FontSize,
                HorizontalScale = options.GlyphScaling.Desired,
            });
            cursorPosition += advance;
        }

        bool LayItems(int start, int end)
        {
            for (var i = start; i < end; i++)
            {
                var item = items[i];
                switch (item.Symbol)
                {
                    case '\n':
                        BreakInto(item.Cluster + 1, lineIndex + 1);
                        NoteParagraphBreak(item.Cluster, lineIndex + 1);
                        if (renderingParameters.TextWrapping != TextWrapping.NoWrap
                            && renderingParameters.TextTrimming != TextTrimming.None
                            && !HasRoomBelow()
                            && i < items.Count - 1)
                        {
                            EndInEllipsis(renderingParameters.TextTrimming);
                            return false;
                        }

                        if (options.EmitNewlineCarets)
                        {
                            var caretRect = new RectangleF((float)cursorPosition, (float)(height + baseLine), 0f, 0f);
                            glyphsData.Add(new GlyphWordData(item.Glyph, '\n', caretRect, item.Cluster, lineIndex)
                            {
                                PenX = cursorPosition,
                                Attributes = item.Attributes,
                                Font = item.Font,
                                FontSize = item.FontSize,
                            });
                        }

                        if (CutsOverflow() && !HasRoomBelow())
                        {
                            return i < items.Count - 1 && Overset(i + 1);
                        }

                        NewLine();
                        break;
                    default:
                    {
                        if (!LineHasContent())
                        {
                            StartLine(i);
                            item = items[i];
                        }

                        var glyphBase = height + baseLine;
                        var glyphRect = ObjectSizeOf(item.Symbol, item.Attributes) is { } objectSize
                            ? ObjectRect(cursorPosition, glyphBase - item.OffsetY, objectSize)
                            : item.Upright
                            ? CellRect(item.Font, item.FontSize, cursorPosition, item.Advance - item.Tail, glyphBase)
                            : Widened(CalculateGlyphPosition(item.Font, item.Glyph,
                                cursorPosition + item.OffsetX,
                                glyphBase - item.OffsetY,
                                item.FontSize / item.Font.UnitsPerEm), cursorPosition + item.OffsetX, item.HorizontalScale);

                        glyphsData.Add(new GlyphWordData(item.Glyph, item.Symbol, glyphRect, item.Cluster, lineIndex)
                        {
                            PenX = cursorPosition,
                            Advance = item.Advance,
                            OffsetX = item.OffsetX,
                            OffsetY = item.OffsetY,
                            Attributes = item.Attributes,
                            Font = item.Font,
                            FontSize = item.FontSize,
                            Upright = item.Upright,
                            HorizontalScale = item.HorizontalScale,
                            Tail = item.Tail,
                        });
                        cursorPosition += item.Advance;

                        switch (renderingParameters.TextWrapping)
                        {
                            case TextWrapping.NoWrap:
                                if (cursorPosition > LineRight(lineIndex))
                                {
                                    if (i < items.Count - 1)
                                    {
                                        var glyphsDataCopy = glyphsData.ToArray();
                                        PrepareDataAndTrim(glyphsDataCopy.Length, 0, i, glyphBase, renderingParameters.TextTrimming);
                                        return false;
                                    }
                                }
                                break;
                            case TextWrapping.WrapBySymbols:
                                {
                                    var right = LineRight(lineIndex);
                                    if (cursorPosition > right)
                                    {
                                        var glyphsDataCopy = glyphsData.ToArray();
                                        if (HasRoomBelow())
                                        {
                                            (lineRawTop, height) = NextTop(0);
                                            lineIndex++;
                                            NoteLine(lineIndex, 0);
                                            glyphBase = height + baseLine;
                                            RearrangeData(glyphsDataCopy, glyphBase, right);
                                        }
                                        else if (i < items.Count - 1)
                                        {
                                            PrepareDataAndTrim(glyphsDataCopy.Length, 0, i, glyphBase, renderingParameters.TextTrimming);
                                            if (CutsOverflow() && renderingParameters.TextTrimming == TextTrimming.None)
                                            {
                                                Overset(i);
                                            }

                                            return false;
                                        }
                                    }
                                }
                                break;
                        }
                        break;
                    }
                }
            }
            return true;
        }

        void ArrangeText()
        {
            var hung = new HashSet<int>();
            var lastLine = glyphsData.Max(x => x.LineIndex);
            foreach (var alignment in Enumerable.Range(0, lastLine + 1).Select(AlignmentOf).Distinct().ToList())
            {
                if (alignment is HorizontalTextAlignment.Left or HorizontalTextAlignment.Right && paragraphs != null)
                {
                    AlignToEachLinesStart(alignment);
                }
                else if (alignment == HorizontalTextAlignment.Center)
                {
                    var maxLines = glyphsData.Max(x => x.LineIndex);
                    var byLine = GlyphsByLine(maxLines + 1);
                    for (int i = 0; i <= maxLines; ++i)
                    {
                        var glyphsForLine = byLine[i];
                        if (glyphsForLine.Length == 0 || AlignmentOf(i) != alignment)
                        {
                            continue;
                        }

                        // By the ink, so leading/trailing spaces don't pull the line off-center.
                        var tab = LastTab(glyphsForLine, 0, glyphsForLine.Length, LineState(i).RightToLeft);
                        var from = AfterTab(tab);
                        var ink = glyphsForLine.Where(x => !IsBlank(x.Symbol) && Aligned(x, from) && !HungAtEnd(x)).ToArray();
                        if (ink.Length == 0 || PlacesWhatFollows(tab)) continue;
                        var minX = ink.Min(LeftOf);
                        var maxX = ink.Max(RightOf);
                        var lineWidth = maxX - minX;
                        var roomLeft = double.IsFinite(from) ? from : LineIndent(i);
                        var diff = PastTab(tab, roomLeft + (AlignRight(i) - roomLeft - lineWidth) / 2 - minX);
                        foreach (var glyphWordData in glyphsForLine)
                        {
                            if (!Aligned(glyphWordData, from))
                            {
                                continue;
                            }

                            var rect = glyphWordData.Rect;
                            rect.X += (float)diff;
                            glyphWordData.Rect = rect;
                            glyphWordData.PenX += diff;
                        }

                        Widen(tab, diff);
                    }
                }
                else if (alignment == HorizontalTextAlignment.Right)
                {
                    var maxLines = glyphsData.Max(x => x.LineIndex);
                    var byLine = GlyphsByLine(maxLines + 1);
                    for (int i = 0; i <= maxLines; ++i)
                    {
                        var glyphsForLine = byLine[i];
                        if (glyphsForLine.Length == 0 || AlignmentOf(i) != alignment)
                        {
                            continue;
                        }

                        var tab = LastTab(glyphsForLine, 0, glyphsForLine.Length, LineState(i).RightToLeft);
                        var from = AfterTab(tab);
                        var ink = glyphsForLine.Where(x => !IsBlank(x.Symbol) && Aligned(x, from) && !HungAtEnd(x)).ToArray();
                        if (ink.Length == 0 || PlacesWhatFollows(tab))
                        {
                            continue;
                        }

                        var maxX = ink.Max(RightOf);
                        var diff = PastTab(tab, AlignRight(i) - maxX);
                        foreach (var glyphWordData in glyphsForLine)
                        {
                            if (!Aligned(glyphWordData, from))
                            {
                                continue;
                            }

                            var rect = glyphWordData.Rect;
                            rect.X += (float)diff;
                            glyphWordData.Rect = rect;
                            glyphWordData.PenX += diff;
                        }

                        Widen(tab, diff);
                    }
                }
                else if (alignment == HorizontalTextAlignment.Justify)
                {
                    var maxLines = glyphsData.Max(x => x.LineIndex);
                    List<(GlyphWordData After, GlyphWordData Extender)> extenders = [];
                    var firstClusters = new int[maxLines + 2];
                    Array.Fill(firstClusters, text.Length);
                    foreach (var glyph in glyphsData)
                    {
                        if (glyph.PositionInString >= 0)
                        {
                            firstClusters[glyph.LineIndex] = Math.Min(firstClusters[glyph.LineIndex], glyph.PositionInString);
                        }
                    }

                    var byLine = GlyphsByLine(maxLines + 1);
                    for (int i = 0; i <= maxLines; ++i)
                    {
                        // Only the gaps between words change; glyphs are shifted, never re-laid, so kerning and bearings stay.
                        var lineGlyphs = byLine[i].OrderBy(x => x.Rect.X).ToArray();
                        if (lineGlyphs.Length == 0 || AlignmentOf(i) != alignment)
                        {
                            continue;
                        }
                        var stretches = JustifiesLastLine()
                                        || (i < maxLines
                                            ? !EndsParagraph(lineGlyphs, firstClusters[i + 1])
                                            : oversetIndex < text.Length && !EndsParagraph(lineGlyphs, oversetIndex));

                        int firstInk = -1, lastInk = -1;
                        for (int k = 0; k < lineGlyphs.Length; k++)
                        {
                            if (IsBlank(lineGlyphs[k].Symbol)) continue;
                            if (firstInk < 0) firstInk = k;
                            lastInk = k;
                        }
                        if (firstInk < 0) continue;

                        var lastTab = Array.FindLastIndex(lineGlyphs, lastInk, lastInk + 1, glyph => glyph.Symbol == '\t');
                        var clusters = VisualClusters(byLine[i]);
                        // A comma or full stop hanging past the end is left out: the line is justified without it.
                        var hungEnd = hungEnds != null && hungEnds.TryGetValue(i, out var hungGlyph) ? hungGlyph : null;
                        var lastInkCluster = clusters.FindLastIndex(cluster => IsInk(cluster) && !cluster.Contains(hungEnd));
                        var fromCluster = lastInkCluster < 0 ? 0 : Math.Max(clusters.FindIndex(IsInk),
                            clusters.FindLastIndex(lastInkCluster, lastInkCluster + 1, cluster => cluster[0].Symbol == '\t'));
                        var spaces = new double[clusters.Count];
                        var letters = new double[clusters.Count];
                        for (var c = fromCluster; c < lastInkCluster; c++)
                        {
                            var unit = SpaceWidth(clusters[c][0].Font, clusters[c][0].FontSize);
                            if (clusters[c][0].Symbol == ' ' && c > fromCluster)
                            {
                                spaces[c] = unit;
                            }
                            else if (IsInk(clusters[c]) && IsSpaced(clusters[c]))
                            {
                                letters[c] = unit;
                            }
                        }

                        // CJK lines, with no spaces, widen between their letters, as CSS's inter-character justification.
                        var cjk = new double[clusters.Count];
                        for (var c = fromCluster; c < lastInkCluster; c++)
                        {
                            if (IsInk(clusters[c]) && IsInk(clusters[c + 1])
                                && (IsCjkCluster(clusters[c]) || IsCjkCluster(clusters[c + 1])))
                            {
                                cjk[c] = 1;
                            }
                        }

                        var cjkUnits = cjk.Sum();
                        var natural = new double[clusters.Count];
                        for (var c = fromCluster; c <= lastInkCluster; c++)
                        {
                            if (IsInk(clusters[c]) && !clusters[c][0].Upright
                                && ObjectSizeOf(clusters[c][0].Symbol, clusters[c][0].Attributes) == null)
                            {
                                natural[c] = clusters[c].Sum(glyph => glyph.Advance / glyph.HorizontalScale);
                            }
                        }

                        var spaceUnits = spaces.Sum();
                        var letterUnits = letters.Sum();
                        var naturalWidth = natural.Sum();
                        var wordStretch = spaceUnits * (options.WordSpacing.Maximum - options.WordSpacing.Desired);
                        var wordShrink = spaceUnits * (options.WordSpacing.Desired - options.WordSpacing.Minimum);
                        var letterStretch = letterUnits * (options.LetterSpacing.Maximum - options.LetterSpacing.Desired);
                        var letterShrink = letterUnits * (options.LetterSpacing.Desired - options.LetterSpacing.Minimum);
                        var scaleStretch = naturalWidth * (options.GlyphScaling.Maximum - options.GlyphScaling.Desired);
                        var scaleShrink = naturalWidth * (options.GlyphScaling.Desired - options.GlyphScaling.Minimum);

                        var rightToLeft = IsRightToLeftLine(lineGlyphs);
                        var lead = rightToLeft ? lineGlyphs[firstInk].PenX - LineIndent(i) : 0;
                        var hangs = options.OpticalMarginAlignment && !rightToLeft;
                        var leftHang = hangs && firstInk == 0 && lastTab < 0 ? Hang(lineGlyphs[firstInk], true) : 0;
                        var rightHang = hangs ? Hang(lineGlyphs[lastInk], false) : 0;
                        var inkRight = lineGlyphs.Where(glyph => !IsBlank(glyph.Symbol) && glyph.Symbol != '\n'
                                                                 && !ReferenceEquals(glyph, hungEnd))
                            .Select(RightOf).DefaultIfEmpty(lineGlyphs[lastInk].Rect.Right).Max();
                        var startRight = rightToLeft && lastTab < 0 ? StartRight(lineGlyphs, inkRight) : inkRight;
                        var extra = AlignRight(i) + leftHang + rightHang - (startRight - lead);

                        double wordShare;
                        double letterShare;
                        double cjkShare = 0;
                        double kashidaShare = 0;
                        double alternateShare = 0;
                        double scaleShare;
                        var alternates = new Glyph[clusters.Count];
                        var kashidas = options.Kashidas && stretches && extra > 0
                            ? KashidaPoints(clusters, fromCluster, lastInkCluster)
                            : new Glyph[clusters.Count];
                        var kashidaRoom = kashidas.Select((glyph, c) => glyph != null ? clusters[c][0].FontSize : 0).Sum();
                        if (extra > 0)
                        {
                            wordShare = Math.Min(extra, wordStretch);
                            letterShare = Math.Min(extra - wordShare, letterStretch);
                            var rest = extra - wordShare - letterShare;
                            cjkShare = stretches && cjkUnits > 0 ? rest : 0;
                            rest -= cjkShare;
                            kashidaShare = Math.Min(rest, kashidaRoom);
                            rest -= kashidaShare;
                            if (options.JustificationAlternates && rest > 0 && stretches)
                            {
                                alternateShare = ChooseAlternates(text, options.Language, clusters, fromCluster,
                                    lastInkCluster, rest, alternates);
                            }

                            scaleShare = Math.Min(rest - alternateShare, scaleStretch);
                            wordShare = spaceUnits > 0
                                ? extra - letterShare - cjkShare - kashidaShare - alternateShare - scaleShare
                                : wordShare;
                        }
                        else
                        {
                            wordShare = Math.Max(extra, -wordShrink);
                            letterShare = Math.Max(extra - wordShare, -letterShrink);
                            scaleShare = Math.Max(extra - wordShare - letterShare, -scaleShrink);
                        }

                        var hasInk = lineGlyphs.Any(glyph => !IsBlank(glyph.Symbol) && glyph.Symbol != '\n');
                        var singleWord = hasInk && stretches && extra > 0 && lastTab < 0 && spaceUnits == 0
                                         && IsOneWord(clusters, lastInkCluster);
                        double offset = 0;
                        if (singleWord)
                        {
                            var rest = extra - wordShare - letterShare - cjkShare - kashidaShare - alternateShare - scaleShare;
                            if (options.SingleWordJustification == HorizontalTextAlignment.Justify && letterUnits > 0)
                            {
                                letterShare += rest;
                            }
                            else
                            {
                                offset = options.SingleWordJustification switch
                                {
                                    HorizontalTextAlignment.Center => rest / 2,
                                    HorizontalTextAlignment.Right => rightToLeft ? 0 : rest,
                                    _ => rightToLeft ? rest : 0,
                                };
                            }
                        }

                        var unspread = wordShare == 0 && letterShare == 0 && cjkShare == 0 && kashidaShare == 0
                                       && alternateShare == 0 && scaleShare == 0 && offset == 0;
                        if (!hasInk || (!singleWord && (unspread || (extra > 0 && !stretches))))
                        {
                            var placement = hasInk && extra > 0 && !stretches && lastTab < 0
                                ? options.LastLineAlignment
                                : HorizontalTextAlignment.Left;
                            var inkLeft = lineGlyphs.Where(glyph => !IsBlank(glyph.Symbol) && glyph.Symbol != '\n')
                                .Select(glyph => glyph.Rect.Left).DefaultIfEmpty(lineGlyphs[firstInk].Rect.Left).Min();
                            var placed = placement switch
                            {
                                HorizontalTextAlignment.Center => (LineIndent(i) + AlignRight(i) - inkLeft - inkRight) / 2,
                                HorizontalTextAlignment.Right => rightToLeft
                                    ? LineIndent(i) - inkLeft
                                    : AlignRight(i) + rightHang - inkRight,
                                _ => rightToLeft ? AlignRight(i) - startRight : 0,
                            };
                            if (placement is HorizontalTextAlignment.Center or HorizontalTextAlignment.Right)
                            {
                                hung.Add(i);
                            }

                            for (var k = 0; k < lineGlyphs.Length && placed != 0; k++)
                            {
                                lineGlyphs[k].Rect.X += (float)placed;
                                lineGlyphs[k].PenX += placed;
                            }

                            continue;
                        }

                        var shift = -leftHang - lead + offset;
                        hung.Add(i);
                        for (var c = 0; c < clusters.Count; c++)
                        {
                            foreach (var glyph in clusters[c])
                            {
                                glyph.Rect.X += (float)shift;
                                glyph.PenX += shift;
                            }

                            if (alternates[c] != null)
                            {
                                shift += TakeAlternate(clusters[c][0], alternates[c]);
                            }

                            if (natural[c] > 0 && scaleShare != 0)
                            {
                                shift += Scale(clusters[c], scaleShare * natural[c] / naturalWidth);
                            }

                            if (kashidas[c] != null && kashidaShare > 0)
                            {
                                var letter = clusters[c][0];
                                var stretch = kashidaShare * letter.FontSize / kashidaRoom;
                                var pen = clusters[c].Max(glyph => glyph.PenX + glyph.Advance);
                                extenders.Add((clusters[c][^1], Extender(letter, kashidas[c], pen, stretch)));
                                letter.Advance += stretch;
                                shift += stretch;
                            }

                            var widening = (spaces[c] > 0 ? wordShare * spaces[c] / spaceUnits
                                : letters[c] > 0 ? Math.Max(letterShare * letters[c] / letterUnits, -clusters[c].Sum(glyph => glyph.Advance))
                                : 0) + (cjk[c] > 0 ? cjkShare * cjk[c] / cjkUnits : 0);
                            if (widening == 0)
                            {
                                continue;
                            }

                            clusters[c][0].Advance += widening;
                            if (spaces[c] > 0)
                            {
                                clusters[c][0].Rect.Width += (float)widening;
                            }

                            shift += widening;
                        }
                    }

                    if (extenders.Count > 0)
                    {
                        var followers = extenders.ToDictionary(pair => pair.After, pair => pair.Extender);
                        var laid = new List<GlyphWordData>(glyphsData.Count + followers.Count);
                        foreach (var glyph in glyphsData)
                        {
                            laid.Add(glyph);
                            if (followers.TryGetValue(glyph, out var extender))
                            {
                                laid.Add(extender);
                            }
                        }

                        glyphsData.Clear();
                        glyphsData.AddRange(laid);
                    }
                }
            }

            if (options.OpticalMarginAlignment)
            {
                HangMargins(hung);
            }

            // The lines, not their ink, are placed, so same-size text does not wobble and nothing leaves the box; a text
            // that sets its own height has no room to place them in.
            var linesAlignment = slots == null && (vertical || renderingParameters.TextArea.Height != Int32.MaxValue)
                ? renderingParameters.VerticalTextAlignment
                : VerticalTextAlignment.Top;
            switch (linesAlignment)
            {
                case VerticalTextAlignment.Center:
                {
                    // Vertical lines centered across the block leave out an empty last column.
                    var lastInk = glyphsData.Max(x => x.LineIndex);
                    var blockTop = lineTops[0] - textArea.Y;
                    var blockHeight = (vertical ? lineTops[lastInk] + lineHeights[lastInk] : height) - lineTops[0];
                    var diff = GridSteps((finalRect.Height - blockHeight) / 2 - blockTop);
                    verticalShift = diff;
                    foreach (var glyphWordData in glyphsData)
                    {
                        var rect = glyphWordData.Rect;
                        rect.Y += (float)diff;
                        glyphWordData.Rect = rect;
                    }
                }
                break;
                case VerticalTextAlignment.Bottom:
                {
                    var diff = GridSteps(finalRect.Height - (height - textArea.Y));
                    verticalShift = diff;
                    foreach (var glyphWordData in glyphsData)
                    {
                        var rect = glyphWordData.Rect;
                        rect.Y += (float)diff;
                        glyphWordData.Rect = rect;
                    }
                }
                break;
            }
        }

        void HangMargins(HashSet<int> hung)
        {
            foreach (var line in glyphsData.GroupBy(glyph => glyph.LineIndex))
            {
                var alignment = AlignmentOf(line.Key);
                if (hung.Contains(line.Key) || alignment == HorizontalTextAlignment.Center)
                {
                    continue;
                }

                var toRight = alignment == HorizontalTextAlignment.Right;

                var glyphs = line.OrderBy(glyph => glyph.PenX).ToArray();
                if (IsRightToLeftLine(glyphs))
                {
                    continue;
                }

                int from;
                int to;
                double shift;
                if (toRight)
                {
                    var last = Array.FindLastIndex(glyphs, glyph => !IsBlank(glyph.Symbol) && glyph.Symbol != '\n');
                    if (last < 0)
                    {
                        continue;
                    }

                    from = Array.FindLastIndex(glyphs, last, last + 1, glyph => glyph.Symbol == '\t') + 1;
                    to = glyphs.Length;
                    // A ruby base does not hang: its annotation would go past the margin with it.
                    if (InABase(glyphs[last]))
                    {
                        continue;
                    }

                    shift = Hang(glyphs[last], false);
                }
                else
                {
                    if (IsBlank(glyphs[0].Symbol) || glyphs[0].Symbol == '\n' || InABase(glyphs[0]))
                    {
                        continue;
                    }

                    var firstTab = Array.FindIndex(glyphs, glyph => glyph.Symbol == '\t');
                    from = 0;
                    to = firstTab < 0 ? glyphs.Length : firstTab;
                    shift = -Hang(glyphs[0], true);
                }

                for (var k = from; k < to; k++)
                {
                    glyphs[k].Rect.X += (float)shift;
                    glyphs[k].PenX += shift;
                }

                if (toRight && from > 0)
                {
                    Widen(glyphs[from - 1], shift);
                }
            }
        }

        bool IsRightToLeftLine(GlyphWordData[] line)
        {
            foreach (var glyph in line)
            {
                if (glyph.PositionInString >= 0)
                {
                    return IsRightToLeftParagraph(glyph.PositionInString);
                }
            }

            return false;
        }

        GlyphWordData[][] GlyphsByLine(int lines)
        {
            var counts = new int[lines];
            foreach (var glyph in glyphsData)
            {
                counts[glyph.LineIndex]++;
            }

            var byLine = new GlyphWordData[lines][];
            for (var i = 0; i < lines; i++)
            {
                byLine[i] = new GlyphWordData[counts[i]];
                counts[i] = 0;
            }

            foreach (var glyph in glyphsData)
            {
                byLine[glyph.LineIndex][counts[glyph.LineIndex]++] = glyph;
            }

            return byLine;
        }

        List<GlyphWordData[]> VisualClusters(GlyphWordData[] lineGlyphs)
        {
            var groups = new List<GlyphWordData[]>();
            var current = new List<GlyphWordData>();
            foreach (var glyph in lineGlyphs)
            {
                var joins = glyph.Advance == 0 && glyph.Symbol != '\n' && !IsBlank(glyph.Symbol)
                            && glyph.PositionInString >= 0;
                if (current.Count > 0 && !joins
                    && (glyph.PositionInString < 0 || glyph.PositionInString != current[0].PositionInString))
                {
                    groups.Add(current.ToArray());
                    current.Clear();
                }

                current.Add(glyph);
            }

            if (current.Count > 0)
            {
                groups.Add(current.ToArray());
            }

            return groups.OrderBy(group => group.Min(glyph => glyph.PenX))
                .ThenBy(group => group.Max(glyph => glyph.PenX + glyph.Advance)).ToList();
        }

        Glyph[] KashidaPoints(List<GlyphWordData[]> clusters, int from, int to)
        {
            var points = new Glyph[clusters.Count];
            var starts = clusters.Select(cluster => cluster[0].PositionInString).Where(position => position >= 0)
                .Distinct().OrderBy(position => position).ToArray();
            var c = from;
            while (c <= to)
            {
                while (c <= to && !IsInk(clusters[c]))
                {
                    c++;
                }

                var best = -1;
                Glyph bestGlyph = null;
                var bestRank = (Priority: 0, Position: -1);
                for (; c < to && IsInk(clusters[c + 1]); c++)
                {
                    var later = Math.Max(clusters[c][0].PositionInString, clusters[c + 1][0].PositionInString);
                    var earlier = Math.Min(clusters[c][0].PositionInString, clusters[c + 1][0].PositionInString);
                    if (earlier < 0)
                    {
                        continue;
                    }

                    var next = Array.BinarySearch(starts, earlier);
                    if (next < 0 || next + 1 >= starts.Length || starts[next + 1] != later)
                    {
                        continue;
                    }

                    var glyph = KashidaAt(earlier, later, clusters[c][0].Font, out var letter);
                    if (glyph == null)
                    {
                        continue;
                    }

                    var rank = (Priority: CodepointAt(text, letter) is >= 0x0633 and <= 0x0636 ? 1 : 0, Position: later);
                    if (rank.CompareTo(bestRank) > 0)
                    {
                        best = c;
                        bestGlyph = glyph;
                        bestRank = rank;
                    }
                }

                if (best >= 0)
                {
                    points[best] = bestGlyph;
                }

                c++;
            }

            return points;
        }

        Glyph KashidaAt(int earlier, int later, IFont font, out int letter)
        {
            letter = later - 1;
            while (letter > earlier)
            {
                if (char.IsLowSurrogate(text[letter]) && char.IsHighSurrogate(text[letter - 1]))
                {
                    letter--;
                }

                if (letter <= earlier
                    || CharUnicodeInfo.GetUnicodeCategory(CodepointAt(text, letter)) != UnicodeCategory.NonSpacingMark)
                {
                    break;
                }

                letter--;
            }

            if (!CursiveScripts.JoinsNext(text, letter))
            {
                return null;
            }

            var extender = CursiveScripts.Extender(CodepointAt(text, letter));
            return extender == 0 ? null : ExtenderGlyph(font, extender);
        }

        bool IsOneWord(List<GlyphWordData[]> clusters, int lastInkCluster)
        {
            if (lineBreaks == null)
            {
                return false;
            }

            var positions = clusters.Take(lastInkCluster + 1).Where(IsInk).Select(cluster => cluster[0].PositionInString)
                .Where(position => position >= 0).ToArray();
            var start = positions.DefaultIfEmpty(-1).Min();
            return positions.All(position => position == start || position >= lineBreaks.Length
                                             || lineBreaks[position] == LineBreakKind.None);
        }

        static bool IsInk(GlyphWordData[] cluster) => cluster.Any(glyph => !IsBlank(glyph.Symbol) && glyph.Symbol != '\n');

        bool IsSpaced(GlyphWordData[] cluster)
        {
            var position = cluster[0].PositionInString;
            if (position < 0 || cluster.Sum(glyph => glyph.Advance) <= 0)
            {
                return false;
            }

            return !CursiveScripts.Contains(CodepointAt(text, position));
        }

        bool EndsParagraph(GlyphWordData[] line, int nextLineStart)
        {
            var last = -1;
            foreach (var glyph in line)
            {
                if (glyph.Symbol == '\n')
                {
                    return true;
                }

                last = Math.Max(last, glyph.PositionInString);
            }

            for (var c = last + 1; last >= 0 && c < nextLineStart; c++)
            {
                if (text[c] is '\n' or LineSeparator or ParagraphSeparator)
                {
                    return true;
                }
            }

            return false;
        }

        GlyphWordData LastTab(IReadOnlyList<GlyphWordData> glyphs, int from, int to, bool rightToLeft)
        {
            GlyphWordData tab = null;
            for (var k = from; k < to && !rightToLeft; k++)
            {
                if (glyphs[k].Symbol == '\t' && (tab == null || glyphs[k].PenX > tab.PenX))
                {
                    tab = glyphs[k];
                }
            }

            return tab;
        }

        bool PlacesWhatFollows(GlyphWordData tab) => tab != null && placingTabs?.Contains(tab) == true;

        static double AfterTab(GlyphWordData tab) => tab == null ? double.NegativeInfinity : tab.PenX + tab.Advance;

        static bool Aligned(GlyphWordData glyph, double from) => glyph.PenX > from - 1e-6;

        static double PastTab(GlyphWordData tab, double shift) => tab == null ? shift : Math.Max(shift, 0);

        static void Widen(GlyphWordData tab, double by)
        {
            if (tab != null)
            {
                tab.Advance += by;
                tab.Rect.Width += (float)by;
            }
        }

        // A line of spaces alone in a paragraph running right to left - an empty paragraph shown by one - has no ink to
        // align by and was left at the left: it stands where a line of text would, at its start, centered or at its end,
        // its caret with it.
        void PlaceBlankRightToLeftLines()
        {
            var lineStart = 0;
            while (lineStart < glyphsData.Count)
            {
                var line = glyphsData[lineStart].LineIndex;
                var lineEnd = lineStart;
                var blank = true;
                var left = double.MaxValue;
                var right = double.MinValue;
                while (lineEnd < glyphsData.Count && glyphsData[lineEnd].LineIndex == line)
                {
                    var glyph = glyphsData[lineEnd++];
                    blank &= glyph.Symbol == ' ';
                    left = Math.Min(left, glyph.PenX);
                    right = Math.Max(right, glyph.PenX + glyph.Advance);
                }

                if (blank && (LineState(line).RightToLeft
                              || (glyphsData[lineStart].PositionInString is >= 0 and var at && IsRightToLeftParagraph(at))))
                {
                    var alignment = AlignmentOf(line) == HorizontalTextAlignment.Justify
                        ? options.LastLineAlignment
                        : AlignmentOf(line);
                    var shift = alignment switch
                    {
                        HorizontalTextAlignment.Center => (LineIndent(line) + AlignRight(line) - left - right) / 2,
                        HorizontalTextAlignment.Right => LineIndent(line) - left,
                        _ => AlignRight(line) - right,
                    };
                    for (var k = lineStart; k < lineEnd; k++)
                    {
                        glyphsData[k].Rect.X += (float)shift;
                        glyphsData[k].PenX += shift;
                    }
                }

                lineStart = lineEnd;
            }
        }

        double StartRight(IEnumerable<GlyphWordData> line, double inkRight)
        {
            var firstInk = int.MaxValue;
            foreach (var glyph in line)
            {
                if (!IsBlank(glyph.Symbol) && glyph.PositionInString >= 0)
                {
                    firstInk = Math.Min(firstInk, glyph.PositionInString);
                }
            }

            foreach (var glyph in line)
            {
                if (IsBlank(glyph.Symbol) && glyph.PositionInString >= 0 && glyph.PositionInString < firstInk)
                {
                    inkRight = Math.Max(inkRight, glyph.PenX + glyph.Advance);
                }
            }

            return inkRight;
        }

        void AlignToEachLinesStart(HorizontalTextAlignment alignment)
        {
            var lineStart = 0;
            while (lineStart < glyphsData.Count)
            {
                var lineEnd = lineStart;
                while (lineEnd < glyphsData.Count && glyphsData[lineEnd].LineIndex == glyphsData[lineStart].LineIndex)
                {
                    lineEnd++;
                }

                var first = -1;
                for (var k = lineStart; k < lineEnd && first < 0; k++)
                {
                    first = glyphsData[k].PositionInString;
                }

                var rightToLeft = first >= 0 && (paragraphs.ParagraphAt(first).Paragraph.BaseLevel & 1) == 1;
                var toRight = rightToLeft == (alignment == HorizontalTextAlignment.Left);
                var line = glyphsData[lineStart].LineIndex;
                var tab = LastTab(glyphsData, lineStart, lineEnd, rightToLeft);
                var from = AfterTab(tab);
                var ink = Enumerable.Range(lineStart, lineEnd - lineStart).Select(k => glyphsData[k])
                    .Where(g => !IsBlank(g.Symbol) && Aligned(g, from)).ToArray();
                if (toRight && ink.Length > 0 && AlignmentOf(line) == alignment && !PlacesWhatFollows(tab))
                {
                    var right = ink.Max(RightOf);
                    if (rightToLeft)
                    {
                        right = StartRight(Enumerable.Range(lineStart, lineEnd - lineStart).Select(k => glyphsData[k])
                            .Where(g => Aligned(g, from)), right);
                    }

                    var shift = PastTab(tab, AlignRight(line) - right);
                    for (var k = lineStart; k < lineEnd; k++)
                    {
                        if (Aligned(glyphsData[k], from))
                        {
                            glyphsData[k].Rect.X += (float)shift;
                            glyphsData[k].PenX += shift;
                        }
                    }

                    Widen(tab, shift);
                }
                lineStart = lineEnd;
            }
        }

        void PlaceLines()
        {
            var mixed = false;
            var setHeights = new bool[lineCount];
            var ownBaselines = new double[lineCount];
            for (var i = 0; i < lineCount; i++)
            {
                var own = OwnLineHeight(i);
                var setsOwn = OwnSetHeight(i) is > 0 and var set && double.IsFinite(set);
                lineHeights[i] = own;
                lineBaselines[i] = setsOwn ? BaselineInLine(_font, scale) + (own - naturalHeight) / 2 : baseLine;
                ownBaselines[i] = lineBaselines[i];
                setHeights[i] = setsOwn || (options.LineHeight > 0 && double.IsFinite(options.LineHeight));
                mixed |= setsOwn;
            }

            var paragraphHeights = mixed;
            mixed |= rubies != null;
            foreach (var glyph in glyphsData)
            {
                mixed |= glyph.FontSize != fontSize || !ReferenceEquals(glyph.Font, _font)
                                                    || ObjectSizeOf(glyph.Symbol, glyph.Attributes) != null;
            }

            if (mixed && options.LineStacking == LineStackingStrategy.MaxHeight)
            {
                var below = new double[lineCount];
                for (var i = 0; i < lineCount; i++)
                {
                    below[i] = lineHeights[i] - lineBaselines[i];
                }

                if (vertical && rubies != null)
                {
                    lineCoreAbove = (double[])lineBaselines.Clone();
                    lineCoreBelow = (double[])below.Clone();
                }

                foreach (var glyph in glyphsData)
                {
                    var line = glyph.LineIndex;
                    var extent = GlyphExtent(glyph, setHeights[line], ownBaselines[line], lineHeights[line]);
                    lineBaselines[line] = Math.Max(lineBaselines[line], extent.Above);
                    below[line] = Math.Max(below[line], extent.Below);
                    if (lineCoreAbove != null)
                    {
                        var core = GlyphExtent(glyph, setHeights[line], ownBaselines[line], lineHeights[line], false);
                        lineCoreAbove[line] = Math.Max(lineCoreAbove[line], core.Above);
                        lineCoreBelow[line] = Math.Max(lineCoreBelow[line], core.Below);
                    }
                }

                for (var i = 0; i < lineCount; i++)
                {
                    lineHeights[i] = lineBaselines[i] + below[i];
                }
            }
            else
            {
                mixed = paragraphHeights;
            }

            for (var i = 0; i < lineCount; i++)
            {
                var top = i == 0 ? textArea.Y : lineTops[i - 1] + lineHeights[i - 1] + SpacingAfter(i - 1) + LineGap(i);
                lineTops[i] = OnGrid(top, lineBaselines[i], i);
            }

            mixed |= grid != null;

            var aligned = false;
            foreach (var glyph in glyphsData)
            {
                if (AlignsToLine(glyph.Attributes) && !IsBlank(glyph.Symbol) && glyph.Symbol != '\n')
                {
                    glyph.OffsetY += LineRaise(glyph.Attributes, glyph.Symbol, lineBaselines[glyph.LineIndex],
                        lineHeights[glyph.LineIndex], glyph.Font, glyph.FontSize);
                    aligned = true;
                }
            }

            if (!mixed && !aligned)
            {
                return;
            }

            foreach (var glyph in glyphsData)
            {
                if (!mixed && !AlignsToLine(glyph.Attributes))
                {
                    continue;
                }

                var lineBase = lineTops[glyph.LineIndex] + lineBaselines[glyph.LineIndex];
                if (IsBlank(glyph.Symbol) || glyph.Symbol == '\n')
                {
                    glyph.Rect.Y = (float)lineBase;
                }
                else if (ObjectSizeOf(glyph.Symbol, glyph.Attributes) is { } objectSize)
                {
                    glyph.Rect = ObjectRect(glyph.PenX, lineBase - glyph.OffsetY, objectSize);
                }
                else if (glyph.Upright)
                {
                    glyph.Rect = CellRect(glyph.Font, glyph.FontSize, glyph.PenX, glyph.Advance - glyph.Tail, lineBase);
                }
                else
                {
                    glyph.Rect = Widened(CalculateGlyphPosition(glyph.Font, glyph.Glyph, glyph.PenX + glyph.OffsetX,
                        lineBase - glyph.OffsetY, glyph.FontSize / glyph.Font.UnitsPerEm), glyph.PenX + glyph.OffsetX,
                        glyph.HorizontalScale);
                }
            }
        }

        // The last line ends on its baseline; the first starts at the top of its tallest capitals, or of an object or an
        // annotation reaching higher, and every glyph moves up with it - by whole units, so the letters stay as sharp. A
        // text on a baseline grid keeps its first line whole.
        void TrimLineBounds()
        {
            var last = lineCount - 1;
            if (options.LineBounds is TextLineBounds.TrimToBaseline or TextLineBounds.Tight)
            {
                lineHeights[last] = Math.Min(lineHeights[last], lineBaselines[last]);
            }

            if (options.LineBounds is not (TextLineBounds.TrimToCapHeight or TextLineBounds.Tight) || grid != null)
            {
                return;
            }

            var cap = CapHeight(_font, fontSize);
            foreach (var glyph in glyphsData)
            {
                if (glyph.LineIndex != 0 || IsBlank(glyph.Symbol) || glyph.Symbol == '\n')
                {
                    continue;
                }

                var reach = ObjectSizeOf(glyph.Symbol, glyph.Attributes) is { } objectSize
                    ? objectSize.Height + glyph.OffsetY
                    : CapHeight(glyph.Font, glyph.FontSize) + glyph.OffsetY;
                foreach (var r in rubies == null ? [] : new[] { RubyIndex(glyph.PositionInString), OtherRuby(glyph.PositionInString) })
                {
                    if (r >= 0 && rubies[r].Annotation is { Position: RubyPosition.Over } && !rubies[r].Pending
                        && (rubyShown == null || rubyShown[r]))
                    {
                        reach = Math.Max(reach, BaseReach(glyph, true) + rubies[r].Ascent + rubies[r].Descent);
                    }
                }

                cap = Math.Max(cap, reach);
            }

            var trim = Math.Floor(lineBaselines[0] - cap);
            if (trim <= 0)
            {
                return;
            }

            lineBaselines[0] -= trim;
            lineHeights[0] -= trim;
            for (var i = 1; i < lineCount; i++)
            {
                lineTops[i] -= trim;
            }

            foreach (var glyph in glyphsData)
            {
                glyph.Rect.Y -= (float)trim;
            }

            static double CapHeight(IFont font, double size) =>
                (font.CapsHeight > 0 ? font.CapsHeight : font.Ascender * 0.7) * size / font.UnitsPerEm;
        }

        // Moves the glyphs past the right edge of the line they left onto the new line.
        void RearrangeData(GlyphWordData[] glyphsDataCopy, double glyphBase, double right)
        {
            var rearrangeList = new List<GlyphWordData>();
            for (int k = glyphsDataCopy.Length - 1; k >= 0; k--)
            {
                var data = glyphsData[k];
                cursorPosition -= data.Advance;
                var wordsLeft = glyphsDataCopy.Take(k).Count(x => IsBlank(x.Symbol)) + 1;
                rearrangeList.Add(data);
                if (wordIndex > 0 &&
                    cursorPosition <= right &&
                    renderingParameters.TextWrapping == TextWrapping.WrapByWords &&
                    IsBlank(data.Symbol))
                {
                    break;
                }
                else if (cursorPosition <= right &&
                         renderingParameters.TextWrapping == TextWrapping.WrapBySymbols &&
                         (k == 0 || !SameBase(glyphsData[k - 1].PositionInString, data.PositionInString)))
                {
                    break;
                }
                else if (wordsLeft == 1 &&
                         cursorPosition > right &&
                         renderingParameters.TextWrapping == TextWrapping.WrapByWords)
                {
                    break;
                }
            }

            rearrangeList.Reverse();
            cursorPosition = LineIndent(lineIndex);

            for (var index = 0; index < rearrangeList.Count; index++)
            {
                var glyphData = rearrangeList[index];
                if (index == 0 && IsBlank(glyphData.Symbol)) continue;

                var glyphRect = ObjectSizeOf(glyphData.Symbol, glyphData.Attributes) is { } objectSize
                    ? ObjectRect(cursorPosition, glyphBase - glyphData.OffsetY, objectSize)
                    : glyphData.Upright
                    ? CellRect(glyphData.Font, glyphData.FontSize, cursorPosition, glyphData.Advance - glyphData.Tail,
                        glyphBase)
                    : Widened(CalculateGlyphPosition(glyphData.Font, glyphData.Glyph,
                        cursorPosition + glyphData.OffsetX,
                        glyphBase - glyphData.OffsetY,
                        glyphData.FontSize / glyphData.Font.UnitsPerEm), cursorPosition + glyphData.OffsetX,
                        glyphData.HorizontalScale);

                glyphData.Rect = glyphRect;
                glyphData.PenX = cursorPosition;
                glyphData.LineIndex = lineIndex;
                cursorPosition += glyphData.Advance;
            }
        }

        void PrepareDataAndTrim(int count, int floor, int position, double glyphBase, TextTrimming trimming)
        {
            var right = LineRight(lineIndex);
            for (int k = count - 1; k >= floor; k--)
            {
                var data = glyphsData[k];
                cursorPosition -= data.Advance;
                glyphsData.RemoveAt(k);
                if (trimming == TextTrimming.None &&
                    cursorPosition <= right)
                {
                    break;
                }
                else if (trimming == TextTrimming.CharEllipses &&
                    cursorPosition + EllipsisWidth() <= right && FitsTrimRoom())
                {
                    break;
                }
                else if (trimming ==
                         TextTrimming.WordEllipses &&
                         cursorPosition + EllipsisWidth() <= right && FitsTrimRoom())
                {
                    if (wordIndex > 0 && !IsBlank(data.Symbol))
                    {
                        continue;
                    }
                    else
                    {
                        break;
                    }
                }
            }

            if (trimming != TextTrimming.None)
            {
                TrimText(position, glyphBase);
            }

            width = cursorPosition;
        }

        void TrimText(int position, double glyphBase)
        {
            var style = EllipsisStyle();
            var (font, dot, size) = EllipsisFont(style);
            var dotScale = size / font.UnitsPerEm;
            var dotAdvance = font.GetAdvanceWidth(dot.Index) * dotScale * options.GlyphScaling.Desired;
            var shift = style is { Upright: false } ? style.OffsetY : 0;
            for (int j = 0; j < 3; j++)
            {
                var glyphRect = Widened(CalculateGlyphPosition(font, dot,
                    cursorPosition,
                    glyphBase - shift,
                    dotScale), cursorPosition, options.GlyphScaling.Desired);
                glyphsData.Add(new GlyphWordData(dot, '.', glyphRect, -1, lineIndex)
                {
                    PenX = cursorPosition,
                    Advance = dotAdvance,
                    OffsetY = shift,
                    Attributes = style?.Attributes,
                    Font = font,
                    FontSize = size,
                    HorizontalScale = options.GlyphScaling.Desired,
                });

                cursorPosition += dotAdvance;
            }
        }

        // The ellipsis is set as the text it follows: in its font and size (the text's own font and size when that one
        // has no full stop, which never raises the line), its colors and decorations, and its baseline shift. After upright letters of vertical text, whose
        // size and offsets set them in their column, it takes only their colors and decorations.
        GlyphWordData EllipsisStyle()
        {
            for (var k = glyphsData.Count - 1; k >= 0 && glyphsData[k].LineIndex == lineIndex; k--)
            {
                var glyph = glyphsData[k];
                if (glyph.PositionInString >= 0 && !IsBlank(glyph.Symbol) && glyph.Symbol != '\n'
                    && ObjectSizeOf(glyph.Symbol, glyph.Attributes) == null)
                {
                    return glyph;
                }
            }

            return null;
        }

        (IFont Font, Glyph Dot, double Size) EllipsisFont(GlyphWordData style)
        {
            if (style is not { Upright: false })
            {
                return (_font, _dotGlyph, fontSize);
            }

            return style.Font.TryGetGlyphIndex('.', out var dot)
                ? (style.Font, style.Font.GetGlyphByIndex(dot), style.FontSize)
                : (_font, _dotGlyph, fontSize);
        }

        double EllipsisWidth()
        {
            var (font, dot, size) = EllipsisFont(EllipsisStyle());
            return font.GetAdvanceWidth(dot.Index) * size / font.UnitsPerEm * 3 * options.GlyphScaling.Desired;
        }
    }

    private static double Hang(GlyphWordData glyph, bool left)
    {
        if ((glyph.PositionInString < 0 && glyph.Symbol != '-') || !MarginHangs.TryGetValue(glyph.Symbol, out var hang))
        {
            return 0;
        }

        return (left ? hang.Left : hang.Right) * glyph.Font.GetAdvanceWidth(glyph.Glyph.Index) * glyph.FontSize
               / glyph.Font.UnitsPerEm * glyph.HorizontalScale;
    }

    private static bool IsWordLetter(string text, int index, int start, int end)
    {
        var symbol = text[index];
        if (symbol is '\'' or '’')
        {
            return index > start && index + 1 < end && char.IsLetter(text[index - 1]) && char.IsLetter(text[index + 1]);
        }

        return char.IsLetter(symbol) || CharUnicodeInfo.GetUnicodeCategory(symbol) is UnicodeCategory.NonSpacingMark
            or UnicodeCategory.SpacingCombiningMark;
    }

    private static Glyph ExtenderGlyph(IFont font, int codepoint)
    {
        foreach (var extender in codepoint == 0x0640 ? font.GetJustificationExtenders("arab") : [])
        {
            if (font.GetAdvanceWidth(extender) > 0)
            {
                return font.GetGlyphByIndex(extender);
            }
        }

        return font.TryGetGlyphIndex(codepoint, out var glyph) && font.GetAdvanceWidth(glyph) > 0
            ? font.GetGlyphByIndex(glyph)
            : null;
    }

    private static GlyphWordData Extender(GlyphWordData letter, Glyph glyph, double pen, double width)
    {
        var scale = letter.FontSize / letter.Font.UnitsPerEm;
        var ink = glyph.BoundingRectangle.Width * scale;
        var natural = ink > 0 ? ink : letter.Font.GetAdvanceWidth(glyph.Index) * scale;
        var widthScale = width / natural;
        var baseline = Baseline(letter);
        var extender = new GlyphWordData(glyph, 'ـ', default, -1, letter.LineIndex)
        {
            PenX = ink > 0 ? pen - letter.Font.GetLeftSideBearing(glyph.Index) * scale * widthScale : pen,
            Font = letter.Font,
            FontSize = letter.FontSize,
            Attributes = letter.Attributes,
            HorizontalScale = widthScale,
        };
        Relay(extender, baseline);
        return extender;
    }

    private static double FontAdvance(GlyphWordData glyph) =>
        glyph.Font.GetAdvanceWidth(glyph.Glyph.Index) * glyph.FontSize / glyph.Font.UnitsPerEm;

    private static double ChooseAlternates(string text, string language, List<GlyphWordData[]> clusters, int from,
        int to, double room, Glyph[] chosen)
    {
        List<(int Cluster, Glyph Glyph, double Gain)> candidates = [];
        for (var c = from; c <= to; c++)
        {
            var glyph = clusters[c][0];
            if (clusters[c].Length != 1 || glyph.Upright || IsBlank(glyph.Symbol) || glyph.Symbol == '\n'
                || glyph.Attributes?.Features?.Any(feature => feature.Tag == "jalt" && feature.Value == 0) == true)
            {
                continue;
            }

            var current = FontAdvance(glyph);
            var codepoint = glyph.PositionInString >= 0 ? CodepointAt(text, glyph.PositionInString) : glyph.Symbol;
            foreach (var alternate in glyph.Font.GetGlyphAlternates(glyph.Glyph.Index, "jalt", codepoint,
                         glyph.Attributes?.Language ?? language))
            {
                var gain = (glyph.Font.GetAdvanceWidth(alternate) * glyph.FontSize / glyph.Font.UnitsPerEm - current)
                           * glyph.HorizontalScale;
                if (gain > 0)
                {
                    candidates.Add((c, glyph.Font.GetGlyphByIndex(alternate), gain));
                    break;
                }
            }
        }

        double taken = 0;
        foreach (var candidate in candidates.OrderByDescending(candidate => clusters[candidate.Cluster][0].PositionInString))
        {
            if (taken + candidate.Gain <= room)
            {
                chosen[candidate.Cluster] = candidate.Glyph;
                taken += candidate.Gain;
            }
        }

        return taken;
    }

    private static double TakeAlternate(GlyphWordData glyph, Glyph alternate)
    {
        var baseline = Baseline(glyph);
        var before = FontAdvance(glyph);
        glyph.Glyph = alternate;
        var gain = (FontAdvance(glyph) - before) * glyph.HorizontalScale;
        glyph.Advance += gain;
        Relay(glyph, baseline);
        return gain;
    }

    private static double Scale(GlyphWordData[] cluster, double added)
    {
        var before = cluster.Sum(glyph => glyph.Advance);
        if (before <= 0)
        {
            return 0;
        }

        var ratio = 1 + added / before;
        var origin = cluster.Min(glyph => glyph.PenX);
        foreach (var glyph in cluster)
        {
            var baseline = Baseline(glyph);
            glyph.PenX = origin + (glyph.PenX - origin) * ratio;
            glyph.OffsetX *= ratio;
            glyph.Advance *= ratio;
            glyph.HorizontalScale *= ratio;
            Relay(glyph, baseline);
        }

        return cluster.Sum(glyph => glyph.Advance) - before;
    }
}
