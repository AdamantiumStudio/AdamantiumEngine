namespace Adamantium.Wayland.Generator;

public sealed record Interface(string Name, int Version, string Summary, IReadOnlyList<Message> Requests,
    IReadOnlyList<Message> Events, IReadOnlyList<WaylandEnum> Enums);
