using System;
using System.Collections.Generic;
using Adamantium.Mathematics.Triangulation;

namespace Adamantium.Mathematics;

public static class ContourProcessingHelper
{
    public static void ProcessContoursIntersections(List<GeometrySegment> contoursSegments1, List<GeometrySegment> contoursSegments2)
    {
        var intersectionsList = new Dictionary<Vector2<double>, GeometryIntersection>();
        var mesh1Intersections = new Dictionary<GeometrySegment, SortedList<double, GeometryIntersection>>();
        var mesh2Intersections = new Dictionary<GeometrySegment, SortedList<double, GeometryIntersection>>();

        // Seed every segment with its own endpoints ONCE (the old double loop re-seeded segment2's endpoints n*m times).
        foreach (var segment1 in contoursSegments1)
        {
            mesh1Intersections[segment1] = new SortedList<double, GeometryIntersection>();
            ProcessIntersection(segment1.Start, intersectionsList, mesh1Intersections, segment1);
            ProcessIntersection(segment1.End, intersectionsList, mesh1Intersections, segment1);
        }
        foreach (var segment2 in contoursSegments2)
        {
            mesh2Intersections[segment2] = new SortedList<double, GeometryIntersection>();
            ProcessIntersection(segment2.Start, intersectionsList, mesh2Intersections, segment2);
            ProcessIntersection(segment2.End, intersectionsList, mesh2Intersections, segment2);
        }

        // Broad-phase: sort segments2 by min-X and, for each segment1, test only those whose X-range overlaps it
        // (sorted, so stop at the first min-X past segment1's max-X). X-disjoint segments can't cross, so this finds
        // exactly the same crossings as the old all-pairs scan but skips most pairs.
        var sorted2 = new List<GeometrySegment>(contoursSegments2);
        sorted2.Sort((a, b) => Math.Min(a.Start.X, a.End.X).CompareTo(Math.Min(b.Start.X, b.End.X)));

        foreach (var segment1 in contoursSegments1)
        {
            var s1MinX = Math.Min(segment1.Start.X, segment1.End.X);
            var s1MaxX = Math.Max(segment1.Start.X, segment1.End.X);

            foreach (var segment2 in sorted2)
            {
                if (Math.Min(segment2.Start.X, segment2.End.X) > s1MaxX) break;          // sorted -> no more overlaps
                if (Math.Max(segment2.Start.X, segment2.End.X) < s1MinX) continue;       // X-disjoint -> can't cross

                if (Collision2D.SegmentSegmentIntersection(segment1, segment2, out var point))
                {
                    ProcessIntersection(point, intersectionsList, mesh1Intersections, segment1);
                    ProcessIntersection(point, intersectionsList, mesh2Intersections, segment2);
                }
            }
        }

        FillMeshSegments(mesh1Intersections);
        FillMeshSegments(mesh2Intersections);
    }
    
    private static void ProcessIntersection(Vector2<double> point, 
                                            Dictionary<Vector2<double>, GeometryIntersection> intersectionsList,
                                            Dictionary<GeometrySegment, SortedList<double, GeometryIntersection>> meshIntersections,
                                            GeometrySegment segment)
    {
        if (!intersectionsList.ContainsKey(point))
        {
            intersectionsList[point] = new GeometryIntersection(point);
        }

        var distanceToStart = (point - segment.Start).Length();
        if (!meshIntersections[segment].ContainsKey(distanceToStart)) meshIntersections[segment].Add(distanceToStart, intersectionsList[point]);
    }
    
    private static void FillMeshSegments(Dictionary<GeometrySegment, SortedList<double, GeometryIntersection>> meshIntersections)
    {
        foreach (var pair in meshIntersections)
        {
            if (pair.Value.Count < 2) continue;

            pair.Key.RemoveSelfFromConnectedSegments();
            pair.Key.RemoveSelfFromParent();

            for (var i = 0; i < pair.Value.Values.Count - 1; i++)
            {
                var newStart = pair.Value.Values[i];
                var newEnd = pair.Value.Values[i + 1];
                var newSegment = new GeometrySegment(pair.Key.Parent, newStart, newEnd);
                pair.Key.Parent.Segments.Add(newSegment);
            }
        }
    }
    
    // Check position of second contour's segments against first contour's segments (if contour 2 partially or fully inside or outside of contour 1) 
    public static List<GeometrySegment> MarkSegments(List<GeometrySegment> contourSegments, List<GeometrySegment> checkedContourSegments)
    {
        var arguableSegments = new List<GeometrySegment>();
        
        foreach (var segment2 in checkedContourSegments)
        {
            var segmentCenter = (segment2.End - segment2.Start) * 0.5 + segment2.Start;
            
            var ray = new Ray2<double>(segmentCenter, Vector2<double>.UnitX);
            var intersectionCnt = 0;
            var rayIntersectionPoints = new HashSet<Vector2<double>>();
            
            foreach (var segment1 in contourSegments)
            {
                if (segment1.IsEqualTo(segment2))
                {
                    segment2.IsArguable = true;
                    arguableSegments.Add(segment2);

                    segment1.RemoveSelfFromConnectedSegments();
                    segment1.RemoveSelfFromParent();

                    break;
                }

                // The ray is horizontal (UnitX) at segmentCenter.Y, so a segment whose Y-range doesn't include that
                // height can't be crossed - skip the ray math (exact: same crossing count, just fewer tests).
                if (segmentCenter.Y < Math.Min(segment1.Start.Y, segment1.End.Y) ||
                    segmentCenter.Y > Math.Max(segment1.Start.Y, segment1.End.Y)) continue;

                var seg = new LineSegment2D(segment1);
                if (Collision2D.RaySegmentIntersection(ref ray, ref seg, out var intPoint))
                {
                    intPoint = Vector2<double>.Round(intPoint, 4);
                    
                    if (!rayIntersectionPoints.Contains(intPoint))
                    {
                        rayIntersectionPoints.Add(intPoint);
                        ++intersectionCnt;
                    }
                }
            }

            if (segment2.IsArguable) continue;
            
            if (intersectionCnt % 2 != 0)
            {
                segment2.IsInner = true;
            }
        }

        return arguableSegments;
    }

    public static void ResolveArguableSegments(List<GeometrySegment> arguableSegments, List<GeometrySegment> contoursSegments)
    {
        if (arguableSegments == null || arguableSegments.Count < 1) return;
        if (contoursSegments == null || contoursSegments.Count < 3) return; // because area is formed with at least 3 segments

        foreach (var arguableSeg in arguableSegments)
        {
            arguableSeg.IsInner = Collision2D.IsSegmentInsideArea(arguableSeg, contoursSegments);
        }
    }
}