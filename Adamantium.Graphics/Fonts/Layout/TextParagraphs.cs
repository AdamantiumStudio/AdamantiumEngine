using System;
using System.Collections.Generic;
using Adamantium.Fonts;
using Adamantium.Fonts.Text;

namespace Adamantium.Graphics.Fonts.Layout;

internal sealed class TextParagraphs
{
    private readonly List<(int Start, int End, BidiParagraph Paragraph)> _paragraphs;
    private byte[] _characterLevels;

    private TextParagraphs(List<(int Start, int End, BidiParagraph Paragraph)> paragraphs)
    {
        _paragraphs = paragraphs;
    }

    public static TextParagraphs Resolve(string text, TextDirection direction)
    {
        if (!BidiParagraph.IsNeeded(text, 0, text.Length, direction))
        {
            return null;
        }

        var paragraphs = new List<(int Start, int End, BidiParagraph Paragraph)>();
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (BidiParagraph.IsParagraphSeparator(text, i))
            {
                paragraphs.Add((start, i + 1, new BidiParagraph(text, start, i + 1, direction)));
                start = i + 1;
            }
        }

        if (start < text.Length || paragraphs.Count == 0)
        {
            paragraphs.Add((start, text.Length, new BidiParagraph(text, start, text.Length, direction)));
        }

        return new TextParagraphs(paragraphs);
    }

    public static bool IsRightToLeftParagraph(TextParagraphs paragraphs, string text, TextDirection direction, int index)
    {
        if (paragraphs == null || string.IsNullOrEmpty(text))
        {
            return direction == TextDirection.RightToLeft;
        }

        return (paragraphs.ParagraphAt(Math.Max(0, Math.Min(index, text.Length - 1))).Paragraph.BaseLevel & 1) == 1;
    }

    public static bool IsRightToLeftAt(TextParagraphs paragraphs, int index)
    {
        if (paragraphs == null || index < 0)
        {
            return false;
        }

        var level = paragraphs._characterLevels != null && index < paragraphs._characterLevels.Length
            ? paragraphs._characterLevels[index]
            : paragraphs.ParagraphAt(index).Paragraph.LevelAt(index);
        return (level & 1) == 1;
    }

    public static IEnumerable<(int Start, int End, TextDirection Direction)> LevelRuns(TextParagraphs paragraphs,
        int start, int end)
    {
        if (paragraphs == null)
        {
            yield return (start, end, TextDirection.Auto);
            yield break;
        }

        var runStart = start;
        var level = paragraphs.ParagraphAt(start).Paragraph.LevelAt(start);
        for (var i = start + 1; i < end; i++)
        {
            var next = paragraphs.ParagraphAt(i).Paragraph.LevelAt(i);
            if (next == level)
            {
                continue;
            }

            yield return (runStart, i, DirectionOf(level));
            runStart = i;
            level = next;
        }

        yield return (runStart, end, DirectionOf(level));
    }

    public (int Start, int End, BidiParagraph Paragraph) ParagraphAt(int index)
    {
        var low = 0;
        var high = _paragraphs.Count - 1;
        while (low < high)
        {
            var middle = (low + high) / 2;
            if (index < _paragraphs[middle].End)
            {
                high = middle;
            }
            else
            {
                low = middle + 1;
            }
        }

        return _paragraphs[low];
    }

    public void ReorderLines(List<GlyphWordData> glyphs)
    {
        _characterLevels = new byte[_paragraphs[_paragraphs.Count - 1].End];
        var start = 0;
        while (start < glyphs.Count)
        {
            var end = start;
            while (end < glyphs.Count && glyphs[end].LineIndex == glyphs[start].LineIndex)
            {
                end++;
            }

            ReorderLine(glyphs, start, end);
            start = end;
        }
    }

    private static TextDirection DirectionOf(int level) =>
        (level & 1) == 1 ? TextDirection.RightToLeft : TextDirection.LeftToRight;

    private void ReorderLine(List<GlyphWordData> glyphs, int start, int end)
    {
        var x = double.MaxValue;
        for (var k = start; k < end; k++)
        {
            x = Math.Min(x, glyphs[k].PenX);
        }

        var segment = start;
        while (segment < end)
        {
            var first = -1;
            for (var k = segment; k < end && first < 0; k++)
            {
                first = glyphs[k].PositionInString;
            }

            if (first < 0)
            {
                return;
            }

            var (_, paragraphEnd, paragraph) = ParagraphAt(first);
            var segmentEnd = segment;
            while (segmentEnd < end && glyphs[segmentEnd].PositionInString < paragraphEnd)
            {
                segmentEnd++;
            }

            var lineEnd = paragraphEnd;
            for (var k = segmentEnd; k < glyphs.Count && segmentEnd == end; k++)
            {
                if (glyphs[k].PositionInString >= first)
                {
                    lineEnd = Math.Min(lineEnd, glyphs[k].PositionInString);
                    break;
                }
            }

            x = PlaceInVisualOrder(glyphs, segment, segmentEnd, paragraph, first, lineEnd, x);
            segment = segmentEnd;
        }
    }

    private double PlaceInVisualOrder(List<GlyphWordData> glyphs, int start, int end, BidiParagraph paragraph,
        int first, int lineEnd, double x)
    {
        var lineLevels = paragraph.LineLevels(first, lineEnd);
        Array.Copy(lineLevels, 0, _characterLevels, first, lineLevels.Length);
        var levels = new byte[end - start];
        var moves = false;
        for (var k = start; k < end; k++)
        {
            var cluster = glyphs[k].PositionInString;
            levels[k - start] = cluster >= first && cluster < lineEnd
                ? lineLevels[cluster - first]
                : (byte)paragraph.BaseLevel;
            moves |= levels[k - start] != 0;
        }

        if (!moves)
        {
            return glyphs[end - 1].PenX + glyphs[end - 1].Advance;
        }

        foreach (var index in BidiParagraph.VisualOrder(levels))
        {
            var glyph = glyphs[start + index];
            var shift = x - glyph.PenX;
            glyph.PenX = x;
            glyph.Rect.X += (float)shift;
            x += glyph.Advance;
        }

        return x;
    }
}
