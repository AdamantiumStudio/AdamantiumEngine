using System;
using System.IO;
using Adamantium.Imaging.Ccitt;
using Adamantium.Imaging.Mq;

namespace Adamantium.Imaging.Jbig2;

/// <summary>Generic region decoding (ITU-T T.88, 6.2): arithmetic with templates 0 to 3, adaptive pixels and typical
/// prediction, or MMR. Each context keeps the standard's bit order - the typical prediction contexts depend on it.
/// The fixed pixels of a template slide along their rows; only the adaptive ones are read anew.</summary>
internal static class Jbig2GenericRegion
{
    private static readonly Jbig2GenericTemplate[] Templates =
    [
        new(-1, 3, 12, -2, 5, 5, 4, [4, 10, 11, 15], 0x9B25),
        new(-1, 4, 9, -2, 5, 4, 3, [3], 0x0795),
        new(-1, 3, 7, -2, 4, 3, 2, [2], 0x00E5),
        new(0, 0, 0, -3, 5, 5, 4, [4], 0x0195),
    ];

    public static int ContextCount(int template) => template switch
    {
        0 => 1 << 16,
        1 => 1 << 13,
        _ => 1 << 10,
    };

    /// <summary>Decodes a region with the arithmetic decoder.</summary>
    public static Jbig2Bitmap Decode(MqDecoder decoder, byte[] contexts, Jbig2GenericParameters parameters,
        Jbig2Limits limits)
    {
        var bitmap = limits.Bitmap(parameters.Width, parameters.Height);
        limits.Spend((long)parameters.Width * parameters.Height);
        if (parameters.Template is < 0 or > 3)
        {
            throw new InvalidDataException("A JBIG2 generic region has no template " + parameters.Template + ".");
        }

        var template = Templates[parameters.Template];
        var at = parameters.At ?? Jbig2GenericParameters.DefaultAt(parameters.Template);
        var atCount = template.AtBits.Length;
        if (at.Length < 2 * atCount)
        {
            throw new InvalidDataException("A JBIG2 generic region has too few adaptive pixels.");
        }

        var width = bitmap.Width;
        var pixels = bitmap.Pixels;
        var skip = parameters.Skip;
        var typical = 0;
        var up2Mask = (1 << template.Up2Count) - 1;
        var up1Mask = (1 << template.Up1Count) - 1;
        var leftMask = (1 << template.LeftCount) - 1;
        for (var y = 0; y < bitmap.Height; y++)
        {
            if (parameters.TypicalPrediction)
            {
                typical ^= decoder.Decode(contexts, template.TypicalContext);
                if (typical == 1)
                {
                    if (y > 0)
                    {
                        Array.Copy(pixels, (y - 1) * width, pixels, y * width, width);
                    }

                    continue;
                }
            }

            var up2 = Window(bitmap, y - 2, template.Up2Left, template.Up2Count);
            var up1 = Window(bitmap, y - 1, template.Up1Left, template.Up1Count);
            var left = 0;
            var row = y * width;
            for (var x = 0; x < width; x++)
            {
                up2 = ((up2 << 1) | bitmap.Get(x + template.Up2Left + template.Up2Count - 1, y - 2)) & up2Mask;
                up1 = ((up1 << 1) | bitmap.Get(x + template.Up1Left + template.Up1Count - 1, y - 1)) & up1Mask;
                var pixel = 0;
                if (skip == null || skip.Get(x, y) == 0)
                {
                    var context = (up2 << template.Up2Shift) | (up1 << template.Up1Shift) | left;
                    for (var i = 0; i < atCount; i++)
                    {
                        context |= bitmap.Get(x + at[2 * i], y + at[2 * i + 1]) << template.AtBits[i];
                    }

                    pixel = decoder.Decode(contexts, context);
                    pixels[row + x] = (byte)pixel;
                }

                left = ((left << 1) | pixel) & leftMask;
            }
        }

        return bitmap;
    }

    /// <summary>Decodes a region coded with MMR and says how many bytes it took.</summary>
    public static Jbig2Bitmap DecodeMmr(byte[] data, int start, int end, int width, int height, Jbig2Limits limits,
        out int bytesRead)
    {
        var bitmap = limits.Bitmap(width, height);
        limits.Spend((long)width * height);
        if (width == 0 || height == 0)
        {
            bytesRead = 0;
            return bitmap;
        }

        var rowBytes = (width + 7) / 8;
        var rows = new byte[(long)rowBytes * height];
        CcittDecoder.Decode(data.AsSpan(start, end - start), rows,
            new CcittOptions { K = -1, Columns = width, BlackIs1 = true }, out bytesRead);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                bitmap.Pixels[y * width + x] = (byte)((rows[y * rowBytes + (x >> 3)] >> (7 - (x & 7))) & 1);
            }
        }

        return bitmap;
    }

    private static int Window(Jbig2Bitmap bitmap, int y, int first, int count)
    {
        var window = 0;
        for (var i = 0; i < count - 1; i++)
        {
            window = (window << 1) | bitmap.Get(first + i, y);
        }

        return window;
    }
}
