namespace Adamantium.Imaging.Jpeg;

/// <summary>How many of a picture's pixels share a chroma sample when it is written as JPEG.</summary>
public enum JpegSubsampling
{
    /// <summary>4:4:4: every pixel has its own.</summary>
    None,

    /// <summary>4:2:2: two pixels side by side share one.</summary>
    HalfWidth,

    /// <summary>4:2:0: a square of four pixels shares one - the most common choice, a third smaller.</summary>
    Half
}
