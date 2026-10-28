using System.Collections.Generic;

namespace Adamantium.Mathematics.Triangulation;

public class GeometrySegment
{
    public MeshContour Parent { get; set; }
    public List<GeometryIntersection> SegmentEnds { get; set; }
    public bool IsInner { get; set; }

    public bool IsArguable { get; set; }
    
    // used for combined geometry contour forming (only for one-point joint case) 
    public bool IsAlreadyInTriangulatorContour { get; set; }

    public Vector2<double> Start => SegmentEnds[0].Coordinates;
    public Vector2<double> End => SegmentEnds[1].Coordinates;
    
    public Vector2<double> Direction { get; }
    public Vector2<double> DirectionNormalized { get; }

    public GeometrySegment(MeshContour parent, GeometryIntersection start, GeometryIntersection end)
    {
        Parent = parent;

        // start.Coordinates = Vector2<double>.Round(start.Coordinates, 3);
        // end.Coordinates = Vector2<double>.Round(end.Coordinates, 3);
        
        Direction = end.Coordinates - start.Coordinates;
        DirectionNormalized = Vector2<double>.Normalize(Direction);
        
        SegmentEnds = new List<GeometryIntersection>
        {
            start,
            end
        };

        AddSelfToConnectedSegments();
    }
    
    public GeometrySegment(MeshContour parent, Vector2<double> point1, Vector2<double> point2)
    {
        Parent = parent;
        
        Direction = point2 - point1;
        DirectionNormalized = Vector2<double>.Normalize(Direction);
        
        var intersection1 = new GeometryIntersection(point1);
        var intersection2 = new GeometryIntersection(point2);

        SegmentEnds = new List<GeometryIntersection>
        {
            intersection1,
            intersection2
        };

        AddSelfToConnectedSegments();
    }

    public void AddSelfToConnectedSegments()
    {
        if (!SegmentEnds[0].ConnectedSegments.Contains(this)) SegmentEnds[0].ConnectedSegments.Add(this);
        if (!SegmentEnds[1].ConnectedSegments.Contains(this)) SegmentEnds[1].ConnectedSegments.Add(this);
    }
    
    public void RemoveSelfFromConnectedSegments()
    {
        if (SegmentEnds[0].ConnectedSegments.Contains(this)) SegmentEnds[0].ConnectedSegments.Remove(this);
        if (SegmentEnds[1].ConnectedSegments.Contains(this)) SegmentEnds[1].ConnectedSegments.Remove(this);
    }

    public void RemoveSelfFromParent()
    {
        if (Parent != null && Parent.Segments.Contains(this)) Parent.Segments.Remove(this);
    }
    
    public GeometrySegment GetNextSegment()
    {
        foreach (var segment in SegmentEnds[1].ConnectedSegments)
        {
            if (!Equals(segment, this) && segment.Parent == Parent) return segment;
        }

        return null;
    }
    
    public GeometrySegment GetPrevSegment()
    {
        foreach (var segment in SegmentEnds[0].ConnectedSegments)
        {
            if (!Equals(segment, this) && segment.Parent == Parent) return segment;
        }

        return null;
    }

    public GeometryIntersection GetOtherEnd(GeometryIntersection end)
    {
        if (SegmentEnds.Count < 2) return null;
        return Equals(SegmentEnds[0], end) ? SegmentEnds[1] : SegmentEnds[0];
    }

    public override int GetHashCode()
    {
        return Start.GetHashCode() ^ End.GetHashCode();
    }

    public override bool Equals(object obj)
    {
        if (obj is GeometrySegment segment)
        {
            return IsEqualTo(segment);
        }

        return false;
    }

    public bool IsEqualTo(GeometrySegment other)
    {
        return ((Start == other.Start && End == other.End) ||
                (Start == other.End && End == other.Start));
    }
    
    public override string ToString()
    {
        return $"Start: {Start} End: {End} IsArguable: {IsArguable} IsInner: {IsInner}";
    }
}