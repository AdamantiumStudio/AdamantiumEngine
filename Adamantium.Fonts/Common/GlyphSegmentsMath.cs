using System;
using System.Collections.Generic;
using Adamantium.Mathematics;

namespace Adamantium.Fonts.Common
{
    public static class GlyphSegmentsMath
    {
        public static bool IsSegmentsConnected(ref LineSegment2D segment1, ref LineSegment2D segment2)
        {
            return segment1.End == segment2.Start || segment2.End == segment1.Start || segment1.Start == segment2.Start || segment1.End == segment2.End ;
        }
        
        public static double GetSegmentsAngle(LineSegment2D current, LineSegment2D next)
        {
            var newSeg = new LineSegment2D(current.End, current.Start);

            return double.RadiansToDegrees(Math.Abs(MathHelper.Angle.Between(newSeg.End - newSeg.Start, next.End - next.Start)));
        }
        
        public static double GetDistanceToSegment(LineSegment2D segment, Vector2<double> point)
        {
            return point.DistanceToPoint(segment.Start, segment.End);
        }

        public static double GetSignedDistanceToSegment(LineSegment2D segment, Vector2<double> point)
        {
            var distance = GetDistanceToSegment(segment, point);

            var startToPointVector = new LineSegment2D(segment.Start, point);

            var cross = Vector2<double>.Determinant(segment.DirectionNormalized, startToPointVector.DirectionNormalized);

            if (cross < 0)
            {
                distance = -distance;
            }

            return distance;
        }

        public static double GetSignedPseudoDistanceToSegment(LineSegment2D segment, Vector2<double> point)
        {
            var distance = MathHelper.Polyline.DistanceToLine(segment.Start, segment.End, point);

            var startToPointVector = new LineSegment2D(segment.Start, point);

            var cross = Vector2<double>.Determinant(segment.DirectionNormalized, startToPointVector.DirectionNormalized);

            if (cross < 0)
            {
                distance = -distance;
            }
            
            return distance;
        }
        
        public static double GetSignedDistanceToSegmentsJoint(List<LineSegment2D> closestSegments, Vector2<double> point, bool pseudo)
        {
            Func<LineSegment2D, Vector2<double>, double> SignedDistanceFunc = null;

            if (pseudo)
            {
                SignedDistanceFunc = GetSignedPseudoDistanceToSegment;
            }
            else
            {
                SignedDistanceFunc = GetSignedDistanceToSegment;
            }

            // there must be at least one closest segment
            if (closestSegments.Count < 1)
            {
                throw new Exception($"Closest segments count < 1");
            }

            // if there is only one closest segment
            // or two closest segments are not connected
            // return distance to segment at index 0
            if (closestSegments.Count == 1)
            {
                //return GetSignedPseudoDistanceToSegment(closestSegments[0], point);
                return SignedDistanceFunc(closestSegments[0], point);
            }
            else
            {
                var seg1 = closestSegments[0];
                var seg2 = closestSegments[1];

                if (!IsSegmentsConnected(ref seg1, ref seg2))
                {
                    //return GetSignedPseudoDistanceToSegment(closestSegments[0], point);
                    return SignedDistanceFunc(closestSegments[0], point);
                }
            }

            // determine the order of the connected closest segments
            var first = closestSegments[0].End == closestSegments[1].Start ? closestSegments[0] : closestSegments[1];
            var second = closestSegments[0].End == closestSegments[1].Start ? closestSegments[1] : closestSegments[0];
            
            // revert first segment, so both segments have same start
            var revertedFirst = new LineSegment2D(first.End, first.Start);

            // find bisection (vector in the middle) of two closest segments
            var bisectionNormalized = (revertedFirst.DirectionNormalized + second.DirectionNormalized);
            bisectionNormalized.Normalize();

            var jointToPoint = new LineSegment2D(first.End, point);
            var bisectionCross = Vector2<double>.Determinant(bisectionNormalized, jointToPoint.DirectionNormalized);

            var firstToBisection = new LineSegment2D(revertedFirst.DirectionNormalized, bisectionNormalized);
            var firstCross = Vector2<double>.Determinant(first.DirectionNormalized, firstToBisection.DirectionNormalized);

            var isBisectionCrossNegative = (bisectionCross < 0);
            var isFirstCrossNegative = (firstCross < 0);
            var isCrossesHaveSameSign = !(isBisectionCrossNegative ^ isFirstCrossNegative);

            //return GetSignedPseudoDistanceToSegment(isCrossesHaveSameSign ? first : second, point);
            return SignedDistanceFunc(isCrossesHaveSameSign ? first : second, point);
        }
    }
}