using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Adamantium.Pdf.Document;

namespace Adamantium.Pdf.Text;

/// <summary>A page's text: its glyphs in the order the page shows them, gathered into lines along their baselines;
/// spaces where glyphs stand apart without one, line breaks between lines. What selection, copying and search work
/// on: a range of glyphs, the caret nearest a point, the glyphs in a rectangle, the matches of a search.</summary>
public sealed class PdfPageText
{
    private const double SpaceGap = 0.15;
    private const double LineDrift = 0.6;
    private const double BackStep = 1.0;
    private const double ColumnGap = 3.0;

    private readonly string _text;
    private readonly string _searchable;
    private readonly int[] _glyphOfChar;
    private readonly bool[] _addedChar;
    private readonly int[] _charOfGlyph;
    private readonly int[] _lineOfGlyph;

    internal PdfPageText(IReadOnlyList<PdfTextGlyph> glyphs)
    {
        Glyphs = glyphs;
        var lines = new List<PdfTextLine>();
        _lineOfGlyph = new int[glyphs.Count];
        _charOfGlyph = new int[glyphs.Count];
        var text = new StringBuilder();
        var glyphOfChar = new List<int>();
        var addedChar = new List<bool>();
        var start = 0;
        for (var i = 0; i <= glyphs.Count; i++)
        {
            if (i < glyphs.Count && (i == start || Continues(glyphs, start, i)))
            {
                continue;
            }

            if (i > start)
            {
                if (lines.Count > 0)
                {
                    text.Append('\n');
                    glyphOfChar.Add(start);
                    addedChar.Add(true);
                }

                for (var g = start; g < i; g++)
                {
                    if (g > start && Gap(glyphs, start, g))
                    {
                        text.Append(' ');
                        glyphOfChar.Add(g);
                        addedChar.Add(true);
                    }

                    _charOfGlyph[g] = text.Length;
                    _lineOfGlyph[g] = lines.Count;
                    text.Append(glyphs[g].Text);
                    for (var c = 0; c < glyphs[g].Text.Length; c++)
                    {
                        glyphOfChar.Add(g);
                        addedChar.Add(false);
                    }
                }

                lines.Add(new PdfTextLine(start, i - start, Merge(glyphs, start, i)));
            }

            start = i;
        }

        Lines = lines;
        _text = text.ToString();
        _searchable = _text.Replace('\n', ' ');
        _glyphOfChar = glyphOfChar.ToArray();
        _addedChar = addedChar.ToArray();
    }

    public IReadOnlyList<PdfTextGlyph> Glyphs { get; }

    public IReadOnlyList<PdfTextLine> Lines { get; }

    /// <summary>All the page's text, its lines one a line.</summary>
    public string Text => _text;

    public override string ToString() => _text;

    /// <summary>The text of the glyphs from <paramref name="start"/> up to <paramref name="end"/>, with the spaces and
    /// line breaks between them.</summary>
    public string GetText(int start, int end)
    {
        (start, end) = Clamp(start, end);
        if (start >= end)
        {
            return "";
        }

        var last = end - 1;
        return _text[_charOfGlyph[start]..(_charOfGlyph[last] + Glyphs[last].Text.Length)];
    }

    /// <summary>The boxes of the glyphs from <paramref name="start"/> up to <paramref name="end"/>, one a line - what
    /// a selection is painted with, and the quadrilaterals of a highlight over it.</summary>
    public IReadOnlyList<PdfQuad> QuadsOf(int start, int end)
    {
        (start, end) = Clamp(start, end);
        var quads = new List<PdfQuad>();
        while (start < end)
        {
            var line = Lines[_lineOfGlyph[start]];
            var stop = Math.Min(end, line.Start + line.Count);
            quads.Add(Merge(Glyphs, start, stop));
            start = stop;
        }

        return quads;
    }

    /// <summary>The caret nearest <paramref name="point"/>: before the glyph whose first half it is over, on the line
    /// it is nearest to - an index from 0 to the number of glyphs.</summary>
    public int CaretAt(PdfPoint point)
    {
        if (Lines.Count == 0)
        {
            return 0;
        }

        PdfTextLine nearest = null;
        var best = double.MaxValue;
        var visible = Lines.Any(line => line.Quad.Bounds.Width + line.Quad.Bounds.Height > 1e-6);
        foreach (var line in Lines)
        {
            if (visible && line.Quad.Bounds.Width + line.Quad.Bounds.Height <= 1e-6)
            {
                continue;
            }

            var distance = line.Quad.Contains(point) ? 0 : Distance(line.Quad.Bounds, point);
            if (distance < best)
            {
                (best, nearest) = (distance, line);
            }
        }

        var (dx, dy) = LineDirection(Glyphs, nearest.Start, nearest.Start + nearest.Count);
        var origin = Glyphs[nearest.Start].Origin;
        var along = (point.X - origin.X) * dx + (point.Y - origin.Y) * dy;
        for (var i = nearest.Start; i < nearest.Start + nearest.Count; i++)
        {
            var center = Glyphs[i].Quad.Center;
            if (along < (center.X - origin.X) * dx + (center.Y - origin.Y) * dy)
            {
                return i;
            }
        }

        return nearest.Start + nearest.Count;
    }

    /// <summary>The glyphs whose middle is inside <paramref name="area"/>, in order - a selection drawn as a box.
    /// </summary>
    public IReadOnlyList<int> GlyphsIn(PdfRectangle area)
    {
        var inside = new List<int>();
        for (var i = 0; i < Glyphs.Count; i++)
        {
            var center = Glyphs[i].Quad.Center;
            if (center.X >= area.Left && center.X <= area.Right && center.Y >= area.Bottom && center.Y <= area.Top)
            {
                inside.Add(i);
            }
        }

        return inside;
    }

    /// <summary>The text of the glyphs inside <paramref name="area"/>: each line's part on a line of its own.</summary>
    public string TextIn(PdfRectangle area)
    {
        var glyphs = GlyphsIn(area);
        var text = new StringBuilder();
        for (var i = 0; i < glyphs.Count;)
        {
            var line = _lineOfGlyph[glyphs[i]];
            var run = i;
            while (run + 1 < glyphs.Count && glyphs[run + 1] == glyphs[run] + 1 && _lineOfGlyph[glyphs[run + 1]] == line)
            {
                run++;
            }

            if (text.Length > 0)
            {
                text.Append(i > 0 && _lineOfGlyph[glyphs[i - 1]] == line ? " " : "\n");
            }

            text.Append(GetText(glyphs[i], glyphs[run] + 1));
            i = run + 1;
        }

        return text.ToString();
    }

    /// <summary>Where <paramref name="query"/> is in the text, in order; a match may run on from one line into the
    /// next, the break between them read as a space.</summary>
    public IReadOnlyList<PdfTextMatch> Find(string query, PdfTextSearchOptions options = null)
    {
        options ??= PdfTextSearchOptions.Default;
        var matches = new List<PdfTextMatch>();
        if (string.IsNullOrEmpty(query) || _searchable.Length == 0)
        {
            return matches;
        }

        var compare = (options.MatchCase ? CompareOptions.None : CompareOptions.IgnoreCase) |
                      (options.MatchDiacritics ? CompareOptions.None : CompareOptions.IgnoreNonSpace);
        var info = CultureInfo.InvariantCulture.CompareInfo;
        for (var from = 0; from < _searchable.Length;)
        {
            var found = info.IndexOf(_searchable.AsSpan(from), query, compare, out var length);
            if (found < 0 || length <= 0)
            {
                break;
            }

            var at = from + found;
            from = at + Math.Max(1, length);
            if (options.WholeWord && (IsWordCharacter(at - 1) || IsWordCharacter(at + length)))
            {
                continue;
            }

            var last = at + length - 1;
            while (last > at && _addedChar[last])
            {
                last--;
            }

            var start = _glyphOfChar[at];
            var end = _glyphOfChar[last] + (_addedChar[last] ? 0 : 1);
            while (end < Glyphs.Count && end > 0 && Glyphs[end].Text.Length == 0 &&
                   _lineOfGlyph[end] == _lineOfGlyph[end - 1])
            {
                end++;
            }

            if (end > start)
            {
                matches.Add(new PdfTextMatch(start, end, QuadsOf(start, end)));
            }
        }

        return matches;
    }

    private bool IsWordCharacter(int index) =>
        index >= 0 && index < _searchable.Length && char.IsLetterOrDigit(_searchable[index]);

    private (int Start, int End) Clamp(int start, int end) =>
        (Math.Clamp(start, 0, Glyphs.Count), Math.Clamp(end, 0, Glyphs.Count));

    private static bool Continues(IReadOnlyList<PdfTextGlyph> glyphs, int start, int index)
    {
        var previous = glyphs[index - 1];
        var next = glyphs[index];
        if (previous.IsVertical != next.IsVertical)
        {
            return false;
        }

        var (dx, dy) = LineDirection(glyphs, start, index);
        if (Advance(next) is { } own && dx * own.X + dy * own.Y < 0.95)
        {
            return false;
        }

        var size = Math.Max(Math.Max(previous.Size, next.Size), 1e-6);
        var origin = glyphs[start].Origin;
        var across = Math.Abs((next.Origin.X - origin.X) * -dy + (next.Origin.Y - origin.Y) * dx);
        var along = (next.Origin.X - previous.End.X) * dx + (next.Origin.Y - previous.End.Y) * dy;
        return across <= LineDrift * size && along >= -BackStep * size && along <= ColumnGap * size;
    }

    private static bool Gap(IReadOnlyList<PdfTextGlyph> glyphs, int start, int index)
    {
        var (previous, next) = (glyphs[index - 1], glyphs[index]);
        if (previous.Text.Length == 0 || next.Text.Length == 0 || char.IsWhiteSpace(previous.Text[^1]) ||
            char.IsWhiteSpace(next.Text[0]))
        {
            return false;
        }

        var (dx, dy) = LineDirection(glyphs, start, index);
        var gap = (next.Origin.X - previous.End.X) * dx + (next.Origin.Y - previous.End.Y) * dy;
        return gap > SpaceGap * Math.Max(previous.Size, next.Size);
    }

    private static (double X, double Y) LineDirection(IReadOnlyList<PdfTextGlyph> glyphs, int start, int end)
    {
        for (var i = end - 1; i >= start; i--)
        {
            if (Advance(glyphs[i]) is { } direction)
            {
                return direction;
            }
        }

        return glyphs[start].IsVertical ? (0, -1) : (1, 0);
    }

    private static (double X, double Y)? Advance(PdfTextGlyph glyph)
    {
        var (x, y) = (glyph.End.X - glyph.Origin.X, glyph.End.Y - glyph.Origin.Y);
        var length = Math.Sqrt(x * x + y * y);
        return length < 1e-9 ? null : (x / length, y / length);
    }

    private static PdfQuad Merge(IReadOnlyList<PdfTextGlyph> glyphs, int start, int end)
    {
        var (first, last) = (glyphs[start].Quad, glyphs[end - 1].Quad);
        return new PdfQuad(first.BottomLeft, last.BottomRight, last.TopRight, first.TopLeft);
    }

    private static double Distance(PdfRectangle box, PdfPoint point)
    {
        var dx = Math.Max(Math.Max(box.Left - point.X, 0), point.X - box.Right);
        var dy = Math.Max(Math.Max(box.Bottom - point.Y, 0), point.Y - box.Top);
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
