using System;
using System.IO;

namespace Adamantium.Engine.Compiler.Models.Gltf;

/// <summary>One sampler of a glTF animation: its key times and values, read at any time between them as it says -
/// held (STEP), blended (LINEAR; a rotation along the sphere) or along a cubic Hermite spline (CUBICSPLINE).</summary>
public sealed class GltfCurve
{
    public const string Linear = "LINEAR";
    public const string Step = "STEP";
    public const string CubicSpline = "CUBICSPLINE";

    private readonly float[] values;
    private readonly int width;

    private GltfCurve(float[] times, float[] values, int width, string interpolation)
    {
        Times = times;
        this.values = values;
        this.width = width;
        Interpolation = interpolation;
    }

    /// <summary>The key times, in seconds.</summary>
    public float[] Times { get; }

    /// <summary>How the values go from key to key.</summary>
    public string Interpolation { get; }

    /// <summary>Reads a sampler whose values have <paramref name="width"/> components each: 3 for a translation or a
    /// scale, 4 for a rotation.</summary>
    public static GltfCurve Read(GltfFile file, GltfAnimationSampler sampler, int width)
    {
        var times = GltfAccessors.ReadFloats(file, sampler.Input, out _);
        var values = GltfAccessors.ReadFloats(file, sampler.Output, out var components);
        if (components != width)
        {
            throw new InvalidDataException($"{file.Path}: an animation's values have {components} components, {width} are wanted");
        }

        var interpolation = sampler.Interpolation is Step or CubicSpline ? sampler.Interpolation : Linear;
        var perKey = interpolation == CubicSpline ? 3 * width : width;
        if (values.Length < times.Length * perKey)
        {
            throw new InvalidDataException($"{file.Path}: an animation has {times.Length} keys and values for fewer");
        }

        return new GltfCurve(times, values, width, interpolation);
    }

    /// <summary>The value at <paramref name="time"/>: the first key's before it, the last's after it.</summary>
    public float[] At(float time)
    {
        var result = new float[width];
        if (time <= Times[0] || Times.Length == 1)
        {
            Copy(0, result);
            return result;
        }

        if (time >= Times[^1])
        {
            Copy(Times.Length - 1, result);
            return result;
        }

        var k = Array.BinarySearch(Times, time);
        if (k >= 0)
        {
            Copy(k, result);
            return result;
        }

        k = ~k - 1;
        var span = Times[k + 1] - Times[k];
        var t = span > 0 ? (time - Times[k]) / span : 0;
        switch (Interpolation)
        {
            case Step:
                Copy(k, result);
                break;
            case CubicSpline:
                Hermite(k, t, span, result);
                break;
            default:
                if (width == 4) Slerp(k, t, result);
                else Blend(k, t, result);
                break;
        }

        if (width == 4) Normalize(result);
        return result;
    }

    private int ValueAt(int key) => Interpolation == CubicSpline ? key * 3 * width + width : key * width;

    private void Copy(int key, float[] into) => Array.Copy(values, ValueAt(key), into, 0, width);

    private void Blend(int key, float t, float[] into)
    {
        int a = ValueAt(key), b = ValueAt(key + 1);
        for (int c = 0; c < width; c++) into[c] = values[a + c] + (values[b + c] - values[a + c]) * t;
    }

    private void Slerp(int key, float t, float[] into)
    {
        int a = ValueAt(key), b = ValueAt(key + 1);
        var dot = 0f;
        for (int c = 0; c < 4; c++) dot += values[a + c] * values[b + c];
        var sign = dot < 0 ? -1 : 1;
        dot = MathF.Abs(dot);
        float wa, wb;
        if (dot > 0.9995f)
        {
            wa = 1 - t;
            wb = t;
        }
        else
        {
            var angle = MathF.Acos(dot);
            var sin = MathF.Sin(angle);
            wa = MathF.Sin((1 - t) * angle) / sin;
            wb = MathF.Sin(t * angle) / sin;
        }

        for (int c = 0; c < 4; c++) into[c] = values[a + c] * wa + values[b + c] * wb * sign;
    }

    private void Hermite(int key, float t, float span, float[] into)
    {
        var t2 = t * t;
        var t3 = t2 * t;
        var h00 = 2 * t3 - 3 * t2 + 1;
        var h10 = t3 - 2 * t2 + t;
        var h01 = -2 * t3 + 3 * t2;
        var h11 = t3 - t2;
        var from = key * 3 * width;
        var to = (key + 1) * 3 * width;
        for (int c = 0; c < width; c++)
        {
            var p0 = values[from + width + c];
            var outTangent = values[from + 2 * width + c];
            var p1 = values[to + width + c];
            var inTangent = values[to + c];
            into[c] = h00 * p0 + h10 * span * outTangent + h01 * p1 + h11 * span * inTangent;
        }
    }

    private static void Normalize(float[] quaternion)
    {
        var length = MathF.Sqrt(quaternion[0] * quaternion[0] + quaternion[1] * quaternion[1] + quaternion[2] * quaternion[2]
                                + quaternion[3] * quaternion[3]);
        if (length < 1e-12f)
        {
            quaternion[3] = 1;
            return;
        }

        for (int c = 0; c < 4; c++) quaternion[c] /= length;
    }
}
