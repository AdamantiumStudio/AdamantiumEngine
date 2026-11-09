using System;
using System.Collections.Generic;
using System.IO;
using Adamantium.Imaging.Mq;

namespace Adamantium.Imaging.Jbig2;

/// <summary>Halftone region decoding (ITU-T T.88, 6.6 and annex C.5): a grid of gray values, coded as Gray-coded bit
/// planes, each value drawing its pattern where the grid puts it.</summary>
internal static class Jbig2HalftoneRegion
{
    /// <summary>Reads the segment's data after its region information.</summary>
    public static Jbig2Bitmap Read(BigEndianReader bytes, int regionWidth, int regionHeight,
        IReadOnlyList<Jbig2Bitmap> patterns, Jbig2Limits limits)
    {
        var flags = bytes.ReadByte();
        var mmr = (flags & 1) != 0;
        var template = (flags >> 1) & 3;
        var enableSkip = (flags & 8) != 0;
        var combination = (Jbig2Combination)((flags >> 4) & 7);
        var defaultPixel = (flags >> 7) & 1;
        var gridWidth = bytes.ReadUInt32();
        var gridHeight = bytes.ReadUInt32();
        long gridX = bytes.ReadInt32();
        long gridY = bytes.ReadInt32();
        long stepX = bytes.ReadUInt16();
        long stepY = bytes.ReadUInt16();
        if (patterns.Count == 0)
        {
            throw new InvalidDataException("A JBIG2 halftone region has no patterns.");
        }

        var region = limits.Bitmap(regionWidth, regionHeight);
        if (defaultPixel != 0)
        {
            region.Fill(1);
        }

        var grid = limits.Bitmap(gridWidth, gridHeight);
        var patternWidth = patterns[0].Width;
        var patternHeight = patterns[0].Height;
        Jbig2Bitmap skip = null;
        if (enableSkip)
        {
            skip = limits.Bitmap(gridWidth, gridHeight);
            for (var m = 0; m < grid.Height; m++)
            {
                for (var n = 0; n < grid.Width; n++)
                {
                    var x = (gridX + m * stepY + n * stepX) >> 8;
                    var y = (gridY + m * stepX - n * stepY) >> 8;
                    if (x + patternWidth <= 0 || x >= regionWidth || y + patternHeight <= 0 || y >= regionHeight)
                    {
                        skip.Set(n, m, 1);
                    }
                }
            }
        }

        var planes = Jbig2SymbolDictionary.CodeLength(patterns.Count);
        var values = GrayValues(bytes, grid.Width, grid.Height, planes, mmr, template, skip, limits);
        for (var m = 0; m < grid.Height; m++)
        {
            for (var n = 0; n < grid.Width; n++)
            {
                var x = (gridX + m * stepY + n * stepX) >> 8;
                var y = (gridY + m * stepX - n * stepY) >> 8;
                if (x <= int.MinValue || x >= int.MaxValue || y <= int.MinValue || y >= int.MaxValue)
                {
                    continue;
                }

                var value = Math.Min(values[m * grid.Width + n], patterns.Count - 1);
                limits.Spend(patternWidth * patternHeight);
                region.Combine(patterns[value], (int)x, (int)y, combination);
            }
        }

        return region;
    }

    private static int[] GrayValues(BigEndianReader bytes, int width, int height, int planes, bool mmr, int template,
        Jbig2Bitmap skip, Jbig2Limits limits)
    {
        var values = new int[width * height];
        if (planes == 0)
        {
            return values;
        }

        var decoder = mmr ? null : new MqDecoder(bytes.Data, bytes.Position, bytes.Remaining);
        var contexts = mmr ? null : new byte[Jbig2GenericRegion.ContextCount(template)];
        var position = bytes.Position;
        Jbig2Bitmap above = null;
        for (var j = planes - 1; j >= 0; j--)
        {
            Jbig2Bitmap plane;
            if (mmr)
            {
                plane = Jbig2GenericRegion.DecodeMmr(bytes.Data, position, bytes.End, width, height, limits,
                    out var used);
                position += used;
            }
            else
            {
                plane = Jbig2GenericRegion.Decode(decoder, contexts, new Jbig2GenericParameters
                {
                    Width = width,
                    Height = height,
                    Template = template,
                    Skip = skip,
                }, limits);
            }

            if (above != null)
            {
                for (var i = 0; i < plane.Pixels.Length; i++)
                {
                    plane.Pixels[i] ^= above.Pixels[i];
                }
            }

            for (var i = 0; i < values.Length; i++)
            {
                values[i] |= plane.Pixels[i] << j;
            }

            above = plane;
        }

        return values;
    }
}
