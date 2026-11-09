using System;
using System.IO;

namespace Adamantium.Imaging.Jbig2;

/// <summary>A segment header of JBIG2 (ITU-T T.88, 7.2) and where its data lies.</summary>
internal sealed class Jbig2Segment
{
    private const uint UnknownLength = 0xFFFFFFFF;

    public uint Number { get; private init; }

    public int Type { get; private init; }

    public uint[] ReferredTo { get; private init; }

    /// <summary>The length of the data the header gives, 0xFFFFFFFF when it does not know.</summary>
    public uint DataLength { get; private init; }

    public int DataStart { get; private init; }

    public int DataEnd { get; private init; }

    /// <summary>Reads the header at the reader's place and moves past the segment's data that follows it.</summary>
    public static Jbig2Segment Read(BigEndianReader bytes)
    {
        var header = ReadHeader(bytes);
        var segment = header.DataLength == UnknownLength
            ? header.With(bytes.Position, UnknownEnd(bytes, header.Type))
            : header.At(bytes.Position, bytes.End);
        bytes.Position = segment.DataEnd;
        return segment;
    }

    /// <summary>Reads only the header, as the random-access organization lists them before all data.</summary>
    public static Jbig2Segment ReadHeader(BigEndianReader bytes)
    {
        var number = bytes.ReadUInt32();
        var flags = bytes.ReadByte();
        var type = flags & 0x3F;
        var longPage = (flags & 0x40) != 0;
        var first = bytes.ReadByte();
        long count = first >> 5;
        if (count == 7)
        {
            bytes.Position--;
            count = bytes.ReadUInt32() & 0x1FFFFFFF;
            if (count > bytes.Remaining)
            {
                throw new InvalidDataException("A JBIG2 segment refers to more segments than it can.");
            }

            bytes.Skip((int)((count + 8) / 8));
        }
        else if (count > 4)
        {
            throw new InvalidDataException("A JBIG2 segment header has a referral count it cannot have.");
        }

        var size = number <= 256 ? 1 : number <= 65536 ? 2 : 4;
        var referred = new uint[count];
        for (var i = 0; i < count; i++)
        {
            referred[i] = size switch
            {
                1 => bytes.ReadByte(),
                2 => bytes.ReadUInt16(),
                _ => bytes.ReadUInt32(),
            };
        }

        if (longPage)
        {
            bytes.Skip(4);
        }
        else
        {
            bytes.Skip(1);
        }

        return new Jbig2Segment
        {
            Number = number,
            Type = type,
            ReferredTo = referred,
            DataLength = bytes.ReadUInt32(),
        };
    }

    /// <summary>This header with its data at <paramref name="start"/>, cut at <paramref name="limit"/>.</summary>
    public Jbig2Segment At(int start, int limit)
    {
        if (DataLength == UnknownLength)
        {
            throw new InvalidDataException("A JBIG2 segment of unknown length is not in a sequential stream.");
        }

        return With(start, (int)Math.Min(limit, (long)start + DataLength));
    }

    private Jbig2Segment With(int start, int end) => new()
    {
        Number = Number,
        Type = Type,
        ReferredTo = ReferredTo,
        DataLength = DataLength,
        DataStart = start,
        DataEnd = Math.Max(start, end),
    };

    private static int UnknownEnd(BigEndianReader bytes, int type)
    {
        if (type is not (Jbig2SegmentTypes.ImmediateGenericRegion or Jbig2SegmentTypes.ImmediateLosslessGenericRegion))
        {
            throw new InvalidDataException("A JBIG2 segment of unknown length is not a generic region.");
        }

        var data = bytes.Data;
        var flagsAt = bytes.Position + Jbig2RegionInfo.Length;
        if (flagsAt >= bytes.End)
        {
            throw new InvalidDataException("A JBIG2 segment ends early.");
        }

        var mmr = (data[flagsAt] & 1) != 0;
        var (marker0, marker1) = mmr ? ((byte)0x00, (byte)0x00) : ((byte)0xFF, (byte)0xAC);
        var codedAt = flagsAt + 1 + (mmr ? 0 : ((data[flagsAt] >> 1) & 3) == 0 ? 8 : 2);
        for (var i = codedAt; i + 5 < bytes.End; i++)
        {
            if (data[i] == marker0 && data[i + 1] == marker1)
            {
                return i + 6;
            }
        }

        throw new InvalidDataException("A JBIG2 generic region of unknown length has no end.");
    }
}
