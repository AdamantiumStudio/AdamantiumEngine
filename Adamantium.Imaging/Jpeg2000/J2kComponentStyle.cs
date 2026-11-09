using System;
using System.IO;

namespace Adamantium.Imaging.Jpeg2000;

/// <summary>How a component is coded (ITU-T T.800, A.6.1 and A.6.2, SPcod and SPcoc): decomposition levels,
/// code-block size and style, the wavelet, the precinct sizes.</summary>
internal sealed class J2kComponentStyle
{
    public const int BypassFlag = 1;
    public const int ResetFlag = 2;
    public const int TerminateAllFlag = 4;
    public const int VerticallyCausalFlag = 8;
    public const int SegmentationSymbolsFlag = 32;
    public const int HighThroughputFlag = 64;

    public int Levels { get; private init; }

    public int BlockWidthExponent { get; private init; }

    public int BlockHeightExponent { get; private init; }

    public int BlockStyle { get; private init; }

    /// <summary>Whether the wavelet is the reversible 5-3 one; otherwise the irreversible 9-7.</summary>
    public bool Reversible { get; private init; }

    /// <summary>Per resolution: the precinct width and height exponents.</summary>
    public (int X, int Y)[] Precincts { get; private init; }

    public bool Has(int flag) => (BlockStyle & flag) != 0;

    /// <summary>Reads SPcod or SPcoc after the style byte that says whether precincts follow.</summary>
    public static J2kComponentStyle Read(BigEndianReader reader, bool precinctsGiven)
    {
        var levels = reader.ReadByte();
        var width = reader.ReadByte() + 2;
        var height = reader.ReadByte() + 2;
        var style = reader.ReadByte();
        var transform = reader.ReadByte();
        if (levels > 32 || width > 10 || height > 10 || width + height > 12)
        {
            throw new InvalidDataException("A JPEG 2000 coding style has sizes it cannot have.");
        }

        if ((style & HighThroughputFlag) != 0)
        {
            throw new NotSupportedException("High-throughput JPEG 2000 (part 15) is not read.");
        }

        var precincts = new (int, int)[levels + 1];
        for (var r = 0; r <= levels; r++)
        {
            if (precinctsGiven)
            {
                var size = reader.ReadByte();
                precincts[r] = (size & 15, size >> 4);
                if (r > 0 && (precincts[r].Item1 == 0 || precincts[r].Item2 == 0))
                {
                    throw new InvalidDataException("A JPEG 2000 precinct is smaller than it may be.");
                }
            }
            else
            {
                precincts[r] = (15, 15);
            }
        }

        return new J2kComponentStyle
        {
            Levels = levels,
            BlockWidthExponent = width,
            BlockHeightExponent = height,
            BlockStyle = style,
            Reversible = transform == 1,
            Precincts = precincts,
        };
    }
}
