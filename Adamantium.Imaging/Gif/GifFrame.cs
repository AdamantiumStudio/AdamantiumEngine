using System;

namespace Adamantium.Imaging.Gif;

/// <summary>What is done with a GIF frame before the next is drawn (GIF89a, Graphic Control Extension).</summary>
public enum GifDisposal
{
    /// <summary>The file does not say: left as it is.</summary>
    Unspecified = 0,

    /// <summary>Left as it is.</summary>
    None = 1,

    /// <summary>Its region cleared - to transparent, as browsers do.</summary>
    Background = 2,

    /// <summary>Its region given back what was under it.</summary>
    Previous = 3
}

/// <summary>A GIF frame: its place on the canvas, how long it is shown and what is done with it after.</summary>
public readonly record struct GifFrame(uint X, uint Y, uint Width, uint Height, ushort DelayCentiseconds,
    GifDisposal Disposal)
{
    /// <summary>How long the frame is shown.</summary>
    public TimeSpan Delay => TimeSpan.FromMilliseconds(DelayCentiseconds * 10);
}
