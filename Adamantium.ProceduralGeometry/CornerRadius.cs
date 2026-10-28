using Adamantium.Core.TypeParsing;
using Adamantium.Mathematics;
using Adamantium.ProceduralGeometry.TypeParsers;

namespace Adamantium.ProceduralGeometry
{
    [TypeParser(typeof(CornerRadiusParser))]
    public struct CornerRadius : IEquatable<CornerRadius>
    {
        public CornerRadius(Double value)
        {
            TopLeft = TopRight = BottomRight = BottomLeft = value;
        }

        public CornerRadius(Double topLeft, Double topRight, Double bottomRight, Double bottomLeft)
        {
            TopLeft = topLeft;
            TopRight = topRight;
            BottomRight = bottomRight;
            BottomLeft = bottomLeft;
        }

        public CornerRadius(IEnumerable<double> values)
        {
            var lst = values as List<double> ?? values.ToList();

            if (lst.Count < 4) throw new ArgumentOutOfRangeException($"Arguments count for Corner radius should be 4, but provided {lst.Count}");

            TopLeft = lst[0];
            TopRight = lst[1];
            BottomRight = lst[2];
            BottomLeft = lst[3];
        }
        
        public Double TopLeft;

        public Double TopRight;

        public Double BottomRight;

        public Double BottomLeft;

        /// <summary>True when all four corners have the same radius (a single value describes the whole shape).</summary>
        public bool IsUniform => MathHelper.Scalar.NearEqual(TopLeft, TopRight)
                                 && MathHelper.Scalar.NearEqual(TopRight, BottomRight)
                                 && MathHelper.Scalar.NearEqual(BottomRight, BottomLeft);

        public bool Equals(CornerRadius other)
        {
            return TopLeft.Equals(other.TopLeft) && TopRight.Equals(other.TopRight) && BottomRight.Equals(other.BottomRight) && BottomLeft.Equals(other.BottomLeft);
        }

        public override bool Equals(object obj)
        {
            return obj is CornerRadius other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(TopLeft, TopRight, BottomRight, BottomLeft);
        }
        
        public override string ToString()
        {
            return $"{nameof(TopLeft)}: {TopLeft} {nameof(TopRight)}: {TopRight} {nameof(BottomRight)}: {BottomRight} {nameof(BottomLeft)}: {BottomLeft}";
        }

        static CornerRadius()
        {
            Empty = new CornerRadius();
        }

        public static CornerRadius Empty { get; }

        public static bool operator ==(CornerRadius radius1, CornerRadius radius2)
        {
            return MathHelper.Scalar.NearEqual(radius1.TopLeft, radius2.TopLeft) &&
                   MathHelper.Scalar.NearEqual(radius1.TopRight, radius2.TopRight) &&
                   MathHelper.Scalar.NearEqual(radius1.BottomRight, radius2.BottomRight) &&
                   MathHelper.Scalar.NearEqual(radius1.BottomLeft, radius2.BottomLeft);
        }

        public static bool operator !=(CornerRadius radius1, CornerRadius radius2)
        {
            return !(radius1 == radius2);
        }
    }
}