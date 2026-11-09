using System.Collections.Generic;

namespace Adamantium.Imaging.Jpeg2000;

/// <summary>A code-block (ITU-T T.800, B.7): its place in its subband and what the packets have said of it so far.
/// </summary>
internal sealed class J2kCodeBlock
{
    public J2kCodeBlock(int x0, int y0, int x1, int y1)
    {
        X0 = x0;
        Y0 = y0;
        X1 = x1;
        Y1 = y1;
    }

    public int X0 { get; }

    public int Y0 { get; }

    public int X1 { get; }

    public int Y1 { get; }

    public int Width => X1 - X0;

    public int Height => Y1 - Y0;

    public bool Included { get; set; }

    /// <summary>The missing most significant bit-planes.</summary>
    public int ZeroPlanes { get; set; }

    /// <summary>Lblock: the bits of a segment length before the pass count adds to them.</summary>
    public int LengthBits { get; set; } = 3;

    public int Passes { get; set; }

    /// <summary>The passes of the resolutions and layers being decoded.</summary>
    public int KeptPasses { get; set; }

    public List<J2kSegment> Segments { get; } = [];

    /// <summary>Whether coding pass <paramref name="pass"/> starts a codeword segment under the block style
    /// (ITU-T T.800, D.4.1 and table D.9).</summary>
    public static bool StartsSegment(int pass, J2kComponentStyle style)
    {
        if (pass == 0 || style.Has(J2kComponentStyle.TerminateAllFlag))
        {
            return true;
        }

        if (!style.Has(J2kComponentStyle.BypassFlag) || pass < 10)
        {
            return false;
        }

        return (pass - 10) % 3 != 1;
    }

    /// <summary>Whether pass <paramref name="pass"/> is coded raw, without the arithmetic coder.</summary>
    public static bool IsRaw(int pass, J2kComponentStyle style) =>
        style.Has(J2kComponentStyle.BypassFlag) && pass >= 10 && (pass - 10) % 3 != 2;
}
