using System;

namespace Adamantium.Pdf.Filters;

/// <summary>The <c>Predictor</c> of <c>FlateDecode</c> and <c>LZWDecode</c> (ISO 32000-2, 7.4.4.4): 2 is TIFF's
/// horizontal differencing, 10 and above PNG's filters, chosen per row by the row's first byte.</summary>
internal static class PdfPredictor
{
    public static Memory<byte> Undo(Memory<byte> data, int predictor, int colors, int bitsPerComponent, int columns)
    {
        if (predictor <= 1 || colors < 1 || bitsPerComponent < 1 || columns < 1)
        {
            return data;
        }

        var bitsPerPixel = (long)colors * bitsPerComponent;
        var rowLength = (bitsPerPixel * columns + 7) / 8;
        if (rowLength <= 0 || rowLength > int.MaxValue / 2)
        {
            return data;
        }

        var bytesPerPixel = (int)Math.Max(1, (bitsPerPixel + 7) / 8);
        if (predictor == 2 && rowLength > data.Length)
        {
            return data;
        }

        if (predictor != 2 && rowLength >= data.Length)
        {
            return UndoPng(data.Span, Math.Max(1, data.Length - 1), bytesPerPixel);
        }

        if (predictor == 2)
        {
            UndoTiff(data.Span, (int)rowLength, colors, bitsPerComponent);
            return data;
        }

        return UndoPng(data.Span, (int)rowLength, bytesPerPixel);
    }

    private static Memory<byte> UndoPng(ReadOnlySpan<byte> data, int rowLength, int bytesPerPixel)
    {
        var rows = (data.Length + rowLength) / (rowLength + 1);
        var output = new byte[rows * rowLength];
        Span<byte> previous = new byte[rowLength];
        var written = 0;
        for (var at = 0; at < data.Length; at += rowLength + 1)
        {
            var filter = data[at];
            var source = data.Slice(at + 1, Math.Min(rowLength, data.Length - at - 1));
            var row = output.AsSpan(written, rowLength);
            source.CopyTo(row);
            Unfilter(filter, row, previous, bytesPerPixel);
            row.CopyTo(previous);
            written += rowLength;
        }

        return output.AsMemory(0, written);
    }

    private static void Unfilter(byte filter, Span<byte> row, ReadOnlySpan<byte> previous, int bytesPerPixel)
    {
        switch (filter)
        {
            case 1:
                for (var i = bytesPerPixel; i < row.Length; i++)
                {
                    row[i] += row[i - bytesPerPixel];
                }

                break;
            case 2:
                for (var i = 0; i < row.Length; i++)
                {
                    row[i] += previous[i];
                }

                break;
            case 3:
                for (var i = 0; i < row.Length; i++)
                {
                    var left = i >= bytesPerPixel ? row[i - bytesPerPixel] : 0;
                    row[i] += (byte)((left + previous[i]) >> 1);
                }

                break;
            case 4:
                for (var i = 0; i < row.Length; i++)
                {
                    var left = i >= bytesPerPixel ? row[i - bytesPerPixel] : 0;
                    var upperLeft = i >= bytesPerPixel ? previous[i - bytesPerPixel] : 0;
                    row[i] += Paeth(left, previous[i], upperLeft);
                }

                break;
        }
    }

    private static byte Paeth(int left, int above, int upperLeft)
    {
        var estimate = left + above - upperLeft;
        var toLeft = Math.Abs(estimate - left);
        var toAbove = Math.Abs(estimate - above);
        var toUpperLeft = Math.Abs(estimate - upperLeft);
        if (toLeft <= toAbove && toLeft <= toUpperLeft)
        {
            return (byte)left;
        }

        return toAbove <= toUpperLeft ? (byte)above : (byte)upperLeft;
    }

    private static void UndoTiff(Span<byte> data, int rowLength, int colors, int bitsPerComponent)
    {
        for (var at = 0; at + rowLength <= data.Length; at += rowLength)
        {
            var row = data.Slice(at, rowLength);
            if (bitsPerComponent == 8)
            {
                for (var i = colors; i < row.Length; i++)
                {
                    row[i] += row[i - colors];
                }
            }
            else if (bitsPerComponent == 16)
            {
                for (var i = colors * 2; i + 1 < row.Length; i += 2)
                {
                    var value = ((row[i] << 8) | row[i + 1]) + ((row[i - colors * 2] << 8) | row[i - colors * 2 + 1]);
                    row[i] = (byte)(value >> 8);
                    row[i + 1] = (byte)value;
                }
            }
            else
            {
                UndoTiffBits(row, colors, bitsPerComponent);
            }
        }
    }

    private static void UndoTiffBits(Span<byte> row, int colors, int bitsPerComponent)
    {
        var mask = (1 << bitsPerComponent) - 1;
        var components = row.Length * 8 / bitsPerComponent;
        for (var i = colors; i < components; i++)
        {
            var value = (Read(row, i, bitsPerComponent) + Read(row, i - colors, bitsPerComponent)) & mask;
            Write(row, i, bitsPerComponent, value);
        }
    }

    private static int Read(ReadOnlySpan<byte> row, int index, int bits)
    {
        var bit = index * bits;
        var shift = 8 - bits - bit % 8;
        return (row[bit / 8] >> shift) & ((1 << bits) - 1);
    }

    private static void Write(Span<byte> row, int index, int bits, int value)
    {
        var bit = index * bits;
        var shift = 8 - bits - bit % 8;
        var mask = ((1 << bits) - 1) << shift;
        row[bit / 8] = (byte)((row[bit / 8] & ~mask) | (value << shift));
    }
}
