using System;
using System.Collections.Generic;
using System.Linq;

namespace Adamantium.MathTests.Golden;

/// <summary>A recorded key taken apart: declaring type, method, whether it is an instance method, and parameter type
/// names, those passed by reference ending in '&amp;'.</summary>
public sealed record GoldenSignature(string Type, string Method, bool IsInstance, IReadOnlyList<string> Parameters)
{
    private static readonly Dictionary<string, int> Sizes = new()
    {
        ["Boolean"] = 1, ["Single"] = 1, ["Double"] = 1, ["Int32"] = 1,
        ["Vector2"] = 2, ["Vector2F"] = 2, ["Vector3"] = 3, ["Vector3F"] = 3, ["Vector4"] = 4, ["Vector4F"] = 4,
        ["Quaternion"] = 4, ["QuaternionF"] = 4, ["Matrix3x2"] = 6, ["Matrix3x2F"] = 6, ["Matrix3x3"] = 9,
        ["Matrix3x3F"] = 9, ["Matrix4x4"] = 16, ["Matrix4x4F"] = 16
    };

    /// <summary>Whether every parameter's type has a known count of numbers.</summary>
    public bool IsSized => Parameters.All(p => SizeOf(p) > 0) && (!IsInstance || SizeOf(Type) > 0);

    /// <summary>Whether the type or a parameter is of single precision: Single, Vector3F, QuaternionF...</summary>
    public bool IsSinglePrecision =>
        Type.EndsWith('F') || Parameters.Any(p => p.TrimEnd('&') == "Single" || p.TrimEnd('&').EndsWith('F'));

    /// <summary>The count of numbers the instance takes, zero for a static method.</summary>
    public int InstanceSize => IsInstance ? SizeOf(Type) : 0;

    /// <summary>Takes <paramref name="key"/> apart.</summary>
    public static GoldenSignature Parse(string key)
    {
        var open = key.IndexOf('(');
        var dot = key.LastIndexOf('.', open);
        var list = key[(open + 1)..^1];
        var isInstance = list.StartsWith("this ", StringComparison.Ordinal);
        if (isInstance)
        {
            list = list[5..];
        }

        return new GoldenSignature(key[..dot], key[(dot + 1)..open], isInstance, list.Length == 0 ? [] : list.Split(','));
    }

    /// <summary>The same with every parameter passed by value.</summary>
    public GoldenSignature ByValue() => this with { Parameters = Parameters.Select(p => p.TrimEnd('&')).ToList() };

    /// <summary>The same without the last parameter.</summary>
    public GoldenSignature WithoutLast() => this with { Parameters = Parameters.Take(Parameters.Count - 1).ToList() };

    /// <summary>The recorded inputs of those of the first <paramref name="count"/> parameters passed by reference.</summary>
    public IEnumerable<double> ReferenceInputs(double[] inputs, int count)
    {
        var offset = InstanceSize;
        for (var p = 0; p < count; p++)
        {
            var size = SizeOf(Parameters[p]);
            if (Parameters[p].EndsWith('&'))
            {
                foreach (var number in inputs.Skip(offset).Take(size))
                {
                    yield return number;
                }
            }

            offset += size;
        }
    }

    /// <inheritdoc />
    public override string ToString() =>
        $"{Type}.{Method}({(IsInstance ? "this " : string.Empty)}{string.Join(",", Parameters)})";

    private static int SizeOf(string type) => Sizes.TryGetValue(type.TrimEnd('&'), out var size) ? size : 0;
}
