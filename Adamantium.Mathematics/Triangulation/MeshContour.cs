using System;
using System.Collections.Generic;
using System.Linq;

namespace Adamantium.Mathematics.Triangulation;

public class MeshContour
{
    /// <summary>Coordinates are snapped to this many decimals on ingestion. The triangulator compares points
    /// exactly (==, Dictionary&lt;Vector2<double>&gt; keys), so without snapping, coordinates that are equal up to
    /// floating-point noise are treated as distinct points — producing spurious strips/holes (e.g. when combining
    /// nested ellipses). 5 decimals is far finer than any 2D UI layout needs.</summary>
    public const int CoordinatePrecision = 5;

    public string Name { get; set; }
    public Vector2<double>[] Points { get; set; } = [];

    public List<GeometryIntersection> GeometryPoints { get; set; }

    public List<GeometrySegment> Segments { get; set; }

    public bool IsGeometryClosed { get; set; }

    /// <summary>True once <see cref="RemoveSelfIntersections"/> has split this contour at a self-crossing. A simple
    /// (false) contour can be triangulated by earcut; a self-intersecting one must use the scanline (which resolves
    /// crossings per fill rule).</summary>
    public bool HadSelfIntersections { get; private set; }

    public RectangleF BoundingBox { get; private set; }

    public MeshContour()
    {

    }

    public MeshContour(IEnumerable<Vector3<double>> points, bool isGeometryClosed = true, bool generateSegments = false) :
        this()
    {
        var pts = new List<Vector2<double>>();
        foreach (var vector3 in points)
        {
            pts.Add((Vector2<double>)vector3);
        }

        IsGeometryClosed = isGeometryClosed;
        SetPoints(pts, generateSegments);
    }
    
    public MeshContour(IEnumerable<Vector2<double>> points, bool isGeometryClosed = true, bool generateSegments = false) : this()
    {
        IsGeometryClosed = isGeometryClosed;
        SetPoints(points, generateSegments);
    }
    
    public MeshContour(IEnumerable<GeometrySegment> segments, bool isGeometryClosed = true) : this()
    {
        Segments = segments.ToList();
        
        IsGeometryClosed = isGeometryClosed;
        
        UpdatePoints();

        CalculateBoundingBox();
    }
    
    private void CalculateBoundingBox()
    {
        if (Points.Length > 0)
        {
            BoundingBox = Rectangle.CreateBounding<double>(Points).As<float>();
        }
    }
    
    public void SetOrUpdatePoints(IEnumerable<Vector2<double>> points, bool generateSegments = true)
    {
        SetPoints(points, generateSegments);
    }

    public void SetPoints(IEnumerable<Vector2<double>> points, bool generateSegments = true)
    {
        var tmpList = new List<Vector2<double>>();
        // Snap to CoordinatePrecision first so floating-point-noise-equal coordinates become exactly equal for the
        // triangulator's exact point comparisons (and the dedup below collapses the now-coincident points).
        var pts = points.Select(p => Vector2<double>.Round(p, CoordinatePrecision)).ToArray();
        // Check for duplicating points going together and remove them
        for (int i = 0; i < pts.Length - 1; i++)
        {
            var p1 = pts[i];
            var p2 = pts[i + 1];
            if (p1 != p2)
            {
                tmpList.Add(p1);
            }
            
            if (i + 1 == pts.Length - 1 && tmpList.Count > 0 && p2 != tmpList[0])
            {
                tmpList.Add(p2);
            }
        }
        
        Points = tmpList.ToArray();
        Segments?.Clear();
        if (generateSegments)
        {
            SplitOnSegments();
        }

        CalculateBoundingBox();
    }

    public void SplitOnSegments()
    {
        Segments = PolygonHelper.SplitOnSegments(this, Points, IsGeometryClosed);
        UpdatePoints();
    }
    
    public void UpdatePoints()
    {
        if (Segments == null || Segments.Count == 0)
        {
            GeometryPoints?.Clear();
            Points = Array.Empty<Vector2<double>>();
            return;
        }

        var pointsHashSet = new HashSet<GeometryIntersection>();
        GeometryPoints = new List<GeometryIntersection>();
        var updatedPoints = new List<Vector2<double>>();

        foreach (var segment in Segments)
        {
            foreach (var end in segment.SegmentEnds)
            {
                if (!pointsHashSet.Contains(end))
                {
                    pointsHashSet.Add(end);
                    GeometryPoints.Add(end);
                    updatedPoints.Add(end.Coordinates);
                }
            }
        }
        
        Points = updatedPoints.ToArray();

        CalculateBoundingBox();
    }
    
    public void UpdateSegmentsOrder()
    {
        if (Segments == null || Segments.Count == 0) return;

        var orderedSegments = new List<GeometrySegment>();
        
        var startSegment = Segments[0];
        var currentSegment = startSegment;

        do
        {
            orderedSegments.Add(currentSegment);
            currentSegment = currentSegment.GetNextSegment();
        } while (currentSegment != startSegment && currentSegment != null);

        Segments = orderedSegments;
        
        UpdatePoints();
    }

    public void RemoveSegmentsByRule(bool removeInner)
    {
        var tmpSegments = new List<GeometrySegment>(Segments);
        
        foreach (var segment in tmpSegments)
        {
            if (segment.IsArguable) continue;
            
            var delete = !(removeInner ^ segment.IsInner);
            if (delete)
            {
                segment.RemoveSelfFromConnectedSegments();
                segment.RemoveSelfFromParent();
            }
        }
        
        UpdatePoints();
    }

    public void RemoveSelfIntersections(FillRule fillRule)
    {
        var intersectionsList = new Dictionary<Vector2<double>, GeometryIntersection>();
        var selfIntersections = new Dictionary<GeometrySegment, SortedList<double, GeometryIntersection>>();
        foreach (var segment in Segments) selfIntersections[segment] = new SortedList<double, GeometryIntersection>();

        void Record(GeometrySegment seg, Vector2<double> point)
        {
            // Snap computed intersection points to the same precision as contour points, so they coincide cleanly
            // with existing endpoints instead of landing a floating-point-epsilon away.
            point = Vector2<double>.Round(point, CoordinatePrecision);
            if (point == seg.Start || point == seg.End) return;
            if (!intersectionsList.TryGetValue(point, out var inter))
            {
                inter = new GeometryIntersection(point);
                intersectionsList[point] = inter;
            }
            var distanceToStart = (point - seg.Start).Length();
            if (!selfIntersections[seg].ContainsKey(distanceToStart)) selfIntersections[seg].Add(distanceToStart, inter);
        }

        // Broad-phase: sort by min-X and sweep an active list, so only segments whose X-ranges overlap are tested.
        // This finds exactly the same intersections as the old O(n^2) all-pairs scan (X-disjoint segments cannot
        // cross), but prunes most pairs -> near-linear for simple contours instead of quadratic.
        var ordered = new List<GeometrySegment>(Segments);
        ordered.Sort((a, b) => Math.Min(a.Start.X, a.End.X).CompareTo(Math.Min(b.Start.X, b.End.X)));
        var active = new List<GeometrySegment>();
        foreach (var seg in ordered)
        {
            var segMinX = Math.Min(seg.Start.X, seg.End.X);
            for (var i = active.Count - 1; i >= 0; i--)
            {
                if (Math.Max(active[i].Start.X, active[i].End.X) < segMinX) active.RemoveAt(i);
            }

            foreach (var other in active)
            {
                if (Collision2D.SegmentSegmentIntersection(seg, other, out var point))
                {
                    Record(seg, point);
                    Record(other, point);
                }
            }
            active.Add(seg);
        }

        Segments.Clear();
        
        foreach (var pair in selfIntersections)
        {
            if (pair.Value.Count == 0)
            {
                Segments.Add(pair.Key);
                continue;
            }

            HadSelfIntersections = true;
            pair.Key.RemoveSelfFromConnectedSegments();
            
            var startPart = new GeometrySegment(pair.Key.Parent, pair.Key.SegmentEnds[0], pair.Value.Values.First());
            Segments.Add(startPart);
            
            if (fillRule == FillRule.EvenOdd)
            {
                for (var i = 0; i < pair.Value.Values.Count - 1; i++)
                {
                    var int1 = pair.Value.Values[i];
                    var int2 = pair.Value.Values[i + 1];

                    var seg = new GeometrySegment(pair.Key.Parent, int1, int2);
                    pair.Key.Parent.Segments.Add(seg);
                }
            }

            var endPart = new GeometrySegment(pair.Key.Parent, pair.Value.Values.Last(), pair.Key.SegmentEnds[1]);
            Segments.Add(endPart);
        }
        
        UpdatePoints();
    }

    public ContainmentType IsCompletelyContains(MeshContour otherContour)
    {
        var intersects = false;
        
        for (var i = 0; i < Segments.Count; i++)
        {
            for (var j = 0; j < otherContour.Segments.Count; j++)
            {
                if (Collision2D.SegmentSegmentIntersection(Segments[i], otherContour.Segments[j], out var point))
                {
                    intersects = true;
                    break;
                }
            }

            if (intersects)
            {
                break;
            }
        }

        return intersects ? ContainmentType.Intersects : ContainmentType.Contains;
    }

    public MeshContour Copy()
    {
        var copy = new MeshContour();
        
        if (Segments != null) copy.Segments = new List<GeometrySegment>(Segments);
        
        if (Points == null) return copy;
        
        copy.Points = new Vector2<double>[Points.Length];
        Array.Copy(Points, copy.Points, Points.Length);
        
        for (var i = 0; i < copy.Points.Length; i++)
        {
            var point = copy.Points[i];
            copy.Points[i] = Vector2<double>.Round(point, CoordinatePrecision);
        }
        
        if (GeometryPoints != null) copy.GeometryPoints = new List<GeometryIntersection>(GeometryPoints);
        copy.Name = Name;
        copy.IsGeometryClosed = IsGeometryClosed;
        copy.BoundingBox = BoundingBox;

        return copy;
    }
}