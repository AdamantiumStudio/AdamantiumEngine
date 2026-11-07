using System.Globalization;
using System.Xml.Linq;
using QuantumBinding.Generator;
using QuantumBinding.Generator.AST;
using QuantumBinding.Generator.Parser;

namespace Adamantium.BindingGenerator.Wayland.Parser;

/// <summary>Reads the Wayland protocol XML files - what wayland-scanner reads - into <see cref="Protocols"/>. The
/// protocols are messages over a wire rather than C declarations, so the translation units stay empty and the code
/// generation passes write from the protocols.</summary>
public sealed class WaylandXmlParser : IMetadataProvider
{
    private readonly List<Protocol> protocols = [];

    /// <summary>The protocols read so far, in the order the module lists their files.</summary>
    public IReadOnlyList<Protocol> Protocols => protocols;

    public ParseResult Parse(TranslationUnit unit, string file)
    {
        protocols.Add(Read(file));
        return ParseResult.Success;
    }

    public void Dispose()
    {
    }

    private static Protocol Read(string path)
    {
        var root = XDocument.Load(path).Root ?? throw new InvalidDataException($"{path}: no root element");
        var name = Required(root, "name");
        var copyright = root.Element("copyright")?.Value ?? string.Empty;
        var interfaces = root.Elements("interface").Select(ReadInterface).ToList();
        return new Protocol(name, Path.GetFileName(path), copyright, interfaces);
    }

    private static Interface ReadInterface(XElement e)
    {
        return new Interface(
            Required(e, "name"),
            int.Parse(Required(e, "version"), CultureInfo.InvariantCulture),
            Summary(e),
            e.Elements("request").Select(ReadMessage).ToList(),
            e.Elements("event").Select(ReadMessage).ToList(),
            e.Elements("enum").Select(ReadEnum).ToList());
    }

    private static Message ReadMessage(XElement e)
    {
        var since = (string)e.Attribute("since");
        return new Message(
            Required(e, "name"),
            (string)e.Attribute("type") == "destructor",
            since == null ? 1 : int.Parse(since, CultureInfo.InvariantCulture),
            Summary(e),
            e.Elements("arg").Select(ReadArgument).ToList());
    }

    private static Argument ReadArgument(XElement e)
    {
        var type = Required(e, "type") switch
        {
            "int" => ArgumentType.Int,
            "uint" => ArgumentType.Uint,
            "fixed" => ArgumentType.Fixed,
            "string" => ArgumentType.String,
            "object" => ArgumentType.Object,
            "new_id" => ArgumentType.NewId,
            "array" => ArgumentType.Array,
            "fd" => ArgumentType.Fd,
            var other => throw new InvalidDataException($"Unknown argument type '{other}'"),
        };

        return new Argument(
            Required(e, "name"),
            type,
            (string)e.Attribute("interface"),
            (string)e.Attribute("allow-null") == "true",
            (string)e.Attribute("enum"),
            (string)e.Attribute("summary"));
    }

    private static WaylandEnum ReadEnum(XElement e)
    {
        return new WaylandEnum(
            Required(e, "name"),
            (string)e.Attribute("bitfield") == "true",
            Summary(e),
            e.Elements("entry").Select(x => new EnumEntry(
                Required(x, "name"),
                ParseValue(Required(x, "value")),
                (string)x.Attribute("summary") ?? Summary(x))).ToList());
    }

    private static uint ParseValue(string value)
    {
        return value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? uint.Parse(value.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture)
            : uint.Parse(value, CultureInfo.InvariantCulture);
    }

    private static string Summary(XElement e)
    {
        return (string)e.Element("description")?.Attribute("summary");
    }

    private static string Required(XElement e, string attribute)
    {
        return (string)e.Attribute(attribute)
               ?? throw new InvalidDataException($"<{e.Name}> has no '{attribute}' attribute");
    }
}
