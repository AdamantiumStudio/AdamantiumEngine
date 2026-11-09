using System.IO;
using Adamantium.Imaging.Mq;

namespace Adamantium.Imaging.Jbig2;

/// <summary>Generic refinement region decoding (ITU-T T.88, 6.3): a bitmap coded against a reference, with templates
/// 0 and 1 and typical prediction. The typical prediction context is the one where only the reference pixel under
/// the decoded one is black.</summary>
internal static class Jbig2RefinementRegion
{
    public static int ContextCount(int template) => template == 0 ? 1 << 13 : 1 << 10;

    /// <summary>The adaptive pixels of template 0 by default: one in the new bitmap, one in the reference.</summary>
    public static int[] DefaultAt => [-1, -1, -1, -1];

    public static Jbig2Bitmap Decode(MqDecoder decoder, byte[] contexts, int width, int height, int template,
        Jbig2Bitmap reference, int dx, int dy, bool typicalPrediction, int[] at, Jbig2Limits limits)
    {
        var bitmap = limits.Bitmap(width, height);
        limits.Spend((long)width * height);
        if (template == 0 && (at == null || at.Length < 4))
        {
            throw new InvalidDataException("A JBIG2 refinement region has too few adaptive pixels.");
        }

        var typicalContext = template == 0 ? 0x100 : 0x80;
        var typical = 0;
        for (var y = 0; y < height; y++)
        {
            if (typicalPrediction)
            {
                typical ^= decoder.Decode(contexts, typicalContext);
            }

            for (var x = 0; x < width; x++)
            {
                var rx = x - dx;
                var ry = y - dy;
                if (typical == 1 && Uniform(reference, rx, ry, out var value))
                {
                    bitmap.Set(x, y, value);
                    continue;
                }

                var context = template == 0
                    ? Context0(bitmap, reference, x, y, rx, ry, at)
                    : Context1(bitmap, reference, x, y, rx, ry);
                bitmap.Set(x, y, decoder.Decode(contexts, context));
            }
        }

        return bitmap;
    }

    private static int Context0(Jbig2Bitmap bitmap, Jbig2Bitmap reference, int x, int y, int rx, int ry, int[] at) =>
        bitmap.Get(x - 1, y) |
        (bitmap.Get(x + 1, y - 1) << 1) |
        (bitmap.Get(x, y - 1) << 2) |
        (bitmap.Get(x + at[0], y + at[1]) << 3) |
        (reference.Get(rx + 1, ry + 1) << 4) |
        (reference.Get(rx, ry + 1) << 5) |
        (reference.Get(rx - 1, ry + 1) << 6) |
        (reference.Get(rx + 1, ry) << 7) |
        (reference.Get(rx, ry) << 8) |
        (reference.Get(rx - 1, ry) << 9) |
        (reference.Get(rx + 1, ry - 1) << 10) |
        (reference.Get(rx, ry - 1) << 11) |
        (reference.Get(rx + at[2], ry + at[3]) << 12);

    private static int Context1(Jbig2Bitmap bitmap, Jbig2Bitmap reference, int x, int y, int rx, int ry) =>
        bitmap.Get(x - 1, y) |
        (bitmap.Get(x + 1, y - 1) << 1) |
        (bitmap.Get(x, y - 1) << 2) |
        (bitmap.Get(x - 1, y - 1) << 3) |
        (reference.Get(rx + 1, ry + 1) << 4) |
        (reference.Get(rx, ry + 1) << 5) |
        (reference.Get(rx + 1, ry) << 6) |
        (reference.Get(rx, ry) << 7) |
        (reference.Get(rx - 1, ry) << 8) |
        (reference.Get(rx, ry - 1) << 9);

    private static bool Uniform(Jbig2Bitmap reference, int x, int y, out int value)
    {
        value = reference.Get(x, y);
        for (var j = -1; j <= 1; j++)
        {
            for (var i = -1; i <= 1; i++)
            {
                if (reference.Get(x + i, y + j) != value)
                {
                    return false;
                }
            }
        }

        return true;
    }
}
