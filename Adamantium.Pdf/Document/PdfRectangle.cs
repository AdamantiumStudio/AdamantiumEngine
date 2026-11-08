using System;
using Adamantium.Pdf.Objects;

namespace Adamantium.Pdf.Document;

/// <summary>A rectangle in default user space, in points: corners normalized so that left is below right and bottom
/// below top.</summary>
public readonly record struct PdfRectangle(double Left, double Bottom, double Right, double Top)
{
    public double Width => Right - Left;

    public double Height => Top - Bottom;

    public bool IsEmpty => Width <= 0 || Height <= 0;

    public PdfRectangle Intersect(PdfRectangle other)
    {
        var left = Math.Max(Left, other.Left);
        var bottom = Math.Max(Bottom, other.Bottom);
        var right = Math.Min(Right, other.Right);
        var top = Math.Min(Top, other.Top);
        return right > left && top > bottom ? new PdfRectangle(left, bottom, right, top) : default;
    }

    /// <summary>The rectangle an array of four numbers stands for, or null when it is not one.</summary>
    public static PdfRectangle? From(PdfObject value, IPdfObjectResolver resolver)
    {
        if (resolver.Resolve(value) is not PdfArray { Count: >= 4 } array)
        {
            return null;
        }

        Span<double> numbers = stackalloc double[4];
        for (var i = 0; i < 4; i++)
        {
            switch (resolver.Resolve(array[i]))
            {
                case PdfInteger integer:
                    numbers[i] = integer.Value;
                    break;
                case PdfReal real when double.IsFinite(real.Value):
                    numbers[i] = real.Value;
                    break;
                default:
                    return null;
            }
        }

        return new PdfRectangle(Math.Min(numbers[0], numbers[2]), Math.Min(numbers[1], numbers[3]),
            Math.Max(numbers[0], numbers[2]), Math.Max(numbers[1], numbers[3]));
    }
}
