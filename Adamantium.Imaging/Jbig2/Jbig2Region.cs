namespace Adamantium.Imaging.Jbig2;

/// <summary>An intermediate region kept for the refinement that refers to it.</summary>
internal sealed record Jbig2Region(Jbig2Bitmap Bitmap, Jbig2RegionInfo Info);
