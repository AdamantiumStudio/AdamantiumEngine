using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Adamantium.Mathematics.Triangulation
{
    /// <summary>
    /// Describes a collection of <see cref="MeshContour"/>s, which will form a single merged triangulated polygon
    /// </summary>
    public class Polygon
    {
        private readonly HashSet<Vector2<double>> mergedPointsHash;
        private readonly List<ContoursContainer> contourContainers;

        public List<MeshContour> ProcessedContours { get; private set; }

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

        private readonly List<ContourPair> checkedContours;
        private readonly object vertexLocker = new object();

        /// <summary>
        /// Constructs <see cref="Polygon"/>
        /// </summary>
        public Polygon(FillRule fillRule = FillRule.EvenOdd)
        {
            contourContainers = new List<ContoursContainer>();
            MergedPoints = new List<GeometryIntersection>();
            mergedPointsHash = new HashSet<Vector2<double>>();
            MergedSegments = new List<GeometrySegment>();
            checkedContours = new List<ContourPair>();
            ProcessedContours = new List<MeshContour>();
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
        /// Triangulate current <see cref="Polygon"/>
        /// </summary>
        /// <returns></returns>
        public List<Vector3<double>> FillIndirect(bool triangulate = true)
        {
            // Fast-path: when every contour is a simple, closed ring and no two edges cross (pure nesting), resolve
            // holes by containment + fill rule and earcut. O(n log n) detection + ~O(n) triangulation, bypassing
            // both the O(n^2) self-intersection scan and the O(n^2) MergeContours. Crossings / self-intersections
            // fall through to the full pipeline below.
            if (TryFastTriangulate(triangulate, out var fast)) return fast;

            var localContourContainers = new List<ContoursContainer>();

            foreach (var container in contourContainers)
            {
                localContourContainers.Add(container.Copy());
            }
            
            Parallel.For(0, localContourContainers.Count, i =>
            {
                localContourContainers[i].SplitContoursOnSegments();
                localContourContainers[i].RemoveContoursSelfIntersections(FillRule);
            });
            
            // process non-intersecting contours - only when the whole container not intersects with other containers and contains only single contour
            // such container is removed from list
            var nonIntersectedContours = GetNonIntersectingContours(localContourContainers);
            
            var vertices = new List<Vector3<double>>();
            
            if (triangulate)
            {
                if (nonIntersectedContours.Count > 0)
                {
                    ProcessedContours.AddRange(nonIntersectedContours);
                    
                    Parallel.ForEach(nonIntersectedContours, item =>
                    {
                        var result1 = TriangulateContour(item);
                        lock (vertexLocker)
                        {
                            vertices.AddRange(result1);
                        }
                    });
                }
            }
            
            // process intersecting contours
            if (localContourContainers.Count > 0)
            {
                MergeContours(localContourContainers);
                
                foreach (var container in localContourContainers)
                {
                    ProcessedContours.AddRange(container.Contours);
                }

                UpdateLeftmostXCoord();
            
                if (triangulate)
                {
                    // Reached only for genuinely intersecting/overlapping contours (the clean nesting case was
                    // handled by TryFastTriangulate above). The scanline resolves crossings per fill rule.
                    var result = Triangulator.Triangulate(this);

                    lock (vertexLocker)
                    {
                        vertices.AddRange(result);
                    }
                }
            }

            return vertices;
        }

        /// <summary>
        /// Fast path for clean geometry: every contour is a simple, closed ring and no two edges cross (pure
        /// nesting). Resolves holes by containment + <see cref="FillRule"/> and triangulates with earcut, bypassing
        /// the O(n^2) self-intersection scan and the O(n^2) MergeContours. Returns false (caller uses the full
        /// pipeline) for anything with crossings or self-intersections.
        /// </summary>
        private bool TryFastTriangulate(bool triangulate, out List<Vector3<double>> result)
        {
            result = null;

            var rings = new List<Vector2<double>[]>();
            var processedContours = new List<MeshContour>();
            foreach (var container in contourContainers)
                foreach (var contour in container.Contours)
                {
                    // A fill treats the contour as closed without a Z; the contour stays open so the stroke gets no phantom
                    // edge.
                    if (contour.Points == null || contour.Points.Length < 3) return false;
                    var copy = contour.Copy();
                    copy.SplitOnSegments();
                    if (copy.Points.Length < 3) return false;
                    processedContours.Add(copy);
                    rings.Add(copy.Points);
                }
            if (rings.Count == 0) return false;

            // A single convex contour is provably simple with no holes -> fan directly, skipping the broad-phase
            // intersection scan entirely (convexity already guarantees no self-intersection).
            if (rings.Count == 1 && MathHelper.Polyline.IsConvex<double>(rings[0]))
            {
                ProcessedContours.AddRange(processedContours);
                result = triangulate ? Triangulator.FanTriangulate(rings[0]) : new List<Vector3<double>>();
                return true;
            }

            // Crossing contours go to PlanarFill, which asks the fill rule once per region they cut the plane into.
            if (HasProperIntersections(rings))
            {
                ProcessedContours.AddRange(processedContours);
                result = triangulate ? PlanarFill.Fill(rings, FillRule) : new List<Vector3<double>>();

                return true;
            }

            ProcessedContours.AddRange(processedContours);
            result = triangulate ? FillRings(rings, FillRule) : new List<Vector3<double>>();
            return true;
        }

        /// <summary>Triangulates rings that do not cross, by the fill rule: which ring is solid and which is a hole is
        /// decided here, and earcut is handed the answer.</summary>
        private List<Vector3<double>> FillRings(List<Vector2<double>[]> rings, FillRule rule)
        {
            var result = new List<Vector3<double>>();

            // Nesting depth of each ring (number of other rings that contain it).
            var depth = new int[rings.Count];
            for (var i = 0; i < rings.Count; i++)
                for (var j = 0; j < rings.Count; j++)
                    if (i != j && PointInPolygon(rings[i][0], rings[j])) depth[i]++;

            // NonZero is the winding rule: a region fills when the rings enclosing it do not cancel out. EvenOdd: even-depth
            // rings fill, odd-depth rings directly inside them are holes.
            var nonZero = rule == FillRule.NonZero;
            var winding = new int[rings.Count];

            if (nonZero)
            {
                var way = new int[rings.Count];

                for (var i = 0; i < rings.Count; i++) way[i] = Area(rings[i]) >= 0 ? 1 : -1;

                for (var i = 0; i < rings.Count; i++)
                {
                    winding[i] = way[i];

                    for (var j = 0; j < rings.Count; j++)
                    {
                        if (i != j && PointInPolygon(rings[i][0], rings[j])) winding[i] += way[j];
                    }
                }
            }

            for (var i = 0; i < rings.Count; i++)
            {
                var isFill = nonZero ? winding[i] != 0 : (depth[i] & 1) == 0;
                if (!isFill) continue;

                // ...and a filled ring INSIDE another filled one adds nothing: that ground is already covered, and
                // triangulating it again is two more triangles drawn over themselves.
                if (nonZero && depth[i] > 0 && Within(rings, winding, i)) continue;

                List<IReadOnlyList<Vector2<double>>> holes = null;

                for (var j = 0; j < rings.Count; j++)
                {
                    if (i == j || depth[j] != depth[i] + 1) continue;
                    if (nonZero && winding[j] != 0) continue;   // it fills too, so it is not a hole in this one
                    if (PointInPolygon(rings[j][0], rings[i])) (holes ??= new List<IReadOnlyList<Vector2<double>>>()).Add(rings[j]);
                }

                if (holes == null)
                    result.AddRange(MathHelper.Polyline.IsConvex<double>(rings[i]) ? Triangulator.FanTriangulate(rings[i]) : Triangulator.EarcutTriangulate(rings[i]));
                else
                    result.AddRange(Triangulator.EarcutWithHoles(rings[i], holes));

            }

            return result;
        }

        /// <summary>True if any two non-adjacent edges of the given rings cross or touch (T-junction). X-sweep
        /// broad-phase, so ~O(n log n) for clean input instead of O(n^2).</summary>
        private static bool HasProperIntersections(List<Vector2<double>[]> rings)
        {
            var edges = new List<(Vector2<double> a, Vector2<double> b)>();
            foreach (var ring in rings)
            {
                var n = ring.Length;
                for (var i = 0; i < n; i++) edges.Add((ring[i], ring[(i + 1) % n]));
            }
            edges.Sort((e, f) => Math.Min(e.a.X, e.b.X).CompareTo(Math.Min(f.a.X, f.b.X)));

            var active = new List<(Vector2<double> a, Vector2<double> b)>();
            foreach (var e in edges)
            {
                var minX = Math.Min(e.a.X, e.b.X);
                for (var i = active.Count - 1; i >= 0; i--)
                    if (Math.Max(active[i].a.X, active[i].b.X) < minX) active.RemoveAt(i);

                foreach (var o in active)
                {
                    if (e.a == o.a || e.a == o.b || e.b == o.a || e.b == o.b) continue; // adjacent (shared vertex) - fine
                    if (SegmentsIntersect(e.a, e.b, o.a, o.b)) return true;
                }
                active.Add(e);
            }
            return false;
        }

        /// <summary>Whether two segments cross properly, each through the other's inside; touching is not crossing, so rings
        /// sharing a vertex still nest.</summary>
        private static bool SegmentsIntersect(Vector2<double> p1, Vector2<double> p2, Vector2<double> p3, Vector2<double> p4)
        {
            int o1 = Orient(p1, p2, p3), o2 = Orient(p1, p2, p4), o3 = Orient(p3, p4, p1), o4 = Orient(p3, p4, p2);

            return o1 != o2 && o3 != o4 && o1 != 0 && o2 != 0 && o3 != 0 && o4 != 0;
        }

        private static int Orient(Vector2<double> a, Vector2<double> b, Vector2<double> c)
        {
            var v = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
            return v > 1e-9 ? 1 : (v < -1e-9 ? -1 : 0);
        }

        private static bool OnSegment(Vector2<double> a, Vector2<double> b, Vector2<double> p)
        {
            return Math.Min(a.X, b.X) - 1e-9 <= p.X && p.X <= Math.Max(a.X, b.X) + 1e-9 &&
                   Math.Min(a.Y, b.Y) - 1e-9 <= p.Y && p.Y <= Math.Max(a.Y, b.Y) + 1e-9;
        }



        private static bool Within(List<Vector2<double>[]> rings, int[] winding, int i)
        {
            for (var k = 0; k < rings.Count; k++)
            {
                if (k != i && winding[k] != 0 && PointInPolygon(rings[i][0], rings[k])) return true;
            }

            return false;
        }

        /// <summary>Twice the signed area of a ring - positive one way round, negative the other. Which way a ring
        /// runs is the whole of the non-zero rule.</summary>
        private static double Area(IReadOnlyList<Vector2<double>> ring)
        {
            double sum = 0;

            for (var i = 0; i < ring.Count; i++)
            {
                var a = ring[i];
                var b = ring[(i + 1) % ring.Count];

                sum += a.X * b.Y - b.X * a.Y;
            }

            return sum;
        }

        private static bool PointInPolygon(Vector2<double> p, IReadOnlyList<Vector2<double>> polygon)
        {
            bool inside = false;
            for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
            {
                var a = polygon[i];
                var b = polygon[j];
                if (((a.Y > p.Y) != (b.Y > p.Y)) &&
                    (p.X < (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X))
                    inside = !inside;
            }
            return inside;
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

        private List<Vector3<double>> TriangulateContour(MeshContour item)
        {
            // A simple (non-self-intersecting) contour triangulates with earcut in ~O(n), ~n-2 triangles (no
            // slivers). Self-intersecting contours fall back to the scanline, which resolves the crossings per
            // fill rule (earcut would mis-handle a non-simple ring).
            if (!item.HadSelfIntersections)
            {
                return Triangulator.EarcutTriangulate(item.Points);
            }

            var polygon = new Polygon
            {
                Name = Name,
                MergedPoints = item.GeometryPoints,
                MergedSegments = item.Segments
            };

            polygon.UpdateLeftmostXCoord();

            return Triangulator.Triangulate(polygon);
        }

        private List<MeshContour> GetNonIntersectingContours(List<ContoursContainer> localContourContainers)
        {
            var contourList = new List<MeshContour>();

            if (localContourContainers.Count == 0) return contourList;

            if (localContourContainers.Count == 1 && localContourContainers[0].Contours.Count == 1)
            {
                contourList.Add(localContourContainers[0].Contours[0]);
                localContourContainers.Clear();
                return contourList;
            }

            foreach (var container1 in localContourContainers)
            {
                var canAdd = true;

                foreach (var container2 in localContourContainers)
                {
                    if (container1 == container2) continue;

                    var containment = container1.BoundingBox.Intersects(container2.BoundingBox);

                    if (containment || container1.Contours.Count > 1)
                    {
                        canAdd = false;
                        break;
                    }
                }

                if (canAdd)
                {
                    contourList.Add(container1.Contours[0]);
                    container1.ClearContours();
                }
            }

            for (var i = 0; i < localContourContainers.Count; i++)
            {
                if (localContourContainers[i].Contours.Count == 0) localContourContainers.RemoveAt(i);
            }

            return contourList;
        }

        /// <summary>
        /// Merge points from all <see cref="MeshContour"/>s
        /// </summary>
        private void MergePoints()
        {
            MergedPoints.Clear();
            
            var pointsHashSet = new HashSet<GeometryIntersection>();

            foreach (var segment in MergedSegments)
            {
                foreach (var end in segment.SegmentEnds)
                {
                    if (!pointsHashSet.Contains(end))
                    {
                        pointsHashSet.Add(end);
                        MergedPoints.Add(end);
                    }
                }
            }
        }

        /// <summary>
        /// Merge segments from all <see cref="MeshContour"/>s
        /// </summary>
        private void MergeSegments(List<ContoursContainer> localContourContainers)
        {
            MergedSegments.Clear();

            foreach (var container in localContourContainers)
            {
                foreach (var contour in container.Contours)
                {
                    MergedSegments.AddRange(contour.Segments);
                }
            }
        }

        private void MergeContours(List<ContoursContainer> localContourContainers)
        {
            if (localContourContainers.Count <= 1)
            {
                MergeSegments(localContourContainers);
                MergePoints();

                return;
            }
            
            var processedSegments = new List<GeometrySegment>();

            // each iteration we are processing next contour with the merged segments of all previous contours 
            for (var i = 1; i < localContourContainers.Count; i++)
            {
                processedSegments.Clear();

                for (var j = 0; j < i; j++)
                {
                    processedSegments.AddRange(localContourContainers[j].MergeContoursSegments());
                }
                
                var nextSegments = new List<GeometrySegment>();
                nextSegments.AddRange(localContourContainers[i].MergeContoursSegments());

                ContourProcessingHelper.ProcessContoursIntersections(processedSegments, nextSegments);
            }

            // update for correct marking
            foreach (var container in localContourContainers)
            {
                container.UpdateContoursPoints();
            }

            if (FillRule == FillRule.NonZero)
            {
                var arguableSegments = new List<GeometrySegment>();

                foreach (var checkedContainer in localContourContainers)
                {
                    foreach (var container in localContourContainers)
                    {
                        if (container == checkedContainer) continue;

                        arguableSegments.AddRange(ContourProcessingHelper.MarkSegments(container.MergeContoursSegments(), checkedContainer.MergeContoursSegments()));
                    }
                }

                if (arguableSegments.Count > 0)
                {
                    // resolve arguable segments as inner or outer
                    var mergedSegments = new List<GeometrySegment>();

                    foreach (var container in localContourContainers)
                    {
                        mergedSegments.AddRange(container.MergeContoursSegments());
                    }

                    ContourProcessingHelper.ResolveArguableSegments(arguableSegments, mergedSegments);
                }

                // remove only inner segments, arguable segments will be skipped
                foreach (var container in localContourContainers)
                {
                    container.RemoveSegmentsByRule(true);
                }

                if (arguableSegments.Count > 0)
                {
                    // additionally remove resolved segments if they are inner
                    foreach (var arguableSeg in arguableSegments)
                    {
                        if (arguableSeg.IsInner)
                        {
                            arguableSeg.RemoveSelfFromConnectedSegments();
                            arguableSeg.RemoveSelfFromParent();
                        }
                    }

                    foreach (var container in localContourContainers)
                    {
                        container.UpdateContoursPoints();
                    }
                }
            }

            MergeSegments(localContourContainers);
            MergePoints();
        }

        /// <summary>
        /// Adds polygons to checklist to not process this polygon pair again
        /// </summary>
        /// <param name="polygon1">First polygon</param>
        /// <param name="polygon2">Second polygon</param>
        /// <param name="intersectionType">Intersection type between two polygons</param>
        private void AddContourPairToList(MeshContour polygon1, MeshContour polygon2,
            ContainmentType intersectionType)
        {
            checkedContours.Add(new ContourPair(polygon1, polygon2, intersectionType));
        }

        /// <summary>
        /// Check does 2 polygons present in check list
        /// </summary>
        /// <param name="contour1">First polygon</param>
        /// <param name="polygon2">Second polygon</param>
        /// <returns>True if polygons already contains in check list, otherwise false</returns>
        private bool ContainsContourPair(MeshContour contour1, MeshContour contour2)
        {
            return checkedContours.Contains(new ContourPair(contour1, contour2));
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

        private class ContourPair : IEquatable<ContourPair>
        {
            public MeshContour Contour1 { get; }

            public MeshContour Contour2 { get; }

            public ContainmentType IntersectionType { get; }

            public ContourPair(MeshContour contour1, MeshContour contour2, ContainmentType intersectionType)
            {
                Contour1 = contour1;
                Contour2 = contour2;
                IntersectionType = intersectionType;
            }

            public ContourPair(MeshContour contour1, MeshContour contour2)
            {
                Contour1 = contour1;
                Contour2 = contour2;
            }

            public bool Equals(ContourPair other)
            {
                if (ReferenceEquals(null, other))
                    return false;
                if (ReferenceEquals(this, other))
                    return true;
                return (Equals(Contour1, other.Contour1) && Equals(Contour2, other.Contour2)) ||
                       (Equals(Contour1, other.Contour2) && Equals(Contour2, other.Contour1));
            }

            public override bool Equals(object obj)
            {
                if (ReferenceEquals(null, obj))
                    return false;
                if (ReferenceEquals(this, obj))
                    return true;
                if (obj.GetType() != this.GetType())
                    return false;
                return Equals((ContourPair) obj);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    var hashCode = (Contour1 != null ? Contour1.GetHashCode() : 0);
                    hashCode = (hashCode * 397) ^ (Contour2 != null ? Contour2.GetHashCode() : 0);
                    hashCode = (hashCode * 397) ^ (int) IntersectionType;
                    return hashCode;
                }
            }

            public override string ToString()
            {
                return $"Polygon1 {Contour1}, Polygon2 {Contour2}, IntersectionType {IntersectionType}";
            }
        }
    }
}