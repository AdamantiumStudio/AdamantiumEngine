namespace Adamantium.Graphics.Core;

/// <summary>How text's edges blend over what is under them. Read on every text draw, so a change shows on the next
/// frame drawn.</summary>
public sealed class TextEdgeSettings
{
    /// <summary>The gamma light ink on dark paper blends by: its edge coverage is raised by one over it.</summary>
    public float LightInkGamma { get; set; } = 2.2f;

    /// <summary>The gamma small dark ink on light paper blends by: its edge coverage is raised by one over it, so thin
    /// stems stay dark.</summary>
    public float SmallDarkInkGamma { get; set; } = 1.9f;

    /// <summary>The gamma large dark ink on light paper blends by: what its edge leaves uncovered is raised by one over
    /// it, so its edges do not darken into a rim.</summary>
    public float LargeDarkInkGamma { get; set; } = 1.6f;

    /// <summary>The size dark ink starts going from the small curve to the large one at, as the glyph field's range in
    /// screen pixels - about a third of the font's size in pixels.</summary>
    public float LargeTextFrom { get; set; } = 3f;

    /// <summary>The size dark ink is on the large curve from, as <see cref="LargeTextFrom"/> measures it.</summary>
    public float LargeTextAt { get; set; } = 7f;

    /// <summary>How many screen pixels a crisp edge ramps over.</summary>
    public float EdgeWidth { get; set; } = 0.5f;
}
