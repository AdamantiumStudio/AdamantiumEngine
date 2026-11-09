namespace Adamantium.Imaging.Jpeg2000;

/// <summary>How much of a JPEG 2000 image to decode: fewer resolutions give a smaller picture, fewer quality layers a
/// coarser one, and both read and decode less.</summary>
public sealed class Jpeg2000Options
{
    /// <summary>The highest resolutions left out, each halving the picture's width and height; no more than the image
    /// has decomposition levels.</summary>
    public int DiscardLevels { get; init; }

    /// <summary>The quality layers decoded, from the first; 0 for all of them.</summary>
    public int Layers { get; init; }
}
