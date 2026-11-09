using System.Text;
using Adamantium.Pdf.Document;
using Adamantium.Pdf.Functions;
using NUnit.Framework;

namespace Adamantium.PdfTests;

public class PdfFunctionTests
{
    private static (PdfDocument Document, int First) Document(params byte[][] objects)
    {
        var builder = new PdfFileBuilder();
        builder.Add("<< /Type /Catalog /Pages 2 0 R >>");
        builder.Add("<< /Type /Pages /Kids [] >>");
        var first = builder.Next;
        foreach (var body in objects)
        {
            builder.Add(body);
        }

        return (PdfDocument.Open(builder.Build("/Root 1 0 R")), first);
    }

    private static double[] Evaluate(PdfFunction function, params double[] input)
    {
        var output = new double[function.Outputs];
        function.Evaluate(input, output);
        return output;
    }

    private static byte[] Text(string text) => Encoding.ASCII.GetBytes(text);

    [Test]
    public void ExponentialInterpolatesBetweenItsEnds()
    {
        var (document, first) = Document(Text("<< /FunctionType 2 /Domain [0 1] /C0 [0 0 1] /C1 [1 0 0] /N 2 >>"));
        var function = PdfFunctions.Create(document.GetObject(first), document);
        Assert.That(Evaluate(function, 0.5), Is.EqualTo(new[] { 0.25, 0, 0.75 }).Within(1e-12));
        Assert.That(Evaluate(function, 7), Is.EqualTo(new[] { 1.0, 0, 0 }).Within(1e-12));
    }

    [Test]
    public void StitchingPicksTheSubdomainAndEncodesIntoIt()
    {
        var (document, first) = Document(
            Text("<< /FunctionType 3 /Domain [0 1] /Functions [4 0 R 4 0 R] /Bounds [0.5] /Encode [0 1 1 0] >>"),
            Text("<< /FunctionType 2 /Domain [0 1] /C0 [0] /C1 [10] /N 1 >>"));
        var function = PdfFunctions.Create(document.GetObject(first), document);
        Assert.That(Evaluate(function, 0.25)[0], Is.EqualTo(5).Within(1e-12));
        Assert.That(Evaluate(function, 0.75)[0], Is.EqualTo(5).Within(1e-12));
        Assert.That(Evaluate(function, 1)[0], Is.EqualTo(0).Within(1e-12));
    }

    [Test]
    public void SampledInterpolatesBetweenSamples()
    {
        var (document, first) = Document(PdfFileBuilder.Stream(
            "/FunctionType 0 /Domain [0 1] /Range [0 1] /Size [3] /BitsPerSample 8", [0, 255, 0]));
        var function = PdfFunctions.Create(document.GetObject(first), document);
        Assert.That(Evaluate(function, 0.25)[0], Is.EqualTo(0.5).Within(1e-12));
        Assert.That(Evaluate(function, 0.5)[0], Is.EqualTo(1).Within(1e-12));
        Assert.That(Evaluate(function, 1)[0], Is.EqualTo(0).Within(1e-12));
    }

    [Test]
    public void SampledOverTwoInputsIsBilinear()
    {
        var (document, first) = Document(PdfFileBuilder.Stream(
            "/FunctionType 0 /Domain [0 1 0 1] /Range [0 1] /Size [2 2] /BitsPerSample 8", [0, 255, 255, 0]));
        var function = PdfFunctions.Create(document.GetObject(first), document);
        Assert.That(Evaluate(function, 0.5, 0.5)[0], Is.EqualTo(0.5).Within(1e-12));
        Assert.That(Evaluate(function, 1, 0)[0], Is.EqualTo(1).Within(1e-12));
    }

    [Test]
    public void PostScriptRunsArithmeticConditionsAndStackOperators()
    {
        var program = "{ dup 0.5 gt { 1 exch sub } if dup 2 copy pop pop exch pop 360 mul cos }";
        var (document, first) = Document(PdfFileBuilder.Stream(
            "/FunctionType 4 /Domain [0 1] /Range [-1 1]", Text(program)));
        var function = PdfFunctions.Create(document.GetObject(first), document);
        Assert.That(Evaluate(function, 0.25)[0], Is.EqualTo(0).Within(1e-12));
        Assert.That(Evaluate(function, 0.75)[0], Is.EqualTo(0).Within(1e-12));
        Assert.That(Evaluate(function, 0.5)[0], Is.EqualTo(-1).Within(1e-12));
    }

    [Test]
    public void PostScriptRollAndIndexMoveTheStackAsTheLanguageSays()
    {
        var (document, first) = Document(PdfFileBuilder.Stream(
            "/FunctionType 4 /Domain [0 1] /Range [0 9 0 9 0 9 0 9]", Text("{ pop 1 2 3 3 1 roll 2 index }")));
        var function = PdfFunctions.Create(document.GetObject(first), document);
        Assert.That(Evaluate(function, 0), Is.EqualTo(new double[] { 3, 1, 2, 3 }));
    }

    [Test]
    public void PostScriptIfElseTakesBothBlocks()
    {
        var (document, first) = Document(PdfFileBuilder.Stream(
            "/FunctionType 4 /Domain [0 1] /Range [0 10 0 10]", Text("{ 0.5 lt { 1 2 } { 3 4 } ifelse }")));
        var function = PdfFunctions.Create(document.GetObject(first), document);
        Assert.That(Evaluate(function, 0.1), Is.EqualTo(new double[] { 1, 2 }));
        Assert.That(Evaluate(function, 0.9), Is.EqualTo(new double[] { 3, 4 }));
    }

    [Test]
    public void APostScriptLoopOfBlocksStopsAtItsStepLimit()
    {
        var deep = new StringBuilder("{ ");
        for (var i = 0; i < 5000; i++)
        {
            deep.Append("true { ");
        }

        deep.Append("1 ");
        for (var i = 0; i < 5000; i++)
        {
            deep.Append("} if ");
        }

        deep.Append('}');
        var (document, first) = Document(PdfFileBuilder.Stream(
            "/FunctionType 4 /Domain [0 1] /Range [0 1]", Text(deep.ToString())));
        var function = PdfFunctions.Create(document.GetObject(first), document);
        Assert.That(Evaluate(function, 0.5)[0], Is.EqualTo(0));
    }

    [Test]
    public void FunctionsThatCannotBeReadAreNull()
    {
        var (document, first) = Document(
            Text("<< /FunctionType 9 /Domain [0 1] >>"),
            Text("<< /FunctionType 3 /Domain [0 1] /Functions [5 0 R] /Bounds [] /Encode [0 1] >>"));
        Assert.That(PdfFunctions.Create(document.GetObject(first), document), Is.Null);
        Assert.That(PdfFunctions.Create(document.GetObject(first + 1), document), Is.Null);
    }
}
