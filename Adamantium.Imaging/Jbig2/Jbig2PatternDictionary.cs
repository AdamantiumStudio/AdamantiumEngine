using System.Collections.Generic;
using System.IO;
using Adamantium.Imaging.Mq;

namespace Adamantium.Imaging.Jbig2;

/// <summary>A pattern dictionary segment (ITU-T T.88, 6.7 and 7.4.4): patterns of one size, cut from one collective
/// bitmap.</summary>
internal static class Jbig2PatternDictionary
{
    /// <summary>The most patterns a dictionary may have: 16-bit gray, more than halftones use.</summary>
    public const int MaxPatterns = 1 << 16;

    public static List<Jbig2Bitmap> Read(BigEndianReader bytes, Jbig2Limits limits)
    {
        var flags = bytes.ReadByte();
        var mmr = (flags & 1) != 0;
        var template = (flags >> 1) & 3;
        var width = bytes.ReadByte();
        var height = bytes.ReadByte();
        var grayMax = bytes.ReadUInt32();
        var count = grayMax + 1L;
        if (width == 0 || height == 0 || count > MaxPatterns || count * width * height > limits.MaxPixels)
        {
            throw new InvalidDataException("A JBIG2 pattern dictionary has patterns it cannot have.");
        }

        limits.Spend(count * 64);

        var collectiveWidth = (int)(count * width);
        Jbig2Bitmap collective;
        if (mmr)
        {
            collective = Jbig2GenericRegion.DecodeMmr(bytes.Data, bytes.Position, bytes.End, collectiveWidth, height,
                limits, out _);
        }
        else
        {
            var at = template == 0 ? new[] { -width, 0, -3, -1, 2, -2, -2, -2 } : [-width, 0];
            collective = Jbig2GenericRegion.Decode(new MqDecoder(bytes.Data, bytes.Position, bytes.Remaining),
                new byte[Jbig2GenericRegion.ContextCount(template)], new Jbig2GenericParameters
                {
                    Width = collectiveWidth,
                    Height = height,
                    Template = template,
                    At = at,
                }, limits);
        }

        var patterns = new List<Jbig2Bitmap>((int)count);
        for (var i = 0; i < count; i++)
        {
            patterns.Add(collective.Crop(i * width, 0, width, height));
        }

        return patterns;
    }
}
