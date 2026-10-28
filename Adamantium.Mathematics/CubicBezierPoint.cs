namespace Adamantium.Mathematics;

public readonly struct CubicBezierPoint
{
    public CubicBezierPoint(Vector2<double> point1, Vector2<double> point2, Vector2<double> controlPoint1, Vector2<double> controlPoint2)
    {
        Point1 = point1;
        Point2 = point2;
        ControlPoint1 = controlPoint1;
        ControlPoint2 = controlPoint2;
    }
    
    public Vector2<double> ControlPoint1 { get; }

    public Vector2<double> ControlPoint2 { get; }

    public Vector2<double> Point1 { get; }

    public Vector2<double> Point2 { get; }
}