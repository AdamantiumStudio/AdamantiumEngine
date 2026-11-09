using System;
using System.IO;

namespace Adamantium.Imaging.Jbig2;

/// <summary>JBIG2 decoding (ITU-T T.88): generic, refinement, text and halftone regions, symbol and pattern
/// dictionaries, standard and custom Huffman tables, pages of unknown height. It draws the first page of the data.
/// Rows come out packed one bit a pixel, most significant first, each row starting on a byte, black as 1.</summary>
public static class Jbig2Decoder
{
    private static ReadOnlySpan<byte> FileId => [0x97, 0x4A, 0x42, 0x32, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>Decodes embedded JBIG2 data - segments with no file header, as PDF holds them - after the segments of
    /// <paramref name="globals"/>, or a JBIG2 file when the data starts with its header.</summary>
    /// <param name="data">The segments of the page.</param>
    /// <param name="globals">The segments the page shares with others, or empty.</param>
    /// <param name="maxPixels">The most pixels a page or any bitmap on the way may have.</param>
    /// <param name="width">The page's width.</param>
    /// <param name="height">The page's height.</param>
    /// <exception cref="InvalidDataException">The data is not JBIG2, or asks for more than
    /// <paramref name="maxPixels"/> allow.</exception>
    /// <exception cref="NotSupportedException">The data uses the extended generic template.</exception>
    public static byte[] Decode(ReadOnlySpan<byte> data, ReadOnlySpan<byte> globals, long maxPixels, out int width,
        out int height)
    {
        var limits = new Jbig2Limits(Math.Max(0, maxPixels));
        var document = new Jbig2Document(limits);
        try
        {
            if (!globals.IsEmpty)
            {
                document.ReadSequential(globals.ToArray());
            }

            if (data.StartsWith(FileId))
            {
                document.ReadFile(data.ToArray());
            }
            else
            {
                document.ReadSequential(data.ToArray());
            }
        }
        catch (Exception exception) when (exception is IndexOutOfRangeException or ArgumentException or
                                              OverflowException)
        {
            throw new InvalidDataException("JBIG2 data is broken: " + exception.Message, exception);
        }

        var page = document.Page?.Bitmap ?? throw new InvalidDataException("JBIG2 data has no page.");
        width = page.Width;
        height = page.Height;
        var rowBytes = (width + 7) / 8;
        var rows = new byte[(long)rowBytes * height];
        for (var y = 0; y < height; y++)
        {
            var from = y * width;
            var to = y * rowBytes;
            for (var x = 0; x < width; x++)
            {
                if (page.Pixels[from + x] != 0)
                {
                    rows[to + (x >> 3)] |= (byte)(0x80 >> (x & 7));
                }
            }
        }

        return rows;
    }
}
