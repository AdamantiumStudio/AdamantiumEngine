using System;
using Adamantium.Vulkan.Core;

namespace Adamantium.Imaging;

/// <summary>Shrinks pixels to fit a size - each new pixel the average of the source area it covers, colors weighted by
/// their alpha so transparent pixels lend them nothing. The source is read a row at a time and nothing the size of it
/// is made, so a picture too large for a device can be brought to what the device holds.</summary>
public static class ImageShrink
{
    /// <summary>Whether <see cref="FitWithin"/> shrinks pixels of <paramref name="format"/>: unsigned normalized ones of
    /// 8 or 16 bits a channel, gray to RGBA in either order.</summary>
    public static bool Supports(SurfaceFormat format) => Layout(format).Channels > 0;

    /// <summary>The <paramref name="width"/> × <paramref name="height"/> <paramref name="pixels"/> of
    /// <paramref name="format"/> brought within <paramref name="maxWidth"/> × <paramref name="maxHeight"/>, the aspect
    /// kept - or the pixels themselves when they fit already.</summary>
    public static byte[] FitWithin(byte[] pixels, uint width, uint height, SurfaceFormat format, uint maxWidth,
        uint maxHeight, out uint fittedWidth, out uint fittedHeight)
    {
        (fittedWidth, fittedHeight) = (width, height);
        if (width <= maxWidth && height <= maxHeight)
        {
            return pixels;
        }

        var (channels, bytes, alpha) = Layout(format);
        if (channels == 0)
        {
            throw new NotSupportedException($"{(Format)format} pixels are not shrunk.");
        }

        if (maxWidth == 0 || maxHeight == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxWidth), "A picture is fitted within a size of at least 1×1.");
        }

        var scale = Math.Max((double)width / maxWidth, (double)height / maxHeight);
        fittedWidth = (uint)Math.Clamp(Math.Round(width / scale), 1, maxWidth);
        fittedHeight = (uint)Math.Clamp(Math.Round(height / scale), 1, maxHeight);
        return Shrink(pixels, (int)width, (int)height, (int)fittedWidth, (int)fittedHeight, channels, bytes, alpha);
    }

    // Channels, bytes a channel and which channel is alpha (-1 for none).
    private static (int Channels, int Bytes, int Alpha) Layout(SurfaceFormat format) => (Format)format switch
    {
        Format.R8_UNORM or Format.R8_SRGB => (1, 1, -1),
        Format.R8G8B8_UNORM or Format.R8G8B8_SRGB or Format.B8G8R8_UNORM or Format.B8G8R8_SRGB => (3, 1, -1),
        Format.R8G8B8A8_UNORM or Format.R8G8B8A8_SRGB or Format.B8G8R8A8_UNORM or Format.B8G8R8A8_SRGB => (4, 1, 3),
        Format.R16_UNORM => (1, 2, -1),
        Format.R16G16B16_UNORM => (3, 2, -1),
        Format.R16G16B16A16_UNORM => (4, 2, 3),
        _ => (0, 0, -1)
    };

    private static byte[] Shrink(byte[] source, int width, int height, int targetWidth, int targetHeight, int channels,
        int bytes, int alpha)
    {
        double scaleX = (double)width / targetWidth, scaleY = (double)height / targetHeight;
        var top = bytes == 1 ? 255.0 : 65535.0;
        var pixel = channels * bytes;
        var target = new byte[(long)targetWidth * targetHeight * pixel];
        // A source row spread across the target's columns, and the target row being summed - colors premultiplied by
        // alpha, alpha last.
        var row = new double[targetWidth * channels];
        var sums = new double[targetWidth * channels];
        var below = new double[targetWidth * channels];
        var targetRow = 0;
        for (var y = 0; y < height; y++)
        {
            Array.Clear(row);
            var line = source.AsSpan(y * width * pixel, width * pixel);
            for (var x = 0; x < width; x++)
            {
                var (first, firstWeight, second) = Split(x, scaleX, targetWidth);
                var a = alpha < 0 ? 1.0 : Read(line, x * pixel + alpha * bytes, bytes) / top;
                for (var c = 0; c < channels; c++)
                {
                    var value = c == alpha ? a : Read(line, x * pixel + c * bytes, bytes) * a;
                    row[first * channels + c] += value * firstWeight;
                    if (second >= 0)
                    {
                        row[second * channels + c] += value * (1 - firstWeight);
                    }
                }
            }

            var (rowFirst, rowWeight, rowSecond) = Split(y, scaleY, targetHeight);
            for (var i = 0; i < row.Length; i++)
            {
                sums[i] += row[i] * rowWeight;
                if (rowSecond >= 0)
                {
                    below[i] += row[i] * (1 - rowWeight);
                }
            }

            // A target row is whole once the source rows over it are summed: written, and the one below goes on.
            if (rowSecond >= 0 || y == height - 1 || Split(y + 1, scaleY, targetHeight).First != rowFirst)
            {
                Write(sums, target.AsSpan(targetRow * targetWidth * pixel, targetWidth * pixel), channels, bytes, alpha,
                    top, scaleX * scaleY);
                targetRow++;
                (sums, below) = (below, sums);
                Array.Clear(below);
            }
        }

        return target;
    }

    // The target index a source index falls in, how much of it does, and the next target index its rest falls in, or
    // -1. A source index covers [i, i + 1), a target index [t * scale, (t + 1) * scale).
    private static (int First, double Weight, int Second) Split(int index, double scale, int count)
    {
        var first = Math.Min((int)(index / scale), count - 1);
        var end = (first + 1) * scale;
        return index + 1 <= end || first == count - 1 ? (first, 1, -1) : (first, end - index, first + 1);
    }

    private static double Read(ReadOnlySpan<byte> line, int at, int bytes) =>
        bytes == 1 ? line[at] : line[at] | line[at + 1] << 8;

    // Sums of an area into a row of pixels: alpha its average, colors their sum over alpha's.
    private static void Write(double[] sums, Span<byte> target, int channels, int bytes, int alpha, double top,
        double area)
    {
        for (var x = 0; x < sums.Length / channels; x++)
        {
            var weight = alpha < 0 ? area : sums[x * channels + alpha];
            for (var c = 0; c < channels; c++)
            {
                var value = c == alpha ? sums[x * channels + c] / area * top
                    : weight > 0 ? sums[x * channels + c] / weight : 0;
                var v = (int)Math.Clamp(Math.Round(value), 0, top);
                var at = (x * channels + c) * bytes;
                target[at] = (byte)v;
                if (bytes == 2)
                {
                    target[at + 1] = (byte)(v >> 8);
                }
            }
        }
    }
}
