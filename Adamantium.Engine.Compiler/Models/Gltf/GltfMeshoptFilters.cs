using System;
using System.Buffers.Binary;
using System.Numerics;

namespace Adamantium.Engine.Compiler.Models.Gltf;

/// <summary>The filters of <c>KHR_meshopt_compression</c> (Appendix B), applied to the decoded elements in place:
/// octahedral unit vectors, quaternions without their largest component, floats as an exponent and a mantissa, and
/// colors as YCoCg - each as the appendix computes it, in 32-bit floats, rounding half away from zero.</summary>
internal static class GltfMeshoptFilters
{
    /// <summary>Whether <paramref name="filter"/> applies to elements of <paramref name="stride"/> bytes.</summary>
    public static bool Fits(string filter, int stride) => filter switch
    {
        "NONE" => true,
        "OCTAHEDRAL" or "COLOR" => stride is 4 or 8,
        "QUATERNION" => stride == 8,
        "EXPONENTIAL" => stride % 4 == 0,
        _ => false
    };

    /// <summary>Applies <paramref name="filter"/> to every element of <paramref name="output"/>.</summary>
    public static void Apply(string filter, Span<byte> output, int stride)
    {
        for (var at = 0; at + stride <= output.Length; at += stride)
        {
            var element = output.Slice(at, stride);
            switch (filter)
            {
                case "OCTAHEDRAL":
                    Octahedral(element, stride / 4);
                    break;
                case "QUATERNION":
                    Quaternion(element);
                    break;
                case "EXPONENTIAL":
                    Exponential(element);
                    break;
                case "COLOR":
                    Color(element, stride / 4);
                    break;
            }
        }
    }

    private static void Octahedral(Span<byte> element, int size)
    {
        var top = size == 1 ? sbyte.MaxValue : short.MaxValue;
        float one = Signed(element, 2, size);
        var x = Signed(element, 0, size) / one;
        var y = Signed(element, 1, size) / one;
        var z = 1f - MathF.Abs(x) - MathF.Abs(y);
        var t = MathF.Min(z, 0f);
        x -= MathF.CopySign(t, x);
        y -= MathF.CopySign(t, y);
        var length = MathF.Sqrt(x * x + y * y + z * z);
        Store(element, 0, size, Round(x / length * top));
        Store(element, 1, size, Round(y / length * top));
        Store(element, 2, size, Round(z / length * top));
    }

    private static void Quaternion(Span<byte> element)
    {
        var range = 1f / MathF.Sqrt(2f);
        var last = Signed(element, 3, 2);
        float one = last | 3;
        var x = Signed(element, 0, 2) / one * range;
        var y = Signed(element, 1, 2) / one * range;
        var z = Signed(element, 2, 2) / one * range;
        var w = MathF.Sqrt(MathF.Max(0f, 1f - x * x - y * y - z * z));
        var largest = last & 3;
        Store(element, (largest + 1) % 4, 2, Round(x * 32767f));
        Store(element, (largest + 2) % 4, 2, Round(y * 32767f));
        Store(element, (largest + 3) % 4, 2, Round(z * 32767f));
        Store(element, largest, 2, Round(w * 32767f));
    }

    private static void Exponential(Span<byte> element)
    {
        for (var at = 0; at < element.Length; at += 4)
        {
            var input = BinaryPrimitives.ReadInt32LittleEndian(element[at..]);
            var exponent = input >> 24;
            var mantissa = (input << 8) >> 8;
            BinaryPrimitives.WriteSingleLittleEndian(element[at..], MathF.ScaleB(mantissa, exponent));
        }
    }

    private static void Color(Span<byte> element, int size)
    {
        var top = size == 1 ? byte.MaxValue : ushort.MaxValue;
        int luma = Unsigned(element, 0, size), co = Signed(element, 1, size), cg = Signed(element, 2, size);
        var alpha = Unsigned(element, 3, size);
        var scale = (1 << (alpha == 0 ? 0 : BitOperations.Log2((uint)alpha) + 1)) - 1;
        var a = alpha & (scale >> 1);
        a = (a << 1) | (a & 1);
        var factor = top / (float)scale;
        Store(element, 0, size, Math.Clamp(Round((luma + co - cg) * factor), 0, top));
        Store(element, 1, size, Math.Clamp(Round((luma + cg) * factor), 0, top));
        Store(element, 2, size, Math.Clamp(Round((luma - co - cg) * factor), 0, top));
        Store(element, 3, size, Math.Clamp(Round(a * factor), 0, top));
    }

    private static int Signed(ReadOnlySpan<byte> element, int component, int size) => size == 1
        ? (sbyte)element[component]
        : BinaryPrimitives.ReadInt16LittleEndian(element[(component * 2)..]);

    private static int Unsigned(ReadOnlySpan<byte> element, int component, int size) => size == 1
        ? element[component]
        : BinaryPrimitives.ReadUInt16LittleEndian(element[(component * 2)..]);

    private static void Store(Span<byte> element, int component, int size, int value)
    {
        if (size == 1)
        {
            element[component] = (byte)value;
        }
        else
        {
            BinaryPrimitives.WriteUInt16LittleEndian(element[(component * 2)..], (ushort)value);
        }
    }

    private static int Round(float value) => (int)MathF.Round(value, MidpointRounding.AwayFromZero);
}
