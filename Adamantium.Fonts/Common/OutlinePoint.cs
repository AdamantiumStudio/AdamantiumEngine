using Adamantium.Mathematics;

namespace Adamantium.Fonts.Common
{
    public struct OutlinePoint
    {
        public double X { get; set; }
        public double Y { get; set; }
        public bool IsControl { get; set; }

        public OutlinePoint(double x, double y, bool control = false)
        {
            X = x;
            Y = y;
            IsControl = control;
        }

        public OutlinePoint(Vector2<double> point, bool control = false)
        {
            X = point.X;
            Y = point.Y;
            IsControl = control;
        }

        public override string ToString()
        {
            return $"X: {X}, Y: {Y}, IsControl: {IsControl}";
        }

        public static implicit  operator Vector2<double>(OutlinePoint point)
        {
            return new(point.X, point.Y);
        }
    }
}
