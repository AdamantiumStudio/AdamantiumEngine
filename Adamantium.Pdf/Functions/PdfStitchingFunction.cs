using System;
using System.Collections.Generic;

namespace Adamantium.Pdf.Functions;

/// <summary>Type 3: one input split at <c>/Bounds</c> into subdomains, each mapped by <c>/Encode</c> onto a function of
/// its own.</summary>
public sealed class PdfStitchingFunction : PdfFunction
{
    private readonly IReadOnlyList<PdfFunction> _functions;
    private readonly double[] _bounds;
    private readonly double[] _encode;

    public PdfStitchingFunction(double[] domain, double[] range, IReadOnlyList<PdfFunction> functions, double[] bounds,
        double[] encode) : base(domain, range)
    {
        _functions = functions;
        _bounds = bounds;
        _encode = encode;
    }

    public override int Outputs => _functions.Count > 0 ? _functions[0].Outputs : 0;

    protected override void Compute(ReadOnlySpan<double> input, Span<double> output)
    {
        if (_functions.Count == 0)
        {
            return;
        }

        var x = input.Length > 0 ? input[0] : 0;
        var index = 0;
        while (index < _bounds.Length && x >= _bounds[index])
        {
            index++;
        }

        index = Math.Min(index, _functions.Count - 1);
        var low = index == 0 ? Domain[0] : _bounds[index - 1];
        var high = index == _bounds.Length ? Domain[1] : _bounds[index];
        var encoded = 2 * index + 1 < _encode.Length
            ? Interpolate(x, low, high, _encode[2 * index], _encode[2 * index + 1])
            : x;
        Span<double> one = [encoded];
        _functions[index].Evaluate(one, output);
    }
}
