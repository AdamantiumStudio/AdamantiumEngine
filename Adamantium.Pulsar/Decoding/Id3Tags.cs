using System.Buffers.Binary;
using System.Text;

namespace Adamantium.Pulsar.Decoding;

/// <summary>An MP3 file's title, artist and album: of the ID3v2 tags before its frames (versions 2.2 to 2.4: TIT2,
/// TPE1, TALB, or TT2, TP1, TAL), else of an ID3v1 tag in its last 128 bytes.</summary>
internal static class Id3Tags
{
    private const int HeaderLength = 10;
    private const int V1Length = 128;
    // Of a tag, as much as is read: its text frames come before its pictures.
    private const int LongestRead = 1 << 18;

    /// <summary>Of the tags from <paramref name="from"/> to <paramref name="to"/>, where the frames begin.</summary>
    public static SoundTags Read(Stream stream, long from, long to)
    {
        var tags = default(SoundTags);
        Span<byte> header = stackalloc byte[HeaderLength];
        for (var at = from; at + HeaderLength <= to && tags.IsEmpty;)
        {
            stream.Position = at;
            if (stream.ReadAtLeast(header, HeaderLength, throwOnEndOfStream: false) < HeaderLength || !header.StartsWith("ID3"u8))
            {
                break;
            }

            var length = header[6] << 21 | header[7] << 14 | header[8] << 7 | header[9];
            var body = new byte[Math.Min(length, LongestRead)];
            var read = stream.ReadAtLeast(body, body.Length, throwOnEndOfStream: false);
            tags = ReadVersion2(body.AsSpan(0, read), header[3], header[5]);
            at += HeaderLength + length + ((header[5] & 0x10) != 0 ? HeaderLength : 0);
        }

        return tags.IsEmpty ? ReadVersion1(stream) : tags;
    }

    private static SoundTags ReadVersion2(Span<byte> body, int version, int flags)
    {
        if ((flags & 0x80) != 0)
        {
            body = WithoutUnsynchronisation(body);
        }

        if ((flags & 0x40) != 0 && version >= 3 && body.Length >= 4)
        {
            // An extended header: of version 4 its size counts itself, of version 3 not.
            var extended = version == 4
                ? body[0] << 21 | body[1] << 14 | body[2] << 7 | body[3]
                : (int)BinaryPrimitives.ReadUInt32BigEndian(body) + 4;
            body = body[Math.Clamp(extended, 0, body.Length)..];
        }

        var idLength = version == 2 ? 3 : 4;
        var headerLength = version == 2 ? 6 : 10;
        string title = null;
        string artist = null;
        string album = null;
        while (body.Length >= headerLength && body[0] != 0)
        {
            var id = body[..idLength];
            int size = version switch
            {
                2 => body[3] << 16 | body[4] << 8 | body[5],
                3 => (int)Math.Min(BinaryPrimitives.ReadUInt32BigEndian(body[4..]), int.MaxValue),
                _ => body[4] << 21 | body[5] << 14 | body[6] << 7 | body[7],
            };
            if (size < 0 || size > body.Length - headerLength)
            {
                break;
            }

            var content = body.Slice(headerLength, size);
            if (id.SequenceEqual("TIT2"u8) || id.SequenceEqual("TT2"u8))
            {
                title ??= TextOf(content);
            }
            else if (id.SequenceEqual("TPE1"u8) || id.SequenceEqual("TP1"u8))
            {
                artist ??= TextOf(content);
            }
            else if (id.SequenceEqual("TALB"u8) || id.SequenceEqual("TAL"u8))
            {
                album ??= TextOf(content);
            }

            body = body[(headerLength + size)..];
        }

        return new SoundTags(title, artist, album);
    }

    // A text frame: its encoding, then its text; of several values, the first.
    private static string TextOf(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < 2)
        {
            return null;
        }

        var bytes = frame[1..];
        var text = frame[0] switch
        {
            0 => Encoding.Latin1.GetString(bytes),
            1 => Utf16Of(bytes),
            2 => Encoding.BigEndianUnicode.GetString(bytes[..(bytes.Length & ~1)]),
            _ => Encoding.UTF8.GetString(bytes),
        };
        var end = text.IndexOf('\0');
        text = (end >= 0 ? text[..end] : text).Trim();
        return text.Length > 0 ? text : null;
    }

    // UTF-16 by its byte order mark; little-endian where it has none.
    private static string Utf16Of(ReadOnlySpan<byte> bytes)
    {
        var isBigEndian = bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF;
        var isMarked = bytes.Length >= 2 && (isBigEndian || bytes[0] == 0xFF && bytes[1] == 0xFE);
        bytes = isMarked ? bytes[2..] : bytes;
        bytes = bytes[..(bytes.Length & ~1)];
        return (isBigEndian ? Encoding.BigEndianUnicode : Encoding.Unicode).GetString(bytes);
    }

    // Unsynchronisation put a zero after each 0xFF: taken out.
    private static Span<byte> WithoutUnsynchronisation(Span<byte> body)
    {
        var written = 0;
        for (var at = 0; at < body.Length; at++)
        {
            body[written++] = body[at];
            if (body[at] == 0xFF && at + 1 < body.Length && body[at + 1] == 0)
            {
                at++;
            }
        }

        return body[..written];
    }

    // TAG, then title, artist and album of 30 bytes each, Latin-1, padded with zeros or spaces.
    private static SoundTags ReadVersion1(Stream stream)
    {
        if (stream.Length < V1Length)
        {
            return default;
        }

        Span<byte> tag = stackalloc byte[V1Length];
        stream.Position = stream.Length - V1Length;
        if (stream.ReadAtLeast(tag, V1Length, throwOnEndOfStream: false) < V1Length || !tag.StartsWith("TAG"u8))
        {
            return default;
        }

        return new SoundTags(FieldOf(tag.Slice(3, 30)), FieldOf(tag.Slice(33, 30)), FieldOf(tag.Slice(63, 30)));
    }

    private static string FieldOf(ReadOnlySpan<byte> field)
    {
        var end = field.IndexOf((byte)0);
        var text = Encoding.Latin1.GetString(end >= 0 ? field[..end] : field).Trim();
        return text.Length > 0 ? text : null;
    }
}
