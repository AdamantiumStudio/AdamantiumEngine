using System;
using System.Collections.Generic;
using System.Linq;

namespace Adamantium.Mathematics.Triangulation
{
    /// <summary>
    /// Describes a collection of <see cref="MeshContour"/>s, which will form a single merged triangulated polygon
    /// </summary>
    public class Polygon
    {
        private readonly HashSet<Vector2<double>> mergedPointsHash;
        private readonly List<ContoursContainer> contourContainers;

        /// <summary>
        /// Precision level for polygon triangulation
        /// </summary>
        /// <remarks>
        /// Seems Epsilon must be less than <see cref="MathHelper.Scalar.ZeroTolerance{T}"/> on 1 magnitude to avoid floating point errors, but not less than 1e-8.
        /// Current ZeroTolerance value = 1e-9
        /// </remarks>
        public static double Epsilon = MathHelper.Scalar.ZeroTolerance<double>();

        /// <summary>
        /// Polygon name
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Collection of points from all <see cref="MeshContour"/>s in current <see cref="Polygon"/>
        /// </summary>
        public List<GeometryIntersection> MergedPoints { get; private set; }

        /// <summary>
        /// Collection of segments from all <see cref="MeshContour"/>s in current <see cref="Polygon"/>
        /// </summary>
        public List<GeometrySegment> MergedSegments { get; private set; }

        /// <summary>
        /// Leftmost X coordinate for polygon
        /// </summary>
        public double LeftmostXCoord;

        /// <summary>
        /// Triangulation rule (true - Even-Odd, false - Non-Zero)
        /// </summary>
        public FillRule FillRule { get; set; }

        /// <summary>
        /// Constructs <see cref="Polygon"/>
        /// </summary>
        public Polygon(FillRule fillRule = FillRule.EvenOdd)
        {
            contourContainers = new List<ContoursContainer>();
            MergedPoints = new List<GeometryIntersection>();
            mergedPointsHash = new HashSet<Vector2<double>>();
            MergedSegments = new List<GeometrySegment>();
            FillRule = fillRule;
        }

        public void AddContour(MeshContour contour, bool newContainer = true)
        {
            if (newContainer) contourContainers.Add(new ContoursContainer());
            contourContainers.Last().AddContour(contour);
        }

        public void AddContours(params MeshContour[] contours)
        {
            foreach (var contour in contours)
            {
                contourContainers.Add(new ContoursContainer());
                contourContainers.Last().AddContour(contour);
            }
        }

        /// <summary>
        /// The triangles filling the contours by <see cref="FillRule"/> (<see cref="RingFill.Fill"/>), each contour taken
        /// as closed; a contour of fewer than three points, or with a coordinate not a finite number, fills nothing.
        /// </summary>
        public List<Vector3<double>> FillIndirect(bool triangulate = true)
        {
            if (!triangulate)
            {
                return new List<Vector3<double>>();
            }

            var rings = new List<Vector2<double>[]>();
            foreach (var container in contourContainers)
            {
                foreach (var contour in container.Contours)
                {
                    if (contour.Points is { Length: >= 3 }) rings.Add(contour.Points);
                }
            }

            return RingFill.Fill(rings, FillRule);
        }

        public List<Vector3<double>> FillDirect(List<GeometryIntersection> points, List<GeometrySegment> segments)
        {
            foreach (var point in points)
            {
                point.Coordinates = Vector2<double>.Round(point.Coordinates, 3);
            }

            for (int i = 0; i < segments.Count; i++)
            {
                var segment = segments[i];
                segment.SegmentEnds[0].Coordinates = Vector2<double>.Round(segment.Start, 3);
                segment.SegmentEnds[1].Coordinates = Vector2<double>.Round(segment.End, 3);
            }

            MergedPoints.AddRange(points);
            MergedSegments.AddRange(segments);
            UpdateLeftmostXCoord();

            var result = Triangulator.Triangulate(this);
            return result;
        }

        /// <summary>
        /// Sort points by its X component from smallest to largest
        /// </summary>
        /// <returns>New collection of </returns>
        public List<GeometryIntersection> SortPoints()
        {
            var sortedList = new List<GeometryIntersection>(MergedPoints);
            sortedList.Sort(VerticalGeometryPointsComparer.Default);
            return sortedList;
        }

        private void UpdateLeftmostXCoord()
        {
            if (MergedPoints.Count <= 0) return;

            var minimum = new Vector2<double>(double.MaxValue);
            var maximum = new Vector2<double>(double.MinValue);

            for (var i = 0; i < MergedPoints.Count; ++i)
            {
                var point = MergedPoints[i].Coordinates;
                minimum = Vector2<double>.Min(minimum, point);
                maximum = Vector2<double>.Max(maximum, point);
            }

            LeftmostXCoord = minimum.X;
        }

        /// <summary>
        /// Update <see cref="MergedSegments"/> collection based on collection of intersection points.
        /// As a result, this method will create new additional segments nad update <see cref="MergedPoints"/> collection
        /// </summary>
        /// <param name="additionalRayIntersections">Collection of intersection points</param>
        internal void UpdatePolygonUsingAdditionalRayInterPoints(Dictionary<GeometrySegment, SortedList<double, GeometryIntersection>> additionalRayIntersections)
        {
            if (additionalRayIntersections.Count == 0) return;

            foreach (var pair in additionalRayIntersections)
            {
                if (pair.Value.Count == 0)
                {
                    continue;
                }

                pair.Key.RemoveSelfFromConnectedSegments();
                if (MergedSegments.Contains(pair.Key)) MergedSegments.Remove(pair.Key);

                var startPart = new GeometrySegment(pair.Key.Parent, pair.Key.SegmentEnds[0], pair.Value.Values.First());
                MergedSegments.Add(startPart);

                for (var i = 0; i < pair.Value.Values.Count - 1; i++)
                {
                    var int1 = pair.Value.Values[i];
                    var int2 = pair.Value.Values[i + 1];

                    var seg = new GeometrySegment(pair.Key.Parent, int1, int2);
                    MergedSegments.Add(seg);
                }

                var endPart = new GeometrySegment(pair.Key.Parent, pair.Value.Values.Last(), pair.Key.SegmentEnds[1]);
                MergedSegments.Add(endPart);
            }

            UpdateMergedPoints();
        }

        /// <summary>
        /// Updates collection of <see cref="MergedPoints"/> based on <see cref="MergedSegments"/> to have correct order of point for <see cref="Polygon"/>
        /// </summary>
        private void UpdateMergedPoints()
        {
            MergedPoints.Clear();
            mergedPointsHash.Clear();
            for (var i = 0; i < MergedSegments.Count; ++i)
            {
                var segment = MergedSegments[i];
                if (!mergedPointsHash.Contains(segment.Start))
                {
                    MergedPoints.Add(segment.SegmentEnds[0]);
                    mergedPointsHash.Add(segment.Start);
                }

                if (!mergedPointsHash.Contains(segment.End))
                {
                    MergedPoints.Add(segment.SegmentEnds[1]);
                    mergedPointsHash.Add(segment.End);
                }
            }
        }

        private class VerticalGeometryPointsComparer : IComparer<GeometryIntersection>
        {
            public static VerticalGeometryPointsComparer Default => new ();

            public int Compare(GeometryIntersection x, GeometryIntersection y)
            {
                if (MathHelper.Scalar.WithinEpsilon(x.Coordinates.Y, y.Coordinates.Y, Epsilon))
                {
                    return 0;
                }

                return x.Coordinates.Y < y.Coordinates.Y ? -1 : 1;
            }
        }

        public override string ToString()
        {
            var contoursCnt = contourContainers.Sum(container => container.Contours.Count);

            return $"{Name}, Contour containers: {contourContainers.Count}, Mesh contours: {contoursCnt}";
        }
    }
}
