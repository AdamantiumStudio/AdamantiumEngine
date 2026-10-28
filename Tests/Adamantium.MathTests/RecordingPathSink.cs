using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Adamantium.Mathematics;
using Adamantium.Mathematics.Svg;

namespace Adamantium.MathTests;

internal sealed class RecordingPathSink : ISvgPathSink
{
    private readonly List<string> steps = [];

    public override string ToString() => string.Join(" ", steps);

    public void MoveTo(Vector2<double> point) => Add("M", point.X, point.Y);

    public void LineTo(Vector2<double> point) => Add("L", point.X, point.Y);

    public void CubicTo(Vector2<double> first, Vector2<double> second, Vector2<double> end) =>
        Add("C", first.X, first.Y, second.X, second.Y, end.X, end.Y);

    public void QuadraticTo(Vector2<double> control, Vector2<double> end) => Add("Q", control.X, control.Y, end.X, end.Y);

    public void ArcTo(double radiusX, double radiusY, double rotation, bool largeArc, bool sweep, Vector2<double> end) =>
        Add("A", radiusX, radiusY, rotation, largeArc ? 1 : 0, sweep ? 1 : 0, end.X, end.Y);

    public void Close() => steps.Add("Z");

    private void Add(string letter, params double[] values) =>
        steps.Add(letter + string.Join(",", values.Select(v => v.ToString(CultureInfo.InvariantCulture))));
}
