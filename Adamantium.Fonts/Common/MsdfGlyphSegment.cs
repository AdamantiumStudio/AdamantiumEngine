using Adamantium.Mathematics;

namespace Adamantium.Fonts.Common
{
    public struct MsdfGlyphSegment
    {
        public Color MsdfColor;
        public LineSegment2D Segment;
        
        public MsdfGlyphSegment(Vector2<double> start, Vector2<double> end)
        {
            Segment = new LineSegment2D(start, end);
            MsdfColor = Colors.White;
        }
        
        public MsdfGlyphSegment(Vector2<double> start, Vector2<double> end, Color msdfColor)
        {
            Segment = new LineSegment2D(start, end);
            MsdfColor = msdfColor;
        }
    }
}