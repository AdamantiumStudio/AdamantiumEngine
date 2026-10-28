namespace Adamantium.Mathematics.Svg;

/// <summary>Receives SVG path data as <see cref="SvgPathData.Walk"/> reads it: every point absolute, a smooth curve's
/// reflected control point worked out, a repeated command as one step after another, and each sub-path begun with
/// <see cref="MoveTo"/>, the one after a <see cref="Close"/> too.</summary>
public interface ISvgPathSink
{
    /// <summary>Begins a sub-path at <paramref name="point"/>.</summary>
    void MoveTo(Vector2<double>point);

    void LineTo(Vector2<double>point);

    void CubicTo(Vector2<double>first, Vector2<double>second, Vector2<double>end);

    void QuadraticTo(Vector2<double>control, Vector2<double>end);

    /// <summary>An elliptical arc to <paramref name="end"/>, its radii and rotation (in degrees) as the data gives them;
    /// <see cref="SvgPathData.ArcToCubics"/> turns it into curves.</summary>
    void ArcTo(double radiusX, double radiusY, double rotation, bool largeArc, bool sweep, Vector2<double>end);

    /// <summary>Closes the sub-path back to its start.</summary>
    void Close();
}
