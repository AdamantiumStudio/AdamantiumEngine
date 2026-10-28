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

        public Vector2<double> Start { get; }

        public Vector2<double> End { get; }

        public Vector2<double> Direction { get; }

        public Vector2<double> DirectionNormalized { get; }

        public LineSegment2D(Vector2<double> start, Vector2<double> end)
        {
            Start = start;
            End = end;
            Direction = end - start;
            DirectionNormalized = Vector2<double>.Normalize(Direction);
        }
        
        public LineSegment2D(Vector3<double> start, Vector3<double> end)
        {
            Start = (Vector2<double>)start;
            End = (Vector2<double>)end;
            Direction = End - Start;
            DirectionNormalized = Vector2<double>.Normalize(Direction);
        }

        public LineSegment2D(GeometrySegment segment)
        {
            Start = segment.Start;
            End = segment.End;
            Direction = End - Start;
            DirectionNormalized = Vector2<double>.Normalize(Direction);
        }
        
        public bool Equals(LineSegment2D other)
        {
            var epsilon = new Vector2<double>(Polygon.Epsilon);
            return Vector2<double>.NearEqual(Start, other.Start, epsilon) && Vector2<double>.NearEqual(End, other.End, epsilon);
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
