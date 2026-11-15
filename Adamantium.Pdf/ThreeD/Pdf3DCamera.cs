using Adamantium.Pdf.Objects;

namespace Adamantium.Pdf.ThreeD;

/// <summary>Where a 3D view's camera stands and how it is turned (ISO 32000-1, 13.6.5, <c>C2W</c>): the camera's axes
/// and its place in the artwork's world. The camera looks along its own Z axis, with its Y axis up.</summary>
public sealed class Pdf3DCamera
{
    public Pdf3DCamera(Pdf3DVector xAxis, Pdf3DVector yAxis, Pdf3DVector zAxis, Pdf3DVector position)
    {
        XAxis = xAxis;
        YAxis = yAxis;
        ZAxis = zAxis;
        Position = position;
    }

    /// <summary>The camera's X axis in the world: to the right of what it sees.</summary>
    public Pdf3DVector XAxis { get; }

    /// <summary>The camera's Y axis in the world: up.</summary>
    public Pdf3DVector YAxis { get; }

    /// <summary>The camera's Z axis in the world: the way it looks.</summary>
    public Pdf3DVector ZAxis { get; }

    /// <summary>The camera's place in the world.</summary>
    public Pdf3DVector Position { get; }

    /// <summary>The camera of a <c>C2W</c> array - twelve numbers, the matrix's three axes and its translation - or null
    /// for anything else.</summary>
    public static Pdf3DCamera From(PdfObject value, IPdfObjectResolver resolver)
    {
        var numbers = PdfValues.Numbers(value, resolver);
        if (numbers is not { Length: 12 })
        {
            return null;
        }

        return new Pdf3DCamera(new Pdf3DVector(numbers[0], numbers[1], numbers[2]),
            new Pdf3DVector(numbers[3], numbers[4], numbers[5]), new Pdf3DVector(numbers[6], numbers[7], numbers[8]),
            new Pdf3DVector(numbers[9], numbers[10], numbers[11]));
    }
}
