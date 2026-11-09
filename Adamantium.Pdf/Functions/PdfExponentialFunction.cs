using System;

namespace Adamantium.Pdf.Functions;

/// <summary>Type 2: C0 + x^N (C1 - C0), one input.</summary>
public sealed class PdfExponentialFunction : PdfFunction
{
    private readonly double[] _c0;
    private readonly double[] _c1;
    private readonly double _exponent;

    public PdfExponentialFunction(double[] domain, double[] range, double[] c0, double[] c1, double exponent)
        : base(domain, range)
    {
        _c0 = c0;
        _c1 = c1;
        _exponent = exponent;
    }

    public override int Outputs => _c0.Length;

    protected override void Compute(ReadOnlySpan<double> input, Span<double> output)
    {
        var x = input.Length > 0 ? input[0] : 0;
        var power = _exponent == 1 ? x : Math.Pow(x, _exponent);
        if (double.IsNaN(power) || double.IsInfinity(power))
        {
            power = 0;
        }

        for (var j = 0; j < output.Length && j < _c0.Length; j++)
        {
            output[j] = _c0[j] + power * (_c1[j] - _c0[j]);
        }
    }
}
