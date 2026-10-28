using System;
using Adamantium.Mathematics;

namespace Adamantium.MathTests.Golden;

/// <summary>The recorded operations of the sizes and rectangles, answered by <see cref="Size{T}"/> and
/// <see cref="Rectangle{T}"/>. The float rectangle held its left, top, right and bottom; its inputs are read and its
/// answers written in that order.</summary>
public static class GeometryGolden
{
    /// <summary>Adds the operations of Size, SizeF, Rectangle and RectangleF to <paramref name="map"/>.</summary>
    public static void Register(GoldenMap map)
    {
        RegisterSizes(map);
        RegisterIntegerRectangle(map);
        RegisterFloatRectangle(map);
        map.NotMapped(key => key.StartsWith("Rectangle") && key.Contains(".get_IsEmpty("),
            "meaning changed: the old was all four values zero, the new is no area; used nowhere");
        map.NotMapped(key => key.StartsWith("Rectangle.") && (key.Contains(".Offset(") || key.Contains(".Inflate(")),
            "old bug: the integer rectangle's X and Y setters moved the right edge into the width, so moving grew it; " +
            "used nowhere; TypedGeometryTests checks the new");
        map.NotMapped("RectangleF.Contains(this Vector2F&,Boolean&)",
            "meaning changed: this overload left out the right and bottom edges while the one by value counted them; " +
            "used nowhere");
        map.NotMapped("RectangleF.Merge(this RectangleF)",
            "Union holds two ordered rectangles as Merge did - ContoursContainer moved to it");
        map.ChangedWhere(key => key.StartsWith("Size.Equals("), inputs => Array.Exists(inputs, double.IsNaN),
            "decided 28.10: Equals takes a NaN as equal to a NaN, as a dictionary key needs and System.Numerics does; " +
            "== still does not");
        map.ChangedWhere(key => key.StartsWith("RectangleF.Intersects("), inputs => Touching(inputs) || NoArea(inputs),
            "meaning changed: rectangles that only touch, or one of no area inside the other, intersected in the float " +
            "one, not in the integer one; the new follows Intersect, which needs an area");
        map.ChangedWhere(key => key.StartsWith("Rectangle.Intersects("),
            inputs => inputs[2] <= 0 || inputs[3] <= 0 || inputs[6] <= 0 || inputs[7] <= 0,
            "old bug: a rectangle of no area, or of a negative size, could intersect another; Intersects needs an area " +
            "now, as Intersect does");
        map.ChangedWhere(key => key == "RectangleF.op_Explicit(RectangleF)",
            inputs => Array.Exists(inputs, value => !double.IsFinite(value) || Math.Abs(value) > 2e9),
            "a value past the integer range: the cast's answer was the processor's, As<int> saturates");
    }

    private static void RegisterSizes(GoldenMap map)
    {
        map.Calls("Size.get_Width(this )", i => Self(i.Size<double>(), x => x.Width));
        map.Calls("Size.get_Height(this )", i => Self(i.Size<double>(), x => x.Height));
        map.Returns("Size.op_Addition(Size,Size)", i => i.Size<double>() + i.Size<double>());
        map.Returns("Size.op_Subtraction(Size,Size)", i => i.Size<double>() - i.Size<double>());
        map.Returns("Size.op_Multiply(Size,Double)", i => i.Size<double>() * i.Number<double>());
        map.Returns("Size.op_Division(Size,Double)", i => i.Size<double>() / i.Number<double>());
        map.Returns("Size.op_Multiply(Size,Vector2)", i => i.Size<double>() * i.Vector2<double>());
        map.Returns("Size.op_Division(Size,Vector2)", i => i.Size<double>() / i.Vector2<double>());
        map.Calls("Size.Constrain(this Size)", i => Self(i.Size<double>(), i.Size<double>(), (x, o) => x.Constrain(o)));
        map.Calls("Size.Deflate(this Thickness)", i =>
        {
            var size = i.Size<double>();
            return [size.Deflate(i.Thickness()), size];
        });
        map.Calls("Size.Inflate(this Thickness)", i =>
        {
            var size = i.Size<double>();
            return [size.Inflate(i.Thickness()), size];
        });
        map.Returns("Size.IsZero(Size)", i => i.Size<double>().IsZero);
        map.Returns("Size.op_Implicit(Size)", i => (Vector2<double>)i.Size<double>());
        map.Returns("Size.op_Equality(Size,Size)", i => i.Size<double>() == i.Size<double>());
        map.Returns("Size.op_Inequality(Size,Size)", i => i.Size<double>() != i.Size<double>());
        map.Calls("Size.Equals(this Size)", i => Self(i.Size<double>(), i.Size<double>(), (x, o) => x.Equals(o)));

        map.Calls("SizeF.Equals(this SizeF)", i => Self(i.Size<float>(), i.Size<float>(), (x, o) => x.Equals(o)));
        map.Returns("SizeF.op_Equality(SizeF,SizeF)", i => i.Size<float>() == i.Size<float>());
        map.Returns("SizeF.op_Inequality(SizeF,SizeF)", i => i.Size<float>() != i.Size<float>());
        map.ExactWhereOldWasNear("SizeF", (a, b) => Size<float>.NearEqual(new GoldenInputs(a).Size<float>(), new GoldenInputs(b).Size<float>()));
    }

    private static void RegisterIntegerRectangle(GoldenMap map)
    {
        const string r = "Rectangle";
        map.Calls($"{r}.get_X(this )", i => Self(i.Rectangle<int>(), x => x.X));
        map.Calls($"{r}.get_Y(this )", i => Self(i.Rectangle<int>(), x => x.Y));
        map.Calls($"{r}.get_Right(this )", i => Self(i.Rectangle<int>(), x => x.Right));
        map.Calls($"{r}.get_Bottom(this )", i => Self(i.Rectangle<int>(), x => x.Bottom));
        map.Calls($"{r}.get_Center(this )", i => Self(i.Rectangle<int>(), x => x.Center.As<double>()));
        map.Calls($"{r}.get_Size(this )", i => Self(i.Rectangle<int>(), x => x.Size.As<float>()));
        map.Calls($"{r}.Contains(this {r})", i => Self(i.Rectangle<int>(), i.Rectangle<int>(), (x, o) => x.Contains(o)));
        map.Calls($"{r}.Contains(this {r}&,Boolean&)", i =>
        {
            var (x, value) = (i.Rectangle<int>(), i.Rectangle<int>());
            return [value, x.Contains(value), x];
        });
        map.Calls($"{r}.Contains(this Single,Single)", i =>
        {
            var x = i.Rectangle<int>();
            return [x.As<float>().Contains(i.Number<float>(), i.Number<float>()), x];
        });
        map.Calls($"{r}.Contains(this Int32,Int32)", i =>
        {
            var x = i.Rectangle<int>();
            return [x.Contains(i.Number<int>(), i.Number<int>()), x];
        });
        map.Calls($"{r}.Intersects(this {r})", i => Self(i.Rectangle<int>(), i.Rectangle<int>(), (x, o) => x.Intersects(o)));
        map.Calls($"{r}.Intersects(this {r}&,Boolean&)", i =>
        {
            var (x, value) = (i.Rectangle<int>(), i.Rectangle<int>());
            return [value, x.Intersects(value), x];
        });
        map.Returns($"{r}.FromCorners(Int32,Int32,Int32,Int32)",
            i => Rectangle.CreateFromCorners(i.Number<int>(), i.Number<int>(), i.Number<int>(), i.Number<int>()));
        map.Returns($"{r}.Intersect({r},{r})", i => Rectangle<int>.Intersect(i.Rectangle<int>(), i.Rectangle<int>()));
        map.Returns($"{r}.Union({r},{r})", i => Rectangle<int>.Union(i.Rectangle<int>(), i.Rectangle<int>()));
        map.Calls($"{r}.Equals(this {r})", i => Self(i.Rectangle<int>(), i.Rectangle<int>(), (x, o) => x.Equals(o)));
        map.Returns($"{r}.op_Equality({r},{r})", i => i.Rectangle<int>() == i.Rectangle<int>());
        map.Returns($"{r}.op_Inequality({r},{r})", i => i.Rectangle<int>() != i.Rectangle<int>());
        map.Returns($"{r}.op_Implicit({r})", i => Corners(i.Rectangle<int>().As<float>()));
    }

    private static void RegisterFloatRectangle(GoldenMap map)
    {
        const string r = "RectangleF";
        map.Calls($"{r}.get_X(this )", i => SelfF(i.RectangleByCorners<float>(), x => x.X));
        map.Calls($"{r}.get_Y(this )", i => SelfF(i.RectangleByCorners<float>(), x => x.Y));
        map.Calls($"{r}.get_Width(this )", i => SelfF(i.RectangleByCorners<float>(), x => x.Width));
        map.Calls($"{r}.get_Height(this )", i => SelfF(i.RectangleByCorners<float>(), x => x.Height));
        map.Calls($"{r}.get_Location(this )", i => SelfF(i.RectangleByCorners<float>(), x => x.Position));
        map.Calls($"{r}.get_Center(this )", i => SelfF(i.RectangleByCorners<float>(), x => x.Center));
        map.Calls($"{r}.get_Size(this )", i => SelfF(i.RectangleByCorners<float>(), x => (Vector2<float>)x.Size));
        map.Calls($"{r}.get_TopLeft(this )", i => SelfF(i.RectangleByCorners<float>(), x => x.TopLeft));
        map.Calls($"{r}.get_TopRight(this )", i => SelfF(i.RectangleByCorners<float>(), x => x.TopRight));
        map.Calls($"{r}.get_BottomLeft(this )", i => SelfF(i.RectangleByCorners<float>(), x => x.BottomLeft));
        map.Calls($"{r}.get_BottomRight(this )", i => SelfF(i.RectangleByCorners<float>(), x => x.BottomRight));
        map.Calls($"{r}.Offset(this Vector2)", i =>
        {
            var x = i.RectangleByCorners<float>();
            x.Offset(i.Vector2<float>());
            return [Corners(x)];
        });
        map.Calls($"{r}.Offset(this Vector2F)", i =>
        {
            var x = i.RectangleByCorners<float>();
            x.Offset(i.Vector2<float>());
            return [Corners(x)];
        });
        map.Calls($"{r}.Offset(this Single,Single)", i =>
        {
            var x = i.RectangleByCorners<float>();
            x.Offset(i.Number<float>(), i.Number<float>());
            return [Corners(x)];
        });
        map.Calls($"{r}.Inflate(this Single,Single)", i =>
        {
            var x = i.RectangleByCorners<float>();
            x.Inflate(i.Number<float>(), i.Number<float>());
            return [Corners(x)];
        });
        map.Calls($"{r}.Contains(this {r})",
            i => SelfF(i.RectangleByCorners<float>(), i.RectangleByCorners<float>(), (x, o) => x.Contains(o)));
        map.Calls($"{r}.Contains(this {r}&,Boolean&)", i =>
        {
            var (x, value) = (i.RectangleByCorners<float>(), i.RectangleByCorners<float>());
            return [Corners(value), x.Contains(value), Corners(x)];
        });
        map.Calls($"{r}.Contains(this Single,Single)", i =>
        {
            var x = i.RectangleByCorners<float>();
            return [x.Contains(i.Number<float>(), i.Number<float>()), Corners(x)];
        });
        map.Calls($"{r}.Contains(this Vector2F)", i =>
        {
            var x = i.RectangleByCorners<float>();
            return [x.Contains(i.Vector2<float>()), Corners(x)];
        });
        map.Calls($"{r}.Contains(this Vector2)", i =>
        {
            var x = i.RectangleByCorners<float>();
            return [x.Contains(i.Vector2<float>()), Corners(x)];
        });
        map.Calls($"{r}.Intersects(this {r})",
            i => SelfF(i.RectangleByCorners<float>(), i.RectangleByCorners<float>(), (x, o) => x.Intersects(o)));
        map.Calls($"{r}.Intersects(this {r}&,Boolean&)", i =>
        {
            var (x, value) = (i.RectangleByCorners<float>(), i.RectangleByCorners<float>());
            return [Corners(value), x.Intersects(value), Corners(x)];
        });
        map.Returns($"{r}.Intersect({r},{r})",
            i => Corners(Rectangle<float>.Intersect(i.RectangleByCorners<float>(), i.RectangleByCorners<float>())));
        map.Returns($"{r}.Union({r},{r})",
            i => Corners(Rectangle<float>.Union(i.RectangleByCorners<float>(), i.RectangleByCorners<float>())));
        map.Calls($"{r}.Equals(this {r})",
            i => SelfF(i.RectangleByCorners<float>(), i.RectangleByCorners<float>(), (x, o) => x.Equals(o)));
        map.Returns($"{r}.op_Equality({r},{r})", i => i.RectangleByCorners<float>() == i.RectangleByCorners<float>());
        map.Returns($"{r}.op_Inequality({r},{r})", i => i.RectangleByCorners<float>() != i.RectangleByCorners<float>());
        map.ExactWhereOldWasNear(r, (a, b) =>
            Rectangle<float>.NearEqual(new GoldenInputs(a).RectangleByCorners<float>(), new GoldenInputs(b).RectangleByCorners<float>()));
        map.Returns($"{r}.op_Explicit({r})", i => i.RectangleByCorners<float>().As<int>());
        map.Returns($"{r}.op_Implicit({r})", i => (Vector4<float>)i.RectangleByCorners<float>());
    }

    private static (float Left, float Top, float Right, float Bottom) Corners(Rectangle<float> value) =>
        (value.X, value.Y, value.Right, value.Bottom);

    private static bool Touching(double[] inputs) =>
        inputs[4] == inputs[2] || inputs[0] == inputs[6] || inputs[5] == inputs[3] || inputs[1] == inputs[7];

    private static bool NoArea(double[] inputs) =>
        inputs[2] <= inputs[0] || inputs[3] <= inputs[1] || inputs[6] <= inputs[4] || inputs[7] <= inputs[5];

    private static object[] Self<TSelf>(TSelf self, Func<TSelf, object> answer) => [answer(self), self];

    private static object[] Self<TSelf>(TSelf self, TSelf other, Func<TSelf, TSelf, object> answer) => [answer(self, other), self];

    private static object[] SelfF(Rectangle<float> self, Func<Rectangle<float>, object> answer) => [answer(self), Corners(self)];

    private static object[] SelfF(Rectangle<float> self, Rectangle<float> other, Func<Rectangle<float>, Rectangle<float>, object> answer) =>
        [answer(self, other), Corners(self)];
}
