using System;

namespace Adamantium.Graphics.Core;

/// <summary>How crisp text's edges blend over what is under them; shadows and glows keep their own soft edge. Read on
/// every text draw, so a change shows on the next frame drawn. Sizes are the glyph field's range in screen pixels:
/// with the engine's default atlas, a quarter of the font's size in pixels.</summary>
public sealed class TextEdgeSettings
{
    private float _lightInkGamma = 2.2f;
    private float _smallDarkInkGamma = 1.9f;
    private float _largeDarkInkGamma = 1.6f;
    private float _largeTextFrom = 3f;
    private float _largeTextAt = 7f;
    private float _edgeWidth = 0.5f;

    /// <summary>The gamma light ink on dark paper blends by: its edge coverage is raised by one over it. Above
    /// zero.</summary>
    public float LightInkGamma
    {
        get => _lightInkGamma;
        set => _lightInkGamma = Positive(value);
    }

    /// <summary>The gamma small dark ink on light paper blends by: its edge coverage is raised by one over it, so thin
    /// stems stay dark. Above zero.</summary>
    public float SmallDarkInkGamma
    {
        get => _smallDarkInkGamma;
        set => _smallDarkInkGamma = Positive(value);
    }

    /// <summary>The gamma large dark ink on light paper blends by: what its edge leaves uncovered is raised by one over
    /// it, so its edges do not darken into a rim. Above zero.</summary>
    public float LargeDarkInkGamma
    {
        get => _largeDarkInkGamma;
        set => _largeDarkInkGamma = Positive(value);
    }

    /// <summary>The size dark ink starts going from the small curve to the large one at. Zero or more.</summary>
    public float LargeTextFrom
    {
        get => _largeTextFrom;
        set => _largeTextFrom = NotNegative(value);
    }

    /// <summary>The size dark ink is on the large curve from; at or below <see cref="LargeTextFrom"/> it switches there
    /// at once. Zero or more.</summary>
    public float LargeTextAt
    {
        get => _largeTextAt;
        set => _largeTextAt = NotNegative(value);
    }

    /// <summary>How many screen pixels each of the four points a pixel's coverage is taken at ramps over; the edge
    /// drawn ramps over 3/4 of a pixel more. Above zero.</summary>
    public float EdgeWidth
    {
        get => _edgeWidth;
        set => _edgeWidth = Positive(value);
    }

    private static float Positive(float value) => float.IsFinite(value) && value > 0
        ? value
        : throw new ArgumentOutOfRangeException(nameof(value), value, "Expected a finite value above zero.");

    private static float NotNegative(float value) => float.IsFinite(value) && value >= 0
        ? value
        : throw new ArgumentOutOfRangeException(nameof(value), value, "Expected a finite value of zero or more.");
}
