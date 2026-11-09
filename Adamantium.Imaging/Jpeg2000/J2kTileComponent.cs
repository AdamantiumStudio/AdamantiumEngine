using System;

namespace Adamantium.Imaging.Jpeg2000;

/// <summary>One component of one tile (ITU-T T.800, B.3 to B.6): its bounds, its coding, its resolutions with their
/// subbands, and the samples the inverse wavelet gives.</summary>
internal sealed class J2kTileComponent
{
    public J2kTileComponent(int component, J2kComponentInfo info, J2kComponentStyle style,
        J2kQuantization quantization, int roiShift, (int X0, int Y0, int X1, int Y1) tile, J2kLimits limits)
    {
        Component = component;
        Info = info;
        Style = style;
        RoiShift = roiShift;
        X0 = (int)Ceiling(tile.X0, info.Dx);
        Y0 = (int)Ceiling(tile.Y0, info.Dy);
        X1 = (int)Ceiling(tile.X1, info.Dx);
        Y1 = (int)Ceiling(tile.Y1, info.Dy);
        var levels = style.Levels;
        Resolutions = new J2kResolution[levels + 1];
        for (var r = 0; r <= levels; r++)
        {
            var scale = levels - r;
            var (ppx, ppy) = style.Precincts[r];
            var widthExponent = Math.Min(style.BlockWidthExponent, r == 0 ? ppx : ppx - 1);
            var heightExponent = Math.Min(style.BlockHeightExponent, r == 0 ? ppy : ppy - 1);
            J2kBand[] bands;
            if (r == 0)
            {
                bands = [Band(J2kBand.LowLow, levels, widthExponent, heightExponent)];
            }
            else
            {
                var level = levels - r + 1;
                bands =
                [
                    Band(J2kBand.HighLow, level, widthExponent, heightExponent),
                    Band(J2kBand.LowHigh, level, widthExponent, heightExponent),
                    Band(J2kBand.HighHigh, level, widthExponent, heightExponent),
                ];
            }

            foreach (var band in bands)
            {
                var index = band.Orientation == J2kBand.LowLow ? 0 : 3 * (r - 1) + band.Orientation;
                var (exponent, mantissa) = quantization.Step(index, band.Level, levels);
                band.Planes = quantization.GuardBits + exponent - 1 + roiShift;
                if (!style.Reversible)
                {
                    var gain = band.Orientation switch
                    {
                        J2kBand.LowLow => 0,
                        J2kBand.HighHigh => 2,
                        _ => 1,
                    };
                    band.Step = (float)(Math.Pow(2, info.Precision + gain - exponent) * (1 + mantissa / 2048.0));
                }
            }

            Resolutions[r] = new J2kResolution(r, (int)Ceiling(X0, 1L << scale), (int)Ceiling(Y0, 1L << scale),
                (int)Ceiling(X1, 1L << scale), (int)Ceiling(Y1, 1L << scale), ppx, ppy, bands, limits);
        }
    }

    public int Component { get; }

    public J2kComponentInfo Info { get; }

    public J2kComponentStyle Style { get; }

    public int RoiShift { get; }

    public int X0 { get; }

    public int Y0 { get; }

    public int X1 { get; }

    public int Y1 { get; }

    public int Width => X1 - X0;

    public int Height => Y1 - Y0;

    public J2kResolution[] Resolutions { get; }

    /// <summary>The samples after the inverse wavelet, row after row.</summary>
    public float[] Samples { get; set; }

    private J2kBand Band(int orientation, int level, int widthExponent, int heightExponent)
    {
        var xob = orientation is J2kBand.HighLow or J2kBand.HighHigh ? 1L : 0L;
        var yob = orientation is J2kBand.LowHigh or J2kBand.HighHigh ? 1L : 0L;
        var offset = level == 0 ? 0 : 1L << (level - 1);
        var size = 1L << level;
        return new J2kBand(orientation, level, (int)Ceiling(X0 - xob * offset, size),
            (int)Ceiling(Y0 - yob * offset, size), (int)Ceiling(X1 - xob * offset, size),
            (int)Ceiling(Y1 - yob * offset, size), widthExponent, heightExponent);
    }

    private static long Ceiling(long value, long divisor) =>
        value >= 0 ? (value + divisor - 1) / divisor : -(-value / divisor);
}
