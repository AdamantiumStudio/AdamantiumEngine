using System;
using System.Buffers.Binary;
using System.Threading.Tasks;

namespace Adamantium.Imaging.Icc;

/// <summary>Pictures brought to sRGB by the ICC profile their file holds: interleaved samples of 8 bits, or 16
/// little-endian, many rows at once on several threads.</summary>
internal static class IccPixels
{
    private const long RowsInParallelFrom = 1 << 20;

    /// <summary>The converter of <paramref name="profile"/> for <paramref name="channels"/> device channels - gray,
    /// RGB or CMYK - and about <paramref name="colors"/> colors, or null when there is no profile, it cannot be read,
    /// or it is not a device profile of that color space.</summary>
    public static IccPixelConverter Converter(byte[] profile, int channels, long colors)
    {
        if (profile == null)
        {
            return null;
        }

        try
        {
            var read = IccProfile.Read(profile);
            var space = channels switch
            {
                1 => IccColorSpace.Gray,
                3 => IccColorSpace.Rgb,
                4 => IccColorSpace.Cmyk,
                _ => IccColorSpace.Other,
            };
            if (read.ColorSpace != space || read.Class is "link" or "abst" or "nmcl")
            {
                return null;
            }

            var transform = IccTransform.ToSrgb(read, IccRenderingIntent.Perceptual);
            return transform.Components == channels ? new IccPixelConverter(transform, colors) : null;
        }
        catch (System.IO.InvalidDataException)
        {
            return null;
        }
    }

    /// <summary>Converts RGBA pixels in place: their red, green and blue - or red alone, for a gray profile - as the
    /// device values; opacity stays.</summary>
    public static void InPlace(IccPixelConverter converter, byte[] rgba, bool wide)
    {
        var pixelBytes = wide ? 8 : 4;
        Convert(converter, rgba, pixelBytes, wide, rgba, pixelBytes, false);
    }

    /// <summary>Converts <paramref name="samples"/> of the converter's channels a pixel, no opacity, into
    /// <paramref name="rgba"/>, opaque.</summary>
    public static void ToRgba(IccPixelConverter converter, byte[] samples, bool wide, byte[] rgba)
    {
        var sampleBytes = wide ? 2 : 1;
        Convert(converter, samples, converter.Channels * sampleBytes, wide, rgba, 4 * sampleBytes, true);
    }

    private static void Convert(IccPixelConverter converter, byte[] source, int sourceBytes, bool wide, byte[] rgba,
        int pixelBytes, bool opaque)
    {
        var count = rgba.Length / pixelBytes;
        if (count < RowsInParallelFrom)
        {
            Run(converter, source, sourceBytes, wide, rgba, pixelBytes, opaque, 0, count);
            return;
        }

        const int chunk = 1 << 16;
        Parallel.For(0, (count + chunk - 1) / chunk, part =>
            Run(converter, source, sourceBytes, wide, rgba, pixelBytes, opaque, part * chunk,
                Math.Min(count, (part + 1) * chunk)));
    }

    private static void Run(IccPixelConverter converter, byte[] source, int sourceBytes, bool wide, byte[] rgba,
        int pixelBytes, bool opaque, int from, int to)
    {
        var channels = converter.Channels;
        var max = wide ? 65535f : 255f;
        Span<float> device = stackalloc float[channels];
        Span<float> rgb = stackalloc float[3];
        for (var p = from; p < to; p++)
        {
            var at = p * sourceBytes;
            for (var c = 0; c < channels; c++)
            {
                device[c] = (wide ? BinaryPrimitives.ReadUInt16LittleEndian(source.AsSpan(at + 2 * c))
                    : source[at + c]) / max;
            }

            converter.Convert(device, rgb);
            var to8 = p * pixelBytes;
            for (var c = 0; c < 4; c++)
            {
                if (c == 3 && !opaque)
                {
                    break;
                }

                var value = c < 3 ? (int)(rgb[c] * max + 0.5f) : (int)max;
                if (wide)
                {
                    BinaryPrimitives.WriteUInt16LittleEndian(rgba.AsSpan(to8 + 2 * c), (ushort)value);
                }
                else
                {
                    rgba[to8 + c] = (byte)value;
                }
            }
        }
    }
}
