namespace Adamantium.DBus;

/// <summary>An object path, told apart from a string the way the wire tells them apart.</summary>
public readonly record struct DBusObjectPath(string Value)
{
    public override string ToString() => Value;
}
