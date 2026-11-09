using System;

namespace Adamantium.Imaging.Jpeg2000;

/// <summary>A resolution level of a tile-component (ITU-T T.800, B.5 and B.6): its bounds, its subbands and its
/// precincts, each precinct the code-blocks of every subband inside it.</summary>
internal sealed class J2kResolution
{
    public J2kResolution(int level, int x0, int y0, int x1, int y1, int precinctWidthExponent,
        int precinctHeightExponent, J2kBand[] bands, J2kLimits limits)
    {
        Level = level;
        X0 = x0;
        Y0 = y0;
        X1 = x1;
        Y1 = y1;
        PrecinctWidthExponent = precinctWidthExponent;
        PrecinctHeightExponent = precinctHeightExponent;
        Bands = bands;
        PrecinctX0 = x0 >> precinctWidthExponent;
        PrecinctY0 = y0 >> precinctHeightExponent;
        PrecinctsAcross = x1 > x0 ? (int)(Ceiling(x1, precinctWidthExponent) - PrecinctX0) : 0;
        PrecinctsDown = y1 > y0 ? (int)(Ceiling(y1, precinctHeightExponent) - PrecinctY0) : 0;
        limits.Spend(((long)PrecinctsAcross * PrecinctsDown + 1 + bands.Length) * J2kLimits.ObjectCost);
        Precincts = new J2kPrecinctBand[(long)PrecinctsAcross * PrecinctsDown][];
    }

    public int Level { get; }

    public int X0 { get; }

    public int Y0 { get; }

    public int X1 { get; }

    public int Y1 { get; }

    public int PrecinctWidthExponent { get; }

    public int PrecinctHeightExponent { get; }

    /// <summary>The index of the first precinct column on the precinct grid anchored at 0.</summary>
    public int PrecinctX0 { get; }

    public int PrecinctY0 { get; }

    public int PrecinctsAcross { get; }

    public int PrecinctsDown { get; }

    public J2kBand[] Bands { get; }

    /// <summary>Per precinct, made when its first packet comes: its part of each subband.</summary>
    public J2kPrecinctBand[][] Precincts { get; }

    /// <summary>The parts of each subband in precinct <paramref name="index"/>.</summary>
    public J2kPrecinctBand[] Precinct(int index, J2kLimits limits)
    {
        if (Precincts[index] != null)
        {
            return Precincts[index];
        }

        var px = PrecinctX0 + index % PrecinctsAcross;
        var py = PrecinctY0 + index / PrecinctsAcross;
        var shift = Level == 0 ? 0 : 1;
        var parts = new J2kPrecinctBand[Bands.Length];
        for (var b = 0; b < Bands.Length; b++)
        {
            var band = Bands[b];
            var width = PrecinctWidthExponent - shift;
            var height = PrecinctHeightExponent - shift;
            var x0 = (int)Math.Max(band.X0, (long)px << width);
            var y0 = (int)Math.Max(band.Y0, (long)py << height);
            var x1 = (int)Math.Min(band.X1, (long)(px + 1) << width);
            var y1 = (int)Math.Min(band.Y1, (long)(py + 1) << height);
            var blockWidth = band.BlockWidthExponent;
            var blockHeight = band.BlockHeightExponent;
            var across = x1 > x0 ? (int)(Ceiling(x1, blockWidth) - (x0 >> blockWidth)) : 0;
            var down = y1 > y0 ? (int)(Ceiling(y1, blockHeight) - (y0 >> blockHeight)) : 0;
            limits.Spend((long)across * down + 1);
            var blocks = new J2kCodeBlock[across * down];
            for (var j = 0; j < down; j++)
            {
                for (var i = 0; i < across; i++)
                {
                    var bx = (x0 >> blockWidth) + i;
                    var by = (y0 >> blockHeight) + j;
                    blocks[j * across + i] = new J2kCodeBlock((int)Math.Max(x0, (long)bx << blockWidth),
                        (int)Math.Max(y0, (long)by << blockHeight), (int)Math.Min(x1, (bx + 1L) << blockWidth),
                        (int)Math.Min(y1, (by + 1L) << blockHeight));
                }
            }

            parts[b] = new J2kPrecinctBand(band, blocks, across, down);
        }

        Precincts[index] = parts;
        return parts;
    }

    /// <summary>ceil(value / 2^exponent).</summary>
    public static long Ceiling(long value, int exponent) => (value + (1L << exponent) - 1) >> exponent;
}
