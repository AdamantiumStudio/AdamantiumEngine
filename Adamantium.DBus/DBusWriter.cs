using System.Buffers.Binary;
using System.Text;

namespace Adamantium.DBus;

/// <summary>Writes values in the D-Bus wire format, little-endian, each aligned from the start of the message.</summary>
public sealed class DBusWriter
{
    private byte[] buffer = new byte[256];

    public int Length { get; private set; }

    public void Align(int alignment)
    {
        var padded = (Length + alignment - 1) / alignment * alignment;
        Reserve(padded - Length);
        Length = padded;
    }

    public void WriteByte(byte value)
    {
        Reserve(1);
        buffer[Length++] = value;
    }

    public void WriteBoolean(bool value) => WriteUInt32(value ? 1u : 0u);

    public void WriteInt16(short value) => WriteUInt16((ushort)value);

    public void WriteUInt16(ushort value)
    {
        Align(2);
        Reserve(2);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(Length), value);
        Length += 2;
    }

    public void WriteInt32(int value) => WriteUInt32((uint)value);

    public void WriteUInt32(uint value)
    {
        Align(4);
        Reserve(4);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(Length), value);
        Length += 4;
    }

    public void WriteInt64(long value) => WriteUInt64((ulong)value);

    public void WriteUInt64(ulong value)
    {
        Align(8);
        Reserve(8);
        BinaryPrimitives.WriteUInt64LittleEndian(buffer.AsSpan(Length), value);
        Length += 8;
    }

    public void WriteDouble(double value) => WriteUInt64(BitConverter.DoubleToUInt64Bits(value));

    /// <summary>Writes <paramref name="value"/> as D-Bus allows a string: a NUL, which would end it early and get the
    /// connection dropped, is written as U+FFFD - as an unpaired surrogate is.</summary>
    public void WriteString(string value)
    {
        if (value.Contains('\0'))
        {
            value = value.Replace('\0', '�');
        }

        var count = Encoding.UTF8.GetByteCount(value);
        WriteUInt32((uint)count);
        Reserve(count + 1);
        Encoding.UTF8.GetBytes(value, buffer.AsSpan(Length));
        Length += count;
        buffer[Length++] = 0;
    }

    public void WriteObjectPath(string value) => WriteString(value);

    public void WriteSignature(string value)
    {
        Reserve(value.Length + 2);
        buffer[Length++] = (byte)value.Length;
        Encoding.ASCII.GetBytes(value, buffer.AsSpan(Length));
        Length += value.Length;
        buffer[Length++] = 0;
    }

    /// <summary>An array: its length is written once <paramref name="elements"/> has written them.</summary>
    public void WriteArray(int elementAlignment, Action<DBusWriter> elements)
    {
        WriteUInt32(0);
        var lengthAt = Length - 4;
        Align(elementAlignment);
        var start = Length;
        elements(this);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(lengthAt), (uint)(Length - start));
    }

    public void WriteStruct(Action<DBusWriter> fields)
    {
        Align(8);
        fields(this);
    }

    public void WriteVariant(DBusVariant variant)
    {
        WriteSignature(variant.Signature);
        WriteValue(variant.Signature, variant.Value);
    }

    /// <summary>An a{sv}: the options and properties dictionary most D-Bus APIs take.</summary>
    public void WriteDictionary(IReadOnlyDictionary<string, DBusVariant> entries)
    {
        WriteArray(8, writer =>
        {
            foreach (var (key, value) in entries)
            {
                writer.WriteStruct(entry =>
                {
                    entry.WriteString(key);
                    entry.WriteVariant(value);
                });
            }
        });
    }

    public byte[] ToArray() => buffer.AsSpan(0, Length).ToArray();

    /// <summary>Any single complete type, from what <see cref="DBusReader.ReadValue"/> gives: lists or sequences for
    /// arrays, dictionaries for dictionaries, object arrays for structs.</summary>
    public void WriteValue(string signature, object value)
    {
        var index = 0;
        WriteValue(signature, ref index, value);
    }

    private void WriteValue(string signature, ref int index, object value)
    {
        var code = signature[index++];
        switch (code)
        {
            case 'y':
                WriteByte(Convert.ToByte(value));
                break;
            case 'b':
                WriteBoolean((bool)value);
                break;
            case 'n':
                WriteInt16(Convert.ToInt16(value));
                break;
            case 'q':
                WriteUInt16(Convert.ToUInt16(value));
                break;
            case 'i':
                WriteInt32(Convert.ToInt32(value));
                break;
            case 'u' or 'h':
                WriteUInt32(Convert.ToUInt32(value));
                break;
            case 'x':
                WriteInt64(Convert.ToInt64(value));
                break;
            case 't':
                WriteUInt64(Convert.ToUInt64(value));
                break;
            case 'd':
                WriteDouble(Convert.ToDouble(value));
                break;
            case 's':
                WriteString((string)value);
                break;
            case 'o':
                WriteObjectPath(value is DBusObjectPath path ? path.Value : (string)value);
                break;
            case 'g':
                WriteSignature((string)value);
                break;
            case 'v':
                WriteVariant((DBusVariant)value);
                break;
            case '(':
            {
                Align(8);
                var fields = (object[])value;
                var field = 0;
                while (signature[index] != ')')
                {
                    WriteValue(signature, ref index, fields[field++]);
                }

                index++;
                break;
            }
            case 'a' when signature[index] == '{':
            {
                var keyStart = index + 1;
                var valueStart = DBusSignature.End(signature, keyStart);
                var valueEnd = DBusSignature.End(signature, valueStart);
                WriteArray(8, writer =>
                {
                    foreach (System.Collections.DictionaryEntry entry in (System.Collections.IDictionary)value)
                    {
                        writer.Align(8);
                        var key = keyStart;
                        writer.WriteValue(signature, ref key, entry.Key);
                        var item = valueStart;
                        writer.WriteValue(signature, ref item, entry.Value);
                    }
                });
                index = valueEnd + 1;
                break;
            }
            case 'a':
            {
                var elementStart = index;
                WriteArray(DBusSignature.Alignment(signature[elementStart]), writer =>
                {
                    foreach (var element in (System.Collections.IEnumerable)value)
                    {
                        var at = elementStart;
                        writer.WriteValue(signature, ref at, element);
                    }
                });
                index = DBusSignature.End(signature, elementStart);
                break;
            }
            default:
                throw new NotSupportedException($"The type {code} is not written.");
        }
    }

    private void Reserve(int count)
    {
        if (Length + count > buffer.Length)
        {
            Array.Resize(ref buffer, Math.Max(buffer.Length * 2, Length + count));
        }
    }
}
