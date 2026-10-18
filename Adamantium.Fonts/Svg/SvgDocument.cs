using System.Collections.Generic;
using System.Xml.Linq;

namespace Adamantium.Fonts.Svg;

internal sealed class SvgDocument
{
    private readonly Dictionary<string, XElement> elements = new();

    public SvgDocument(XDocument document)
    {
        if (SvgStyleSheet.Parse(document) is { } sheet)
        {
            document.AddAnnotation(sheet);
        }

        foreach (var element in document.Descendants())
        {
            var id = (string)element.Attribute("id");
            if (id != null && !elements.ContainsKey(id))
            {
                elements[id] = element;
            }
        }
    }

    public XElement Find(string id) => id != null && elements.TryGetValue(id, out var element) ? element : null;

    public XElement FindReference(string reference)
    {
        if (reference == null)
        {
            return null;
        }

        reference = reference.Trim();
        if (reference.StartsWith("url(", System.StringComparison.Ordinal))
        {
            var close = reference.IndexOf(')');
            reference = close > 4 ? reference.Substring(4, close - 4).Trim().Trim('\'', '"') : reference;
        }

        return reference.StartsWith("#", System.StringComparison.Ordinal) ? Find(reference.Substring(1)) : null;
    }
}
