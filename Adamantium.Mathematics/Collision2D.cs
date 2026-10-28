using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Adamantium.Mathematics.Triangulation;

namespace Adamantium.Mathematics
{
    public static class Collision2D
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining|MethodImplOptions.AggressiveOptimization)]
        public static bool IsPointOnSegment(ref LineSegment2D segment2D, ref Vector2<double> point)
        {
            var ab = segment2D.Direction.Length();
            var ap = Vector2<double>.Subtract(point, segment2D.Start).Length();
            var bp = Vector2<double>.Subtract(point, segment2D.End).Length();
            return MathHelper.Scalar.WithinEpsilon(ab, ap + bp, Polygon.Epsilon);
        }

        public static bool SegmentSegmentIntersection(ref LineSegment2D segment1, ref LineSegment2D segment2, out Vector2<double> point)
        {
            var p = segment1.Start;
            var q = segment2.Start;
            var r = segment1.Direction;
            var s = segment2.Direction;
            // t = (q − p) × s / (r × s)
            // u = (q − p) × r / (r × s)

            var denominator = Vector2<double>.Determinant(r, s);

            if (denominator == 0.0)
            {
                point = Vector2<double>.Zero;
                return false;
            }

            var tNumerator = Vector2<double>.Determinant(q - p, s);
            var uNumerator = Vector2<double>.Determinant(q - p, r);
            var t = tNumerator / denominator;
            var u = uNumerator / denominator;

            if (t < 0 || t > 1 || u < 0 || u > 1)
            {
                point = Vector2<double>.Zero;
                return false;
            }

            point = p + r * t;
            point = Vector2<double>.Round(point, 4);
            return true;
        }
        
        public static bool SegmentSegmentIntersection(GeometrySegment segment1, GeometrySegment segment2, out Vector2<double> point)
        {
            var p = segment1.Start;
            var q = segment2.Start;
            var r = segment1.Direction;
            var s = segment2.Direction;
            // t = (q − p) × s / (r × s)
            // u = (q − p) × r / (r × s)

            var denominator = Vector2<double>.Determinant(r, s);

            if (denominator == 0.0)
            {
                point = Vector2<double>.Zero;
                return false;
            }

            var tNumerator = Vector2<double>.Determinant(q - p, s);
            var uNumerator = Vector2<double>.Determinant(q - p, r);
            var t = tNumerator / denominator;
            var u = uNumerator / denominator;

            if (t < 0 || t > 1 || u < 0 || u > 1)
            {
                point = Vector2<double>.Zero;
                return false;
            }

            point = p + r * t;
            point = Vector2<double>.Round(point, 4);
            return true;
        }

        public static bool RaySegmentIntersection(ref Ray2<double> ray, ref Vector2<double> start, ref Vector2<double> end, out Vector2<double> point)
        {
            var segment = new LineSegment2D(start, end);
            return RaySegmentIntersection(ref ray, ref segment, out point);
        }

        public static bool RaySegmentIntersection(ref Ray2<double> ray, ref LineSegment2D segment2D, out Vector2<double> point)
        {
            var collinear = ray.Direction.IsCollinear(segment2D.Direction);
            if (collinear)
            {
                point = Vector2<double>.Zero;
                return false;
            }

            var p = ray.Position;
            var r = ray.Direction;
            var q = segment2D.Start;
            var s = segment2D.Direction;
            // t = (q − p) × s / (r × s)
            // u = (q − p) × r / (r × s)

            var denominator = Vector2<double>.Determinant(r, s);

            if (denominator == 0.0f)
            {
                point = Vector2<double>.Zero;
                return false;
            }

            var tNumerator = Vector2<double>.Determinant(q - p, s);
            var uNumerator = Vector2<double>.Determinant(q - p, r);

            var t = tNumerator / denominator;
            var u = uNumerator / denominator;

            
            if (MathHelper.Scalar.NearEqual(t, 0) && t < 0)
            {
                t = 0;
            }

            if (MathHelper.Scalar.NearEqual(t, 1) && t > 1)
            {
                t = 1;
            }

            if (MathHelper.Scalar.NearEqual(u, 0) && u < 0)
            {
                u = 0;
            }

            if (MathHelper.Scalar.NearEqual(u, 1) && u > 1)
            {
                u = 1;
            }
            

            if (t < 0 || u < 0 || u > 1)
            {
                point = Vector2<double>.Zero;
                return false;
            }

            point = p + r * t;
            return true;
        }
        
        public static bool RaySegmentIntersection(ref Ray2<double> ray, GeometrySegment segment, out Vector2<double> point)
        {
            var collinear = ray.Direction.IsCollinear(segment.Direction);
            if (collinear)
            {
                point = Vector2<double>.Zero;
                return false;
            }

            var p = ray.Position;
            var r = ray.Direction;
            var q = segment.Start;
            var s = segment.Direction;
            // t = (q − p) × s / (r × s)
            // u = (q − p) × r / (r × s)

            var denominator = Vector2<double>.Determinant(r, s);

            if (denominator == 0.0f)
            {
                point = Vector2<double>.Zero;
                return false;
            }

            var tNumerator = Vector2<double>.Determinant(q - p, s);
            var uNumerator = Vector2<double>.Determinant(q - p, r);

            var t = tNumerator / denominator;
            var u = uNumerator / denominator;

            
            if (MathHelper.Scalar.NearEqual(t, 0) && t < 0)
            {
                t = 0;
            }

            if (MathHelper.Scalar.NearEqual(t, 1) && t > 1)
            {
                t = 1;
            }

            if (MathHelper.Scalar.NearEqual(u, 0) && u < 0)
            {
                u = 0;
            }

            if (MathHelper.Scalar.NearEqual(u, 1) && u > 1)
            {
                u = 1;
            }
            

            if (t < 0 || u < 0 || u > 1)
            {
                point = Vector2<double>.Zero;
                return false;
            }

            point = p + r * t;

            return true;
        }


        public static bool RaySegmentIntersection(ref Ray2<double> ray, ref LineSegment2D segment2D, out Vector2<double> point, out double distance)
        {
            var collinear = ray.Direction.IsCollinear(segment2D.Direction);
            distance = float.PositiveInfinity;
            point = Vector2<double>.Zero;
            if (collinear) return false;
            
            var p = ray.Position;
            var q = segment2D.Start;
            var r = ray.Direction;
            var s = segment2D.Direction;
            // t = (q − p) × s / (r × s)
            // u = (q − p) × r / (r × s)

            var denominator = Vector2<double>.Determinant(r, s);

            if (MathHelper.Scalar.IsZero(denominator))
            {
                point = Vector2<double>.Zero;
                return false;
            }

            var tNumerator = Vector2<double>.Determinant(q - p, s);
            var uNumerator = Vector2<double>.Determinant(q - p, r);

            var t = tNumerator / denominator;
            var u = uNumerator / denominator;

            point = p + r * t;
            //If ray and line not intersecting, try to find out closest distance between them
            //and if distance is less than Epsilon, then we can say that ray intersects line
            //because distance is near zero
            var a = Vector2<double>.Dot(segment2D.Start, segment2D.Direction);
            var b = Vector2<double>.Dot(point, segment2D.Direction);
            distance = Math.Abs(b - a);

            if (t < 0 || t > 1 || u < 0 || u > 1)
            {
                return false;
            }
            return true;
        }
        
        public static double Distance(Vector2<double> v1, Vector2<double> v2)
        {
            //return Math.Sqrt(Math.Pow(v2.X - v1.X, 2) + Math.Pow(v2.Y - v1.Y, 2));
            return (v2 - v1).Length(); //Math.Sqrt((v2.X - v1.X) * (v2.X - v1.X) + (v2.Y - v1.Y) * (v2.Y - v1.Y));
        }
        
        public static double Distance(double x1, double y1, double x2, double y2)
        {
            //return Math.Sqrt(Math.Pow(x2 - x1, 2) + Math.Pow(y2 - y1, 2));
            return Math.Sqrt((x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1));
        }

        public static double DistanceToPoint(this Vector2<double> point, Vector2<double> start, Vector2<double> end)
        {
            double dStart = Distance(point, start);
            double dEnd = Distance(point, end);
            double dStartEnd = Distance(start, end);

            if (dStart >= Distance(dEnd, dStartEnd, 0, 0))
            {
                return dEnd;
            }
            else if (dEnd >= Distance(dStart, dStartEnd, 0 ,0))
            {
                return dStart;
            }
            else
            {
                double a = end.Y - start.Y;
                double b = start.X - end.X;
                double c = -start.X * (end.Y - start.Y) + start.Y * (end.X - start.X);
                double t = Distance(a, b, 0, 0);

                if (c > 0)
                {
                    a = -a;
                    b = -b;
                    c = -c;
                }

                double ret = (a * point.X + b * point.Y + c) / t;

                return Math.Abs(ret);
            }
        }
        
        public static double DistanceToPoint(this Vector2<double> point, Vector2<double> start, Vector2<double> end, out byte pointRelativePosition)
        {
            double dStart = Distance(point, start);
            double dEnd = Distance(point, end);
            double dStartEnd = Distance(start, end);

            if (dStart >= Distance(dEnd, dStartEnd, 0, 0))
            {
                pointRelativePosition = 2;
                return Distance(point, end);
            }
            else if (dEnd >= Distance(dStart, dStartEnd, 0 ,0))
            {
                pointRelativePosition = 0;
                return Distance(point, start);
            }
            else
            {
                pointRelativePosition = 1;
                double a = end.Y - start.Y;
                double b = start.X - end.X;
                double c = -start.X * (end.Y - start.Y) + start.Y * (end.X - start.X);
                double t = Distance(a, b, 0, 0);

                if (c > 0)
                {
                    a = -a;
                    b = -b;
                    c = -c;
                }

                double ret = (a * point.X + b * point.Y + c) / t;

                return Math.Abs(ret);
            }
        }
        
        public static bool LineLineIntersection(Vector2<double> a, Vector2<double> b, Vector2<double> c, Vector2<double> d, out Vector2<double> v)
        {
            // Line 'ab' represented as a1x + b1y = c1 
            double a1 = b.Y - a.Y;
            double b1 = a.X - b.X;
            double c1 = a1 * (a.X) + b1 * (a.Y);
  
            // Line 'cd' represented as a2x + b2y = c2 
            double a2 = d.Y - c.Y;
            double b2 = c.X - d.X;
            double c2 = a2 * (c.X) + b2 * (c.Y);
  
            double determinant = a1 * b2 - a2 * b1;
  
            if (determinant == 0)
            {
                v = Vector2<double>.Zero;
                // The lines are parallel 
                return false;
            }
            
            double x = (b2 * c1 - b1 * c2) / determinant;
            double y = (a1 * c2 - a2 * c1) / determinant;
            v = new Vector2<double>(x, y);
            return true;
        }
        
        /// <summary>
        /// Checks if point is completely inside polygon, formed by segments
        /// </summary>
        /// <param name="point"></param>
        /// <param name="area"></param>
        /// <returns>True if point is completely inside polygon, otherwise false</returns>
        public static bool IsPointInsideArea(Vector2<double> point, IEnumerable<LineSegment2D> area)
        {
            var rayLeft = new Ray2<double>(point, -Vector2<double>.UnitX);
            var rayDown = new Ray2<double>(point, -Vector2<double>.UnitY);
            var rayRight = new Ray2<double>(point, Vector2<double>.UnitX);
            var rayUp = new Ray2<double>(point, Vector2<double>.UnitY);
            var sides = IntersectionSides.None;
            
            foreach (var a in area)
            {
                var segment = a;
                if (RaySegmentIntersection(ref rayLeft, ref segment, out _))
                {
                    sides |= IntersectionSides.Left;
                }

                if (RaySegmentIntersection(ref rayDown, ref segment, out _))
                {
                    sides |= IntersectionSides.Down;
                }

                if (RaySegmentIntersection(ref rayRight, ref segment, out _))
                {
                    sides |= IntersectionSides.Right;
                }

                if (RaySegmentIntersection(ref rayUp, ref segment, out _))
                {
                    sides |= IntersectionSides.Up;
                }

                if (sides.HasFlag(IntersectionSides.All))
                {
                    return true;
                }
            }
            return false;
        }
        
        public static bool IsSegmentInsideArea(GeometrySegment segment, IEnumerable<GeometrySegment> area)
        {
            var segmentCenter = (segment.End - segment.Start) * 0.5 + segment.Start;
            
            var rayList = new List<Ray2<double>>()
            {
                new (segmentCenter, -Vector2<double>.UnitX),
                new (segmentCenter, Vector2<double>.UnitX),
                new (segmentCenter, -Vector2<double>.UnitY),
                new (segmentCenter, Vector2<double>.UnitY)
            };

            var segmentsArray = area.ToArray();
            
            foreach (var ray in rayList)
            {
                var intersection = false;

                foreach (var areaSeg in segmentsArray)
                {
                    if (areaSeg == segment) continue;
                    
                    var tmpRay = ray;

                    if (RaySegmentIntersection(ref tmpRay, areaSeg, out _))
                    {
                        intersection = true;
                        break;
                    }
                }

                if (!intersection) return false;
            }

            return true;
        }
    }
}
