using System;
using System.Buffers.Binary;
using System.IO;
using Adamantium.Imaging.Png;
using Adamantium.Vulkan.Core;

namespace Adamantium.Imaging.Gif;

/// <summary>Writes pictures as GIF89a, several frames as an animation. Each frame is shown as the picture's frame is,
/// written as only the rectangle that changed from the one before, the pixels in it that did not change transparent;
/// a frame followed by one with transparent pixels is written whole and cleared after. Each has its own color table:
/// its colors exactly when there are 256 or fewer, else reduced by <see cref="ColorQuantizer"/>. Pixels of alpha below
/// 128 are transparent.</summary>
public static class GifEncoder
{
    // A frame's delay when the picture gives none.
    private const ushort DefaultDelay = 10;

    /// <summary>Writes <paramref name="image"/> to <paramref name="output"/>: gray, RGB and RGBA pixels of 8 or 16 bits,
    /// in either channel order; others throw <see cref="NotSupportedException"/>. Frame delays and the play count are
    /// taken from a <see cref="GifImage"/> or <see cref="PngImage"/>; <paramref name="dither"/> spreads the error of
    /// colors reduced.</summary>
    public static void Encode(IRawBitmap image, Stream output, bool dither = true)
    {
        int width = (int)image.Width, height = (int)image.Height;
        if (width is < 1 or > ushort.MaxValue || height is < 1 or > ushort.MaxValue)
        {
            throw new NotSupportedException($"A GIF is 1 to 65535 pixels a side, not {width}×{height}.");
        }

        var frames = Math.Max(1u, image.FramesCount);
        output.Write("GIF89a"u8);
        Span<byte> header = stackalloc byte[10];
        BinaryPrimitives.WriteUInt16LittleEndian(header, (ushort)width);
        BinaryPrimitives.WriteUInt16LittleEndian(header[2..], (ushort)height);
        // No global table: each frame has its own; 8 bits of color resolution.
        (header[4], header[5], header[6]) = (0x70, 0, 0);
        output.Write(header[..7]);
        // NETSCAPE2.0: the repeats after the first play, 0 for forever; an animation played once has none.
        var plays = image.NumberOfReplays;
        if (frames > 1 && plays != 1)
        {
            var loops = (ushort)(plays == 0 ? 0 : Math.Min(plays - 1, ushort.MaxValue));
            output.Write([0x21, 0xFF, 11, .. "NETSCAPE2.0"u8, 3, 1, (byte)loops, (byte)(loops >> 8), 0]);
        }

        // What is under the frame about to be drawn: the frame before when it stays, nothing when it was cleared.
        byte[] under = null;
        var current = Pixels(image, 0);
        for (uint f = 0; f < frames; f++)
        {
            var next = f + 1 < frames ? Pixels(image, f + 1) : null;
            var cleared = next != null && AnyTransparent(next);
            var (x, y, w, h) = cleared || under == null ? (0, 0, width, height) : Changed(current, under, width, height);
            var region = new byte[w * h * 4];
            for (var row = 0; row < h; row++)
            {
                var from = ((y + row) * width + x) * 4;
                current.AsSpan(from, w * 4).CopyTo(region.AsSpan(row * w * 4));
                if (under == null)
                {
                    continue;
                }

                // Unchanged pixels are left to show through.
                for (var i = 0; i < w * 4; i += 4)
                {
                    if (Same(current, under, from + i))
                    {
                        region[row * w * 4 + i + 3] = 0;
                    }
                }
            }

            // An animation's every frame says which index is transparent, used or not: readers that clear a frame to
            // the background clear it to that, and take a first frame without one as never transparent.
            var quantized = ColorQuantizer.Quantize(region, w, h, frames > 1 ? 255 : 256, dither);
            if (frames > 1 && quantized.TransparentIndex < 0)
            {
                var palette = quantized.Palette;
                Array.Resize(ref palette, palette.Length + 3);
                quantized = quantized with { Palette = palette, Colors = quantized.Colors + 1,
                    TransparentIndex = quantized.Colors };
            }

            var bits = 1;
            while (1 << bits < quantized.Colors)
            {
                bits++;
            }

            var transparent = quantized.TransparentIndex >= 0;
            if (frames > 1 || transparent)
            {
                // Graphic Control Extension: disposal, transparency, delay.
                var disposal = cleared ? GifDisposal.Background : GifDisposal.None;
                var delay = Delay(image, f);
                output.Write([
                    0x21, 0xF9, 4, (byte)(((int)disposal << 2) | (transparent ? 1 : 0)), (byte)delay,
                    (byte)(delay >> 8), (byte)Math.Max(0, quantized.TransparentIndex), 0
                ]);
            }

            header.Clear();
            header[0] = 0x2C;
            BinaryPrimitives.WriteUInt16LittleEndian(header[1..], (ushort)x);
            BinaryPrimitives.WriteUInt16LittleEndian(header[3..], (ushort)y);
            BinaryPrimitives.WriteUInt16LittleEndian(header[5..], (ushort)w);
            BinaryPrimitives.WriteUInt16LittleEndian(header[7..], (ushort)h);
            header[9] = (byte)(0x80 | (bits - 1));
            output.Write(header);
            var table = new byte[3 << bits];
            quantized.Palette.CopyTo(table, 0);
            output.Write(table);
            // LZW codes start a bit wider than the indices, 2 bits at least.
            var codeSize = Math.Max(2, bits);
            output.WriteByte((byte)codeSize);
            GifLzw.Encode(quantized.Indices, codeSize, output);
            under = cleared ? new byte[current.Length] : current;
            current = next;
        }

        output.WriteByte(0x3B);
        output.Flush();
    }

    private static byte[] Pixels(IRawBitmap image, uint frame) => image.GetRgba8Pixels(frame)
        ?? throw new NotSupportedException($"GIF is not written from {(Format)image.PixelFormat} pixels.");

    private static bool AnyTransparent(byte[] rgba)
    {
        for (var i = 3; i < rgba.Length; i += 4)
        {
            if (rgba[i] < 128)
            {
                return true;
            }
        }

        return false;
    }

    // Pixels the same as shown: both transparent, or both opaque of one color.
    private static bool Same(byte[] a, byte[] b, int at) => a[at + 3] < 128
        ? b[at + 3] < 128
        : b[at + 3] >= 128 && a[at] == b[at] && a[at + 1] == b[at + 1] && a[at + 2] == b[at + 2];

    // The rectangle of pixels that differ from those under them; one pixel when none does.
    private static (int X, int Y, int Width, int Height) Changed(byte[] current, byte[] under, int width, int height)
    {
        int left = width, top = height, right = -1, bottom = -1;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (!Same(current, under, (y * width + x) * 4))
                {
                    (left, right) = (Math.Min(left, x), Math.Max(right, x));
                    (top, bottom) = (Math.Min(top, y), Math.Max(bottom, y));
                }
            }
        }

        return right < 0 ? (0, 0, 1, 1) : (left, top, right - left + 1, bottom - top + 1);
    }

    private static ushort Delay(IRawBitmap image, uint frame) => image switch
    {
        GifImage gif when frame < gif.Frames.Count => gif.Frames[(int)frame].DelayCentiseconds,
        PngImage png when frame < png.Frames.Count =>
            (ushort)Math.Min(ushort.MaxValue, Math.Round(png.Frames[(int)frame].Delay.TotalMilliseconds / 10)),
        _ => DefaultDelay
    };
}
