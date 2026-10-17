using System;
using System.Collections.Generic;
using Adamantium.Mathematics;

namespace Adamantium.Fonts.Common
{
    internal static class BezierSampler
    {
        private const double ToleranceShare = 1.0 / 1024;
        private const double LeastTolerance = 0.25;
        private const int MostChords = 256;

        public static SampledOutline[] GenerateOutlines(this Glyph glyph, byte rate)
        {
            var box = glyph.BoundingRectangle;
            var tolerance = Math.Max(Math.Max(Math.Abs(box.Width), Math.Abs(box.Height)) * ToleranceShare, LeastTolerance);
            return glyph.OutlineType switch
            {
                OutlineType.TrueType => GenerateQuadraticBezierOutlines(glyph.Outlines, rate, tolerance),
                OutlineType.CompactFontFormat => GenerateCubicBezierOutlines(glyph.Outlines, rate, tolerance),
                _ => Array.Empty<SampledOutline>(),
            };
        }

        private static int WangChords(byte rate, double tolerance, double factor, double secondDifference)
        {
            var needed = Math.Ceiling(Math.Sqrt(factor * secondDifference / tolerance));
            if (double.IsNaN(needed))
            {
                needed = rate;
            }

            return (int)Math.Min(Math.Max(Math.Max(rate, needed), 1), MostChords);
        }

        private static double SecondDifference(Vector2 a, Vector2 b, Vector2 c) => (a - 2 * b + c).Length();

        // Cubic Bezier generator
        private static SampledOutline[] GenerateCubicBezierOutlines(IEnumerable<Outline> outlines, byte rate,
            double tolerance)
        {
            var sampledOutlines = new List<SampledOutline>();

            foreach (var outline in outlines)
            {
                var sampledSegments = new List<LineSegment2D>();

                foreach (var segment in outline.Segments)
                {
                    var sampledPoints = GenerateCubicOutlineFromSegment(segment, rate, tolerance);
                    for (var i = 0; i < (sampledPoints.Count - 1); i++)
                    {
                        var sampledSegment = new LineSegment2D(sampledPoints[i], sampledPoints[i + 1]);
                        sampledSegments.Add(sampledSegment);
                    }
                }

                sampledOutlines.Add(new SampledOutline(sampledSegments.ToArray()));
            }

            return sampledOutlines.ToArray();
        }

        private static List<Vector2> GenerateCubicOutlineFromSegment(OutlineSegment segment, byte rate, double tolerance)
        {
            var sampledPoints = new List<Vector2>();

            // If this is a line, then just return these points without modification
            if (segment.Points.Count == 2)
            {
                foreach (var point in segment.Points)
                {
                    sampledPoints.Add(new Vector2(Math.Round(point.X, 4), Math.Round(point.Y, 4)));
                }

                return sampledPoints;
            }

            var p = segment.Points;
            var chords = WangChords(rate, tolerance, 3.0 / 4,
                Math.Max(SecondDifference(p[0], p[1], p[2]), SecondDifference(p[1], p[2], p[3])));
            for (var i = 0; i <= chords; i++)
            {
                var d = (double)i / chords;
                var bezierX = Math.Pow(1 - d, 3) * p[0].X + 3 * d * Math.Pow(1 - d, 2) * p[1].X +
                              3 * Math.Pow(d, 2) * (1 - d) * p[2].X + Math.Pow(d, 3) * p[3].X;
                var bezierY = Math.Pow(1 - d, 3) * p[0].Y + 3 * d * Math.Pow(1 - d, 2) * p[1].Y +
                              3 * Math.Pow(d, 2) * (1 - d) * p[2].Y + Math.Pow(d, 3) * p[3].Y;
                sampledPoints.Add(new Vector2(Math.Round(bezierX, 4), Math.Round(bezierY, 4)));
            }

            return sampledPoints;
        }
        
        // Quadratic Bezier generator
        public static SampledOutline[] GenerateQuadraticBezierOutlines(IEnumerable<Outline> outlines, byte rate,
            double tolerance)
        {
            var sampledOutlines = new List<SampledOutline>();
            foreach (var outline in outlines)
            {
                var sampledOutline = GenerateQuadraticBezierCurves(outline, rate, tolerance);
                if (sampledOutline == SampledOutline.Empty) continue;

                sampledOutlines.Add(sampledOutline);
            }

            return sampledOutlines.ToArray();
        }
        
        private static SampledOutline GenerateQuadraticBezierCurves(Outline outline, byte rate, double tolerance)
        {
            var points = new List<Vector2>();

            foreach (var segment in outline.Segments)
            {
                // all points from segments are added omitting the [0] point in segment points list,
                // to avoid point duplication in resulting list
                
                if (segment.Points.Count <= 1) continue;

                if (segment.Points.Count == 2)
                {
                    // simple line
                    points.Add(segment.Points[1]);
                }
                else
                {
                    // bezier curve
                    var p = segment.Points;
                    var chords = WangChords(rate, tolerance, 1.0 / 4, SecondDifference(p[0], p[1], p[2]));
                    for (var i = 1; i <= chords; i++)
                    {
                        points.Add(GetQuadraticCurvePoint(p[0], p[1], p[2], (double)i / chords));
                    }
                }
            }

            if (points.Count == 0) return SampledOutline.Empty;

            // close contour by adding first point to the end of points list
            points.Add(points[0]);
            
            List<LineSegment2D> segments = new List<LineSegment2D>();
            
            for (var i = 0; i < (points.Count - 1); i++)
            {
                var sampledSegment = new LineSegment2D(points[i], points[i + 1]);
                segments.Add(sampledSegment);
            }

            return new SampledOutline(segments.ToArray());
        }

        private static Vector2 GetQuadraticCurvePoint(Vector2 begin, Vector2 control, Vector2 end, double t)
        {
            var x = QuadraticEquation(begin.X, control.X, end.X, t);
            var y = QuadraticEquation(begin.Y, control.Y, end.Y, t);
            
            // Round results because if its double, we will get a lot of digits after point and this will negatively influence on triangulation results
            // 4 digits after point will be enough
            return new Vector2(Math.Round(x, 0, MidpointRounding.AwayFromZero), Math.Round(y, 0, MidpointRounding.AwayFromZero));
        }
        
        private static double QuadraticEquation(double begin, double control, double end, double t)
        {
            var first = Math.Pow(1 - t, 2);
            var second = 2 * t * (1 - t);
            var third = Math.Pow(t, 2);

            return first * begin + second * control + third * end;
        }
    }
}