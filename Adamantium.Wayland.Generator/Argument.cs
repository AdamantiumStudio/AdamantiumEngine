namespace Adamantium.Wayland.Generator;

public sealed record Argument(string Name, ArgumentType Type, string Interface, bool AllowNull, string Enum,
    string Summary);
