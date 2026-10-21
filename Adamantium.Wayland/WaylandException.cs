namespace Adamantium.Wayland;

/// <summary>A failure of the connection to the compositor, or one it reported.</summary>
public sealed class WaylandException(string message) : Exception(message);
