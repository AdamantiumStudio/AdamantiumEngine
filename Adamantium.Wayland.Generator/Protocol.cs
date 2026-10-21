namespace Adamantium.Wayland.Generator;

/// <summary>A protocol as its XML describes it: what wayland-scanner reads, read here straight into C#.</summary>
public sealed record Protocol(string Name, string FileName, string Copyright, IReadOnlyList<Interface> Interfaces);
