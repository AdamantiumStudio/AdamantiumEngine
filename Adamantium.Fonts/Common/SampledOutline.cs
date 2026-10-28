using Adamantium.Mathematics;

namespace Adamantium.Fonts.Common
{
    public class SampledOutline
    {
        public Vector2<double>[] Points { get; }

        public LineSegment2D[] Segments { get; }

        public SampledOutline(Vector2<double>[] points)
        {
            Points = points;
        }

        public SampledOutline(LineSegment2D[] segments)
        {
            Segments = segments;
        }
        
        private SampledOutline()
        {
            
        }

        static SampledOutline()
        {
            Empty = new SampledOutline();
        }

        public static SampledOutline Empty;
    }
}