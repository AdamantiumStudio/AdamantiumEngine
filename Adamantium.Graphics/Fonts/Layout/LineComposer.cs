using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
        var laidOutParameters = renderingParameters;

        var _b0 = System.GC.GetAllocatedBytesForCurrentThread();


        var _b1 = System.GC.GetAllocatedBytesForCurrentThread();

        var scale = fontSize / _font.UnitsPerEm;
        var vertical = options.Vertical;
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
                TextArea = new Rectangle(0, 0, screenArea.Height, screenArea.Width),
                TextWrapping = renderingParameters.TextWrapping,
                TextTrimming = renderingParameters.TextTrimming,
                HorizontalTextAlignment = renderingParameters.HorizontalTextAlignment,
                VerticalTextAlignment = renderingParameters.VerticalTextAlignment,
                JustifyLastLine = renderingParameters.JustifyLastLine,
                Color = renderingParameters.Color,
            };
        }

        var oversetIndex = text.Length;
        var tabStops = options.TabStops.OrderBy(stop => stop.Position).ToArray();
        var paragraphs = TextParagraphs.Resolve(text, options.Direction);
        var dropEnd = options.DropCap != null && renderingParameters.TextWrapping == TextWrapping.WrapByWords
                                              && !TextParagraphs.IsRightToLeftParagraph(paragraphs, text, options.Direction, 0)
                                              && !vertical
                                              && (options.MaxLines <= 0 || options.MaxLines >= options.DropCap.Lines)
            ? DropCapEnd(text, options.DropCap)
            : 0;
        var graphemes = options.Fallback == null ? null : TextBoundaries.Graphemes(text);
        var items = _shaper.Shape(text, attributed, fontSize, dropEnd, vertical, options, paragraphs, graphemes,
            out var hasPendingFonts);
        var tabStop = _font.GetAdvanceWidth(_spaceGlyph.Index) * scale * Math.Max(options.TabSize, 1);

        var _b2 = System.GC.GetAllocatedBytesForCurrentThread();

        var naturalHeight = LineAdvance(_font, fontSize);
        var lineHeight = options.LineHeight > 0 && double.IsFinite(options.LineHeight) ? options.LineHeight : naturalHeight;
        var baseLine = BaselineInLine(_font, scale) + (lineHeight - naturalHeight) / 2;
        var lineSpacing = double.IsFinite(options.LineSpacing) ? Math.Max(options.LineSpacing, 1 - lineHeight) : 0;
        var linePitch = lineHeight + lineSpacing;

        var dotGlyphsWidth = _font.GetAdvanceWidth(_dotGlyph.Index) * scale * 3 * options.GlyphScaling.Desired;

        var textArea = renderingParameters.TextArea;
        double width = 0;
        double height = textArea.Y;
        double cursorPosition = 0;
        double wordStartPosition = 0;
        var glyphsData = new List<GlyphWordData>();
        var leaders = new Dictionary<GlyphWordData, string>();
        int wordIndex = 0;
        int lineIndex = 0;
        double verticalShift = 0;
        Size? realTextDimensions = null;
        int[] laidOutLineFrames = null;
        var lineBreaks = renderingParameters.TextWrapping == TextWrapping.WrapByWords
            ? TextBoundaries.LineBreaks(text)
            : null;
        var slots = lineBreaks != null && frames is { Count: > 0 }
            ? LineSlots(frames, exclusions, lineHeight, lineSpacing, 2 * fontSize, text.Length + 1)
            : null;
        var frameRects = slots != null ? frames.ToArray() : null;
        if (slots is { Count: 0 })
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
                RealTextDimensions = Size.Zero,
                HasPendingFonts = hasPendingFonts,
                NoRoom = true,
                DotGlyph = _dotGlyph,
            };
        }

        for (var i = 1; lineBreaks != null && i < text.Length; i++)
        {
            if (text[i - 1] == SoftHyphen && lineBreaks[i] == LineBreakKind.Allowed)
            {
                lineBreaks[i] = LineBreakKind.None;
            }
        }

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

        cursorPosition = LineIndent(0);
        if (lineBreaks != null && options.LineBreaking == LineBreaking.Paragraph
                               && (slots != null || textArea.Width < Int32.MaxValue))
        {
            Compose();
        }

        var wordStart = dropItems;
        while (wordStart <= items.Count)
        {
            var wordEnd = WordEnd(wordStart);
            if (!ProcessWord(wordStart, wordEnd))
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
                var stop = isTab ? NextTabStop(cursorPosition) : default;
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

                cursorPosition += advance;
            }

            wordIndex++;
            wordStart = wordEnd + 1;
        }

        paragraphs?.ReorderLines(glyphsData);

        // Not published yet: the render thread reads the laid-out text, and the alignment below still moves every glyph.
        // The height is a font metric (the bottom of the last line, its descent included), not the ink, so same-size
        // strings measure alike and a turned label keeps its descenders. A line is as tall as its largest text.
        var lineCount = Math.Max(lineIndex, glyphsData.Count > 0 ? glyphsData.Max(x => x.LineIndex) : 0) + 1;
        var lineTops = new double[lineCount];
        var lineHeights = new double[lineCount];
        var lineBaselines = new double[lineCount];
        var lineAscents = new double[lineCount];
        PlaceLines();
        var lastBaseline = lineTops[lineCount - 1] + lineBaselines[lineCount - 1];
        height = lineTops[lineCount - 1] + lineHeights[lineCount - 1];
        if (dropGlyphs != null)
        {
            lastBaseline = Math.Max(lastBaseline, DropBaseline());
            height = Math.Max(height, DropBaseline() + lineHeight - baseLine);
        }

        var _b3 = System.GC.GetAllocatedBytesForCurrentThread();


        if (glyphsData.Count > 0)
        {
            CalculateRealTextDimensions(glyphsData);
        }

        var maxX = Math.Max(glyphsData.Count > 0 ? glyphsData.Max(InkRight) : 0, dropIndent);
        var finalRect = new Size(Math.Ceiling(maxX), Math.Ceiling(height));
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
            finalRect = new Size(Math.Ceiling(frames.Max(frame => frame.Right)), Math.Ceiling(frames.Max(frame => frame.Bottom)));
        }

        if (glyphsData.Count > 0)
        {
            ArrangeText();
        }

        for (var i = 0; i < lineCount; i++)
        {
            lineTops[i] += verticalShift;
        }

        if (slots != null)
        {
            PlaceInFrames();
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
            CalculateRealTextDimensions(glyphsData);
        }

        if (leaders.Count > 0)
        {
            glyphsData = WithLeaders(glyphsData, leaders,
                slots == null && textArea.Width < Int32.MaxValue ? textArea.Width : double.MaxValue);
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
            FontSize = fontSize,
            Font = _font,
            Options = options,
            RenderingParameters = laidOutParameters,
            Glyphs = glyphsData,
            LineTops = lineTops,
            LineHeights = lineHeights,
            LineBaselines = lineBaselines,
            VerticalShift = verticalShift,
            LineSpacing = lineSpacing,
            ColumnsRight = columnsRight,
            LineFrames = laidOutLineFrames,
            FrameRects = frameRects,
            Paragraphs = paragraphs,
            Graphemes = graphemes,
            OversetIndex = oversetIndex,
            Size = finalRect,
            RealTextDimensions = realTextDimensions,
            HasPendingFonts = hasPendingFonts,
            DotGlyph = _dotGlyph,
        };

        double Raise(TextAttributes attributes, IFont font, double size, double baseSize) =>
            TextGeometry.Raise(_font, options.WritingMode, attributes, font, size, baseSize);

        double LineRaise(TextAttributes attributes, char symbol, double lineBaseline, double lineBox, IFont font,
            double size) =>
            TextGeometry.LineRaise(options.WritingMode, attributes, symbol, lineBaseline, lineBox, font, size);

        bool IsRightToLeftParagraph(int index) =>
            TextParagraphs.IsRightToLeftParagraph(paragraphs, text, options.Direction, index);

        void CalculateRealTextDimensions(List<GlyphWordData> glyphs)
        {
            var minX = glyphs.Min(InkLeft);
            var maxInk = glyphs.Max(InkRight);
            var minY = glyphs.Min(x => x.Rect.Top);
            var maxY = glyphs.Max(x => x.Rect.Bottom);
            realTextDimensions = new Size(maxInk - minX, maxY - minY);
        }

        TabStop NextTabStop(double x)
        {
            foreach (var stop in tabStops)
            {
                if (stop.Position > x + 1e-6)
                {
                    return stop;
                }
            }

            return new TabStop((Math.Floor(x / tabStop) + 1) * tabStop);
        }

        double TabAdvance(TabStop stop, int tab)
        {
            var advance = stop.Position - cursorPosition;
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

        int WordEnd(int start)
        {
            var end = start;
            while (end < items.Count && !IsBlank(items[end].Symbol)
                   && (end == start || lineBreaks == null || lineBreaks[items[end].Cluster] == LineBreakKind.None))
            {
                end++;
            }

            return end;
        }

        double DropBaseline() => dropLines - 1 < lineTops.Length
            ? lineTops[dropLines - 1] + lineBaselines[dropLines - 1]
            : lineTops[lineTops.Length - 1] + lineBaselines[lineTops.Length - 1] + (dropLines - lineTops.Length) * linePitch;

        List<GlyphWordData> DropCapGlyphs()
        {
            var attributes = items[0].Attributes;
            var shift = attributes?.BaselineShift ?? 0;
            var baseline = DropBaseline() - shift;
            List<GlyphWordData> glyphs = [];
            double pen = slots != null ? frames[slots[0].Frame].Left + slots[0].Left : 0;
            foreach (var shaped in dropGlyphs)
            {
                var glyph = dropFont.GetGlyphByIndex(shaped.GlyphIndex);
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

            return glyphs;
        }

        void SetUpDropCap()
        {
            var dropCap = options.DropCap;
            if (slots != null)
            {
                if (slots.Count < dropCap.Lines)
                {
                    return;
                }

                for (var line = 1; line < dropCap.Lines; line++)
                {
                    if (slots[line].Frame != slots[0].Frame || Math.Abs(slots[line].Top - slots[line - 1].Top - linePitch) > 0.5)
                    {
                        return;
                    }
                }
            }

            var first = items[0];
            var shapingOptions = new ShapingOptions(null, first.Attributes?.Language ?? options.Language,
                first.Attributes?.Features);
            var font = dropCap.Font ?? first.Font;
            var shaped = TextShaper.Shape(font, text, 0, dropEnd, shapingOptions);
            if (!ReferenceEquals(font, first.Font) && shaped.Any(glyph => glyph.GlyphIndex == 0))
            {
                font = first.Font;
                shaped = TextShaper.Shape(font, text, 0, dropEnd, shapingOptions);
            }

            double top = 0;
            double pen = 0;
            double inkRight = 0;
            foreach (var glyph in shaped)
            {
                var bounds = font.GetGlyphByIndex(glyph.GlyphIndex).BoundingRectangle;
                top = Math.Max(top, bounds.Y + bounds.Height);
                if (bounds.Width > 0)
                {
                    inkRight = Math.Max(inkRight,
                        pen + glyph.XOffset + font.GetLeftSideBearing(glyph.GlyphIndex) + bounds.Width);
                }

                pen += glyph.XAdvance;
            }

            var bodyScale = first.FontSize / first.Font.UnitsPerEm;
            var cap = (first.Font.CapsHeight > 0 ? first.Font.CapsHeight : first.Font.Ascender * 0.7) * bodyScale;
            if (top <= 0)
            {
                return;
            }

            var capScale = (cap + (dropCap.Lines - 1) * linePitch) / top;
            var indent = Math.Max(pen * capScale, inkRight * capScale + 0.1 * first.FontSize);
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
        }

        void NewLine()
        {
            lineIndex++;
            height += linePitch;
            cursorPosition = LineIndent(lineIndex);
        }

        double LineIndent(int line) =>
            (slots != null ? slots[Math.Min(line, slots.Count - 1)].Left : 0) + (line < dropLines ? dropIndent : 0);

        double LineRight(int line) => slots != null ? slots[Math.Min(line, slots.Count - 1)].Right : textArea.Width;

        bool LineHasContent() => cursorPosition > LineIndent(lineIndex) + 1e-9;

        bool HasRoomBelow() => (options.MaxLines <= 0 || lineIndex + 1 < options.MaxLines)
                               && (slots != null ? lineIndex + 1 < slots.Count : height + linePitch < textArea.Height);

        bool CutsOverflow() => slots != null || options.MaxLines > 0;

        void EndInEllipsis(TextTrimming trimming)
        {
            var floor = LineFirstGlyph();
            if (cursorPosition + dotGlyphsWidth <= LineRight(lineIndex))
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
            if (composedStarts.Contains(start) && LineHasContent())
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
            while (firstCut < newline && !composedCuts.Contains(firstCut))
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
                if (!composedCuts.Contains(k) || k == passedCut)
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

            wordStartPosition = cursorPosition;
            if (trims && !LineHasContent() && cursorPosition + WordWidth(start, newline) > LineRight(lineIndex))
            {
                var lastLine = !HasRoomBelow();
                LayItems(start, newline);
                EndInEllipsis(TextTrimming.CharEllipses);
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
            var start = dropItems;
            while (start < items.Count)
            {
                var end = WordEnd(start);
                var newline = end > start && items[end - 1].Symbol == '\n' ? end - 1 : end;
                List<int> points = options.Hyphens == Hyphens.None ? [] : HyphenationPoints(start, newline);
                var automatic = options.Hyphens == Hyphens.Auto && !HasSoftHyphen(start, newline);
                var piece = start;
                foreach (var point in points)
                {
                    AddWordPiece(paragraph, piece, point, justified, false);
                    paragraph.Add((ComposerItem.Penalty(HyphenAdvance(items[point - 1]), 50, true), point, true, automatic));
                    piece = point;
                }

                var lastLetter = 0.0;
                if (newline > piece)
                {
                    lastLetter = AddWordPiece(paragraph, piece, newline, justified, true);
                }

                if (newline < end || end >= items.Count)
                {
                    ComposeParagraph(paragraph, justified);
                    paragraph.Clear();
                    start = end;
                    continue;
                }

                var next = end;
                while (next < items.Count && IsBlank(items[next].Symbol))
                {
                    next++;
                }

                if (next == end)
                {
                    var hyphen = items[end - 1].Symbol is '-' or '‐';
                    AddBreak(paragraph, next, ComposerItem.Penalty(0, hyphen ? 50 : 0, hyphen), justified, raggedStretch);
                }
                else
                {
                    AddBreak(paragraph, next, WordGlue(end, lastLetter), justified, raggedStretch);
                    for (var k = end + 1; k < next; k++)
                    {
                        var glue = WordGlue(k, 0);
                        paragraph.Add((justified ? glue : ComposerItem.Glue(glue.Width, 0, 0), next, false, false));
                    }
                }

                start = next;
            }

            if (paragraph.Count > 0)
            {
                ComposeParagraph(paragraph, justified);
            }
        }

        void ComposeParagraph(List<(ComposerItem Item, int Next, bool Cut, bool Automatic)> paragraph, bool justified)
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

            List<double> widths = [];
            var distinct = Math.Min(Math.Max(dropLines, slots?.Count ?? 0), composedLines + paragraph.Count + 1);
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
            var exact = paragraph.Where(entry => !entry.Automatic).ToList();
            var chosen = exact;
            var breaks = ParagraphComposer.Break(exact.Select(entry => entry.Item).ToList(), widths, 100);
            if (breaks == null)
            {
                chosen = paragraph;
                var all = paragraph.Select(entry => entry.Item).ToList();
                breaks = ParagraphComposer.Break(all, widths, 200)
                         ?? ParagraphComposer.BreakAnyway(all, widths, 2 * fontSize);
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
            paragraph.Add((ComposerItem.Penalty(0, gap.Kind == ComposerItemKind.Penalty ? gap.Cost : 0,
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
            if (wrapsByWords && options.Hyphens != Hyphens.None && Overflows(wordWidth))
            {
                var rest = HyphenateWord(start, end);
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
            if (wrapsByWords
                && wordIndex > 0
                && LineHasContent()
                && Overflows(wordWidth))
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

            wordStartPosition = cursorPosition;
            if (wrapsByWords
                && renderingParameters.TextTrimming != TextTrimming.None
                && Overflows(wordWidth))
            {
                var newline = end > start && items[end - 1].Symbol == '\n' ? end - 1 : end;
                var lastLine = !HasRoomBelow();
                var trimming = LineHasContent() ? renderingParameters.TextTrimming : TextTrimming.CharEllipses;
                LayItems(start, newline);
                EndInEllipsis(trimming);
                return !lastLine && LayItems(newline, end);
            }

            return LayItems(start, end);
        }

        bool JustifiesLastLine() =>
            renderingParameters.JustifyLastLine || options.LastLineAlignment == HorizontalTextAlignment.Justify;

        bool Overflows(double width) =>
            cursorPosition + width > LineRight(lineIndex) && cursorPosition + width > LineRight(lineIndex) + LineShrink();

        double AlignRight(int line) => slots != null ? LineRight(line) : finalRect.Width;

        void PlaceInFrames()
        {
            var lineFrames = new int[lineCount];
            var shiftsX = new double[lineCount];
            var shiftsY = new double[lineCount];
            for (var i = 0; i < lineCount; i++)
            {
                var slot = slots[Math.Min(i, slots.Count - 1)];
                lineFrames[i] = slot.Frame;
                shiftsX[i] = frames[slot.Frame].Left;
                shiftsY[i] = slot.Top - lineTops[i];
                lineTops[i] = slot.Top;
            }

            foreach (var glyph in glyphsData)
            {
                glyph.Rect.X += (float)shiftsX[glyph.LineIndex];
                glyph.Rect.Y += (float)shiftsY[glyph.LineIndex];
                glyph.PenX += shiftsX[glyph.LineIndex];
            }

            laidOutLineFrames = lineFrames;
        }

        RectangleF AlongColumns(RectangleF rect) =>
            new(rect.Y, (float)(columnsRight - rect.Right), rect.Height, rect.Width);

        void TurnIntoColumns()
        {
            if (slots == null)
            {
                columnsRight = finalRect.Height;
            }

            var right = columnsRight;
            foreach (var glyph in glyphsData)
            {
                var line = Math.Min(glyph.LineIndex, lineCount - 1);
                var axis = lineTops[line] + lineHeights[line] / 2;
                var glyphScale = glyph.FontSize / glyph.Font.UnitsPerEm;
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

            finalRect = new Size(Math.Ceiling(slots != null ? options.Frames.Max(frame => frame.Right) : right),
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
            if (renderingParameters.HorizontalTextAlignment != HorizontalTextAlignment.Justify)
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

        int HyphenateWord(int start, int end)
        {
            var newline = start;
            while (newline < end && items[newline].Symbol != '\n')
            {
                newline++;
            }

            List<int> points = null;
            while (Overflows(WordWidth(start, newline))
                   && (HasRoomBelow() || (CutsOverflow() && renderingParameters.TextTrimming == TextTrimming.None)))
            {
                points ??= HyphenationPoints(start, newline);
                var cut = -1;
                var pen = cursorPosition;
                var k = start;
                var room = LineRight(lineIndex) + LineShrink();
                foreach (var point in points)
                {
                    for (; k < point; k++)
                    {
                        pen += items[k].Advance;
                    }

                    if (point > start && pen + HyphenInk(items[point - 1]) <= room)
                    {
                        cut = point;
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

                LayItems(start, cut);
                AddHyphen(items[cut - 1]);
                if (!HasRoomBelow())
                {
                    Overset(cut);
                    return -1;
                }

                NewLine();
                start = cut;
            }

            return start;
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

            for (var k = start + 1; k < end; k++)
            {
                if (items[k].Cluster != items[k - 1].Cluster && breaks.Contains(items[k].Cluster))
                {
                    points.Add(k);
                }
            }

            return points;
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
                        if (renderingParameters.TextWrapping == TextWrapping.WrapByWords
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
                        var glyphBase = height + baseLine;
                        var glyphRect = ObjectSizeOf(item.Symbol, item.Attributes) is { } objectSize
                            ? ObjectRect(cursorPosition, glyphBase - item.OffsetY, objectSize)
                            : item.Upright
                            ? CellRect(item.Font, item.FontSize, cursorPosition, item.Advance, glyphBase)
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
                        });
                        cursorPosition += item.Advance;

                        switch (renderingParameters.TextWrapping)
                        {
                            case TextWrapping.NoWrap:
                                if (cursorPosition > textArea.Width)
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
                                    if (cursorPosition > textArea.Width)
                                    {
                                        var glyphsDataCopy = glyphsData.ToArray();
                                        if (HasRoomBelow())
                                        {
                                            lineIndex++;
                                            height += linePitch;
                                            glyphBase = height + baseLine;
                                            RearrangeData(glyphsDataCopy, glyphBase);
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
            var minX = glyphsData.Min(x => x.Rect.Left);
            var maxX = glyphsData.Max(x => x.Rect.Right);
            var minY = glyphsData.Min(x => x.Rect.Top);
            switch (renderingParameters.HorizontalTextAlignment)
            {
                case HorizontalTextAlignment.Left or HorizontalTextAlignment.Right when paragraphs != null:
                    AlignToEachLinesStart();
                    break;
                case HorizontalTextAlignment.Center:
                {
                    var maxLines = glyphsData.Max(x => x.LineIndex);
                    for (int i = 0; i <= maxLines; ++i)
                    {
                        var glyphsForLine = glyphsData.Where(x => x.LineIndex == i).ToArray();
                        if (glyphsForLine.Length == 0) break;

                        // By the ink, so leading/trailing spaces don't pull the line off-center.
                        var ink = glyphsForLine.Where(x => !IsBlank(x.Symbol)).ToArray();
                        if (ink.Length == 0) continue;
                        minX = ink.Min(x => x.Rect.Left);
                        maxX = ink.Max(x => x.Rect.Right);
                        var lineWidth = maxX - minX;
                        var diff = LineIndent(i) + (AlignRight(i) - LineIndent(i) - lineWidth) / 2 - minX;
                        foreach (var glyphWordData in glyphsForLine)
                        {
                            var rect = glyphWordData.Rect;
                            rect.X += (float)diff;
                            glyphWordData.Rect = rect;
                            glyphWordData.PenX += diff;
                        }
                    }
                }
                break;
                case HorizontalTextAlignment.Right:
                {
                    var maxLines = glyphsData.Max(x => x.LineIndex);
                    for (int i = 0; i <= maxLines; ++i)
                    {
                        var glyphsForLine = glyphsData.Where(x => x.LineIndex == i).ToArray();
                        if (glyphsForLine.Length == 0) break;

                        maxX = glyphsForLine.Where(x=>!IsBlank(x.Symbol)).Max(x => x.Rect.Right);
                        var diff = (AlignRight(i) - maxX);
                        foreach (var glyphWordData in glyphsForLine)
                        {
                            var rect = glyphWordData.Rect;
                            rect.X += (float)diff;
                            glyphWordData.Rect = rect;
                            glyphWordData.PenX += diff;
                        }
                    }
                }
                break;
                case HorizontalTextAlignment.Justify:
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

                    for (int i = 0; i <= maxLines; ++i)
                    {
                        // Only the gaps between words change; glyphs are shifted, never re-laid, so kerning and bearings stay.
                        var lineGlyphs = glyphsData.Where(x => x.LineIndex == i).OrderBy(x => x.Rect.X).ToArray();
                        if (lineGlyphs.Length == 0) continue;
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
                        var clusters = VisualClusters(i);
                        var lastInkCluster = clusters.FindLastIndex(IsInk);
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
                        var inkRight = lineGlyphs.Where(glyph => !IsBlank(glyph.Symbol) && glyph.Symbol != '\n')
                            .Select(glyph => glyph.Rect.Right).DefaultIfEmpty(lineGlyphs[lastInk].Rect.Right).Max();
                        var extra = AlignRight(i) + leftHang + rightHang - (inkRight - lead);

                        double wordShare;
                        double letterShare;
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
                            kashidaShare = Math.Min(rest, kashidaRoom);
                            rest -= kashidaShare;
                            if (options.JustificationAlternates && rest > 0 && stretches)
                            {
                                alternateShare = ChooseAlternates(clusters, fromCluster, lastInkCluster, rest, alternates);
                            }

                            scaleShare = Math.Min(rest - alternateShare, scaleStretch);
                            wordShare = spaceUnits > 0
                                ? extra - letterShare - kashidaShare - alternateShare - scaleShare
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
                            var rest = extra - wordShare - letterShare - kashidaShare - alternateShare - scaleShare;
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

                        var unspread = wordShare == 0 && letterShare == 0 && kashidaShare == 0 && alternateShare == 0
                                       && scaleShare == 0 && offset == 0;
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
                                _ => rightToLeft ? AlignRight(i) - inkRight : 0,
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

                            var widening = spaces[c] > 0 ? wordShare * spaces[c] / spaceUnits
                                : letters[c] > 0 ? Math.Max(letterShare * letters[c] / letterUnits, -clusters[c].Sum(glyph => glyph.Advance))
                                : 0;
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
                break;
            }

            if (options.OpticalMarginAlignment && renderingParameters.HorizontalTextAlignment != HorizontalTextAlignment.Center)
            {
                HangMargins(hung);
            }

            switch (slots == null ? renderingParameters.VerticalTextAlignment : VerticalTextAlignment.Top)
            {
                case VerticalTextAlignment.Center:
                {
                    // Centered by ascent above the baseline, not the ink: ink differs per string and made same-size
                    // text wobble. No descent reserve, so descenders hang below.
                    var lastInk = glyphsData.Max(x => x.LineIndex);
                    var ascent = lineAscents[0];
                    var firstBaseline = lineTops[0] - textArea.Y + lineBaselines[0];
                    var lastInkBaseline = lineTops[lastInk] + lineBaselines[lastInk];
                    if (dropGlyphs != null)
                    {
                        lastInkBaseline = Math.Max(lastInkBaseline, DropBaseline());
                    }

                    var blockHeight = lastInkBaseline - textArea.Y - firstBaseline + ascent;
                    var blockTop = firstBaseline - ascent;
                    if (vertical)
                    {
                        blockTop = lineTops[0] - textArea.Y;
                        blockHeight = lineTops[lastInk] + lineHeights[lastInk] - lineTops[0];
                    }

                    var diff = (finalRect.Height - blockHeight) / 2 - blockTop;
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
                    // The last baseline on the bottom edge, not the lowest ink, for the same reason.
                    var diff = finalRect.Height - (vertical ? height : lastBaseline);
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
            var toRight = renderingParameters.HorizontalTextAlignment == HorizontalTextAlignment.Right;
            foreach (var line in glyphsData.GroupBy(glyph => glyph.LineIndex))
            {
                if (hung.Contains(line.Key))
                {
                    continue;
                }

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
                    shift = Hang(glyphs[last], false);
                }
                else
                {
                    if (IsBlank(glyphs[0].Symbol) || glyphs[0].Symbol == '\n')
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

        List<GlyphWordData[]> VisualClusters(int line)
        {
            var groups = new List<GlyphWordData[]>();
            var current = new List<GlyphWordData>();
            foreach (var glyph in glyphsData)
            {
                if (glyph.LineIndex != line)
                {
                    continue;
                }

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
                if (text[c] is '\n' or LineSeparator)
                {
                    return true;
                }
            }

            return false;
        }

        void AlignToEachLinesStart()
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
                var toRight = rightToLeft == (renderingParameters.HorizontalTextAlignment == HorizontalTextAlignment.Left);
                var ink = Enumerable.Range(lineStart, lineEnd - lineStart).Select(k => glyphsData[k])
                    .Where(g => !IsBlank(g.Symbol)).ToArray();
                if (toRight && ink.Length > 0)
                {
                    var shift = AlignRight(glyphsData[lineStart].LineIndex) - ink.Max(g => g.Rect.Right);
                    for (var k = lineStart; k < lineEnd; k++)
                    {
                        glyphsData[k].Rect.X += (float)shift;
                        glyphsData[k].PenX += shift;
                    }
                }

                lineStart = lineEnd;
            }
        }

        void PlaceLines()
        {
            var mixed = false;
            for (var i = 0; i < lineCount; i++)
            {
                lineAscents[i] = _font.Ascender * scale;
                lineHeights[i] = lineHeight;
                lineBaselines[i] = baseLine;
            }

            foreach (var glyph in glyphsData)
            {
                mixed |= glyph.FontSize != fontSize || !ReferenceEquals(glyph.Font, _font)
                                                    || ObjectSizeOf(glyph.Symbol, glyph.Attributes) != null;
            }

            mixed &= options.LineStacking == LineStackingStrategy.MaxHeight;
            if (mixed)
            {
                var below = new double[lineCount];
                Array.Fill(below, lineHeight - baseLine);
                var setHeight = options.LineHeight > 0 && double.IsFinite(options.LineHeight);
                foreach (var glyph in glyphsData)
                {
                    var line = glyph.LineIndex;
                    var glyphScale = glyph.FontSize / glyph.Font.UnitsPerEm;
                    var ownSize = setHeight && glyph.FontSize == fontSize;
                    var glyphBaseline = ownSize ? baseLine : BaselineInLine(glyph.Font, glyphScale);
                    var glyphBelow = ownSize ? lineHeight - baseLine : LineAdvance(glyph.Font, glyph.FontSize) - glyphBaseline;
                    var glyphAscent = glyph.Font.Ascender * glyphScale;
                    lineBaselines[line] = Math.Max(lineBaselines[line], glyphBaseline);
                    below[line] = Math.Max(below[line], glyphBelow);
                    lineAscents[line] = Math.Max(lineAscents[line], glyphAscent);
                    if (ObjectSizeOf(glyph.Symbol, glyph.Attributes) is { } objectSize)
                    {
                        lineBaselines[line] = Math.Max(lineBaselines[line], objectSize.Height + glyph.OffsetY);
                        lineAscents[line] = Math.Max(lineAscents[line], objectSize.Height + glyph.OffsetY);
                        below[line] = Math.Max(below[line], -glyph.OffsetY);
                    }
                }

                for (var i = 0; i < lineCount; i++)
                {
                    lineHeights[i] = lineBaselines[i] + below[i];
                }
            }

            lineTops[0] = textArea.Y;
            for (var i = 1; i < lineCount; i++)
            {
                lineTops[i] = lineTops[i - 1] + lineHeights[i - 1] + lineSpacing;
            }

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
                    glyph.Rect = CellRect(glyph.Font, glyph.FontSize, glyph.PenX, glyph.Advance, lineBase);
                }
                else
                {
                    glyph.Rect = Widened(CalculateGlyphPosition(glyph.Font, glyph.Glyph, glyph.PenX + glyph.OffsetX,
                        lineBase - glyph.OffsetY, glyph.FontSize / glyph.Font.UnitsPerEm), glyph.PenX + glyph.OffsetX,
                        glyph.HorizontalScale);
                }
            }
        }

        void RearrangeData(GlyphWordData[] glyphsDataCopy, double glyphBase)
        {
            var rearrangeList = new List<GlyphWordData>();
            for (int k = glyphsDataCopy.Length - 1; k >= 0; k--)
            {
                var data = glyphsData[k];
                cursorPosition -= data.Advance;
                var wordsLeft = glyphsDataCopy.Take(k).Count(x => IsBlank(x.Symbol)) + 1;
                rearrangeList.Add(data);
                if (wordIndex > 0 &&
                    cursorPosition <= textArea.Width &&
                    renderingParameters.TextWrapping == TextWrapping.WrapByWords &&
                    IsBlank(data.Symbol))
                {
                    break;
                }
                else if (cursorPosition <= textArea.Width &&
                         renderingParameters.TextWrapping == TextWrapping.WrapBySymbols)
                {
                    break;
                }
                else if (wordsLeft == 1 &&
                         cursorPosition > textArea.Width &&
                         renderingParameters.TextWrapping == TextWrapping.WrapByWords)
                {
                    break;
                }
            }

            rearrangeList.Reverse();
            cursorPosition = 0;

            for (var index = 0; index < rearrangeList.Count; index++)
            {
                var glyphData = rearrangeList[index];
                if (index == 0 && IsBlank(glyphData.Symbol)) continue;

                var glyphRect = ObjectSizeOf(glyphData.Symbol, glyphData.Attributes) is { } objectSize
                    ? ObjectRect(cursorPosition, glyphBase - glyphData.OffsetY, objectSize)
                    : glyphData.Upright
                    ? CellRect(glyphData.Font, glyphData.FontSize, cursorPosition, glyphData.Advance, glyphBase)
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
                    cursorPosition + dotGlyphsWidth <= right)
                {
                    break;
                }
                else if (trimming ==
                         TextTrimming.WordEllipses &&
                         cursorPosition + dotGlyphsWidth <= right)
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
            var dotAdvance = _font.GetAdvanceWidth(_dotGlyph.Index) * scale * options.GlyphScaling.Desired;
            for (int j = 0; j < 3; j++)
            {
                var glyphRect = Widened(CalculateGlyphPosition(_font, _dotGlyph,
                    cursorPosition,
                    glyphBase,
                    scale), cursorPosition, options.GlyphScaling.Desired);
                glyphsData.Add(new GlyphWordData(_dotGlyph, '.', glyphRect, -1, lineIndex)
                {
                    PenX = cursorPosition,
                    Advance = dotAdvance,
                    Font = _font,
                    FontSize = fontSize,
                    HorizontalScale = options.GlyphScaling.Desired,
                });

                cursorPosition += dotAdvance;
            }
        }
    }

    private static int DropCapEnd(string text, DropCap dropCap)
    {
        if (char.IsWhiteSpace(text[0]) || char.IsControl(text[0]) || text[0] == ObjectReplacement)
        {
            return 0;
        }

        var graphemes = TextBoundaries.Graphemes(text);
        var end = 0;
        var letters = 0;
        while (end < text.Length && letters < dropCap.Characters
                                 && text[end] is not ('\n' or LineSeparator or ObjectReplacement))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(text, end) is not (UnicodeCategory.OpenPunctuation
                or UnicodeCategory.InitialQuotePunctuation))
            {
                letters++;
            }

            end = TextBoundaries.Next(graphemes, end);
        }

        return end;
    }

    private static List<(int Frame, double Left, double Right, double Top)> LineSlots(IReadOnlyList<RectangleF> frames,
        IReadOnlyList<RectangleF> exclusions, double lineHeight, double spacing, double narrowest, int mostLines)
    {
        List<(int Frame, double Left, double Right, double Top)> slots = [];
        var pitch = lineHeight + spacing;
        for (var f = 0; f < frames.Count && slots.Count < mostLines; f++)
        {
            var frame = frames[f];
            var rows = (int)Math.Max(1, Math.Min(Math.Floor((frame.Height + spacing) / pitch), 4.0 * mostLines));
            for (var row = 0; row < rows && slots.Count < mostLines; row++)
            {
                var top = frame.Top + row * pitch;
                List<(double Left, double Right)> spans = [(frame.Left, frame.Right)];
                foreach (var exclusion in exclusions ?? [])
                {
                    if (exclusion.Top >= top + lineHeight || exclusion.Bottom <= top)
                    {
                        continue;
                    }

                    List<(double Left, double Right)> rest = [];
                    foreach (var (left, right) in spans)
                    {
                        if (exclusion.Left > left)
                        {
                            rest.Add((left, Math.Min(right, exclusion.Left)));
                        }

                        if (exclusion.Right < right)
                        {
                            rest.Add((Math.Max(left, exclusion.Right), right));
                        }
                    }

                    spans = rest;
                }

                var widest = spans.Where(span => span.Right - span.Left >= narrowest)
                    .OrderByDescending(span => span.Right - span.Left).FirstOrDefault();
                if (widest.Right - widest.Left >= narrowest)
                {
                    slots.Add((f, widest.Left - frame.Left, widest.Right - frame.Left, top));
                }
            }
        }

        return slots;
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

    private static double ChooseAlternates(List<GlyphWordData[]> clusters, int from, int to, double room,
        Glyph[] chosen)
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
            foreach (var alternate in glyph.Font.GetGlyphAlternates(glyph.Glyph.Index))
            {
                var gain = (glyph.Font.GetAdvanceWidth(alternate.Glyph) * glyph.FontSize / glyph.Font.UnitsPerEm - current)
                           * glyph.HorizontalScale;
                if (alternate.Feature == "jalt" && gain > 0)
                {
                    candidates.Add((c, glyph.Font.GetGlyphByIndex(alternate.Glyph), gain));
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
