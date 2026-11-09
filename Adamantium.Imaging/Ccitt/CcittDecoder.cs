using System;

namespace Adamantium.Imaging.Ccitt;

/// <summary>CCITT fax decoding (ITU-T T.4 and T.6): modified Huffman run lengths, Group 3 one- and two-dimensional, and
/// Group 4. Rows come out packed one bit a pixel, most significant first, each row starting on a byte. A code that is
/// not one - an end-of-block code among them - ends the data: the rows after it are white.</summary>
public static class CcittDecoder
{
    /// <summary>The widest row the decoder takes.</summary>
    public const int MaxColumns = 1 << 24;

    private const int EndOfBlock = 0x001001;

    /// <summary>Decodes <paramref name="input"/> into as many rows as fill <paramref name="output"/> and returns the
    /// number of rows the data gave.</summary>
    public static int Decode(ReadOnlySpan<byte> input, Span<byte> output, CcittOptions options) =>
        Decode(input, output, options, out _);

    /// <summary>Decodes as <see cref="Decode(ReadOnlySpan{byte}, Span{byte}, CcittOptions)"/> and says how many
    /// bytes the rows took, an end-of-block code after them included.</summary>
    internal static int Decode(ReadOnlySpan<byte> input, Span<byte> output, CcittOptions options, out int bytesRead)
    {
        ArgumentNullException.ThrowIfNull(options);
        var rowBytes = RowBytes(options);
        var rows = output.Length / rowBytes;
        if (options.Rows > 0)
        {
            rows = Math.Min(rows, options.Rows);
        }

        var decoder = new CcittRowDecoder(options);
        var reader = new CcittBitReader(input);
        var decoded = 0;
        while (decoded < rows)
        {
            var start = reader.Position;
            if (!decoder.Next(ref reader, output.Slice(decoded * rowBytes, rowBytes)))
            {
                reader.Position = start;
                break;
            }

            decoded++;
        }

        if (options.K < 0 && reader.Peek(24) == EndOfBlock)
        {
            reader.Skip(24);
        }

        bytesRead = reader.BytesTaken(input.Length);
        output.Slice(decoded * rowBytes, (rows - decoded) * rowBytes).Fill(options.BlackIs1 ? (byte)0 : (byte)0xFF);
        return decoded;
    }

    /// <summary>Decodes <paramref name="input"/> into <see cref="CcittOptions.Rows"/> rows, or as many as the data
    /// gives when that is 0, but never more than <paramref name="maxLength"/> bytes.</summary>
    public static byte[] Decode(ReadOnlySpan<byte> input, CcittOptions options, long maxLength, out int rows)
    {
        ArgumentNullException.ThrowIfNull(options);
        var rowBytes = RowBytes(options);
        var limit = (int)Math.Min(Math.Max(maxLength, 0) / rowBytes, int.MaxValue / rowBytes);
        if (options.Rows > 0)
        {
            rows = Math.Min(options.Rows, limit);
            var output = new byte[rows * rowBytes];
            Decode(input, output, options);
            return output;
        }

        var decoder = new CcittRowDecoder(options);
        var reader = new CcittBitReader(input);
        var buffer = new byte[rowBytes * Math.Min(limit, 64)];
        rows = 0;
        while (rows < limit && !reader.IsAtEnd)
        {
            if ((rows + 1) * rowBytes > buffer.Length)
            {
                Array.Resize(ref buffer, (int)Math.Min((long)buffer.Length * 2, (long)limit * rowBytes));
            }

            if (!decoder.Next(ref reader, buffer.AsSpan(rows * rowBytes, rowBytes)))
            {
                break;
            }

            rows++;
        }

        Array.Resize(ref buffer, rows * rowBytes);
        return buffer;
    }

    private static int RowBytes(CcittOptions options)
    {
        if (options.Columns is < 1 or > MaxColumns)
        {
            throw new ArgumentException("A fax row is from 1 to " + MaxColumns + " pixels wide.", nameof(options));
        }

        return (options.Columns + 7) / 8;
    }
}
