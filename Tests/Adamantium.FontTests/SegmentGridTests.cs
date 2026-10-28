using System.Collections.Generic;
using System.Linq;
using Adamantium.Fonts;
using Adamantium.Fonts.Common;
using Adamantium.Fonts.TextureGeneration;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.FontTests;

public class SegmentGridTests
{
    [TestCase("TTFFonts/SourceSans3-Regular.ttf", "@&gQW8%")]
    [TestCase("OTFFonts/SourceSans3-Regular.otf", "@&gQW8%")]
    public void FindsTheSameNearestSegmentsAsTryingEveryOne_InEveryChannel(string path, string characters)
    {
        var font = Typeface.LoadFont(path, 3).GetFont(0);
        foreach (var character in characters)
        {
            var glyph = font.GetGlyphByCharacter(character);
            glyph.Sample(3);
            var segments = glyph.GetMergedOutlineSegments();
            var channels = segments.Select((_, index) => (1 << (index % 3)) | (index % 5 == 0 ? 7 : 0) | 8).ToList();
            var search = new SegmentSearch(new SegmentGrid(segments, channels));
            var bounds = glyph.BoundingRectangle;
            List<LineSegment2D>[] found = [[], [], [], []];

            for (var y = bounds.Y - 100.0; y <= bounds.Bottom + 100; y += 7.3)
            {
                for (var x = bounds.X - 100.0; x <= bounds.Right + 100; x += 7.3)
                {
                    var point = new Vector2<double>(x, y);
                    search.FindNearest(point, found);

                    for (var channel = 0; channel < SegmentGrid.ChannelCount; channel++)
                    {
                        var members = segments.Where((_, index) => (channels[index] & (1 << channel)) != 0).ToList();
                        Assert.That(found[channel], Is.EqualTo(Nearest(members, point)),
                            $"'{character}' at {x}, {y}, channel {channel}");
                    }
                }
            }
        }
    }

    private static List<LineSegment2D> Nearest(List<LineSegment2D> segments, Vector2<double> point)
    {
        var best = double.MaxValue;
        var nearest = new List<LineSegment2D>();
        foreach (var segment in segments)
        {
            var distance = GlyphSegmentsMath.GetDistanceToSegment(segment, point);
            var tolerance = 1e-9 * System.Math.Max(System.Math.Min(best, distance), 1);
            if (distance < best - tolerance)
            {
                best = distance;
                nearest.Clear();
                nearest.Add(segment);
            }
            else if (distance <= best + tolerance)
            {
                best = System.Math.Min(best, distance);
                nearest.Add(segment);
            }
        }

        return nearest;
    }

    [Test]
    public void BothEdgesOfAVertex_AreNearest_WhenTheirDistancesDifferInTheLastDigits()
    {
        LineSegment2D[] diamond =
        [
            Segment(0, 710, 600, 1310),
            Segment(600, 1310, 1200, 710),
            Segment(1200, 710, 600, 110),
            Segment(600, 110, 0, 710),
        ];
        var point = new Vector2<double>(1342.1052631578946, 852.1052631578948);
        List<LineSegment2D>[] found = [[], [], [], []];

        new SegmentSearch(new SegmentGrid(diamond, [15, 15, 15, 15])).FindNearest(point, found);

        Assert.That(found[3], Is.EqualTo(new[] { diamond[1], diamond[2] }), "the two edges meeting at (1200, 710)");
        Assert.That(GlyphSegmentsMath.GetSignedDistanceToSegmentsJoint(found[3], point, false), Is.GreaterThan(0),
            "the point lies outside the diamond, whichever edge its distance is taken from");
    }

    private static LineSegment2D Segment(double x0, double y0, double x1, double y1) =>
        new(new Vector2<double>(x0, y0), new Vector2<double>(x1, y1));
}
