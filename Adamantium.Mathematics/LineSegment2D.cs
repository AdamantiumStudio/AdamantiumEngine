using System;
using Adamantium.Mathematics.Triangulation;

namespace Adamantium.Mathematics
{
    public readonly struct LineSegment2D //: IEquatable<LineSegment2D>
    {
        public override bool Equals(object obj)
        {
            return obj is LineSegment2D other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Start, End);
        }

        public Vector2 Start { get; }

        public Vector2 End { get; }

        public Vector2 Direction { get; }

        public Vector2 DirectionNormalized { get; }

        public LineSegment2D(Vector2 start, Vector2 end)
        {
            Start = start;
            End = end;
            Direction = end - start;
            DirectionNormalized = Vector2.Normalize(Direction);
        }
        
        public LineSegment2D(Vector3 start, Vector3 end)
        {
            Start = (Vector2)start;
            End = (Vector2)end;
            Direction = End - Start;
            DirectionNormalized = Vector2.Normalize(Direction);
        }

        public LineSegment2D(GeometrySegment segment)
        {
            Start = segment.Start;
            End = segment.End;
            Direction = End - Start;
            DirectionNormalized = Vector2.Normalize(Direction);
        }
        
        public bool Equals(LineSegment2D other)
        {
            return MathHelper.WithinEpsilon(Start, other.Start, Polygon.Epsilon) && MathHelper.WithinEpsilon(End, other.End, Polygon.Epsilon);
        }

        public bool EqualsInvariant(ref LineSegment2D other)
        {
            return (Start == other.Start && End == other.End) ||
                   (Start == other.End && End == other.Start);
        }

        public static bool operator ==(LineSegment2D segment1, LineSegment2D segment2)
        {
            return segment1.Equals(segment2);
        }

        public static bool operator !=(LineSegment2D segment1, LineSegment2D segment2)
        {
            return !segment1.Equals(segment2);
        }

        public override string ToString()
        {
            return $"Start = {Start} End = {End}";
        }
    }
}
