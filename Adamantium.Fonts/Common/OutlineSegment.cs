using System.Collections.Generic;
using Adamantium.Mathematics;

namespace Adamantium.Fonts.Common
{
    public class OutlineSegment
    {
        public List<Vector2<double>> Points { get; }

        public OutlineSegment()
        {
            Points = new List<Vector2<double>>();
        }
        
        public OutlineSegment(List<Vector2<double>> points)
        {
            Points = points;
        }

        public void AddPoint(Vector2<double> point)
        {
            Points.Add(point);
        }
    }
}