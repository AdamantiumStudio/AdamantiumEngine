using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Adamantium.Mathematics;

namespace Adamantium.Fonts.Subsetting;

/// <summary>A font of the few glyphs a document uses, written as a CID-keyed CFF font program (Adobe Technical Notes 5176
/// and 5177) from their outlines - whatever file they came from, at whatever axis values. A glyph's CID is its index in
/// its own font. No hints: it is made for printing.</summary>
public static class CffSubset
{
    /// <summary>The units to the em of every font written: a program that wrote its own would need a font matrix, which
    /// not every reader of a CID-keyed font takes from the same place.</summary>
    public const int UnitsPerEm = 1000;

    private const int One = 65536;
    private const int AdobeId = 391;        // the first SIDs past the standard strings
    private const int IdentityId = 392;
    private const int MaxName = 127;

    /// <summary>Writes <paramref name="glyphs"/> of <paramref name="font"/>, glyph 0 always among them, as a font called
    /// <paramref name="name"/>.</summary>
    public static byte[] Write(IFont font, IEnumerable<uint> glyphs, string name)
    {
        ArgumentNullException.ThrowIfNull(font);
        ArgumentNullException.ThrowIfNull(glyphs);
        ArgumentException.ThrowIfNullOrEmpty(name);
        var indices = IndicesOf(font, glyphs);
        var box = new Box();
        var charStrings = indices.Select(index => CharString(font, index, box) ?? [139, 14]).ToList();   // no outline

        var header = new byte[] { 1, 0, 4, 4 };
        var names = Index([Encoding.ASCII.GetBytes(Plain(name))]);
        var strings = Index(["Adobe"u8.ToArray(), "Identity"u8.ToArray()]);
        var globalSubrs = Index([]);
        var charset = Charset(indices);
        var select = Select(indices.Count);
        var outlines = Index(charStrings);
        var privateDict = new DictWriter().Operator(20, 0).Operator(21, 0).ToArray();   // default and nominal widths

        // Every offset is five bytes long, so the dictionaries are as long whatever they say.
        var start = header.Length + names.Length + Index([TopDict(box, indices, 0, 0, 0, 0)]).Length + strings.Length +
                    globalSubrs.Length;
        var selectAt = start + charset.Length;
        var outlinesAt = selectAt + select.Length;
        var privateAt = outlinesAt + outlines.Length;
        var fontsAt = privateAt + privateDict.Length;
        var top = Index([TopDict(box, indices, start, selectAt, outlinesAt, fontsAt)]);
        var fonts = Index([new DictWriter().Offsets(18, privateDict.Length, privateAt).ToArray()]);

        using var output = new MemoryStream();
        foreach (var part in (byte[][])[header, names, top, strings, globalSubrs, charset, select, outlines, privateDict, fonts])
        {
            output.Write(part);
        }

        return output.ToArray();
    }

    /// <summary>Whether a subset of <paramref name="font"/> can hold the glyph's outline: one of its file's own - not made
    /// beside them, as a color layer's is - that could be read, and that a charstring has the room for.</summary>
    public static bool CanHold(IFont font, uint glyph)
    {
        ArgumentNullException.ThrowIfNull(font);
        return IsOwn(font, glyph) && !font.GetGlyphByIndex(glyph).IsInvalid && CharString(font, glyph, new Box()) != null;
    }

    /// <summary>The glyphs written, in order: glyph 0, then each asked for that the font's file has, once.</summary>
    public static IReadOnlyList<uint> IndicesOf(IFont font, IEnumerable<uint> glyphs)
    {
        ArgumentNullException.ThrowIfNull(font);
        ArgumentNullException.ThrowIfNull(glyphs);
        var indices = new SortedSet<uint>(glyphs.Where(index => IsOwn(font, index))) { 0 };
        return [.. indices];
    }

    private static bool IsOwn(IFont font, uint glyph) => glyph < font.Typeface.GlyphCount && glyph <= ushort.MaxValue;

    // Null where the outline does not fit in one.
    private static byte[] CharString(IFont font, uint index, Box box)
    {
        // A charstring is 65535 bytes at most (5177, appendix B): a glyph of thousands of curves is kept to whole units,
        // a byte or two each where a fraction takes five.
        var written = CharString(font, index, box, 1);
        if (written?.Length > ushort.MaxValue)
        {
            written = CharString(font, index, box, One);
        }

        return written?.Length <= ushort.MaxValue ? written : null;
    }

    private static byte[] CharString(IFont font, uint index, Box box, int step)
    {
        var scale = (double)UnitsPerEm / font.UnitsPerEm;
        var pen = new Pen(box, step);
        var width = font.GetAdvanceWidth(index) * scale;
        if (width > Pen.Reach)
        {
            return null;
        }

        pen.Number((int)Math.Round(width * One));
        foreach (var contour in font.GetGlyphByIndex(index).GetContours())
        {
            var segments = contour.Segments
                .Where(segment => segment.Kind != GlyphSegmentKind.Line || segment.Start != segment.End).ToList();
            if (segments.Count == 0)
            {
                continue;
            }

            pen.MoveTo(segments[0].Start * scale);
            for (var at = 0; at < segments.Count; at++)
            {
                var segment = segments[at];
                switch (segment.Kind)
                {
                    case GlyphSegmentKind.Line:
                        // The last line home is drawn by the contour's closing.
                        if (at < segments.Count - 1 || segment.End != segments[0].Start)
                        {
                            pen.LineTo(segment.End * scale);
                        }

                        break;
                    case GlyphSegmentKind.Quadratic:
                        // A quadratic is a cubic whose controls stand two thirds of the way to its own.
                        pen.CurveTo((segment.Start + (segment.Control1 - segment.Start) * (2.0 / 3.0)) * scale,
                            (segment.End + (segment.Control1 - segment.End) * (2.0 / 3.0)) * scale, segment.End * scale);
                        break;
                    default:
                        pen.CurveTo(segment.Control1 * scale, segment.Control2 * scale, segment.End * scale);
                        break;
                }
            }
        }

        return pen.Reached ? pen.End() : null;
    }

    private static byte[] TopDict(Box box, IReadOnlyList<uint> indices, int charset, int select, int outlines, int fonts) =>
        new DictWriter()
            .Escaped(30, AdobeId, IdentityId, 0)                 // ROS: first, it says the font is CID-keyed
            .Operator(5, box.Left, box.Bottom, box.Right, box.Top)
            .Escaped(34, (int)indices[^1] + 1)                   // CIDCount
            .Offsets(15, charset)
            .EscapedOffset(37, select)
            .Offsets(17, outlines)
            .EscapedOffset(36, fonts)
            .ToArray();

    // Format 2: ranges of CIDs, the glyph 0 left out.
    private static byte[] Charset(IReadOnlyList<uint> indices)
    {
        using var output = new MemoryStream();
        output.WriteByte(2);
        for (var at = 1; at < indices.Count;)
        {
            var end = at + 1;
            while (end < indices.Count && indices[end] == indices[end - 1] + 1)
            {
                end++;
            }

            Write16(output, (int)indices[at]);
            Write16(output, end - at - 1);
            at = end;
        }

        return output.ToArray();
    }

    // Format 3: one range, all in the one font dictionary.
    private static byte[] Select(int count)
    {
        using var output = new MemoryStream();
        output.WriteByte(3);
        Write16(output, 1);
        Write16(output, 0);
        output.WriteByte(0);
        Write16(output, count);
        return output.ToArray();
    }

    private static byte[] Index(IReadOnlyList<byte[]> items)
    {
        using var output = new MemoryStream();
        Write16(output, items.Count);
        if (items.Count == 0)
        {
            return output.ToArray();
        }

        output.WriteByte(4);
        var offset = 1;
        Write32(output, offset);
        foreach (var item in items)
        {
            offset += item.Length;
            Write32(output, offset);
        }

        foreach (var item in items)
        {
            output.Write(item);
        }

        return output.ToArray();
    }

    /// <summary>A name as a PostScript name may be: what cannot stand in one taken out.</summary>
    private static string Plain(string name)
    {
        var plain = new StringBuilder();
        foreach (var letter in name)
        {
            if (letter is > ' ' and < (char)127 && "[](){}<>/%".IndexOf(letter) < 0 && plain.Length < MaxName)
            {
                plain.Append(letter);
            }
        }

        return plain.Length > 0 ? plain.ToString() : "Font";
    }

    private static void Write16(Stream output, int value)
    {
        Span<byte> bytes = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(bytes, (ushort)value);
        output.Write(bytes);
    }

    private static void Write32(Stream output, int value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(bytes, value);
        output.Write(bytes);
    }

    /// <summary>What all the outlines lie in, in whole units.</summary>
    private sealed class Box
    {
        private double _left = double.MaxValue, _bottom = double.MaxValue, _right = double.MinValue, _top = double.MinValue;

        public int Left => _left > _right ? 0 : (int)Math.Floor(_left);

        public int Bottom => _left > _right ? 0 : (int)Math.Floor(_bottom);

        public int Right => _left > _right ? 0 : (int)Math.Ceiling(_right);

        public int Top => _left > _right ? 0 : (int)Math.Ceiling(_top);

        public void Add(Vector2<double> point)
        {
            _left = Math.Min(_left, point.X);
            _right = Math.Max(_right, point.X);
            _bottom = Math.Min(_bottom, point.Y);
            _top = Math.Max(_top, point.Y);
        }
    }

    /// <summary>Writes a Type 2 charstring. Points are kept in 1/65536 of a unit, and a step is taken between two kept so:
    /// the rounding does not add up along a contour.</summary>
    private sealed class Pen(Box box, int step)
    {
        /// <summary>How far from the origin a step of 16.16 can span from end to end.</summary>
        public const double Reach = 16000;

        private readonly MemoryStream _output = new();
        private int _x;
        private int _y;

        /// <summary>Whether every point was within <see cref="Reach"/>.</summary>
        public bool Reached { get; private set; } = true;

        public void MoveTo(Vector2<double> point)
        {
            Step(point);
            _output.WriteByte(21);   // rmoveto
        }

        public void LineTo(Vector2<double> point)
        {
            Step(point);
            _output.WriteByte(5);    // rlineto
        }

        public void CurveTo(Vector2<double> control1, Vector2<double> control2, Vector2<double> end)
        {
            Step(control1);
            Step(control2);
            Step(end);
            _output.WriteByte(8);    // rrcurveto
        }

        public byte[] End()
        {
            _output.WriteByte(14);   // endchar
            return _output.ToArray();
        }

        public void Number(int value)
        {
            if (value % One == 0)
            {
                WriteInteger(_output, value / One);
                return;
            }

            _output.WriteByte(255);
            Write32(_output, value);
        }

        private void Step(Vector2<double> point)
        {
            if (!(Math.Abs(point.X) <= Reach && Math.Abs(point.Y) <= Reach))
            {
                Reached = false;
                return;
            }

            box.Add(point);
            var x = Kept(point.X);
            var y = Kept(point.Y);
            Number(x - _x);
            Number(y - _y);
            _x = x;
            _y = y;
        }

        private int Kept(double value) => (int)Math.Round(value * One / step) * step;
    }

    private static void WriteInteger(Stream output, int value)
    {
        switch (value)
        {
            case >= -107 and <= 107:
                output.WriteByte((byte)(value + 139));
                break;
            case >= 108 and <= 1131:
                output.WriteByte((byte)((value - 108) / 256 + 247));
                output.WriteByte((byte)((value - 108) % 256));
                break;
            case >= -1131 and <= -108:
                output.WriteByte((byte)((-value - 108) / 256 + 251));
                output.WriteByte((byte)((-value - 108) % 256));
                break;
            default:
                output.WriteByte(28);
                Write16(output, (ushort)(short)value);
                break;
        }
    }

    private sealed class DictWriter
    {
        private readonly MemoryStream _output = new();

        public DictWriter Operator(byte code, params int[] operands)
        {
            foreach (var operand in operands)
            {
                Number(operand);
            }

            _output.WriteByte(code);
            return this;
        }

        public DictWriter Escaped(byte code, params int[] operands)
        {
            foreach (var operand in operands)
            {
                Number(operand);
            }

            _output.WriteByte(12);
            _output.WriteByte(code);
            return this;
        }

        public DictWriter Offsets(byte code, params int[] offsets)
        {
            foreach (var offset in offsets)
            {
                Long(offset);
            }

            _output.WriteByte(code);
            return this;
        }

        public DictWriter EscapedOffset(byte code, int offset)
        {
            Long(offset);
            _output.WriteByte(12);
            _output.WriteByte(code);
            return this;
        }

        public byte[] ToArray() => _output.ToArray();

        private void Number(int value)
        {
            if (value is >= short.MinValue and <= short.MaxValue)
            {
                WriteInteger(_output, value);
            }
            else
            {
                Long(value);
            }
        }

        private void Long(int value)
        {
            _output.WriteByte(29);
            Write32(_output, value);
        }
    }
}
