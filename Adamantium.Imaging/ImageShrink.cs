using System;
using System.Threading.Tasks;
using Adamantium.Vulkan.Core;

namespace Adamantium.Imaging;

/// <summary>Shrinks pixels to fit a size - each new pixel the average of the source area it covers, colors weighted by
/// their alpha so transparent pixels lend them nothing, sRGB ones averaged as light. The source is read a row at a time,
/// in bands on every core, and nothing the size of it is made, so a picture too large for a device can be brought to
/// what the device holds.</summary>
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
        if (width <= maxWidth && height <= maxHeight || width == 0 || height == 0)
        {
            return pixels;
        }

        var (channels, bytes, alpha, srgb) = Layout(format);
        if (channels == 0)
        {
            throw new NotSupportedException($"{(Format)format} pixels are not shrunk.");
        }

        if (maxWidth == 0 || maxHeight == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxWidth), "A picture is fitted within a size of at least 1×1.");
        }

        if (pixels.LongLength < (long)width * height * channels * bytes)
        {
            throw new ArgumentException($"{pixels.LongLength} bytes are fewer than {width}×{height} pixels hold.",
                nameof(pixels));
        }

        var scale = Math.Max((double)width / maxWidth, (double)height / maxHeight);
        fittedWidth = (uint)Math.Clamp(Math.Round(width / scale), 1, maxWidth);
        fittedHeight = (uint)Math.Clamp(Math.Round(height / scale), 1, maxHeight);
        return Shrink(pixels, (int)width, (int)height, (int)fittedWidth, (int)fittedHeight, channels, bytes, alpha,
            srgb);
    }

    // Channels, bytes a channel, which channel is alpha (-1 for none) and whether the colors are sRGB-encoded.
    private static (int Channels, int Bytes, int Alpha, bool Srgb) Layout(SurfaceFormat format) => (Format)format switch
    {
        Format.R8_UNORM => (1, 1, -1, false),
        Format.R8_SRGB => (1, 1, -1, true),
        Format.R8G8B8_UNORM or Format.B8G8R8_UNORM => (3, 1, -1, false),
        Format.R8G8B8_SRGB or Format.B8G8R8_SRGB => (3, 1, -1, true),
        Format.R8G8B8A8_UNORM or Format.B8G8R8A8_UNORM => (4, 1, 3, false),
        Format.R8G8B8A8_SRGB or Format.B8G8R8A8_SRGB => (4, 1, 3, true),
        Format.R16_UNORM => (1, 2, -1, false),
        Format.R16G16B16_UNORM => (3, 2, -1, false),
        Format.R16G16B16A16_UNORM => (4, 2, 3, false),
        _ => (0, 0, -1, false)
    };

    private static byte[] Shrink(byte[] source, int width, int height, int targetWidth, int targetHeight, int channels,
        int bytes, int alpha, bool srgb)
    {
        double scaleX = (double)width / targetWidth, scaleY = (double)height / targetHeight;
        var top = bytes == 1 ? 255.0 : 65535.0;
        var pixel = channels * bytes;
        var target = new byte[(long)targetWidth * targetHeight * pixel];
        var linear = srgb ? SrgbToLinear.Value : null;
        var bands = Math.Min(targetHeight, Environment.ProcessorCount * 4);
        var length = targetWidth * channels;
        Parallel.For(0, bands, () => (new double[length], new double[length], new double[length]), (band, _, buffers) =>
        {
            int first = (int)((long)targetHeight * band / bands), end = (int)((long)targetHeight * (band + 1) / bands);
            var (row, sums, below) = buffers;
            Array.Clear(sums);
            Array.Clear(below);
            var targetRow = first;
            for (var y = Math.Max(0, (int)(first * scaleY) - 1); y < height && targetRow < end; y++)
            {
                var (rowFirst, rowWeight, rowSecond) = Split(y, scaleY, targetHeight);
                if (rowFirst < first && rowSecond != first)
                {
                    continue;
                }

                Array.Clear(row);
                var line = source.AsSpan(y * width * pixel, width * pixel);
                for (var x = 0; x < width; x++)
                {
                    var (column, columnWeight, next) = Split(x, scaleX, targetWidth);
                    var a = alpha < 0 ? 1.0 : Read(line, x * pixel + alpha * bytes, bytes) / top;
                    for (var c = 0; c < channels; c++)
                    {
                        var value = c == alpha ? a
                            : (linear != null ? linear[line[x * pixel + c]] : Read(line, x * pixel + c * bytes, bytes)) * a;
                        row[column * channels + c] += value * columnWeight;
                        if (next >= 0)
                        {
                            row[next * channels + c] += value * (1 - columnWeight);
                        }
                    }
                }

                if (rowFirst < first)
                {
                    (rowWeight, rowSecond) = (1 - rowWeight, -1);
                }

                for (var i = 0; i < row.Length; i++)
                {
                    sums[i] += row[i] * rowWeight;
                    if (rowSecond >= 0)
                    {
                        below[i] += row[i] * (1 - rowWeight);
                    }
                }

                if (y == height - 1 || Split(y + 1, scaleY, targetHeight).First != targetRow)
                {
                    Write(sums, target.AsSpan(targetRow * targetWidth * pixel, targetWidth * pixel), channels, bytes,
                        alpha, top, scaleX * scaleY, srgb);
                    targetRow++;
                    (sums, below) = (below, sums);
                    Array.Clear(below);
                }
            }

            return (row, sums, below);
        }, _ => { });

        return target;
    }

    // An sRGB-encoded 8-bit value as linear light, 0 to 255, so averages are of light, not of its encoding.
    private static readonly Lazy<double[]> SrgbToLinear = new(() =>
    {
        var table = new double[256];
        for (var i = 0; i < 256; i++)
        {
            var v = i / 255.0;
            table[i] = 255 * (v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4));
        }

        return table;
    });

    private static double LinearToSrgb(double value)
    {
        var v = Math.Clamp(value / 255, 0, 1);
        return 255 * (v <= 0.0031308 ? v * 12.92 : 1.055 * Math.Pow(v, 1 / 2.4) - 0.055);
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
        double area, bool srgb)
    {
        for (var x = 0; x < sums.Length / channels; x++)
        {
            var weight = alpha < 0 ? area : sums[x * channels + alpha];
            var clear = alpha >= 0 && Math.Round(weight / area * top, MidpointRounding.AwayFromZero) <= 0;
            for (var c = 0; c < channels; c++)
            {
                var value = c == alpha ? weight / area * top
                    : clear || weight <= 0 ? 0 : sums[x * channels + c] / weight;
                if (srgb && c != alpha && !clear)
                {
                    value = LinearToSrgb(value);
                }

                var v = (int)Math.Clamp(Math.Round(value, MidpointRounding.AwayFromZero), 0, top);
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
