using System;

namespace Adamantium.Pdf.Functions;

/// <summary>A PDF function (ISO 32000-2, 7.10): m inputs clipped to its domain, n outputs clipped to its range.
/// </summary>
public abstract class PdfFunction
{
    protected PdfFunction(double[] domain, double[] range)
    {
        Domain = domain;
        Range = range;
    }

    /// <summary>Pairs of bounds, one pair an input.</summary>
    public double[] Domain { get; }

    /// <summary>Pairs of bounds, one pair an output; null when the function does not clip its outputs.</summary>
    public double[] Range { get; }

    public int Inputs => Domain.Length / 2;

    public abstract int Outputs { get; }

    public void Evaluate(ReadOnlySpan<double> input, Span<double> output)
    {
        Span<double> clipped = stackalloc double[Math.Min(Inputs, 32)];
        for (var i = 0; i < clipped.Length; i++)
        {
            var value = i < input.Length ? input[i] : 0;
            clipped[i] = Clip(double.IsNaN(value) ? 0 : value, Domain[2 * i], Domain[2 * i + 1]);
        }

        output.Clear();
        Compute(clipped, output);
        if (Range == null)
        {
            return;
        }

        for (var j = 0; j < output.Length && 2 * j + 1 < Range.Length; j++)
        {
            output[j] = Clip(output[j], Range[2 * j], Range[2 * j + 1]);
        }
    }

    protected abstract void Compute(ReadOnlySpan<double> input, Span<double> output);

    protected static double Clip(double value, double low, double high) =>
        value < low ? low : value > high ? high : double.IsNaN(value) ? low : value;

    protected static double Interpolate(double x, double xMin, double xMax, double yMin, double yMax) =>
        xMax == xMin ? yMin : yMin + (x - xMin) * (yMax - yMin) / (xMax - xMin);
}
