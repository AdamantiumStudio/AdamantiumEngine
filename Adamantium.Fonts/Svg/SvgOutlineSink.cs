using System;
using System.Collections.Generic;
using Adamantium.Fonts.Common;
using Adamantium.Mathematics;
using Adamantium.Mathematics.Svg;

namespace Adamantium.Fonts.Svg;

internal sealed class SvgOutlineSink : ISvgPathSink
{
    private const double MaxArcPiece = Math.PI / 2;

    private List<OutlinePoint> contour;
    private Vector2 current;

    public List<List<OutlinePoint>> Contours { get; } = [];

    public void MoveTo(Vector2 point)
    {
        Finish();
        contour = [new OutlinePoint(point)];
        current = point;
    }

    public void LineTo(Vector2 point)
    {
        contour.Add(new OutlinePoint(point));
        current = point;
    }

    public void CubicTo(Vector2 first, Vector2 second, Vector2 end)
    {
        contour.Add(new OutlinePoint(first, true));
        contour.Add(new OutlinePoint(second, true));
        contour.Add(new OutlinePoint(end));
        current = end;
    }

    public void QuadraticTo(Vector2 control, Vector2 end)
    {
        CubicTo(current + (control - current) * (2.0 / 3), end + (control - end) * (2.0 / 3), end);
    }

    public void ArcTo(double radiusX, double radiusY, double rotation, bool largeArc, bool sweep, Vector2 end)
    {
        SvgPathData.ArcToCubics(current, radiusX, radiusY, rotation, largeArc, sweep, end, MaxArcPiece, this);
        current = end;
    }

    public void Close()
    {
        Finish();
    }

    public List<List<OutlinePoint>> Finish()
    {
        if (contour == null)
        {
            return Contours;
        }

        var last = contour[contour.Count - 1];
        if (contour.Count > 2 && !contour[contour.Count - 2].IsControl && (Vector2)last == (Vector2)contour[0])
        {
            contour.RemoveAt(contour.Count - 1);
        }

        if (contour.Count > 2)
        {
            Contours.Add(contour);
        }

        contour = null;
        return Contours;
    }
}
