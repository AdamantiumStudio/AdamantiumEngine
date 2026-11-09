namespace Adamantium.Imaging.Jpeg2000;

/// <summary>A packet of a tile: one layer of one precinct of one resolution of one component, with the place on the
/// reference grid the position-driven progressions order it by.</summary>
internal readonly record struct J2kPacket(int Layer, int Resolution, int Component, int Precinct, long Y, long X);
