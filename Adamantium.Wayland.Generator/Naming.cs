using System.Text;

namespace Adamantium.Wayland.Generator;

/// <summary>The protocols' snake_case names, as C# writes them.</summary>
public static class Naming
{
    private static readonly HashSet<string> Keywords =
    [
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class", "const",
        "continue", "decimal", "default", "delegate", "do", "double", "else", "enum", "event", "explicit", "extern",
        "false", "finally", "fixed", "float", "for", "foreach", "goto", "if", "implicit", "in", "int", "interface",
        "internal", "is", "lock", "long", "namespace", "new", "null", "object", "operator", "out", "override",
        "params", "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short",
        "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true", "try", "typeof",
        "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual", "void", "volatile", "while",
    ];

    /// <summary>xdg_toplevel → XdgToplevel, set_min_size → SetMinSize. A name that would start with a digit takes
    /// <paramref name="prefix"/> in front: wl_output.transform's 90 → Transform90.</summary>
    public static string Pascal(string name, string? prefix = null)
    {
        var builder = new StringBuilder(name.Length);
        foreach (var part in name.Split('_', StringSplitOptions.RemoveEmptyEntries))
        {
            builder.Append(char.ToUpperInvariant(part[0]));
            builder.Append(part, 1, part.Length - 1);
        }

        if (builder.Length == 0 || char.IsDigit(builder[0]))
        {
            builder.Insert(0, prefix ?? "_");
        }

        return builder.ToString();
    }

    /// <summary>An argument's name: camelCase, with @ in front of a C# keyword (wl_registry.global's interface).</summary>
    public static string Camel(string name)
    {
        var pascal = Pascal(name);
        var camel = char.ToLowerInvariant(pascal[0]) + pascal[1..];
        return Keywords.Contains(camel) ? "@" + camel : camel;
    }

    /// <summary>A local derived from an argument's name, which needs no @ of its own.</summary>
    public static string Local(string argument, string suffix)
    {
        return argument.TrimStart('@') + suffix;
    }

    public static string FileName(string protocolFile)
    {
        return Pascal(Path.GetFileNameWithoutExtension(protocolFile).Replace('-', '_')) + ".cs";
    }
}
