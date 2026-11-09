namespace Adamantium.Imaging.Jpeg2000;

/// <summary>The code-blocks of one subband inside one precinct, in raster order, with their two tag trees.</summary>
internal sealed class J2kPrecinctBand
{
    public J2kPrecinctBand(J2kBand band, J2kCodeBlock[] blocks, int across, int down)
    {
        Band = band;
        Blocks = blocks;
        Inclusion = new J2kTagTree(across, down);
        ZeroPlanes = new J2kTagTree(across, down);
    }

    public J2kBand Band { get; }

    public J2kCodeBlock[] Blocks { get; }

    public J2kTagTree Inclusion { get; }

    public J2kTagTree ZeroPlanes { get; }
}
