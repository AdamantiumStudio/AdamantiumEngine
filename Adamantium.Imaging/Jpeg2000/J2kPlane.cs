namespace Adamantium.Imaging.Jpeg2000;

/// <summary>Where a component's decoded samples lie: its bounds at the resolution decoded, and how many resolutions
/// below the full one that is.</summary>
internal readonly record struct J2kPlane(int X0, int Y0, int Width, int Height, int Scale);
