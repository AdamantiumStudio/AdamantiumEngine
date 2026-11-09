using System;
using System.Collections.Generic;

namespace Adamantium.Imaging.Ccitt;

/// <summary>Decodes fax rows one after another, keeping the row before as the reference of two-dimensional coding.
/// A row is the list of its changes of color, from white at its left edge.</summary>
internal sealed class CcittRowDecoder
{
    private readonly CcittOptions _options;
    private readonly int _width;
    private List<int> _reference = [];
    private List<int> _line = [];
    private bool _first = true;

    public CcittRowDecoder(CcittOptions options)
    {
        _options = options;
        _width = options.Columns;
    }

    /// <summary>Decodes the next row into <paramref name="row"/>; false when the data has no more rows.</summary>
    public bool Next(ref CcittBitReader reader, Span<byte> row)
    {
        if (_options.EncodedByteAlign && !_first)
        {
            reader.AlignToByte();
        }

        _first = false;
        _line.Clear();
        bool twoDimensional;
        if (_options.K < 0)
        {
            twoDimensional = true;
        }
        else
        {
            reader.SkipEndOfLine();
            twoDimensional = _options.K > 0 && reader.Read(1) == 0;
        }

        var ok = twoDimensional ? DecodeTwoDimensional(ref reader) : DecodeOneDimensional(ref reader);
        if (!ok || reader.IsOverrun)
        {
            return false;
        }

        Render(row);
        (_reference, _line) = (_line, _reference);
        return true;
    }

    private bool DecodeOneDimensional(ref CcittBitReader reader)
    {
        var position = 0;
        var black = false;
        while (position < _width)
        {
            var run = ReadRun(ref reader, black);
            if (run < 0)
            {
                return false;
            }

            position = (int)Math.Min(_width, (long)position + run);
            AddChange(position);
            black = !black;
        }

        return true;
    }

    private bool DecodeTwoDimensional(ref CcittBitReader reader)
    {
        var a0 = -1;
        var black = false;
        var scan = 0;
        while (a0 < _width)
        {
            var (b1, b2) = Below(ref scan, a0, black);
            if (reader.Read(1) == 1)
            {
                Change(ref a0, ref black, b1);
                continue;
            }

            if (reader.Read(1) == 1)
            {
                var vertical = reader.Read(1) == 1 ? 1 : -1;
                Change(ref a0, ref black, b1 + vertical);
                continue;
            }

            if (reader.Read(1) == 1)
            {
                var start = Math.Max(a0, 0);
                var first = ReadRun(ref reader, black);
                var second = first < 0 ? -1 : ReadRun(ref reader, !black);
                if (second < 0)
                {
                    return false;
                }

                var a1 = (int)Math.Min(_width, (long)start + first);
                var a2 = (int)Math.Min(_width, (long)a1 + second);
                AddChange(a1);
                AddChange(a2);
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
                Change(ref a0, ref black, b1 + (code == 3 ? 2 : -2));
                continue;
            }

            var third = code == 1 ? reader.Read(1) : -1;
            if (third < 0)
            {
                return false;
            }

            Change(ref a0, ref black, b1 + (third == 1 ? 3 : -3));
        }

        return true;
    }

    private (int B1, int B2) Below(ref int scan, int a0, bool black)
    {
        while (scan < _reference.Count && _reference[scan] <= a0)
        {
            scan++;
        }

        var i = scan;
        if ((i % 2 == 1) != black)
        {
            i++;
        }

        var b1 = i < _reference.Count ? _reference[i] : _width;
        var b2 = i + 1 < _reference.Count ? _reference[i + 1] : _width;
        return (b1, b2);
    }

    private void Change(ref int a0, ref bool black, int a1)
    {
        a1 = Math.Clamp(a1, Math.Max(a0, 0), _width);
        AddChange(a1);
        a0 = a1;
        black = !black;
    }

    private void AddChange(int position)
    {
        if (position < _width)
        {
            _line.Add(position);
        }
    }

    private int ReadRun(ref CcittBitReader reader, bool black)
    {
        var codes = black ? CcittCodes.Black : CcittCodes.White;
        var total = 0;
        while (true)
        {
            var entry = codes[reader.Peek(CcittCodes.Bits)];
            if (entry == 0)
            {
                return -1;
            }

            reader.Skip(entry & 15);
            if (reader.IsOverrun)
            {
                return -1;
            }

            var run = entry >> 4;
            total += run;
            if (total > _width)
            {
                return -1;
            }

            if (run < 64)
            {
                return total;
            }
        }
    }

    private void Render(Span<byte> row)
    {
        row.Clear();
        for (var i = 0; i < _line.Count; i += 2)
        {
            var end = i + 1 < _line.Count ? _line[i + 1] : _width;
            Fill(row, _line[i], end);
        }

        if (!_options.BlackIs1)
        {
            for (var i = 0; i < row.Length; i++)
            {
                row[i] = (byte)~row[i];
            }
        }
    }

    private static void Fill(Span<byte> row, int start, int end)
    {
        while (start < end && (start & 7) != 0)
        {
            row[start >> 3] |= (byte)(0x80 >> (start & 7));
            start++;
        }

        while (start + 8 <= end)
        {
            row[start >> 3] = 0xFF;
            start += 8;
        }

        while (start < end)
        {
            row[start >> 3] |= (byte)(0x80 >> (start & 7));
            start++;
        }
    }
}
