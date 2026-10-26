using System;
using System.Buffers.Binary;

namespace Adamantium.Imaging.Png;

/// <summary>The CRC-32 of PNG chunks (PNG, 5.5 and Annex D): polynomial 0xEDB88320 reflected, register preset to ones
/// and the result inverted - over a chunk's type and data. Eight bytes a step, by eight tables: table k gives the
/// register's change for a byte k places before the end of the step.</summary>
internal static class PngCrc
{
    private static readonly uint[][] Tables = BuildTables();

    private static uint[][] BuildTables()
    {
        var tables = new uint[8][];
        tables[0] = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }

            tables[0][n] = c;
        }

        for (var t = 1; t < 8; t++)
        {
            tables[t] = new uint[256];
            for (var n = 0; n < 256; n++)
            {
                var c = tables[t - 1][n];
                tables[t][n] = tables[0][c & 0xFF] ^ (c >> 8);
            }
        }

        return tables;
    }

    /// <summary>The CRC of <paramref name="bytes"/>.</summary>
    public static uint Compute(ReadOnlySpan<byte> bytes) => Finish(Update(Start, bytes));

    /// <summary>The register before any byte.</summary>
    public const uint Start = 0xFFFFFFFFu;

    /// <summary>The register after <paramref name="bytes"/> more.</summary>
    public static uint Update(uint crc, ReadOnlySpan<byte> bytes)
    {
        uint[] t0 = Tables[0], t1 = Tables[1], t2 = Tables[2], t3 = Tables[3], t4 = Tables[4], t5 = Tables[5],
            t6 = Tables[6], t7 = Tables[7];
        while (bytes.Length >= 8)
        {
            var low = BinaryPrimitives.ReadUInt32LittleEndian(bytes) ^ crc;
            var high = BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..]);
            crc = t7[low & 0xFF] ^ t6[(low >> 8) & 0xFF] ^ t5[(low >> 16) & 0xFF] ^ t4[low >> 24]
                  ^ t3[high & 0xFF] ^ t2[(high >> 8) & 0xFF] ^ t1[(high >> 16) & 0xFF] ^ t0[high >> 24];
            bytes = bytes[8..];
        }

        foreach (var value in bytes)
        {
            crc = t0[(crc ^ value) & 0xFF] ^ (crc >> 8);
        }

        return crc;
    }

    /// <summary>The CRC a register stands for.</summary>
    public static uint Finish(uint crc) => crc ^ 0xFFFFFFFFu;
}
