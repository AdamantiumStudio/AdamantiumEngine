using System.Buffers.Binary;

namespace Adamantium.Pulsar.Tests;

/// <summary>An Ogg file's pages as they are in it, changed and sealed again with their checksums (RFC 3533).</summary>
internal static class OggPages
{
    /// <summary>Where each page begins.</summary>
    public static List<int> Of(byte[] file)
    {
        var pages = new List<int>();
        for (var at = 0; at + 27 <= file.Length;)
        {
            pages.Add(at);
            var segments = file[at + 26];
            var body = 0;
            foreach (var length in file.AsSpan(at + 27, segments))
            {
                body += length;
            }

            at += 27 + segments + body;
        }

        return pages;
    }

    public static long GranuleOf(byte[] file, int page) => BinaryPrimitives.ReadInt64LittleEndian(file.AsSpan(page + 6));

    public static void SetGranule(byte[] file, int page, long granule) =>
        BinaryPrimitives.WriteInt64LittleEndian(file.AsSpan(page + 6), granule);

    /// <summary>The page's first packet's bytes from its beginning.</summary>
    public static void SetFirstBytes(byte[] file, int page, ReadOnlySpan<byte> bytes) =>
        bytes.CopyTo(file.AsSpan(page + 27 + file[page + 26]));

    /// <summary>Its checksum made again for what it holds now.</summary>
    public static void Seal(byte[] file, int page)
    {
        var segments = file[page + 26];
        var length = 27 + segments;
        foreach (var segment in file.AsSpan(page + 27, segments))
        {
            length += segment;
        }

        var bytes = file.AsSpan(page, length);
        bytes[22..26].Clear();
        var sum = 0u;
        foreach (var value in bytes)
        {
            sum ^= (uint)value << 24;
            for (var bit = 0; bit < 8; bit++)
            {
                sum = (sum & 0x80000000) != 0 ? sum << 1 ^ 0x04C11DB7 : sum << 1;
            }
        }

        BinaryPrimitives.WriteUInt32LittleEndian(bytes[22..], sum);
    }
}
