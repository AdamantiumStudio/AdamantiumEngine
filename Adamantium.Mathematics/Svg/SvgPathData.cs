using System;
using System.Collections.Generic;

namespace Adamantium.Mathematics.Svg;

/// <summary>SVG's number and path grammar: numbers separated by commas, spaces or by nothing at all ("1-2.5.5" is three,
/// an exponent's sign belongs to it), and path data walked step by step.</summary>
public static class SvgPathData
{
    /// <summary>Walks <paramref name="data"/> (a path's <c>d</c>) into <paramref name="sink"/>. An arc's flags are one
    /// digit each, so "a1 1 0 11 5 5" reads them. Walking stops at the first thing that is neither a command nor a whole
    /// set of arguments, or at data that does not begin with a move, keeping what came before, as SVG renders a path up to
    /// its first error.</summary>
    public static void Walk(string data, ISvgPathSink sink)
    {
        var reader = new SvgNumberReader(data);
        var current = Vector2<double>.Zero;
        var start = Vector2<double>.Zero;
        var cubic = (Vector2<double>?)null;
        var quadratic = (Vector2<double>?)null;
        var open = false;
        var begun = false;
        var arguments = new double[7];
        while (!reader.AtEnd)
        {
            var command = reader.Take();
            var letter = char.ToUpperInvariant(command);
            var relative = command != letter;
            var arity = Arity(letter);
            if (arity < 0 || (!begun && letter != 'M'))
            {
                return;
            }

            if (letter == 'Z')
            {
                sink.Close();
                current = start;
                cubic = null;
                quadratic = null;
                open = false;
                continue;
            }

            var first = true;
            do
            {
                if (!ReadSet(reader, arguments, arity))
                {
                    return;
                }

                var origin = relative ? current : Vector2<double>.Zero;
                if (letter == 'M' && first)
                {
                    current = start = origin + new Vector2<double>(arguments[0], arguments[1]);
                    sink.MoveTo(current);
                    begun = true;
                    open = true;
                    first = false;
                    cubic = null;
                    quadratic = null;
                    continue;
                }

                if (!open)
                {
                    sink.MoveTo(current);
                    open = true;
                }

                Vector2<double>? nextCubic = null;
                Vector2<double>? nextQuadratic = null;
                switch (letter)
                {
                    case 'M':
                    case 'L':
                        current = origin + new Vector2<double>(arguments[0], arguments[1]);
                        sink.LineTo(current);
                        break;
                    case 'H':
                        current = new Vector2<double>((relative ? current.X : 0) + arguments[0], current.Y);
                        sink.LineTo(current);
                        break;
                    case 'V':
                        current = new Vector2<double>(current.X, (relative ? current.Y : 0) + arguments[0]);
                        sink.LineTo(current);
                        break;
                    case 'C':
                    {
                        var second = origin + new Vector2<double>(arguments[2], arguments[3]);
                        var end = origin + new Vector2<double>(arguments[4], arguments[5]);
                        sink.CubicTo(origin + new Vector2<double>(arguments[0], arguments[1]), second, end);
                        nextCubic = second;
                        current = end;
                        break;
                    }
                    case 'S':
                    {
                        var firstControl = cubic is { } had ? current * 2 - had : current;
                        var second = origin + new Vector2<double>(arguments[0], arguments[1]);
                        var end = origin + new Vector2<double>(arguments[2], arguments[3]);
                        sink.CubicTo(firstControl, second, end);
                        nextCubic = second;
                        current = end;
                        break;
                    }
                    case 'Q':
                    {
                        var control = origin + new Vector2<double>(arguments[0], arguments[1]);
                        var end = origin + new Vector2<double>(arguments[2], arguments[3]);
                        sink.QuadraticTo(control, end);
                        nextQuadratic = control;
                        current = end;
                        break;
                    }
                    case 'T':
                    {
                        var control = quadratic is { } had ? current * 2 - had : current;
                        var end = origin + new Vector2<double>(arguments[0], arguments[1]);
                        sink.QuadraticTo(control, end);
                        nextQuadratic = control;
                        current = end;
                        break;
                    }
                    case 'A':
                    {
                        var end = origin + new Vector2<double>(arguments[5], arguments[6]);
                        sink.ArcTo(arguments[0], arguments[1], arguments[2], arguments[3] != 0, arguments[4] != 0, end);
                        current = end;
                        break;
                    }
                }

                cubic = nextCubic;
                quadratic = nextQuadratic;
            }
            while (IsNumberStart(reader.Peek()));
        }
    }

    /// <summary>The numbers of <paramref name="text"/>, as a transform's arguments or a polygon's points are written.</summary>
    public static List<double> ReadNumbers(string text)
    {
        var numbers = new List<double>();
        var reader = new SvgNumberReader(text);
        while (reader.TryReadNumber(out var number))
        {
            numbers.Add(number);
        }

        return numbers;
    }

    /// <summary>An SVG arc from <paramref name="start"/> to <paramref name="end"/> as cubic curves into
    /// <paramref name="sink"/>, none sweeping more than <paramref name="maxPiece"/> radians: the radii taken as their
    /// sizes and grown to reach the end when too small, as SVG says; a line for a radius of zero or infinity, nothing for
    /// an arc that ends where it starts.</summary>
    public static void ArcToCubics(Vector2<double> start, double radiusX, double radiusY, double rotation, bool largeArc,
        bool sweep, Vector2<double> end, double maxPiece, ISvgPathSink sink)
    {
        var rx = Math.Abs(radiusX);
        var ry = Math.Abs(radiusY);
        if (start == end)
        {
            return;
        }

        if (rx < 1e-12 || ry < 1e-12 || double.IsInfinity(rx) || double.IsInfinity(ry))
        {
            sink.LineTo(end);
            return;
        }

        var phi = rotation * Math.PI / 180;
        var cos = Math.Cos(phi);
        var sin = Math.Sin(phi);
        var dx = (start.X - end.X) / 2;
        var dy = (start.Y - end.Y) / 2;
        var x1 = cos * dx + sin * dy;
        var y1 = -sin * dx + cos * dy;
        var lambda = x1 * x1 / (rx * rx) + y1 * y1 / (ry * ry);
        if (lambda > 1)
        {
            rx *= Math.Sqrt(lambda);
            ry *= Math.Sqrt(lambda);
        }

        var numerator = rx * rx * ry * ry - rx * rx * y1 * y1 - ry * ry * x1 * x1;
        var denominator = rx * rx * y1 * y1 + ry * ry * x1 * x1;
        var factor = Math.Sqrt(Math.Max(0, numerator / denominator)) * (largeArc == sweep ? -1 : 1);
        var cx1 = factor * rx * y1 / ry;
        var cy1 = -factor * ry * x1 / rx;
        var cx = cos * cx1 - sin * cy1 + (start.X + end.X) / 2;
        var cy = sin * cx1 + cos * cy1 + (start.Y + end.Y) / 2;
        var theta = Angle(1, 0, (x1 - cx1) / rx, (y1 - cy1) / ry);
        var delta = Angle((x1 - cx1) / rx, (y1 - cy1) / ry, (-x1 - cx1) / rx, (-y1 - cy1) / ry);
        if (!sweep && delta > 0)
        {
            delta -= 2 * Math.PI;
        }
        else if (sweep && delta < 0)
        {
            delta += 2 * Math.PI;
        }

        var pieces = Math.Max(1, (int)Math.Ceiling(Math.Abs(delta) / maxPiece - 1e-9));
        var piece = delta / pieces;
        var handle = 4.0 / 3 * Math.Tan(piece / 4);
        for (var i = 0; i < pieces; i++)
        {
            var a0 = theta + i * piece;
            var a1 = a0 + piece;
            var p0 = ArcPoint(cx, cy, rx, ry, cos, sin, a0);
            var p3 = i == pieces - 1 ? end : ArcPoint(cx, cy, rx, ry, cos, sin, a1);
            sink.CubicTo(p0 + ArcTangent(rx, ry, cos, sin, a0) * handle, p3 - ArcTangent(rx, ry, cos, sin, a1) * handle, p3);
        }
    }

    private static bool ReadSet(SvgNumberReader reader, double[] arguments, int arity)
    {
        for (var i = 0; i < arity; i++)
        {
            var isFlag = arity == 7 && i is 3 or 4;
            if (!(isFlag ? reader.TryReadFlag(out arguments[i]) : reader.TryReadNumber(out arguments[i])))
            {
                return false;
            }
        }

        return true;
    }

    private static double Angle(double ux, double uy, double vx, double vy) =>
        Math.Atan2(ux * vy - uy * vx, ux * vx + uy * vy);

    private static Vector2<double> ArcPoint(double cx, double cy, double rx, double ry, double cos, double sin, double angle)
    {
        var x = rx * Math.Cos(angle);
        var y = ry * Math.Sin(angle);
        return new Vector2<double>(cx + cos * x - sin * y, cy + sin * x + cos * y);
    }

    private static Vector2<double> ArcTangent(double rx, double ry, double cos, double sin, double angle)
    {
        var x = -rx * Math.Sin(angle);
        var y = ry * Math.Cos(angle);
        return new Vector2<double>(cos * x - sin * y, sin * x + cos * y);
    }

    private static bool IsNumberStart(char c) => c is >= '0' and <= '9' or '-' or '+' or '.';

    private static int Arity(char letter)
    {
        switch (letter)
        {
            case 'Z':
                return 0;
            case 'H':
            case 'V':
                return 1;
            case 'M':
            case 'L':
            case 'T':
                return 2;
            case 'S':
            case 'Q':
                return 4;
            case 'C':
                return 6;
            case 'A':
                return 7;
            default:
                return -1;
        }
    }
}
