using System.IO;

namespace Adamantium.Imaging;

/// <summary>Reads big-endian fields of a part of a byte array, as JBIG2 and JPEG 2000 store them; reading past the
/// end of the part is an error of the data.</summary>
internal sealed class BigEndianReader
{
    public BigEndianReader(byte[] data, int start, int end)
    {
        Data = data;
        Position = start;
        End = end;
    }

    public byte[] Data { get; }

    public int Position { get; set; }

    public int End { get; }

    public int Remaining => End - Position;

    public byte ReadByte()
    {
        Need(1);
        return Data[Position++];
    }

    public sbyte ReadSByte() => (sbyte)ReadByte();

    public ushort ReadUInt16()
    {
        Need(2);
        var value = (ushort)((Data[Position] << 8) | Data[Position + 1]);
        Position += 2;
        return value;
    }

    public uint ReadUInt32()
    {
        Need(4);
        var value = ((uint)Data[Position] << 24) | ((uint)Data[Position + 1] << 16) | ((uint)Data[Position + 2] << 8) |
                    Data[Position + 3];
        Position += 4;
        return value;
    }

    public ulong ReadUInt64() => ((ulong)ReadUInt32() << 32) | ReadUInt32();

    public int ReadInt32() => (int)ReadUInt32();

    public void Skip(int count)
    {
        Need(count);
        Position += count;
    }

    private void Need(int count)
    {
        if (count < 0 || Position > End - count)
        {
            throw new InvalidDataException("Image data ends early.");
        }
    }
}
