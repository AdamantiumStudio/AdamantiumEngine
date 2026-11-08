using System;
using System.Collections.Generic;

namespace Adamantium.Pdf.Functions;

/// <summary>An array of one-output functions read as one function of as many outputs, as shadings and tint
/// transforms allow.</summary>
public sealed class PdfFunctionArray : PdfFunction
{
    private readonly IReadOnlyList<PdfFunction> _functions;

    public PdfFunctionArray(IReadOnlyList<PdfFunction> functions) : base(functions[0].Domain, null)
    {
        _functions = functions;
    }

    public override int Outputs => _functions.Count;

    protected override void Compute(ReadOnlySpan<double> input, Span<double> output)
    {
        Span<double> one = stackalloc double[1];
        for (var j = 0; j < output.Length && j < _functions.Count; j++)
        {
            _functions[j].Evaluate(input, one);
            output[j] = one[0];
        }
    }
}
