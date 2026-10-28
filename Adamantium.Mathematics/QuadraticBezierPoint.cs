namespace Adamantium.Mathematics;

public readonly struct QuadraticBezierPoint
{
    public QuadraticBezierPoint(Vector2<double> point1, Vector2<double> point2, Vector2<double> controlPoint)
    {
        Point1 = point1;
        Point2 = point2;
        ControlPoint = controlPoint;
    }
    
    public Vector2<double> ControlPoint { get; }

    public Vector2<double> Point1 { get; }

    public Vector2<double> Point2 { get; }
}