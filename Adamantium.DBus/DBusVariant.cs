namespace Adamantium.DBus;

/// <summary>A value with its signature: what a variant carries on the wire.</summary>
public readonly record struct DBusVariant(string Signature, object Value)
{
    public static DBusVariant String(string value) => new("s", value);

    public static DBusVariant ObjectPath(string value) => new("o", new DBusObjectPath(value));

    public static DBusVariant Boolean(bool value) => new("b", value);

    public static DBusVariant UInt32(uint value) => new("u", value);

    public static DBusVariant Int32(int value) => new("i", value);

    public static DBusVariant UInt64(ulong value) => new("t", value);

    public static DBusVariant Double(double value) => new("d", value);
}
