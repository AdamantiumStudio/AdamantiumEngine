namespace Adamantium.BindingGenerator.Wayland;

public sealed record WaylandEnum(string Name, bool IsBitfield, string Summary, IReadOnlyList<EnumEntry> Entries);
