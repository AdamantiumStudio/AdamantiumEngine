using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using NUnit.Framework;

namespace Adamantium.MathTests.Golden;

/// <summary>What the mathematics being replaced answered, as Tests/Adamantium.MathGolden recorded it: per operation,
/// the inputs of each call, whether it threw, and its answer.</summary>
public sealed class GoldenFile
{
    private static readonly Lazy<GoldenFile> recorded = new(() => Read(FindRecorded()));

    private GoldenFile(List<GoldenOperation> operations)
    {
        Operations = operations;
    }

    /// <summary>The file in Tests/TestAssets/MathGolden, read once.</summary>
    public static GoldenFile Recorded => recorded.Value;

    /// <summary>The operations in the order recorded; a key appears more than once for conversions to several
    /// types.</summary>
    public IReadOnlyList<GoldenOperation> Operations { get; }

    /// <summary>Reads the file at <paramref name="path"/>.</summary>
    public static GoldenFile Read(string path)
    {
        var operations = new List<GoldenOperation>();
        using var file = File.OpenRead(path);
        using var zip = new GZipStream(file, CompressionMode.Decompress);
        using var reader = new BinaryReader(zip, Encoding.UTF8);
        for (var key = reader.ReadString(); key.Length > 0; key = reader.ReadString())
        {
            var cases = new GoldenCase[reader.ReadInt32()];
            for (var c = 0; c < cases.Length; c++)
            {
                var inputs = ReadNumbers(reader);
                var threw = reader.ReadBoolean();
                cases[c] = new GoldenCase(inputs, threw, threw ? [] : ReadNumbers(reader));
            }

            operations.Add(new GoldenOperation(key, operations.Count, cases));
        }

        return new GoldenFile(operations);
    }

    private static double[] ReadNumbers(BinaryReader reader)
    {
        var numbers = new double[reader.ReadInt32()];
        for (var i = 0; i < numbers.Length; i++)
        {
            numbers[i] = reader.ReadDouble();
        }

        return numbers;
    }

    private static string FindRecorded()
    {
        for (var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "Tests", "TestAssets", "MathGolden", "golden.bin.gz");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException("Tests/TestAssets/MathGolden/golden.bin.gz");
    }
}
