using System;
using System.Globalization;
using Adamantium.Core.TypeParsing;

namespace Adamantium.Mathematics.TypeParsing;

public class Vector2Parser : ITypeParser<Vector2>
{
    public Vector2 Parse(string value)
    {
        var values = value.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries);
        if (values.Length < 2)
            throw new ArgumentException($"{value} must contain 2 components (\"x y\" or \"x,y\"), but has {values.Length}");

        return new Vector2(
            double.Parse(values[0], CultureInfo.InvariantCulture),
            double.Parse(values[1], CultureInfo.InvariantCulture));
    }
}
