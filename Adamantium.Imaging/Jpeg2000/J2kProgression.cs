namespace Adamantium.Imaging.Jpeg2000;

/// <summary>The orders packets can come in (ITU-T T.800, table A.16): by layer, resolution, component and position.
/// </summary>
internal enum J2kProgression
{
    Lrcp = 0,
    Rlcp = 1,
    Rpcl = 2,
    Pcrl = 3,
    Cprl = 4,
}
