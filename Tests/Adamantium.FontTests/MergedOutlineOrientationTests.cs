using Adamantium.Fonts;
using Adamantium.Fonts.Common;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.FontTests;

public class MergedOutlineOrientationTests
{
    [TestCase(OutlineType.TrueType)]
    [TestCase(OutlineType.CompactFontFormat)]
    public void MergedSegmentsFillOnTheSideTheirFormatFillsOn(OutlineType outlineType)
    {
        var glyph = new Glyph(1, outlineType);
        glyph.AddOutline(Square(0, 0, 100, clockwise: true));
        glyph.AddOutline(Square(300, 0, 100, clockwise: false));
        glyph.RecalculateBounds(true);

        glyph.Sample(3);

        var fillsOnLeft = outlineType != OutlineType.TrueType;
        foreach (var segment in glyph.GetMergedOutlineSegments())
        {
            var direction = segment.End - segment.Start;
            var left = new Vector2<double>(-direction.Y, direction.X) * (1 / direction.Length());
            var middle = (segment.Start + segment.End) * 0.5;
            var inside = fillsOnLeft ? middle + left : middle - left;

            Assert.That(IsInEitherSquare(inside), Is.True, $"{segment.Start} -> {segment.End}");
        }
    }

    private static Outline Square(double x, double y, double size, bool clockwise)
    {
        var outline = new Outline();
        Vector2<double>[] corners = clockwise
            ? [new(x, y), new(x, y + size), new(x + size, y + size), new(x + size, y)]
            : [new(x, y), new(x + size, y), new(x + size, y + size), new(x, y + size)];
        foreach (var corner in corners)
        {
            outline.Points.Add(new OutlinePoint(corner));
        }

        outline.NumberOfPoints = (ushort)corners.Length;
        return outline;
    }

    private static bool IsInEitherSquare(Vector2<double> point)
    {
        return point.Y is > 0 and < 100 && (point.X is > 0 and < 100 || point.X is > 300 and < 400);
    }
}
