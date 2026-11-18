namespace Adamantium.Pdf.Text;

/// <summary>Where a line of <see cref="PdfPageText"/> stands, for fitting its box against its neighbours: its direction
/// and up, its size, and its baseline, box and extent measured along them.</summary>
internal readonly struct PdfTextLineShape
{
    public PdfTextLineShape(double alongX, double alongY, double upX, double upY, double size, double baseline,
        double low, double high, double start, double end)
    {
        AlongX = alongX;
        AlongY = alongY;
        UpX = upX;
        UpY = upY;
        Size = size;
        Baseline = baseline;
        Low = low;
        High = high;
        Start = start;
        End = end;
    }

    public double AlongX { get; }

    public double AlongY { get; }

    public double UpX { get; }

    public double UpY { get; }

    /// <summary>The size of its biggest glyph.</summary>
    public double Size { get; }

    /// <summary>The baseline of its biggest glyph, along its up.</summary>
    public double Baseline { get; }

    /// <summary>The bottom of its box, along its up.</summary>
    public double Low { get; }

    /// <summary>The top of its box, along its up.</summary>
    public double High { get; }

    /// <summary>Where it starts along its direction.</summary>
    public double Start { get; }

    /// <summary>Where it ends along its direction.</summary>
    public double End { get; }

    /// <summary>Whether another line runs exactly the same way, up the same side.</summary>
    public bool RunsAs(PdfTextLineShape other) =>
        AlongX * other.AlongX + AlongY * other.AlongY > 1 - 1e-9 && UpX * other.UpX + UpY * other.UpY > 1 - 1e-9;
}
