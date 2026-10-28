using System.Collections.Generic;
using Adamantium.Mathematics;

namespace Adamantium.Fonts.Common
{
    internal class ContourSegment
    {
        // each point is a delta to previous point or to (0, 0) if it's the first point
        public List<Vector2<double>> Points { get; } // size() == 2 points - line, size() == 3 points - bezier quadratic curve

        public ContourSegment()
        {
            Points = new List<Vector2<double>>();
        }
    };
}
