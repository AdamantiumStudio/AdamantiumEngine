using System.Collections.Generic;
using Adamantium.Mathematics.Triangulation;

namespace Adamantium.Mathematics;

public class ContoursContainer
{
    private List<MeshContour> contours { get; }

    public IReadOnlyList<MeshContour> Contours => contours.AsReadOnly();

    public RectangleF BoundingBox { get; private set; }
    
    public ContoursContainer()
    {
        contours = new List<MeshContour>();
    }

    public void AddContour(MeshContour contour)
    {
        contours.Add(contour);
        CalculateBoundingBox();
    }
    
    public ContoursContainer Copy()
    {
        var copy = new ContoursContainer();

        foreach (var contour in contours)
        {
            copy.AddContour(contour.Copy());
        }

        return copy;
    }

    public void SplitContoursOnSegments()
    {
        foreach (var contour in contours)
        {
            contour.SplitOnSegments();
        }
    }
    
    public void RemoveContoursSelfIntersections(FillRule fillRule)
    {
        foreach (var contour in contours)
        {
            contour.RemoveSelfIntersections(fillRule);
        }
    }
    
    private void CalculateBoundingBox()
    {
        BoundingBox = contours[0].BoundingBox;

        for (var i = 1; i < contours.Count; i++)
        {
            BoundingBox = Rectangle<float>.Union(BoundingBox, contours[i].BoundingBox);
        }
    }

    public void ClearContours()
    {
        contours.Clear();
    }

    public void UpdateContoursPoints()
    {
        foreach (var contour in contours)
        {
            contour.UpdatePoints();
        }
    }

    public List<GeometrySegment> MergeContoursSegments()
    {
        var mergedSegments = new List<GeometrySegment>();

        foreach (var contour in contours)
        {
            mergedSegments.AddRange(contour.Segments);
        }

        return mergedSegments;
    }

    public void RemoveSegmentsByRule(bool removeInner)
    {
        foreach (var contour in contours)
        {
            contour.RemoveSegmentsByRule(removeInner);
        }
    }
}