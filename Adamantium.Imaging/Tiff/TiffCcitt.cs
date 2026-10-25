using System;
using System.Collections.Generic;

namespace Adamantium.Imaging.Tiff;

/// <summary>CCITT fax decoding (ITU-T T.4 and T.6): modified Huffman run lengths, Group 3 one- and two-dimensional, and
/// Group 4. Rows come out packed one bit per pixel, most significant first, black as 1 - the sense of MinIsWhite.
/// </summary>
internal static class TiffCcitt
{
    private const int MaxCodeLength = 13;
    private const int EndOfLine = 1;

    /// <summary>Decodes <paramref name="input"/> into rows of <paramref name="width"/> pixels filling
    /// <paramref name="output"/>. A code that is not one stops the strip: the rows after it stay white.</summary>
    public static void Decode(ReadOnlySpan<byte> input, Span<byte> output, int width, TiffCompression compression,
        ulong t4Options)
    {
        var rowBytes = (width + 7) / 8;
        var rows = output.Length / rowBytes;
        var reader = new BitReader(input);
        var twoDimensional = compression == TiffCompression.CcittGroup3 && (t4Options & 1) != 0;
        List<int> reference = [];
        List<int> line = [];
        for (var row = 0; row < rows; row++)
        {
            line.Clear();
            bool ok;
            switch (compression)
            {
                case TiffCompression.CcittRle:
                    if (row > 0)
                    {
                        reader.AlignToByte();
                    }

                    ok = DecodeOneDimensional(ref reader, width, line);
                    break;
                case TiffCompression.CcittGroup3:
                    reader.SkipEndOfLine();
                    var oneDimensional = !twoDimensional || reader.Read(1) == 1;
                    ok = oneDimensional
                        ? DecodeOneDimensional(ref reader, width, line)
                        : DecodeTwoDimensional(ref reader, width, reference, line);
                    break;
                default:
                    ok = DecodeTwoDimensional(ref reader, width, reference, line);
                    break;
            }

            if (!ok)
            {
                break;
            }

            Render(line, width, output.Slice(row * rowBytes, rowBytes));
            (reference, line) = (line, reference);
        }
    }

    private static bool DecodeOneDimensional(ref BitReader reader, int width, List<int> changes)
    {
        var position = 0;
        var black = false;
        while (position < width)
        {
            var run = ReadRun(ref reader, black, width);
            if (run < 0)
            {
                return false;
            }

            position = (int)Math.Min(width, (long)position + run);
            if (position < width)
            {
                changes.Add(position);
            }

            black = !black;
        }

        return true;
    }

    private static bool DecodeTwoDimensional(ref BitReader reader, int width, List<int> reference, List<int> changes)
    {
        var a0 = -1;
        var black = false;
        var scan = 0;
        while (a0 < width)
        {
            var (b1, b2) = Below(reference, ref scan, a0, black, width);
            if (reader.Read(1) == 1)
            {
                Change(changes, ref a0, ref black, b1, width);
                continue;
            }

            if (reader.Read(1) == 1)
            {
                var vertical = reader.Read(1) == 1 ? 1 : -1;
                Change(changes, ref a0, ref black, b1 + vertical, width);
                continue;
            }

            if (reader.Read(1) == 1)
            {
                var start = Math.Max(a0, 0);
                var first = ReadRun(ref reader, black, width);
                var second = first < 0 ? -1 : ReadRun(ref reader, !black, width);
                if (second < 0)
                {
                    return false;
                }

                var a1 = (int)Math.Min(width, (long)start + first);
                var a2 = (int)Math.Min(width, (long)a1 + second);
                AddChange(changes, a1, width);
                AddChange(changes, a2, width);
                a0 = a2;
                continue;
            }

            if (reader.Read(1) == 1)
            {
                a0 = b2;
                continue;
            }

            var code = reader.Read(2);
            if (code >= 2)
            {
                Change(changes, ref a0, ref black, b1 + (code == 3 ? 2 : -2), width);
                continue;
            }

            var third = code == 1 ? reader.Read(1) : -1;
            if (third < 0)
            {
                return false;
            }

            Change(changes, ref a0, ref black, b1 + (third == 1 ? 3 : -3), width);
        }

        return true;
    }

    private static (int B1, int B2) Below(List<int> reference, ref int scan, int a0, bool black, int width)
    {
        while (scan < reference.Count && reference[scan] <= a0)
        {
            scan++;
        }

        var i = scan;
        if ((i % 2 == 1) != black)
        {
            i++;
        }

        var b1 = i < reference.Count ? reference[i] : width;
        var b2 = i + 1 < reference.Count ? reference[i + 1] : width;
        return (b1, b2);
    }

    private static void Change(List<int> changes, ref int a0, ref bool black, int a1, int width)
    {
        a1 = Math.Clamp(a1, Math.Max(a0, 0), width);
        AddChange(changes, a1, width);
        a0 = a1;
        black = !black;
    }

    private static void AddChange(List<int> changes, int position, int width)
    {
        if (position < width)
        {
            changes.Add(position);
        }
    }

    private static int ReadRun(ref BitReader reader, bool black, int width)
    {
        var codes = black ? TiffCcittCodes.Black : TiffCcittCodes.White;
        var total = 0;
        while (true)
        {
            var bits = 0;
            var found = -1;
            for (var length = 1; length <= MaxCodeLength && found < 0; length++)
            {
                var bit = reader.Read(1);
                if (bit < 0)
                {
                    return -1;
                }

                bits = (bits << 1) | bit;
                if (length >= 2 && codes.TryGetValue(TiffCcittCodes.Key(bits, length), out var run))
                {
                    found = run;
                }
            }

            if (found < 0)
            {
                return -1;
            }

            total += found;
            if (total > width)
            {
                return -1;
            }

            if (found < 64)
            {
                return total;
            }
        }
    }

    private static void Render(List<int> changes, int width, Span<byte> row)
    {
        row.Clear();
        for (var i = 0; i < changes.Count; i += 2)
        {
            var end = i + 1 < changes.Count ? changes[i + 1] : width;
            for (var x = changes[i]; x < end; x++)
            {
                row[x >> 3] |= (byte)(0x80 >> (x & 7));
            }
        }
    }

    private ref struct BitReader
    {
        private readonly ReadOnlySpan<byte> _input;
        private long _bit;

        public BitReader(ReadOnlySpan<byte> input)
        {
            _input = input;
        }

        public int Read(int count)
        {
            var value = 0;
            for (var i = 0; i < count; i++, _bit++)
            {
                if (_bit >> 3 >= _input.Length)
                {
                    return -1;
                }

                value = (value << 1) | ((_input[(int)(_bit >> 3)] >> (7 - (int)(_bit & 7))) & 1);
            }

            return value;
        }

        public void AlignToByte() => _bit = (_bit + 7) & ~7L;

        public void SkipEndOfLine()
        {
            var at = _bit;
            var zeros = 0;
            while ((at >> 3) < _input.Length && ((_input[(int)(at >> 3)] >> (7 - (int)(at & 7))) & 1) == 0)
            {
                zeros++;
                at++;
            }

            if (zeros >= 11 && (at >> 3) < _input.Length)
            {
                _bit = at + EndOfLine;
            }
        }
    }
}
