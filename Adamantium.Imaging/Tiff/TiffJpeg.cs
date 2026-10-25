using System;
using System.IO;
using Adamantium.Imaging.Jpeg;
using Adamantium.Imaging.Jpeg.Decoder;

namespace Adamantium.Imaging.Tiff;

/// <summary>JPEG strips and tiles (TIFF Technical Note 2): each one a JPEG stream, its tables perhaps shared through
/// the JPEGTables tag, decoded by the engine's JPEG decoder.</summary>
internal static class TiffJpeg
{
    /// <summary>Decodes one strip or tile into <paramref name="output"/> as rows of <paramref name="width"/> pixels of
    /// <paramref name="samples"/> 8-bit samples: gray, or RGB - YCbCr data is converted. A stream whose frame is larger
    /// than <paramref name="width"/> × <paramref name="height"/>, or that the JPEG decoder cannot read, throws
    /// <see cref="InvalidDataException"/>.</summary>
    public static void Decode(ReadOnlySpan<byte> input, byte[] tables, bool rgb, int width, int height, int samples,
        Span<byte> output)
    {
        byte[] stream = tables is { Length: > 4 } && input is [0xFF, 0xD8, ..]
            ? [.. tables.AsSpan(0, tables.Length - 2), .. input[2..]]
            : input.ToArray();
        var (frames, frameWidth, frameHeight) = Frames(stream);
        if (frames != 1 || frameWidth == 0 || frameHeight == 0 || frameWidth > width || frameHeight > height)
        {
            throw new InvalidDataException($"A JPEG strip or tile of {frames} frames, {frameWidth}×{frameHeight}, in "
                                           + $"one of {width}×{height}.");
        }

        JpegImage image;
        try
        {
            image = new JpegDecoder(new MemoryStream(stream)) { ComponentsAreRgb = rgb }.Decode();
        }
        catch (Exception exception) when (exception is not (InvalidDataException or NotSupportedException
                                              or OutOfMemoryException))
        {
            throw new InvalidDataException("A JPEG strip or tile is broken: " + exception.Message, exception);
        }

        var pixels = image.GetRawPixels(0);
        var rows = Math.Min(output.Length / (width * samples), (int)image.Height);
        var columns = Math.Min(width, (int)image.Width);
        for (var y = 0; y < rows; y++)
        {
            for (var x = 0; x < columns; x++)
            {
                for (var s = 0; s < samples; s++)
                {
                    output[(y * width + x) * samples + s] = pixels[((long)y * image.Width + x) * 4 + s];
                }
            }
        }
    }

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
