using Adamantium.Core.TypeParsing;

namespace Adamantium.Mathematics.TypeParsing;

public class ColorParser : ITypeParser<Color>
{
    public Color Parse(string value)
    {
        return Colors.Get(value);
    }
}