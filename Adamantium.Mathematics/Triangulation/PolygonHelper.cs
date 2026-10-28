using System.Collections.Generic;
using System.Linq;

namespace Adamantium.Mathematics.Triangulation
{
    public static class PolygonHelper
    {
        /// <summary>
        /// Inserts segment in collection of segments at specifies index
        /// </summary>
        /// <param name="segments">Segments collection</param>
        /// <param name="segment2D">Segment to insert</param>
        /// <param name="index">Index to insert at</param>
        /// <returns>True if segment collection does not already has such segment, otherwise false</returns>
        public static bool InsertSegment(List<LineSegment2D> segments, LineSegment2D segment2D, int index)
        {
            if (!segments.Contains(segment2D) && !IsSameStartEnd(ref segment2D))
            {
                segments.Insert(index, segment2D);
                return true;
            }
            return false;
        }

        public static bool IsSameStartEnd(ref LineSegment2D segment2D)
        {
            return segment2D.Start == segment2D.End;
        }
        
        public static bool IsSameStartEnd(ref GeometrySegment segment)
        {
            return segment.SegmentEnds[0] == segment.SegmentEnds[1];
        }

        /// <summary>
        /// Creates a collection of segments from a collection of points
        /// </summary>
        /// <param name="inPoints"></param>
        /// <param name="isContourClosed"></param>
        /// <returns>Collection of segments if at least 2 points present in collection, otherwise null</returns>
        public static List<GeometrySegment> SplitOnSegments(MeshContour parent, IEnumerable<Vector2<double>> inPoints, bool isContourClosed = true)
        {
            var points = inPoints as Vector2<double>[] ?? inPoints.ToArray();

            if (points.Length <= 1) return new List<GeometrySegment>();

            var intersectionsList = new Dictionary<Vector2<double>, GeometryIntersection>();
            var segmentHashSet = new HashSet<GeometrySegment>();
            var segments = new List<GeometrySegment>();

            for (var i = 0; i < points.Length - 1; i++)
            {
                var start = points[i];
                var end = points[i + 1];
                
                if (!intersectionsList.ContainsKey(start))
                {
                    intersectionsList[start] = new GeometryIntersection(start);
                }
                
                if (!intersectionsList.ContainsKey(end))
                {
                    intersectionsList[end] = new GeometryIntersection(end);
                }
                
                var segment = new GeometrySegment(parent, intersectionsList[start], intersectionsList[end]);

                if (!IsSameStartEnd(ref segment))
                {
                    if (!segmentHashSet.Contains(segment))
                    {
                        segmentHashSet.Add(segment);
                        segments.Add(segment);
                    }
                }
            }

            if (!isContourClosed) return segments;

            var lastSegment = new GeometrySegment(parent, intersectionsList[points[points.Length - 1]], intersectionsList[points[0]]);
            if (!IsSameStartEnd(ref lastSegment))
            {
                if (!segmentHashSet.Contains(lastSegment))
                {
                    segmentHashSet.Add(lastSegment);
                    segments.Add(lastSegment);
                }
            }

            return segments;
        }

        /// <summary>
        /// Creates a collection of segments from a collection of points
        /// </summary>
        /// <param name="inPoints"></param>
        /// <param name="isContourClosed"></param>
        /// <returns>Collection of segments if at least 2 points present in collection, otherwise null</returns>
        public static List<GeometrySegment> SplitOnSegments(MeshContour parent, IEnumerable<Vector3<double>> inPoints, bool isContourClosed = true)
        {
            var points = new List<Vector2<double>>();

            foreach (var inPoint in inPoints)
            {
                points.Add(new Vector2<double>(inPoint.X, inPoint.Y));
            }
            
            return SplitOnSegments(parent, points, isContourClosed);
        }

        /// <summary>
        /// Checks if two points forms single segment in collection of segments
        /// </summary>
        /// <param name="start">Start segment point</param>
        /// <param name="end">End segment point</param>
        /// <param name="segments">Collection of segments</param>
        /// <returns>True is points connected, otherwise false</returns>
        public static bool IsConnected(Vector2<double> start, Vector2<double> end, List<LineSegment2D> segments)
        {
            return IsConnectedInvariant(start, end, segments, out var segment2D);
        }
        
        public static bool IsConnected(GeometryIntersection start, GeometryIntersection end)
        {
            foreach (var segment in start.ConnectedSegments)
            {
                if (segment.GetOtherEnd(start) == end) return true;
            }

            return false;
        }

        /// <summary>
        /// Checks if two points forms single segment in collection of segments and output correctly linked segment containing two points
        /// </summary>
        /// <param name="start">Start segment point</param>
        /// <param name="end">End segment point</param>
        /// <param name="segments">Collection of segments</param>
        /// <param name="correctSegment2D">Correct segment from start and end points</param>
        /// <returns>True is points connected, otherwise false</returns>
        /// <remarks>
        /// Segment could be connected not the same way as parameters. For ex. it could starts from end point and end in start point. 
        /// This method will also check for such cases and will check for segments very close to each other (less then <see cref="Polygon.Epsilon"/>) 
        /// to 100% find correct segment
        /// </remarks>
        public static bool IsConnectedInvariant(Vector2<double> start, Vector2<double> end, List<LineSegment2D> segments, out LineSegment2D correctSegment2D)
        {
            correctSegment2D = new LineSegment2D(start, end);
            for (var i = 0; i < segments.Count; ++i)
            {
                var currentSegment = segments[i];
                if (currentSegment.EqualsInvariant(ref correctSegment2D))
                {
                    correctSegment2D = currentSegment;
                    return true;
                }
            }
            return false;
        }
        
        public static bool IsConnected(Vector2<double> start, Vector2<double> end, List<LineSegment2D> segments, out LineSegment2D correctSegment2D)
        {
            correctSegment2D = new LineSegment2D(start, end);
            for (var i = 0; i < segments.Count; ++i)
            {
                var currentSegment = segments[i];
                if (currentSegment == correctSegment2D) continue;
                
                if (currentSegment.Start == end)
                {
                    correctSegment2D = currentSegment;
                    segments.Remove(currentSegment);
                    return true;
                }
                else if (currentSegment.End == end)
                {
                    correctSegment2D = new LineSegment2D(currentSegment.End, currentSegment.Start);
                    segments.Remove(currentSegment);
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Find all segments that lies on certain point in segments collection
        /// </summary>
        /// <param name="segments">Collection of segments</param>
        /// <param name="point">point in 2D</param>
        /// <param name="foundSegments">Collection of found segments</param>
        /// <returns>True if at least one segment was found, otherwise false</returns>
        public static bool GetSegmentsFromPoint(IEnumerable<LineSegment2D> segments, Vector2<double> point, out List<LineSegment2D> foundSegments)
        {
            foundSegments = new List<LineSegment2D>();
            foreach (var segment in segments)
            {
                var s = segment;
                if (Collision2D.IsPointOnSegment(ref s, ref point))
                {
                    foundSegments.Add(s);
                }
            }
            return foundSegments.Count > 0;
        }

        /// <summary>
        /// Check does collection of points forms a concave <see cref="PolygonItem"/>
        /// </summary>
        /// <param name="points">Collection of points to check</param>
        /// <returns>True if polygon is concave, otherwise false</returns>
        public static bool IsPolygonConcave(List<Vector3<double>> points)
        {
            for (var i = 0; i < points.Count - 2; i++)
            {
                var p0 = points[i];
                var p1 = points[i + 1];
                var p2 = points[i + 2];

                var vec0 = p0 - p1;
                var vec1 = p2 - p1;
                vec0.Normalize();
                vec1.Normalize();

                var angle = double.RadiansToDegrees(MathHelper.Angle.Turn(new Vector2<double>(vec0.X, vec0.Y), new Vector2<double>(vec1.X, vec1.Y)));
                if (angle > 180)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Check does collection of segments forms a concave <see cref="PolygonItem"/>
        /// </summary>
        /// <param name="segments">Collection of segments to check</param>
        /// <returns>True if polygon is concave, otherwise false</returns>
        public static bool IsPolygonConcave(List<LineSegment2D> segments)
        {
            for (var i = 0; i < segments.Count - 1; i++)
            {
                var vec0 = segments[i].DirectionNormalized;
                var vec1 = segments[i + 1].DirectionNormalized;

                var angle = double.RadiansToDegrees(MathHelper.Angle.Turn(vec0, vec1));
                if (angle > 180)
                {
                    return true;
                }
            }

            return false;
        }
        
        public static bool IsPolygonConcave(List<GeometrySegment> segments)
        {
            for (var i = 0; i < segments.Count - 1; i++)
            {
                var vec0 = segments[i].DirectionNormalized;
                var vec1 = segments[i + 1].DirectionNormalized;

                var angle = double.RadiansToDegrees(MathHelper.Angle.Turn(vec0, vec1));
                if (angle > 180)
                {
                    return true;
                }
            }

            return false;
        }
    }
}