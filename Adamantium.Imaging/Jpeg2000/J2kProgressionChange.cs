namespace Adamantium.Imaging.Jpeg2000;

/// <summary>One progression of a POC marker (ITU-T T.800, A.6.6): the packets of resolutions, components and
/// layers in the given ranges, in the given order; the ends are exclusive.</summary>
internal sealed record J2kProgressionChange(int FirstResolution, int FirstComponent, int EndLayer,
    int EndResolution, int EndComponent, J2kProgression Order);
