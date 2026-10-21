using System.Buffers.Binary;
using System.Text;

namespace Adamantium.DBus;

/// <summary>Reads values in the D-Bus wire format, little-endian, each aligned from the start of what is read.</summary>
public sealed class DBusReader
{
    private readonly byte[] data;
    private readonly int start;
    private readonly int end;

    public DBusReader(byte[] data) : this(data, 0, data.Length)
    {
    }

    public DBusReader(byte[] data, int start, int length)
    {
        this.data = data;
        this.start = start;
        end = start + length;
        Position = start;
    }

    public int Position { get; private set; }

    public bool AtEnd => Position >= end;

    public void Align(int alignment)
    {
        var offset = Position - start;
        Position = start + (offset + alignment - 1) / alignment * alignment;
    }

    public byte ReadByte() => data[Position++];

    public bool ReadBoolean() => ReadUInt32() != 0;

    public short ReadInt16() => (short)ReadUInt16();

    public ushort ReadUInt16()
    {
        Align(2);
        var value = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(Position));
        Position += 2;
        return value;
    }

    public int ReadInt32() => (int)ReadUInt32();

    public uint ReadUInt32()
    {
        Align(4);
        var value = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(Position));
        Position += 4;
        return value;
    }

    public long ReadInt64() => (long)ReadUInt64();

    public ulong ReadUInt64()
    {
        Align(8);
        var value = BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(Position));
        Position += 8;
        return value;
    }

    public double ReadDouble() => BitConverter.UInt64BitsToDouble(ReadUInt64());

    public string ReadString()
    {
        var length = (int)ReadUInt32();
        var value = Encoding.UTF8.GetString(data, Position, length);
        Position += length + 1;
        return value;
    }

    public DBusObjectPath ReadObjectPath() => new(ReadString());

    public string ReadSignature()
    {
        var length = data[Position++];
        var value = Encoding.ASCII.GetString(data, Position, length);
        Position += length + 1;
        return value;
    }

    /// <summary>Starts an array; its elements run up to the position returned.</summary>
    public int ReadArrayEnd(int elementAlignment)
    {
        var length = (int)ReadUInt32();
        Align(elementAlignment);
        return Position + length;
    }

    public DBusVariant ReadVariant()
    {
        var signature = ReadSignature();
        return new DBusVariant(signature, ReadValue(signature));
    }

    /// <summary>Any single complete type: arrays as lists, dictionaries as dictionaries, structs as object arrays.</summary>
    public object ReadValue(string signature)
    {
        var index = 0;
        return ReadValue(signature, ref index);
    }

    private object ReadValue(string signature, ref int index)
    {
        var code = signature[index++];
        switch (code)
        {
            case 'y':
                return ReadByte();
            case 'b':
                return ReadBoolean();
            case 'n':
                return ReadInt16();
            case 'q':
                return ReadUInt16();
            case 'i':
                return ReadInt32();
            case 'u':
                return ReadUInt32();
            case 'x':
                return ReadInt64();
            case 't':
                return ReadUInt64();
            case 'd':
                return ReadDouble();
            case 'h':
                return ReadUInt32();
            case 's':
                return ReadString();
            case 'o':
                return ReadObjectPath();
            case 'g':
                return ReadSignature();
            case 'v':
                return ReadVariant();
            case '(':
            {
                Align(8);
                List<object> fields = [];
                while (signature[index] != ')')
                {
                    fields.Add(ReadValue(signature, ref index));
                }

                index++;
                return fields.ToArray();
            }
            case 'a' when signature[index] == '{':
            {
                var keyStart = index + 1;
                var valueStart = DBusSignature.End(signature, keyStart);
                var valueEnd = DBusSignature.End(signature, valueStart);
                var arrayEnd = ReadArrayEnd(8);
                Dictionary<object, object> entries = [];
                while (Position < arrayEnd)
                {
                    Align(8);
                    var keyIndex = keyStart;
                    var key = ReadValue(signature, ref keyIndex);
                    var valueIndex = valueStart;
                    entries[key] = ReadValue(signature, ref valueIndex);
                }

                index = valueEnd + 1;
                return entries;
            }
            case 'a':
            {
                var elementStart = index;
                var elementEnd = DBusSignature.End(signature, elementStart);
                var arrayEnd = ReadArrayEnd(DBusSignature.Alignment(signature[elementStart]));
                List<object> elements = [];
                while (Position < arrayEnd)
                {
                    var element = elementStart;
                    elements.Add(ReadValue(signature, ref element));
                }

                index = elementEnd;
                return elements;
            }
            default:
                throw new NotSupportedException($"The type {code} is not read.");
        }
    }
}
