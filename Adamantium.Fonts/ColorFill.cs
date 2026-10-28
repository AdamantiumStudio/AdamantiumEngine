using System.Collections.Generic;
using Adamantium.Mathematics;

namespace Adamantium.Fonts;

/// <summary>
/// What a fill step of a color glyph paints ('COLR' version 1): one color or a gradient. Points and radii are in the
/// gradient's own space, which <see cref="Transform"/> maps into the glyph's, in font units with y up.
/// </summary>
public sealed class ColorFill
{
    public ColorFill(ColorFillKind kind, IReadOnlyList<ColorStop> stops, ColorExtend extend, Matrix3x2<double> transform)
    {
        Kind = kind;
        Stops = stops;
        Extend = extend;
        Transform = transform;
    }

    public ColorFillKind Kind { get; }

    /// <summary>The colors: one stop for a solid fill, in order of offset for a gradient.</summary>
    public IReadOnlyList<ColorStop> Stops { get; }

    public ColorExtend Extend { get; }

    /// <summary>From the gradient's space into the glyph's.</summary>
    public Matrix3x2<double> Transform { get; }

    /// <summary>A linear gradient's start; a radial gradient's first circle's center; a sweep's center.</summary>
    public Vector2<double> Point0 { get; internal set; }

    /// <summary>A linear gradient's end; a radial gradient's second circle's center.</summary>
    public Vector2<double> Point1 { get; internal set; }

    /// <summary>A linear gradient's rotation point: the gradient runs along Point0 - Point1, perpendicular to
    /// Point0 - Point2.</summary>
    public Vector2<double> Point2 { get; internal set; }

    /// <summary>A radial gradient's first circle's radius.</summary>
    public double Radius0 { get; internal set; }

    /// <summary>A radial gradient's second circle's radius.</summary>
    public double Radius1 { get; internal set; }

    /// <summary>A sweep's start angle, in degrees counter-clockwise from the x axis.</summary>
    public double StartAngle { get; internal set; }

    /// <summary>A sweep's end angle, in degrees.</summary>
    public double EndAngle { get; internal set; }
}
