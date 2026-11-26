namespace Adamantium.Pulsar.Decoding;

/// <summary>The checksums of a FLAC frame, RFC 9639, 9.1.8 and 9.3.</summary>
internal static class FlacChecksums
{
    private static readonly byte[] HeaderTable = CreateHeaderTable();
    private static readonly ushort[] FrameTable = CreateFrameTable();

    public static byte OfHeader(ReadOnlySpan<byte> bytes)
    {
        byte sum = 0;
        foreach (var value in bytes)
        {
            sum = HeaderTable[sum ^ value];
        }

        return sum;
    }

    public static ushort OfFrame(ReadOnlySpan<byte> bytes)
    {
        ushort sum = 0;
        foreach (var value in bytes)
        {
            sum = (ushort)(sum << 8 ^ FrameTable[sum >> 8 ^ value]);
        }

        return sum;
    }

    private static byte[] CreateHeaderTable()
    {
        var table = new byte[256];
        for (var index = 0; index < table.Length; index++)
        {
            var sum = index;
            for (var bit = 0; bit < 8; bit++)
            {
                sum = (sum & 0x80) != 0 ? sum << 1 ^ 0x07 : sum << 1;
            }

            table[index] = (byte)sum;
        }

        return table;
    }

    private static ushort[] CreateFrameTable()
    {
        var table = new ushort[256];
        for (var index = 0; index < table.Length; index++)
        {
            var sum = index << 8;
            for (var bit = 0; bit < 8; bit++)
            {
                sum = (sum & 0x8000) != 0 ? sum << 1 ^ 0x8005 : sum << 1;
            }

            table[index] = (ushort)sum;
        }

        return table;
    }
}
