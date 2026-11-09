using System;

namespace Adamantium.Imaging.Jbig2;

/// <summary>A bilevel image of JBIG2, one byte a pixel, 1 black; pixels outside it read as 0.</summary>
internal sealed class Jbig2Bitmap
{
    public Jbig2Bitmap(int width, int height)
    {
        if (width < 0 || height < 0 || (long)width * height > Array.MaxLength)
        {
            throw new System.IO.InvalidDataException("A JBIG2 bitmap of " + width + " by " + height + " is too big.");
        }

        Width = width;
        Height = height;
        Pixels = new byte[width * height];
    }

    public int Width { get; }

    public int Height { get; }

    public byte[] Pixels { get; }

    public int Get(int x, int y) =>
        (uint)x < (uint)Width && (uint)y < (uint)Height ? Pixels[y * Width + x] : 0;

    public void Set(int x, int y, int value) => Pixels[y * Width + x] = (byte)value;

    public void Fill(int value) => Array.Fill(Pixels, (byte)value);

    /// <summary>Draws <paramref name="source"/> with its top left at (<paramref name="x"/>, <paramref name="y"/>),
    /// clipped to this bitmap.</summary>
    public void Combine(Jbig2Bitmap source, int x, int y, Jbig2Combination combination)
    {
        var left = Math.Max(0, -(long)x);
        var top = Math.Max(0, -(long)y);
        var right = Math.Min(source.Width, (long)Width - x);
        var bottom = Math.Min(source.Height, (long)Height - y);
        for (var sy = (int)top; sy < bottom; sy++)
        {
            var target = (int)((y + sy) * (long)Width + x);
            var from = sy * source.Width;
            for (var sx = (int)left; sx < right; sx++)
            {
                var s = source.Pixels[from + sx];
                ref var d = ref Pixels[target + sx];
                d = combination switch
                {
                    Jbig2Combination.Or => (byte)(d | s),
                    Jbig2Combination.And => (byte)(d & s),
                    Jbig2Combination.Xor => (byte)(d ^ s),
                    Jbig2Combination.Xnor => (byte)(1 - (d ^ s)),
                    _ => s,
                };
            }
        }
    }

    /// <summary>The part of this bitmap at the given place, outside pixels 0.</summary>
    public Jbig2Bitmap Crop(int x, int y, int width, int height)
    {
        var part = new Jbig2Bitmap(width, height);
        CopyTo(part, x, y);
        return part;
    }

    /// <summary>Fills <paramref name="part"/> with the pixels of this bitmap from (<paramref name="x"/>,
    /// <paramref name="y"/>) on, outside pixels 0.</summary>
    public void CopyTo(Jbig2Bitmap part, int x, int y)
    {
        for (var row = 0; row < part.Height; row++)
        {
            for (var column = 0; column < part.Width; column++)
            {
                part.Pixels[row * part.Width + column] = (byte)Get(x + column, y + row);
            }
        }
    }
}
