using System.Globalization;
using System.Xml.Linq;

namespace Adamantium.Wayland.Generator;

/// <summary>A protocol as its XML describes it: what wayland-scanner reads, read here straight into C#.</summary>
public sealed record Protocol(string Name, string FileName, string Copyright, IReadOnlyList<Interface> Interfaces);

public sealed record Interface(string Name, int Version, string? Summary, IReadOnlyList<Message> Requests,
    IReadOnlyList<Message> Events, IReadOnlyList<WaylandEnum> Enums);

public sealed record Message(string Name, bool IsDestructor, int Since, string? Summary, IReadOnlyList<Argument> Arguments)
{
    /// <summary>The untyped new_id of wl_registry.bind: the caller names the interface, which goes on the wire as a
    /// string and a version ahead of the id.</summary>
    public bool HasUntypedNewId => Arguments.Any(a => a.Type == ArgumentType.NewId && a.Interface == null);

    public Argument? NewId => Arguments.FirstOrDefault(a => a.Type == ArgumentType.NewId);
}

public enum ArgumentType
{
    Int,
    Uint,
    Fixed,
    String,
    Object,
    NewId,
    Array,
    Fd,
}

public sealed record Argument(string Name, ArgumentType Type, string? Interface, bool AllowNull, string? Enum,
    string? Summary);

public sealed record WaylandEnum(string Name, bool IsBitfield, string? Summary, IReadOnlyList<EnumEntry> Entries);

public sealed record EnumEntry(string Name, uint Value, string? Summary);

public static class ProtocolReader
{
    public static Protocol Read(string path)
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
        var since = (string?)e.Attribute("since");
        return new Message(
            Required(e, "name"),
            (string?)e.Attribute("type") == "destructor",
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
            (string?)e.Attribute("interface"),
            (string?)e.Attribute("allow-null") == "true",
            (string?)e.Attribute("enum"),
            (string?)e.Attribute("summary"));
    }

    private static WaylandEnum ReadEnum(XElement e)
    {
        return new WaylandEnum(
            Required(e, "name"),
            (string?)e.Attribute("bitfield") == "true",
            Summary(e),
            e.Elements("entry").Select(x => new EnumEntry(
                Required(x, "name"),
                ParseValue(Required(x, "value")),
                (string?)x.Attribute("summary") ?? Summary(x))).ToList());
    }

    private static uint ParseValue(string value)
    {
        return value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? uint.Parse(value.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture)
            : uint.Parse(value, CultureInfo.InvariantCulture);
    }

    private static string? Summary(XElement e)
    {
        return (string?)e.Element("description")?.Attribute("summary");
    }

    private static string Required(XElement e, string attribute)
    {
        return (string?)e.Attribute(attribute)
               ?? throw new InvalidDataException($"<{e.Name}> has no '{attribute}' attribute");
    }
}
