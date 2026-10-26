using System;
using System.Buffers;
using System.Buffers.Binary;
using System.IO;
using Adamantium.Imaging.Jpeg;

namespace Adamantium.Imaging.Tiff;

/// <summary>JPEG strips and tiles (TIFF Technical Note 2): each one a JPEG stream, its tables perhaps shared through
/// the JPEGTables tag, decoded by the engine's JPEG decoder.</summary>
internal static class TiffJpeg
{
    /// <summary>Decodes one strip or tile into <paramref name="output"/> as rows of <paramref name="width"/> pixels of
    /// <paramref name="samples"/> samples of <paramref name="bits"/> - 8, or 12 packed as TIFF keeps them: gray, RGB -
    /// YCbCr data converted - or CMYK. Rows of the frame past the output are left out. A stream whose frame is wider
    /// than <paramref name="width"/> or taller than <paramref name="height"/>, or that does not match the samples, or
    /// that the JPEG decoder cannot read, throws <see cref="InvalidDataException"/>.</summary>
    public static void Decode(ReadOnlySpan<byte> input, JpegStream tables, bool rgb, int width, int height,
        int samples, int bits, Span<byte> output)
    {
        var (frames, frameWidth, frameHeight) = Frames(input);
        if (frames != 1 || frameWidth == 0 || frameHeight == 0 || frameWidth > width || frameHeight > height)
        {
            throw new InvalidDataException($"A JPEG strip or tile of {frames} frames, {frameWidth}×{frameHeight}, in "
                                           + $"one of {width}×{height}.");
        }

        try
        {
            if (bits == 8)
            {
                Check(JpegDecoder.DecodeSamples(input, tables, rgb, output, width * samples,
                    output.Length / (width * samples)), samples, bits);
                return;
            }

            // Samples of 12 bits come packed, as TIFF keeps them: two in three bytes, most significant first.
            var packedRow = (width * samples * 12 + 7) / 8;
            var rows = Math.Min(height, output.Length / packedRow);
            var wide = ArrayPool<byte>.Shared.Rent(width * samples * 2 * rows);
            try
            {
                Check(JpegDecoder.DecodeSamples(input, tables, rgb, wide, width * samples * 2, rows), samples, bits);
                for (var y = 0; y < rows; y++)
                {
                    Pack(wide.AsSpan(y * width * samples * 2, width * samples * 2), output.Slice(y * packedRow,
                        packedRow));
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(wide);
            }
        }
        catch (Exception exception) when (exception is not (InvalidDataException or NotSupportedException
                                              or OutOfMemoryException))
        {
            throw new InvalidDataException("A JPEG strip or tile is broken: " + exception.Message, exception);
        }
    }

    private static void Check((int Width, int Height, int Channels, int Precision) decoded, int samples, int bits)
    {
        if (decoded.Channels != samples || decoded.Precision != bits)
        {
            throw new InvalidDataException($"A JPEG strip or tile of {decoded.Channels} channels of "
                                           + $"{decoded.Precision} bits, in an image of {samples} of {bits}.");
        }
    }

    // 12-bit samples, each in 2 little-endian bytes, packed into a row of 12 bits a sample.
    private static void Pack(ReadOnlySpan<byte> samples, Span<byte> row)
    {
        row.Clear();
        long bit = 0;
        for (var i = 0; i + 1 < samples.Length; i += 2, bit += 12)
        {
            var value = BinaryPrimitives.ReadUInt16LittleEndian(samples[i..]) & 0xFFF;
            var at = (int)(bit >> 3);
            if ((bit & 7) == 0)
            {
                row[at] = (byte)(value >> 4);
                row[at + 1] = (byte)((value & 15) << 4);
            }
            else
            {
                row[at] |= (byte)(value >> 8);
                row[at + 1] = (byte)value;
            }
        }
    }

    /// <summary>The tables the JPEGTables tag holds - an abbreviated JPEG stream (B.5) - read once for all the strips
    /// and tiles; null for none.</summary>
    public static JpegStream Tables(ReadOnlySpan<byte> tag) => tag.Length > 4 ? JpegStream.Tables(tag) : null;

    private static (int Count, int Width, int Height) Frames(ReadOnlySpan<byte> stream)
    {
        var count = 0;
        var (width, height) = (0, 0);
        var at = 0;
        while (at + 1 < stream.Length)
        {
            if (stream[at] != 0xFF || stream[at + 1] == 0xFF)
            {
                at++;
                continue;
            }

            var marker = stream[at + 1];
            if (marker is 0x00 or 0x01 or 0xD8 or 0xD9 or >= 0xD0 and <= 0xD7)
            {
                at += 2;
                continue;
            }

            if (at + 4 > stream.Length)
            {
                break;
            }

            var length = (stream[at + 2] << 8) | stream[at + 3];
            if (marker is >= 0xC0 and <= 0xCF and not (0xC4 or 0xC8 or 0xCC))
            {
                count++;
                if (at + 9 <= stream.Length)
                {
                    (width, height) = ((stream[at + 7] << 8) | stream[at + 8], (stream[at + 5] << 8) | stream[at + 6]);
                }
            }

            at += 2 + Math.Max(length, 2);
        }

        return (count, width, height);
    }
}
