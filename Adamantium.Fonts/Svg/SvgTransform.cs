using System;
using Adamantium.Mathematics;
using Adamantium.Mathematics.Svg;

namespace Adamantium.Fonts.Svg;

internal static class SvgTransform
{
    public static Matrix3x2<double> Parse(string text)
    {
        var result = Matrix3x2<double>.Identity;
        if (string.IsNullOrWhiteSpace(text))
        {
            return result;
        }

        var at = 0;
        while (at < text.Length)
        {
            var open = text.IndexOf('(', at);
            var close = open < 0 ? -1 : text.IndexOf(')', open);
            if (close < 0)
            {
                break;
            }

            var name = text.Substring(at, open - at).Trim(' ', ',', '\t', '\r', '\n');
            var values = SvgPathData.ReadNumbers(text.Substring(open + 1, close - open - 1));
            result = Matrix3x2<double>.Multiply(Item(name, values), result);
            at = close + 1;
        }

        return result;
    }

    private static Matrix3x2<double> Item(string name, System.Collections.Generic.List<double> v)
    {
        switch (name)
        {
            case "matrix" when v.Count == 6:
                return new Matrix3x2<double>(v[0], v[1], v[2], v[3], v[4], v[5]);
            case "translate" when v.Count >= 1:
                return new Matrix3x2<double>(1, 0, 0, 1, v[0], v.Count > 1 ? v[1] : 0);
            case "scale" when v.Count >= 1:
                return new Matrix3x2<double>(v[0], 0, 0, v.Count > 1 ? v[1] : v[0], 0, 0);
            case "rotate" when v.Count >= 1:
                var angle = v[0] * Math.PI / 180;
                var rotation = new Matrix3x2<double>(Math.Cos(angle), Math.Sin(angle), -Math.Sin(angle), Math.Cos(angle), 0, 0);
                if (v.Count < 3)
                {
                    return rotation;
                }

                return Matrix3x2<double>.Multiply(Matrix3x2<double>.Multiply(new Matrix3x2<double>(1, 0, 0, 1, -v[1], -v[2]), rotation),
                    new Matrix3x2<double>(1, 0, 0, 1, v[1], v[2]));
            case "skewX" when v.Count == 1:
                return new Matrix3x2<double>(1, 0, Math.Tan(v[0] * Math.PI / 180), 1, 0, 0);
            case "skewY" when v.Count == 1:
                return new Matrix3x2<double>(1, Math.Tan(v[0] * Math.PI / 180), 0, 1, 0, 0);
            default:
                return Matrix3x2<double>.Identity;
        }
    }
}
