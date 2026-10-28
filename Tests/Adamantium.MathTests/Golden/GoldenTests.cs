using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Adamantium.MathTests.Golden;

/// <summary>
/// The new types answer every recorded operation of the old ones as the old ones did: within 1e-5 of the size of the
/// answer - its largest element, at least one - for single precision, 1e-12 for double; and throw where they threw.
/// NaN matches only NaN, except in an answer that overflowed - finite inputs, not all finite out: there any infinity or
/// NaN matches any other, as the order of the sums decides which comes out. Run it under each instruction level as well -
/// DOTNET_EnableAVX=0 (128-bit paths), DOTNET_EnableHWIntrinsic=0 (no SIMD); on .NET 10 DOTNET_EnableFMA=0 changes
/// nothing.
/// </summary>
[TestFixture]
public class GoldenTests
{
    private static readonly string[] Checked =
    [
        "Vector2", "Vector2F", "Vector3", "Vector3F", "Vector4", "Vector4F", "Quaternion", "QuaternionF", "Matrix3x2",
        "Matrix3x2F", "Matrix3x3", "Matrix3x3F", "Matrix4x4", "Matrix4x4F", "Size", "SizeF", "Rectangle", "RectangleF", "Ray", "Plane", "BoundingBox", "BoundingSphere", "Collision"
    ];

    private static readonly Lazy<GoldenMap> Map = new(() =>
    {
        var map = new GoldenMap();
        VectorsGolden.Register(map);
        MatricesGolden.Register(map);
        GeometryGolden.Register(map);
        VolumesGolden.Register(map);
        return map;
    });

    private static IEnumerable<GoldenOperation> Operations =>
        GoldenFile.Recorded.Operations.Where(o => Checked.Contains(GoldenSignature.Parse(o.Key).Type));

    private static IEnumerable<GoldenOperation> Mapped => Operations.Where(o => Map.Value.Resolve(o.Key).Count > 0);

    [Test]
    public void EveryOperation_IsCheckedOrSaysWhyNot()
    {
        var unexplained = Operations.Select(o => o.Key).Distinct()
            .Where(key => Map.Value.Resolve(key).Count == 0 && Map.Value.ReasonNotMapped(key) == null).ToList();

        Assert.That(unexplained, Is.Empty);
    }

    [TestCaseSource(nameof(Mapped))]
    public void Answers_AsTheOldTypesDid(GoldenOperation operation)
    {
        var calls = Map.Value.Resolve(operation.Key);
        var tolerance = Map.Value.ToleranceFor(operation.Key) ?? (GoldenSignature.Parse(operation.Key).IsSinglePrecision ? 1e-5 : 1e-12);
        var failures = new List<string>();
        var checkedCases = 0;
        foreach (var recorded in operation.Cases)
        {
            if (Map.Value.IsChanged(operation.Key, recorded.Inputs))
            {
                continue;
            }

            checkedCases++;
            var answers = calls.Select(call => Answer(call, recorded.Inputs)).ToList();
            if (recorded.Threw)
            {
                if (answers.All(a => a != null) && !Map.Value.MayAnswerWhereOldThrew(operation.Key))
                {
                    failures.Add($"[{Format(recorded.Inputs)}] threw before, answers [{Format(answers[0])}] now");
                }

                continue;
            }

            if (!answers.Any(a => a != null && IsNear(a, recorded.Outputs, recorded.Inputs, tolerance)))
            {
                var closest = answers.FirstOrDefault(a => a?.Length == recorded.Outputs.Length) ?? answers[0];
                failures.Add($"[{Format(recorded.Inputs)}]: was [{Format(recorded.Outputs)}], is " +
                             (closest == null ? "a throw" : $"[{Format(closest)}]"));
            }
        }

        Assert.That(failures, Is.Empty);
        Assert.That(checkedCases, Is.GreaterThan(0), "every case is excused as changed");
    }

    private static double[] Answer(Func<double[], double[]> call, double[] inputs)
    {
        try
        {
            return call(inputs);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool IsNear(double[] actual, double[] expected, double[] inputs, double tolerance)
    {
        if (actual.Length != expected.Length)
        {
            return false;
        }

        var size = expected.Where(double.IsFinite).Select(Math.Abs).DefaultIfEmpty(0).Max();
        var overflowed = inputs.All(double.IsFinite) && !expected.All(double.IsFinite);
        for (var i = 0; i < actual.Length; i++)
        {
            var a = actual[i];
            var e = expected[i];
            if (a == e || (double.IsNaN(a) && double.IsNaN(e)) || (overflowed && !double.IsFinite(a) && !double.IsFinite(e)))
            {
                continue;
            }

            if (!(Math.Abs(a - e) <= tolerance * Math.Max(1, size)))
            {
                return false;
            }
        }

        return true;
    }

    private static string Format(double[] numbers) => string.Join(", ", numbers.Select(n => n.ToString("R")));
}
