using System;
using System.Collections.Generic;
using System.Linq;
using Adamantium.Fonts;
using Adamantium.Fonts.Text;
using Adamantium.Mathematics;
using static Adamantium.Graphics.Fonts.Layout.TextGeometry;

namespace Adamantium.Graphics.Fonts.Layout;

internal sealed class TextNavigator
{
    private readonly LaidOutText _laid;
    private CaretStop[] _caretStops;
    private int[] _lineStarts;
    private bool[] _words;

    public TextNavigator(LaidOutText laid)
    {
        _laid = laid;
    }

    private string Text => _laid.Text;

    private WritingMode WritingMode => _laid.Options?.WritingMode ?? WritingMode.Horizontal;

    public CaretStop[] GetCaretStops()
    {
        var text = Text ?? string.Empty;
        var stops = new CaretStop[text.Length + 1];
        var data = _laid.Glyphs;
        if (data == null || text.Length == 0)
        {
            return stops;
        }

        var filled = new bool[text.Length + 1];
        var endX = 0.0;
        var endLine = 0;
        var i = 0;
        while (i < data.Count)
        {
            var cluster = data[i].PositionInString;
            if (cluster < 0 || cluster >= text.Length)
            {
                i++;
                continue;
            }

            var x = data[i].PenX;
            var line = data[i].LineIndex;
            var width = 0.0;
            var j = i;
            while (j < data.Count && data[j].PositionInString == cluster)
            {
                x = Math.Min(x, data[j].PenX);
                width += data[j].Advance;
                j++;
            }

            var after = j;
            while (after < data.Count && data[after].PositionInString < 0)
            {
                after++;
            }

            var next = after < data.Count && data[after].PositionInString > cluster
                ? data[after].PositionInString
                : Math.Max(cluster + 1, Math.Min(_laid.OversetIndex, text.Length));
            var rightToLeft = _laid.IsRightToLeftAt(cluster);
            var lineEnd = cluster + 1;
            while (lineEnd < next && !IsHardBreak(text[lineEnd]))
            {
                lineEnd++;
            }

            DistributeCluster(text, cluster, lineEnd, x, width, line, rightToLeft, stops, filled);
            if (lineEnd < next && !filled[lineEnd])
            {
                stops[lineEnd] = new CaretStop(rightToLeft ? x : x + width, line, 0, rightToLeft);
                filled[lineEnd] = true;
            }

            endX = rightToLeft ? x : x + width;
            endLine = line;
            i = j;
        }

        if (_laid.Paragraphs != null)
        {
            var rightToLeft = _laid.IsRightToLeftParagraph(text.Length - 1);
            for (var k = 0; k < text.Length; k++)
            {
                if (filled[k] && stops[k].LineIndex == endLine)
                {
                    endX = rightToLeft ? Math.Min(endX, stops[k].Left) : Math.Max(endX, stops[k].Right);
                }
            }
        }

        // A break with no glyph of its own that does not end a line of text ends an empty line: the one after the break
        // before it, or the first - if it was laid out: trimming leaves lines after its ellipsis unlaid.
        var laidLines = _laid.LineTops?.Length ?? 0;
        var laid = Math.Min(_laid.OversetIndex, text.Length);
        for (var k = 0; k < laid; k++)
        {
            if (filled[k] || !IsHardBreak(text[k]) || (k > 0 && (!filled[k - 1] || !IsHardBreak(text[k - 1]))))
            {
                continue;
            }

            var line = k == 0 ? 0 : stops[k - 1].LineIndex + 1;
            if (line >= laidLines)
            {
                break;
            }

            stops[k] = EmptyLineStop(k, line, stops, filled);
            filled[k] = true;
            if (line > endLine)
            {
                endLine = line;
                endX = stops[k].X;
            }
        }

        var options = _laid.Options;
        // The empty last line stands below a frame with no room for it, but not after an ellipsis.
        var trimmed = (_laid.RenderingParameters?.TextTrimming ?? TextTrimming.None) != TextTrimming.None;
        if (IsHardBreak(text[text.Length - 1]) && (endLine + 1 < laidLines || !trimmed)
            && _laid.OversetIndex >= text.Length && (options.MaxLines <= 0 || endLine + 1 < options.MaxLines))
        {
            stops[text.Length] = EmptyLineStop(text.Length, endLine + 1, stops, filled);
            filled[text.Length] = true;
        }

        for (var k = 0; k <= text.Length; k++)
        {
            if (!filled[k])
            {
                stops[k] = new CaretStop(endX, endLine);
            }
        }

        return stops;
    }

    // An empty line stands as its paragraph says: between its margins, in its own frame, and its indent at its start when
    // a paragraph separator opened it; after a line break it goes on with the paragraph before.
    private CaretStop EmptyLineStop(int at, int line, CaretStop[] stops, bool[] filled)
    {
        var frames = _laid.FrameRects;
        var lineFrames = _laid.LineFrames;
        var inFrame = frames != null && lineFrames != null && line < lineFrames.Length;
        double areaLeft = inFrame ? frames[lineFrames[line]].Left : 0;
        double areaRight = inFrame ? frames[lineFrames[line]].Right : _laid.Size.Width;
        var text = Text;
        var options = _laid.Options;
        var rightToLeft = options.Direction == TextDirection.RightToLeft
            || (options.Direction == TextDirection.Auto && _laid.IsRightToLeftParagraph(Math.Min(at, text.Length - 1)));
        var right = inFrame ? areaRight : 0.0;
        for (var k = 0; k < text.Length && rightToLeft && !inFrame; k++)
        {
            right = filled[k] ? Math.Max(right, stops[k].Right) : right;
        }

        var opens = at == 0 || text[at - 1] == ParagraphSeparator;
        var paragraph = AttributedText.ParagraphIn(_laid.ParagraphRuns, opens ? at : at - 1, text.Length)?.Attributes;
        var indent = opens ? Finite(paragraph?.TextIndent) : 0;
        var left = areaLeft + Math.Max(0, Finite(paragraph?.MarginLeft) + (rightToLeft ? 0 : indent));
        var edge = areaRight - Math.Max(0, Finite(paragraph?.MarginRight) + (rightToLeft ? indent : 0));
        var alignment = paragraph?.Alignment ?? _laid.RenderingParameters?.HorizontalTextAlignment;
        var last = _laid.RenderingParameters?.JustifyLastLine == true
            ? HorizontalTextAlignment.Left
            : options.LastLineAlignment;
        var x = (alignment == HorizontalTextAlignment.Justify ? last : alignment) switch
        {
            HorizontalTextAlignment.Center => (left + edge) / 2,
            HorizontalTextAlignment.Right => rightToLeft ? left : edge,
            _ => rightToLeft ? (paragraph != null ? edge : right) : left,
        };
        return new CaretStop(x, line);
    }

    private static bool IsHardBreak(char symbol) => symbol is '\n' or LineSeparator or ParagraphSeparator;

    public int LineCount => LineStarts().Length - 1;

    public TextLineMetrics GetLine(int lineIndex)
    {
        var stops = Stops();
        var starts = LineStarts();
        var start = 0;
        var end = 0;
        if (lineIndex >= starts.Length - 1)
        {
            start = end = stops.Length - 1;
        }
        else if (lineIndex >= 0)
        {
            start = starts[lineIndex];
            end = starts[lineIndex + 1];
        }

        var width = 0.0;
        for (var i = start; i < stops.Length && stops[i].LineIndex == lineIndex; i++)
        {
            width = Math.Max(width, stops[i].Right);
        }

        if (start < stops.Length && stops[start].LineIndex != lineIndex)
        {
            start = end;
        }

        var top = LineTop(lineIndex);
        var baseline = Math.Round(top - _laid.VerticalShift + LineMetric(_laid.LineBaselines, lineIndex))
                       + _laid.VerticalShift;
        return new TextLineMetrics(start, end, top, baseline, LineMetric(_laid.LineHeights, lineIndex), width);
    }

    public IReadOnlyList<RectangleF> GetRangeRects(int start, int end)
    {
        return RangeSegments(start, end)
            .Select(s => Placed(new RectangleF((float)s.Left, (float)LineTop(s.Line),
                (float)(s.Right - s.Left), (float)LineMetric(_laid.LineHeights, s.Line))))
            .ToList();
    }

    public IReadOnlyList<TextAdornment> GetAdornments()
    {
        var adornments = new List<TextAdornment>();
        if (_laid.Attributed == null)
        {
            return adornments;
        }

        double fontSize = (float)_laid.FontSize;
        foreach (var run in _laid.AttributeRuns ?? _laid.Attributed.Runs)
        {
            var attributes = run.Attributes;
            var decorations = attributes.Decorations ?? TextDecorations.None;
            var lines = attributes.DecorationLines ?? [];
            if (attributes.Background == null && decorations == TextDecorations.None && lines.Count == 0)
            {
                continue;
            }

            var font = RunFont(_laid.Font, attributes, fontSize);
            double em = font.UnitsPerEm;
            var scale = (attributes.FontSize ?? fontSize) / em;
            var thickness = (font.UnderlineThickness > 0 ? font.UnderlineThickness : em / 20) * scale;
            var underline = (font.UnderlinePosition != 0 ? font.UnderlinePosition : -em / 10) * scale;
            var strikeout = (font.StrikeoutPosition != 0 ? font.StrikeoutPosition : em / 4) * scale;
            var strikeoutSize = font.StrikeoutSize > 0 ? font.StrikeoutSize * scale : thickness;
            var ascender = (font.Ascender > 0 ? font.Ascender : em * 0.8) * scale;

            var lineColor = attributes.DecorationColor ?? attributes.Foreground;
            var vertical = WritingMode == WritingMode.VerticalRightToLeft;
            var size = attributes.FontSize ?? fontSize;
            var runShift = Raise(_laid.Font, WritingMode, attributes, font, size, fontSize);
            foreach (var segment in RangeSegments(run.Start, run.End))
            {
                var left = (float)segment.Left;
                var width = (float)(segment.Right - segment.Left);
                var top = LineTop(segment.Line);
                var shift = runShift + LineRaise(WritingMode, attributes, '\0',
                    LineMetric(_laid.LineBaselines, segment.Line), LineMetric(_laid.LineHeights, segment.Line), font, size);
                var baseline = Math.Round(top - _laid.VerticalShift + LineMetric(_laid.LineBaselines, segment.Line) - shift)
                               + _laid.VerticalShift;
                var axis = top + (_laid.LineAxes != null
                    ? LineMetric(_laid.LineAxes, segment.Line)
                    : LineMetric(_laid.LineHeights, segment.Line) / 2) - shift;
                var beside = axis - (attributes.FontSize ?? fontSize) / 2 - thickness;
                var underlineTop = vertical ? beside - thickness : baseline - underline;
                if (attributes.Background is { } background)
                {
                    adornments.Add(new TextAdornment(TextAdornmentKind.Background,
                        Placed(new RectangleF(left, (float)LineTop(segment.Line), width,
                            (float)LineMetric(_laid.LineHeights, segment.Line))), background));
                }

                if ((decorations & TextDecorations.Underline) != 0)
                {
                    AddLine(TextDecorationLocation.Underline, thickness, 0, lineColor, null);
                }

                if ((decorations & TextDecorations.Strikethrough) != 0)
                {
                    AddLine(TextDecorationLocation.Strikethrough, strikeoutSize, 0, lineColor, null);
                }

                if ((decorations & TextDecorations.Overline) != 0)
                {
                    AddLine(TextDecorationLocation.Overline, thickness, 0, lineColor, null);
                }

                foreach (var line in lines)
                {
                    if (line == null)
                    {
                        continue;
                    }

                    var own = line.Thickness is { } set && set > 0 && double.IsFinite(set) ? set
                        : line.Location == TextDecorationLocation.Strikethrough ? strikeoutSize : thickness;
                    var dashes = line.Dashes is { Count: > 0 } pattern
                                 && pattern.All(length => length >= 0 && double.IsFinite(length))
                                 && pattern.Any(length => length > 0)
                        ? pattern
                        : null;
                    AddLine(line.Location, own, double.IsFinite(line.Offset) ? line.Offset : 0,
                        line.Color ?? lineColor, dashes);
                }

                if ((decorations & TextDecorations.Squiggle) != 0)
                {
                    adornments.Add(new TextAdornment(TextAdornmentKind.Squiggle,
                        Placed(new RectangleF(left, (float)(vertical ? beside - 3 * thickness : underlineTop), width,
                            (float)(thickness * 3))), lineColor));
                }

                void AddLine(TextDecorationLocation location, double lineThickness, double offset, Color? color,
                    IReadOnlyList<double> dashes)
                {
                    var y = vertical
                        ? location switch
                        {
                            TextDecorationLocation.Underline => axis - size / 2 - thickness - lineThickness,
                            TextDecorationLocation.Overline => axis + size / 2 + thickness,
                            _ => axis - lineThickness / 2,
                        }
                        : location switch
                        {
                            TextDecorationLocation.Underline => baseline - underline,
                            TextDecorationLocation.Overline => baseline - ascender - lineThickness,
                            TextDecorationLocation.Strikethrough =>
                                baseline - strikeout + (strikeoutSize - lineThickness) / 2,
                            _ => baseline - lineThickness / 2,
                        };
                    var kind = location switch
                    {
                        TextDecorationLocation.Underline => TextAdornmentKind.Underline,
                        TextDecorationLocation.Overline => TextAdornmentKind.Overline,
                        TextDecorationLocation.Strikethrough => TextAdornmentKind.Strikethrough,
                        _ => TextAdornmentKind.Baseline,
                    };
                    adornments.Add(new TextAdornment(kind,
                        Placed(new RectangleF(left, (float)(y + offset), width, (float)lineThickness)), color, dashes));
                }
            }
        }

        return adornments;
    }

    public IReadOnlyList<TextInlineObject> GetInlineObjects()
    {
        List<TextInlineObject> objects = [];
        if (WritingMode == WritingMode.VerticalRightToLeft)
        {
            return objects;
        }

        foreach (var word in _laid.Glyphs ?? [])
        {
            if (ObjectSizeOf(word.Symbol, word.Attributes) != null)
            {
                objects.Add(new TextInlineObject(word.PositionInString, word.Rect));
            }
        }

        objects.Sort((left, right) => left.Index.CompareTo(right.Index));
        return objects;
    }

    public int NextCaretStop(int index) => TextBoundaries.Next(_laid.Graphemes, Clamp(index));

    public int PreviousCaretStop(int index) => TextBoundaries.Previous(_laid.Graphemes, Clamp(index));

    public (int Start, int End) GetWordAt(int index)
    {
        var words = Words();
        var text = Text ?? string.Empty;
        index = Clamp(index);
        if (index == text.Length && index > 0)
        {
            index--;
        }

        var start = words[index] ? index : TextBoundaries.Previous(words, index);
        return (start, TextBoundaries.Next(words, start));
    }

    public int NextWordStop(int index)
    {
        var words = Words();
        var text = Text ?? string.Empty;
        var stop = TextBoundaries.Next(words, Clamp(index));
        while (stop < text.Length && char.IsWhiteSpace(text[stop]))
        {
            stop = TextBoundaries.Next(words, stop);
        }

        return stop;
    }

    public int PreviousWordStop(int index)
    {
        var words = Words();
        var text = Text ?? string.Empty;
        var stop = TextBoundaries.Previous(words, Clamp(index));
        while (stop > 0 && char.IsWhiteSpace(text[stop]))
        {
            stop = TextBoundaries.Previous(words, stop);
        }

        return stop;
    }

    public TextHit HitTest(double x, double y)
    {
        var text = Text ?? string.Empty;
        if (text.Length == 0)
        {
            return new TextHit(0, false, false, 0);
        }

        var stops = Stops();
        var graphemes = _laid.Graphemes;
        var lineIndex = _laid.LineFrames != null ? LineInFrames(x, y) : LineAt(y);
        var insideY = lineIndex >= 0 && lineIndex < LineCount;
        lineIndex = Math.Max(0, Math.Min(lineIndex, LineCount - 1));
        var line = GetLine(lineIndex);

        var newline = -1;
        var leftmost = -1;
        var rightmost = -1;
        var lineLeft = double.MaxValue;
        var lineRight = double.MinValue;
        for (var g = line.Start; g < line.End && g < text.Length; g = TextBoundaries.Next(graphemes, g))
        {
            if (text[g] is '\n' or '\r' or LineSeparator or ParagraphSeparator)
            {
                newline = newline < 0 ? g : newline;
                continue;
            }

            var next = TextBoundaries.Next(graphemes, g);
            var left = double.MaxValue;
            var right = double.MinValue;
            for (var c = g; c < next && c < stops.Length; c++)
            {
                left = Math.Min(left, stops[c].Left);
                right = Math.Max(right, stops[c].Right);
            }

            if (left < lineLeft)
            {
                lineLeft = left;
                leftmost = g;
            }

            if (right > lineRight)
            {
                lineRight = right;
                rightmost = g;
            }

            if (x < left || x >= right)
            {
                continue;
            }

            var leading = stops[g].IsRightToLeft ? x >= (left + right) / 2 : x < (left + right) / 2;
            return leading ? new TextHit(g, false, insideY, g) : new TextHit(g, true, insideY, Math.Min(next, line.End));
        }

        if (leftmost < 0)
        {
            var at = newline >= 0 ? newline : line.Start;
            return new TextHit(at, false, false, at);
        }

        var onLeft = x < lineLeft;
        var nearest = onLeft ? leftmost : rightmost;
        var logicalEnd = LogicalLineEnd(line);
        if (logicalEnd >= 0 && onLeft == _laid.IsRightToLeftParagraph(line.Start))
        {
            return new TextHit(nearest, false, false, logicalEnd);
        }

        var after = Math.Min(TextBoundaries.Next(graphemes, nearest), line.End);
        if (newline >= 0)
        {
            after = Math.Min(after, newline);
        }

        return stops[nearest].IsRightToLeft == onLeft
            ? new TextHit(nearest, true, false, after)
            : new TextHit(nearest, false, false, nearest);
    }

    public (double X, int LineIndex) GetCaretPoint(CaretPosition position)
    {
        var stops = Stops();
        var text = Text ?? string.Empty;
        var index = Clamp(position.Index);
        var held = position.AfterPrevious ? HeldCharacter(index) : -1;
        if (held >= 0)
        {
            return (stops[held].After, stops[held].LineIndex);
        }

        var line = stops[index].LineIndex;
        if (_laid.Paragraphs != null && index < text.Length && text[index] is '\n' or '\r' or LineSeparator or ParagraphSeparator)
        {
            var boxes = LineBoxes(line);
            if (boxes.Count > 0)
            {
                return (_laid.IsRightToLeftParagraph(index) ? boxes[0].Left : boxes[boxes.Count - 1].Right, line);
            }
        }

        return (stops[index].X, line);
    }

    public CaretPosition MoveVisually(CaretPosition from, bool toRight)
    {
        var (x, lineIndex) = GetCaretPoint(from);
        var boxes = LineBoxes(lineIndex);
        if (boxes.Count > 0)
        {
            var edge = EdgeOf(from, boxes, x);
            if (toRight)
            {
                while (edge + 1 < boxes.Count && boxes[edge].Right <= boxes[edge].Left)
                {
                    edge++;
                }

                if (edge < boxes.Count)
                {
                    return RightEdgeOf(boxes[edge]);
                }
            }
            else
            {
                while (edge > 1 && boxes[edge - 1].Right <= boxes[edge - 1].Left)
                {
                    edge--;
                }

                if (edge > 0)
                {
                    return LeftEdgeOf(boxes[edge - 1]);
                }
            }
        }

        var forward = toRight != _laid.IsRightToLeftParagraph(GetLine(lineIndex).Start);
        var target = lineIndex + (forward ? 1 : -1);
        if (target < 0 || target >= LineCount)
        {
            return from;
        }

        var targetBoxes = LineBoxes(target);
        if (targetBoxes.Count == 0)
        {
            return new CaretPosition(GetLine(target).Start);
        }

        return toRight ? LeftEdgeOf(targetBoxes[0]) : RightEdgeOf(targetBoxes[targetBoxes.Count - 1]);
    }

    public CaretPosition GetLineStart(int lineIndex)
    {
        var line = GetLine(lineIndex);
        var boxes = LineBoxes(lineIndex);
        if (boxes.Count == 0)
        {
            return new CaretPosition(line.Start);
        }

        return _laid.IsRightToLeftParagraph(line.Start) ? RightEdgeOf(boxes[boxes.Count - 1]) : LeftEdgeOf(boxes[0]);
    }

    public CaretPosition GetLineEnd(int lineIndex)
    {
        var line = GetLine(lineIndex);
        var end = LogicalLineEnd(line);
        var boxes = LineBoxes(lineIndex);
        if (end >= 0 || boxes.Count == 0)
        {
            return new CaretPosition(end >= 0 ? end : line.Start);
        }

        return _laid.IsRightToLeftParagraph(line.Start) ? LeftEdgeOf(boxes[0]) : RightEdgeOf(boxes[boxes.Count - 1]);
    }

    public IReadOnlyList<(int Start, int End)> GetVisualRanges(CaretPosition anchor, CaretPosition focus)
    {
        var text = Text ?? string.Empty;
        var selected = new bool[text.Length];
        var (anchorX, anchorLine) = GetCaretPoint(anchor);
        var (focusX, focusLine) = GetCaretPoint(focus);
        if (anchorLine == focusLine)
        {
            SelectBetween(anchorLine, Math.Min(anchorX, focusX), Math.Max(anchorX, focusX), selected);
        }
        else
        {
            var (upper, upperX, lower, lowerX) = anchorLine < focusLine
                ? (anchorLine, anchorX, focusLine, focusX)
                : (focusLine, focusX, anchorLine, anchorX);
            var upperLine = GetLine(upper);
            if (_laid.IsRightToLeftParagraph(upperLine.Start))
            {
                SelectBetween(upper, double.MinValue, upperX, selected);
            }
            else
            {
                SelectBetween(upper, upperX, double.MaxValue, selected);
            }

            var newline = LogicalLineEnd(upperLine);
            for (var c = Math.Max(newline, 0); newline >= 0 && c < upperLine.End && c < text.Length; c++)
            {
                selected[c] = true;
            }

            for (var lineIndex = upper + 1; lineIndex < lower; lineIndex++)
            {
                var line = GetLine(lineIndex);
                for (var c = line.Start; c < line.End && c < text.Length; c++)
                {
                    selected[c] = true;
                }
            }

            if (_laid.IsRightToLeftParagraph(GetLine(lower).Start))
            {
                SelectBetween(lower, lowerX, double.MaxValue, selected);
            }
            else
            {
                SelectBetween(lower, double.MinValue, lowerX, selected);
            }
        }

        var ranges = new List<(int Start, int End)>();
        for (var i = 0; i < selected.Length; i++)
        {
            if (!selected[i])
            {
                continue;
            }

            var start = i;
            while (i < selected.Length && selected[i])
            {
                i++;
            }

            ranges.Add((start, i));
        }

        return ranges;
    }

    private int Clamp(int index) => Math.Max(0, Math.Min(index, (Text ?? string.Empty).Length));

    private RectangleF Placed(RectangleF rect) => WritingMode == WritingMode.VerticalRightToLeft
        ? new RectangleF((float)(_laid.ColumnsRight - rect.Bottom), rect.X, rect.Height, rect.Width)
        : rect;

    private double LineTop(int line)
    {
        var tops = _laid.LineTops;
        var heights = _laid.LineHeights;
        if (tops == null || tops.Length == 0)
        {
            return 0;
        }

        var last = tops.Length - 1;
        if (line < 0)
        {
            return tops[0] + line * (heights[0] + _laid.LineSpacing);
        }

        return line <= last ? tops[line] : tops[last] + (line - last) * (heights[last] + _laid.LineSpacing);
    }

    private static double LineMetric(double[] metrics, int line)
    {
        if (metrics == null || metrics.Length == 0)
        {
            return 0;
        }

        return metrics[Math.Max(0, Math.Min(line, metrics.Length - 1))];
    }

    private int LineInFrames(double x, double y)
    {
        var frameRects = _laid.FrameRects;
        var lineFrames = _laid.LineFrames;
        var tops = _laid.LineTops;
        var heights = _laid.LineHeights;
        var frame = 0;
        var nearest = double.MaxValue;
        for (var f = 0; f < frameRects.Length; f++)
        {
            var rect = frameRects[f];
            var dx = Math.Max(0, Math.Max(rect.Left - x, x - rect.Right));
            var dy = Math.Max(0, Math.Max(rect.Top - y, y - rect.Bottom));
            var distance = dx * dx + dy * dy;
            if (distance < nearest)
            {
                nearest = distance;
                frame = f;
            }
        }

        var found = -1;
        for (var i = 0; i < lineFrames.Length && i < tops.Length; i++)
        {
            if (lineFrames[i] != frame)
            {
                continue;
            }

            found = found < 0 || y >= tops[i] ? i : found;
            if (y < tops[i] + heights[i])
            {
                return i;
            }
        }

        if (found >= 0)
        {
            return found;
        }

        for (var i = lineFrames.Length - 1; i >= 0; i--)
        {
            if (lineFrames[i] < frame)
            {
                return i;
            }
        }

        return 0;
    }

    private int LineAt(double y)
    {
        var tops = _laid.LineTops;
        var heights = _laid.LineHeights;
        if (tops == null || tops.Length == 0 || y < tops[0])
        {
            return -1;
        }

        for (var i = 0; i < tops.Length; i++)
        {
            if (y < tops[i] + heights[i])
            {
                return i;
            }
        }

        return tops.Length;
    }

    private bool[] Words() => _words ??= TextBoundaries.Words(Text ?? string.Empty);

    private CaretStop[] Stops() => _caretStops ??= GetCaretStops();

    private int[] LineStarts()
    {
        if (_lineStarts != null)
        {
            return _lineStarts;
        }

        var stops = Stops();
        var count = stops.Max(s => s.LineIndex) + 1;
        var starts = new int[count + 1];
        var line = 0;
        for (var i = 0; i < stops.Length; i++)
        {
            while (line <= stops[i].LineIndex)
            {
                starts[line++] = i;
            }
        }

        starts[count] = Math.Max(starts[count - 1], Math.Min(_laid.OversetIndex, stops.Length - 1));
        return _lineStarts = starts;
    }

    private int HeldCharacter(int index)
    {
        var text = Text ?? string.Empty;
        if (index <= 0 || index > text.Length || text[index - 1] is '\n' or '\r' or LineSeparator or ParagraphSeparator)
        {
            return -1;
        }

        var character = index - 1;
        return character > 0 && char.IsLowSurrogate(text[character]) && char.IsHighSurrogate(text[character - 1])
            ? character - 1
            : character;
    }

    private List<(int Start, int End, double Left, double Right)> LineBoxes(int lineIndex)
    {
        var text = Text ?? string.Empty;
        var stops = Stops();
        var graphemes = _laid.Graphemes;
        var line = GetLine(lineIndex);
        var boxes = new List<(int Start, int End, double Left, double Right)>();
        for (var g = line.Start; g < line.End && g < text.Length; g = TextBoundaries.Next(graphemes, g))
        {
            if (text[g] is '\n' or '\r' or LineSeparator or ParagraphSeparator)
            {
                continue;
            }

            var next = TextBoundaries.Next(graphemes, g);
            var left = double.MaxValue;
            var right = double.MinValue;
            for (var c = g; c < next && c < stops.Length; c++)
            {
                left = Math.Min(left, stops[c].Left);
                right = Math.Max(right, stops[c].Right);
            }

            boxes.Add((g, next, left, right));
        }

        return boxes.OrderBy(b => b.Left).ThenBy(b => stops[b.Start].IsRightToLeft ? -b.Start : b.Start).ToList();
    }

    private int EdgeOf(CaretPosition position, List<(int Start, int End, double Left, double Right)> boxes, double x)
    {
        var stops = Stops();
        var held = position.AfterPrevious ? HeldCharacter(Clamp(position.Index)) : -1;
        for (var k = 0; k < boxes.Count; k++)
        {
            var rightToLeft = stops[boxes[k].Start].IsRightToLeft;
            if (held >= boxes[k].Start && held < boxes[k].End)
            {
                return rightToLeft ? k : k + 1;
            }

            if (held < 0 && position.Index == boxes[k].Start)
            {
                return rightToLeft ? k + 1 : k;
            }
        }

        var edge = 0;
        var distance = double.MaxValue;
        for (var k = 0; k <= boxes.Count; k++)
        {
            var edgeX = k < boxes.Count ? boxes[k].Left : boxes[k - 1].Right;
            if (Math.Abs(edgeX - x) < distance)
            {
                distance = Math.Abs(edgeX - x);
                edge = k;
            }
        }

        return edge;
    }

    private int LogicalLineEnd(TextLineMetrics line)
    {
        var text = Text ?? string.Empty;
        for (var c = line.Start; c < line.End && c < text.Length; c++)
        {
            if (text[c] is '\n' or '\r' or LineSeparator or ParagraphSeparator)
            {
                return c;
            }
        }

        return line.End >= text.Length ? text.Length : -1;
    }

    private CaretPosition LeftEdgeOf((int Start, int End, double Left, double Right) box) =>
        Stops()[box.Start].IsRightToLeft ? new CaretPosition(box.End, true) : new CaretPosition(box.Start);

    private CaretPosition RightEdgeOf((int Start, int End, double Left, double Right) box) =>
        Stops()[box.Start].IsRightToLeft ? new CaretPosition(box.Start) : new CaretPosition(box.End, true);

    private void SelectBetween(int lineIndex, double left, double right, bool[] selected)
    {
        foreach (var box in LineBoxes(lineIndex))
        {
            var middle = (box.Left + box.Right) / 2;
            if (middle <= left || middle >= right)
            {
                continue;
            }

            for (var c = box.Start; c < box.End; c++)
            {
                selected[c] = true;
            }
        }
    }

    private IEnumerable<(int Line, double Left, double Right)> RangeSegments(int start, int end)
    {
        var stops = Stops();
        start = Math.Max(0, start);
        end = Math.Min(end, stops.Length - 1);
        var line = -1;
        var pieces = new List<(double Left, double Right)>();
        for (var i = start; i < end; i++)
        {
            if (stops[i].LineIndex != line)
            {
                foreach (var piece in Merged(pieces))
                {
                    yield return (line, piece.Left, piece.Right);
                }

                pieces.Clear();
                line = stops[i].LineIndex;
            }

            pieces.Add((stops[i].Left, stops[i].Right));
        }

        foreach (var piece in Merged(pieces))
        {
            yield return (line, piece.Left, piece.Right);
        }
    }

    private static List<(double Left, double Right)> Merged(List<(double Left, double Right)> pieces)
    {
        var merged = new List<(double Left, double Right)>();
        foreach (var piece in pieces.OrderBy(p => p.Left))
        {
            if (merged.Count > 0 && piece.Left <= merged[merged.Count - 1].Right + 0.01)
            {
                var last = merged[merged.Count - 1];
                merged[merged.Count - 1] = (last.Left, Math.Max(last.Right, piece.Right));
                continue;
            }

            merged.Add(piece);
        }

        return merged;
    }

    private static void DistributeCluster(string text, int start, int end, double x, double width, int line,
        bool rightToLeft, CaretStop[] stops, bool[] filled)
    {
        var count = 0;
        for (var c = start; c < end; c++)
        {
            if (!IsTrailingSurrogate(text, c))
            {
                count++;
            }
        }

        var share = width / Math.Max(count, 1);
        var ordinal = 0;
        for (var c = start; c < end; c++)
        {
            if (IsTrailingSurrogate(text, c))
            {
                stops[c] = new CaretStop(stops[c - 1].X, line, 0, rightToLeft);
            }
            else
            {
                var caret = rightToLeft ? x + width - share * ordinal : x + share * ordinal;
                stops[c] = new CaretStop(caret, line, share, rightToLeft);
                ordinal++;
            }

            filled[c] = true;
        }
    }
}
