using System;
using System.Collections.Generic;
using System.IO;

namespace Adamantium.Imaging.Jpeg2000;

/// <summary>JPEG 2000 decoding (ITU-T T.800, part 1): JP2 files and bare codestreams, every progression and POC,
/// precincts, packed packet headers, every code-block style, both wavelets, both component transforms, regions of
/// interest, subsampled components, palettes and channel definitions. A codestream that ends early draws what it
/// has.</summary>
public static class Jpeg2000Decoder
{
    private const int MaxPlaneBits = 30;

    /// <summary>Decodes <paramref name="data"/>.</summary>
    /// <param name="data">A JP2 file or a codestream.</param>
    /// <param name="maxPixels">The most pixels the image may have.</param>
    /// <exception cref="InvalidDataException">The data is not JPEG 2000 or asks for more than
    /// <paramref name="maxPixels"/> allow.</exception>
    /// <exception cref="NotSupportedException">The data is high-throughput JPEG 2000 or of a later part.</exception>
    public static Jpeg2000Image Decode(ReadOnlySpan<byte> data, long maxPixels) => Decode(data, maxPixels, null);

    /// <summary>Decodes as much of <paramref name="data"/> as <paramref name="options"/> say: the whole image when
    /// they are null.</summary>
    /// <exception cref="InvalidDataException">The data is not JPEG 2000 or asks for more than
    /// <paramref name="maxPixels"/> allow.</exception>
    /// <exception cref="NotSupportedException">The data is high-throughput JPEG 2000 or of a later part.</exception>
    public static Jpeg2000Image Decode(ReadOnlySpan<byte> data, long maxPixels, Jpeg2000Options options)
    {
        try
        {
            return Decode(data.ToArray(), maxPixels, options ?? new Jpeg2000Options());
        }
        catch (AggregateException aggregate)
        {
            throw Unwrapped(aggregate);
        }
        catch (Exception exception) when (exception is IndexOutOfRangeException or ArgumentException or
                                              OverflowException)
        {
            throw new InvalidDataException("JPEG 2000 data is broken: " + exception.Message, exception);
        }
    }

    /// <summary>The exception a parallel loop wrapped, as the decoder promises its exceptions.</summary>
    internal static Exception Unwrapped(AggregateException aggregate)
    {
        var inner = aggregate.Flatten().InnerExceptions[0];
        return inner is InvalidDataException or NotSupportedException
            ? inner
            : new InvalidDataException("JPEG 2000 data is broken: " + inner.Message, inner);
    }

    private static Jpeg2000Image Decode(byte[] data, long maxPixels, Jpeg2000Options options)
    {
        var file = Jp2File.Read(data);
        var codestream = J2kCodestream.Read(data, file.CodestreamStart, file.CodestreamEnd, maxPixels);
        var size = codestream.SizeInfo;
        var main = codestream.Main;
        var layers = main.CodingStyle(null).Layers;
        var levels = int.MaxValue;
        for (var c = 0; c < size.Components.Length; c++)
        {
            levels = Math.Min(levels, main.ComponentStyle(c, null).Levels);
        }

        var discard = Math.Clamp(options.DiscardLevels, 0, levels);
        var grid = (X0: Shrink(size.X0, discard), Y0: Shrink(size.Y0, discard), X1: Shrink(size.X1, discard),
            Y1: Shrink(size.Y1, discard));
        var limits = new J2kLimits((long)size.Width * size.Height, size.Components.Length);
        var planes = new int[size.Components.Length][];
        var planeBounds = new J2kPlane[planes.Length];
        for (var c = 0; c < planes.Length; c++)
        {
            var info = size.Components[c];
            var scale = Math.Min(discard, main.ComponentStyle(c, null).Levels);
            var x0 = Shrink(Ceiling(size.X0, info.Dx), scale);
            var y0 = Shrink(Ceiling(size.Y0, info.Dy), scale);
            var width = Shrink(Ceiling(size.X1, info.Dx), scale) - x0;
            var height = Shrink(Ceiling(size.Y1, info.Dy), scale) - y0;
            limits.Spend((long)width * height);
            planes[c] = new int[(long)width * height];
            planeBounds[c] = new J2kPlane(x0, y0, width, height, scale);
        }

        long coded = 0, used = 0;
        var decoded = 0;
        Exception failure = null;
        foreach (var tileData in codestream.Tiles)
        {
            if (!tileData.Seen)
            {
                continue;
            }

            J2kTile tile;
            try
            {
                tile = J2kTile.Decode(codestream, tileData, limits, discard, options.Layers);
            }
            catch (InvalidDataException exception)
            {
                failure = exception;
                continue;
            }
            catch (AggregateException aggregate)
            {
                failure = Unwrapped(aggregate);
                if (failure is NotSupportedException)
                {
                    throw failure;
                }

                continue;
            }

            decoded++;
            coded += tile.CodedBytes;
            used += tile.CodedBytesUsed;
            foreach (var component in tile.Components)
            {
                Place(component, planes[component.Component], planeBounds[component.Component]);
            }
        }

        if (decoded == 0)
        {
            throw failure ?? new InvalidDataException("A JPEG 2000 codestream has no tile-parts.");
        }

        var image = Compose(size, discard, grid, planes, planeBounds, file, limits);
        return image.With(levels, layers, coded, used);
    }

    private static int Shrink(int value, int levels) => (int)J2kResolution.Ceiling(value, levels);

    private static void Place(J2kTileComponent component, int[] plane, J2kPlane bounds)
    {
        var info = component.Info;
        var shift = 1L << (info.Precision - 1);
        var drop = Math.Max(0, info.Precision - MaxPlaneBits);
        var max = (1L << (info.Precision - drop)) - 1;
        var samples = component.Samples;
        var width = component.OutputWidth;
        for (var y = 0; y < component.OutputHeight; y++)
        {
            var py = component.OutputY0 + y - bounds.Y0;
            if ((uint)py >= (uint)bounds.Height)
            {
                continue;
            }

            for (var x = 0; x < width; x++)
            {
                var px = component.OutputX0 + x - bounds.X0;
                if ((uint)px >= (uint)bounds.Width)
                {
                    continue;
                }

                var value = ((long)MathF.Round(samples[y * width + x]) + shift) >> drop;
                plane[py * bounds.Width + px] = (int)Math.Clamp(value, 0, max);
            }
        }
    }

    private static Jpeg2000Image Compose(J2kSize size, int discard, (int X0, int Y0, int X1, int Y1) grid,
        int[][] planes, J2kPlane[] bounds, Jp2File file, J2kLimits limits)
    {
        var channels = new List<(int Component, int Column)>();
        if (file.Palette != null && file.Mapping != null)
        {
            channels.AddRange(file.Mapping);
        }
        else
        {
            for (var c = 0; c < planes.Length; c++)
            {
                channels.Add((c, -1));
            }
        }

        var alpha = -1;
        var premultiplied = false;
        var color = new List<int>();
        var associated = new SortedDictionary<int, int>();
        for (var i = 0; i < channels.Count; i++)
        {
            if (file.Channels.TryGetValue(i, out var definition))
            {
                if (definition.Type is 1 or 2)
                {
                    if (alpha < 0)
                    {
                        alpha = i;
                        premultiplied = definition.Type == 2;
                    }

                    continue;
                }

                if (definition.Association is > 0 and < 65535)
                {
                    associated[definition.Association] = i;
                    continue;
                }
            }

            color.Add(i);
        }

        if (associated.Count > 0)
        {
            var ordered = new List<int>(associated.Values);
            ordered.AddRange(color);
            color = ordered;
        }

        var wanted = file.ColorSpace switch
        {
            Jpeg2000ColorSpace.Gray => 1,
            Jpeg2000ColorSpace.Srgb or Jpeg2000ColorSpace.Sycc => 3,
            Jpeg2000ColorSpace.Cmyk => 4,
            _ => color.Count,
        };
        if (wanted > 0 && wanted < color.Count)
        {
            color.RemoveRange(wanted, color.Count - wanted);
        }

        if (color.Count == 0)
        {
            throw new InvalidDataException("A JPEG 2000 image has no color channels.");
        }

        var width = grid.X1 - grid.X0;
        var height = grid.Y1 - grid.Y0;
        var order = new List<int>(color);
        if (alpha >= 0)
        {
            order.Add(alpha);
        }

        var stride = order.Count;
        limits.Spend((long)width * height * stride);
        var samples = new byte[(long)width * height * stride];
        var depths = new int[stride];
        for (var k = 0; k < order.Count; k++)
        {
            var (component, column) = channels[order[k]];
            if (component >= planes.Length)
            {
                continue;
            }

            var plane = planes[component];
            var (x0, y0, planeWidth, planeHeight, scale) = bounds[component];
            var info = size.Components[component];
            var stepX = (long)info.Dx << scale;
            var stepY = (long)info.Dy << scale;
            var palette = column >= 0 && column < file.Palette.Length ? file.Palette[column] : null;
            var bits = palette != null ? Math.Min(file.PaletteBits[column], 31) : Math.Min(info.Precision, MaxPlaneBits);
            var max = (1L << bits) - 1;
            depths[k] = bits;
            for (var y = 0; y < height; y++)
            {
                var sy = (int)Math.Clamp((((long)grid.Y0 + y) << discard) / stepY - y0, 0, planeHeight - 1);
                for (var x = 0; x < width; x++)
                {
                    var sx = (int)Math.Clamp((((long)grid.X0 + x) << discard) / stepX - x0, 0, planeWidth - 1);
                    long value = plane[sy * planeWidth + sx];
                    if (palette != null)
                    {
                        value = palette[Math.Clamp((int)value, 0, palette.Length - 1)];
                    }

                    samples[((long)y * width + x) * stride + k] = (byte)((value * 255 + max / 2) / max);
                }
            }
        }

        if (file.ColorSpace == Jpeg2000ColorSpace.Sycc && color.Count == 3)
        {
            SyccToRgb(samples, stride);
        }

        return new Jpeg2000Image(width, height, color.Count, alpha >= 0, samples, file.ColorSpace, file.IccProfile,
            premultiplied, depths);
    }

    private static void SyccToRgb(byte[] samples, int stride)
    {
        for (var i = 0; i < samples.Length; i += stride)
        {
            float y = samples[i];
            float cb = samples[i + 1] - 128;
            float cr = samples[i + 2] - 128;
            samples[i] = Byte(y + 1.402f * cr);
            samples[i + 1] = Byte(y - 0.344136f * cb - 0.714136f * cr);
            samples[i + 2] = Byte(y + 1.772f * cb);
        }
    }

    private static byte Byte(float value) => (byte)Math.Clamp((int)MathF.Round(value), 0, 255);

    private static int Ceiling(int value, int divisor) => (int)((value + (long)divisor - 1) / divisor);
}
