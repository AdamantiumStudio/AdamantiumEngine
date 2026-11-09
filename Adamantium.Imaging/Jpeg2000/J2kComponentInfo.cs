namespace Adamantium.Imaging.Jpeg2000;

/// <summary>A component as the SIZ marker gives it (ITU-T T.800, A.5.1): depth, sign and subsampling.</summary>
internal sealed record J2kComponentInfo(int Precision, bool Signed, int Dx, int Dy);
