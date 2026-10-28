using System;
using System.Globalization;
using Adamantium.Mathematics;
using Adamantium.Mathematics.Svg;
using NUnit.Framework;

namespace Adamantium.MathTests;

public class SvgPathDataTests
{
    private static string Walk(string data)
    {
        var sink = new RecordingPathSink();
        SvgPathData.Walk(data, sink);
        return sink.ToString();
    }

    [TestCase("M1,2L3 4", "M1,2 L3,4")]
    [TestCase("m1-2.5.5 1", "M1,-2.5 L1.5,-1.5")]
    [TestCase("M0 0 1 1 2 2", "M0,0 L1,1 L2,2")]
    [TestCase("M1e2-1E-1", "M100,-0.1")]
    [TestCase("M1 1h5v-5H0V2z", "M1,1 L6,1 L6,-4 L0,-4 L0,2 Z")]
    [TestCase("M0 0C1 2 3 4 5 6 7 8 9 10 11 12", "M0,0 C1,2,3,4,5,6 C7,8,9,10,11,12")]
    [TestCase("M0 0c1 2 3 4 5 6 1 1 1 1 1 1", "M0,0 C1,2,3,4,5,6 C6,7,6,7,6,7")]
    [TestCase("M0 0Q1 1 2 0q1-1 2 0", "M0,0 Q1,1,2,0 Q3,-1,4,0")]
    public void ReadsCommandsAndNumbersAsSvgWritesThem(string data, string expected)
    {
        Assert.That(Walk(data), Is.EqualTo(expected));
    }

    [Test]
    public void SmoothCurves_ReflectThePreviousControlOfTheirKind()
    {
        Assert.That(Walk("M0 0C1 1 2 1 3 0S5 -1 6 0"), Is.EqualTo("M0,0 C1,1,2,1,3,0 C4,-1,5,-1,6,0"));
        Assert.That(Walk("M0 0L3 0S5 -1 6 0"), Is.EqualTo("M0,0 L3,0 C3,0,5,-1,6,0"));
        Assert.That(Walk("M0 0Q1 1 2 0T4 0"), Is.EqualTo("M0,0 Q1,1,2,0 Q3,-1,4,0"));
        Assert.That(Walk("M0 0C1 1 2 1 3 0T4 0"), Is.EqualTo("M0,0 C1,1,2,1,3,0 Q3,0,4,0"));
        Assert.That(Walk("M0 0Q1 1 2 0S3 -1 4 0"), Is.EqualTo("M0,0 Q1,1,2,0 C2,0,3,-1,4,0"));
    }

    [Test]
    public void SmoothCurves_ChainAndReadRelativePoints()
    {
        Assert.That(Walk("M0 0S1 1 2 0S3 -1 4 0"), Is.EqualTo("M0,0 C0,0,1,1,2,0 C3,-1,3,-1,4,0"));
        Assert.That(Walk("M0 0c1 1 2 1 3 0s2-1 3 0"), Is.EqualTo("M0,0 C1,1,2,1,3,0 C4,-1,5,-1,6,0"));
        Assert.That(Walk("M0 0q1 1 2 0t2 0t2 0"), Is.EqualTo("M0,0 Q1,1,2,0 Q3,-1,4,0 Q5,1,6,0"));
    }

    [Test]
    public void AMoveOrAClose_EndsTheReflection()
    {
        Assert.That(Walk("M0 0C1 1 2 1 3 0M5 5S6 6 7 5"), Is.EqualTo("M0,0 C1,1,2,1,3,0 M5,5 C5,5,6,6,7,5"));
        Assert.That(Walk("M0 0C1 1 2 1 3 0zS1 1 2 0"), Is.EqualTo("M0,0 C1,1,2,1,3,0 Z M0,0 C0,0,1,1,2,0"));
    }

    [Test]
    public void AfterAClose_TheNextStepBeginsAtTheStart()
    {
        Assert.That(Walk("M1 1L2 2zl1 0"), Is.EqualTo("M1,1 L2,2 Z M1,1 L2,1"));
        Assert.That(Walk("M1 1L2 2zm1 0l1 0"), Is.EqualTo("M1,1 L2,2 Z M2,1 L3,1"));
    }

    [Test]
    public void AnArcsFlags_AreOneDigitEach()
    {
        Assert.That(Walk("M0 0a1 1 0 11 5 5"), Is.EqualTo("M0,0 A1,1,0,1,1,5,5"));
        Assert.That(Walk("M1 1A2,2,30,0,1,-3-4"), Is.EqualTo("M1,1 A2,2,30,0,1,-3,-4"));
        Assert.That(Walk("M1 1a1 1 0 0 1 2 0"), Is.EqualTo("M1,1 A1,1,0,0,1,3,1"));
    }

    [Test]
    public void ReadingStops_AtTheFirstError()
    {
        Assert.That(Walk("M1 2 L3 4 X 5 6 L7 8"), Is.EqualTo("M1,2 L3,4"));
        Assert.That(Walk("M1 2 L3"), Is.EqualTo("M1,2"));
        Assert.That(Walk("L1 2 M3 4"), Is.EqualTo(""));
    }

    [Test]
    public void AQuarterArc_IsTwoCubicsOnTheCircle()
    {
        var sink = new RecordingPathSink();
        SvgPathData.ArcToCubics(new Vector2<double>(1, 0), 1, 1, 0, false, true, new Vector2<double>(0, 1), Math.PI / 4, sink);
        var steps = sink.ToString().Split(' ');
        Assert.That(steps, Has.Length.EqualTo(2));
        Assert.That(steps[1], Does.EndWith(",0,1"));
        var middle = steps[0].Substring(1).Split(',');
        var x = double.Parse(middle[4], CultureInfo.InvariantCulture);
        var y = double.Parse(middle[5], CultureInfo.InvariantCulture);
        Assert.That(Math.Sqrt(x * x + y * y), Is.EqualTo(1).Within(1e-12));
        Assert.That(x, Is.EqualTo(y).Within(1e-12));
    }

    [Test]
    public void AnArc_WithoutAFiniteRadius_IsALine_AndToItsStart_IsNothing()
    {
        var sink = new RecordingPathSink();
        SvgPathData.ArcToCubics(new Vector2<double>(1, 0), 0, 1, 0, false, true, new Vector2<double>(0, 1), Math.PI / 4, sink);
        SvgPathData.ArcToCubics(new Vector2<double>(0, 1), double.PositiveInfinity, 1, 0, false, true, new Vector2<double>(2, 1),
            Math.PI / 4, sink);
        SvgPathData.ArcToCubics(new Vector2<double>(2, 1), 1, 1, 0, false, true, new Vector2<double>(2, 1), Math.PI / 4, sink);
        Assert.That(sink.ToString(), Is.EqualTo("L0,1 L2,1"));
    }

    [Test]
    public void Numbers_ReadAsATransformWritesThem()
    {
        Assert.That(SvgPathData.ReadNumbers("0.125 0 .045,.999-1e1"), Is.EqualTo(new[] { 0.125, 0, 0.045, 0.999, -10 }));
    }
}
