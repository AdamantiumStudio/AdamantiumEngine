namespace Adamantium.DBus;

/// <summary>An error a peer answered a call with, or a failure of the connection.</summary>
public sealed class DBusException(string name, string message) : Exception($"{name}: {message}")
{
    /// <summary>The D-Bus error name, as org.freedesktop.DBus.Error.ServiceUnknown.</summary>
    public string Name { get; } = name;
}
