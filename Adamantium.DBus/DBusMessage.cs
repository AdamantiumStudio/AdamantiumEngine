using System.Buffers.Binary;

namespace Adamantium.DBus;

/// <summary>A D-Bus message: a call, a reply, an error or a signal, with its header fields and body.</summary>
public sealed class DBusMessage
{
    /// <summary>The first byte of a little-endian message, the only kind read and written here.</summary>
    public const byte LittleEndian = (byte)'l';

    private const byte ProtocolVersion = 1;
    private const byte NoReplyExpectedFlag = 1;

    private const byte PathField = 1;
    private const byte InterfaceField = 2;
    private const byte MemberField = 3;
    private const byte ErrorNameField = 4;
    private const byte ReplySerialField = 5;
    private const byte DestinationField = 6;
    private const byte SenderField = 7;
    private const byte SignatureField = 8;

    /// <summary>The bytes before the header fields: endianness, type, flags, version, body length, serial, field length.</summary>
    public const int FixedHeaderLength = 16;

    public DBusMessageType Type { get; init; }

    public bool NoReplyExpected { get; init; }

    public uint Serial { get; private init; }

    public uint ReplySerial { get; init; }

    public string Path { get; init; }

    public string Interface { get; init; }

    public string Member { get; init; }

    public string ErrorName { get; init; }

    public string Destination { get; init; }

    public string Sender { get; init; }

    public string Signature { get; init; } = string.Empty;

    public byte[] Body { get; init; } = [];

    public static DBusMessage MethodCall(string destination, string path, string interfaceName, string member,
        string signature = "", Action<DBusWriter> body = null)
    {
        var writer = new DBusWriter();
        body?.Invoke(writer);
        return new DBusMessage
        {
            Type = DBusMessageType.MethodCall,
            Destination = destination,
            Path = path,
            Interface = interfaceName,
            Member = member,
            Signature = signature,
            Body = writer.ToArray(),
        };
    }

    /// <summary>The error a call is answered with.</summary>
    public static DBusMessage Error(DBusMessage call, string name, string text)
    {
        var writer = new DBusWriter();
        writer.WriteString(text);
        return new DBusMessage
        {
            Type = DBusMessageType.Error,
            NoReplyExpected = true,
            ReplySerial = call.Serial,
            Destination = call.Sender,
            ErrorName = name,
            Signature = "s",
            Body = writer.ToArray(),
        };
    }

    public DBusReader Reader() => new(Body);

    /// <summary>The whole length of a message whose fixed header is <paramref name="header"/>.</summary>
    public static int LengthOf(ReadOnlySpan<byte> header)
    {
        var body = BinaryPrimitives.ReadInt32LittleEndian(header[4..]);
        var fields = BinaryPrimitives.ReadInt32LittleEndian(header[12..]);
        return (FixedHeaderLength + fields + 7) / 8 * 8 + body;
    }

    public byte[] Serialize(uint serial)
    {
        var writer = new DBusWriter();
        writer.WriteByte(LittleEndian);
        writer.WriteByte((byte)Type);
        writer.WriteByte(NoReplyExpected ? NoReplyExpectedFlag : (byte)0);
        writer.WriteByte(ProtocolVersion);
        writer.WriteUInt32((uint)Body.Length);
        writer.WriteUInt32(serial);
        writer.WriteArray(8, fields =>
        {
            Field(fields, PathField, Path == null ? null : DBusVariant.ObjectPath(Path));
            Field(fields, InterfaceField, Interface == null ? null : DBusVariant.String(Interface));
            Field(fields, MemberField, Member == null ? null : DBusVariant.String(Member));
            Field(fields, ErrorNameField, ErrorName == null ? null : DBusVariant.String(ErrorName));
            Field(fields, ReplySerialField, ReplySerial == 0 ? null : DBusVariant.UInt32(ReplySerial));
            Field(fields, DestinationField, Destination == null ? null : DBusVariant.String(Destination));
            Field(fields, SignatureField, string.IsNullOrEmpty(Signature) ? null : new DBusVariant("g", Signature));
        });
        writer.Align(8);
        var header = writer.ToArray();
        var message = new byte[header.Length + Body.Length];
        header.CopyTo(message, 0);
        Body.CopyTo(message, header.Length);
        return message;
    }

    public static DBusMessage Parse(byte[] message)
    {
        if (message[0] != LittleEndian)
        {
            throw new DBusException("Adamantium.DBus.BigEndian", "a big-endian message is not read");
        }

        var bodyLength = BinaryPrimitives.ReadInt32LittleEndian(message.AsSpan(4));
        var reader = new DBusReader(message);
        reader.ReadUInt32();
        reader.ReadUInt32();
        var serial = reader.ReadUInt32();
        Dictionary<byte, object> fields = [];
        var end = reader.ReadArrayEnd(8);
        while (reader.Position < end)
        {
            reader.Align(8);
            var code = reader.ReadByte();
            fields[code] = reader.ReadVariant().Value;
        }

        reader.Align(8);
        return new DBusMessage
        {
            Type = (DBusMessageType)message[1],
            NoReplyExpected = (message[2] & NoReplyExpectedFlag) != 0,
            Serial = serial,
            ReplySerial = fields.TryGetValue(ReplySerialField, out var reply) ? (uint)reply : 0,
            Path = fields.TryGetValue(PathField, out var path) ? ((DBusObjectPath)path).Value : null,
            Interface = fields.GetValueOrDefault(InterfaceField) as string,
            Member = fields.GetValueOrDefault(MemberField) as string,
            ErrorName = fields.GetValueOrDefault(ErrorNameField) as string,
            Destination = fields.GetValueOrDefault(DestinationField) as string,
            Sender = fields.GetValueOrDefault(SenderField) as string,
            Signature = fields.GetValueOrDefault(SignatureField) as string ?? string.Empty,
            Body = message.AsSpan(reader.Position, bodyLength).ToArray(),
        };
    }

    private static void Field(DBusWriter writer, byte code, DBusVariant? value)
    {
        if (value is not { } variant)
        {
            return;
        }

        writer.WriteStruct(field =>
        {
            field.WriteByte(code);
            field.WriteVariant(variant);
        });
    }
}
