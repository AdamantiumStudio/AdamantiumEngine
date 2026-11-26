using Adamantium.Core.Compression;

namespace Adamantium.Pulsar.Decoding;

/// <summary>The Huffman codes of Layer III, table 3-B.7: pairs of 16 tables, the higher ones with linbits, and the
/// quadruples of table A.</summary>
internal static class Mp3Huffman
{
    private const int Quadruples = 32;

    // Tables 16 to 23 are 16's codes, 24 to 31 are 24's.
    private static readonly int[] Linbits = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 2, 3, 4, 6, 8, 10, 13, 4, 5, 6, 7, 8, 9, 11, 13];
    private static readonly PrefixCode[] Codes = ReadCodes();

    public static bool IsMissing(int table) => table is 4 or 14;

    public static int LinbitsOf(int table) => Linbits[table];

    /// <summary>x in the upper four bits, y in the lower; -1 for no code.</summary>
    public static int DecodePair(ref Mp3Bits bits, int table) => Decode(ref bits, Codes[CodesOf(table)]);

    /// <summary>v, w, x and y, the highest first; -1 for no code.</summary>
    public static int DecodeQuadruple(ref Mp3Bits bits) => Decode(ref bits, Codes[Quadruples]);

    private static int CodesOf(int table) => table >= 24 ? 24 : table >= 16 ? 16 : table;

    private static int Decode(ref Mp3Bits bits, PrefixCode code)
    {
        var value = code.Decode((ulong)bits.Peek() << 32, out var length);
        bits.Position += length;
        return value;
    }

    // Lines "table n", then "x y code", or "v w x y code" for the quadruples' "table 32".
    private static PrefixCode[] ReadCodes()
    {
        var words = new List<PrefixCodeWord>[Quadruples + 1];
        using var resource = typeof(Mp3Huffman).Assembly.GetManifestResourceStream("Mp3Huffman.txt");
        using var reader = new StreamReader(resource);
        List<PrefixCodeWord> table = null;
        while (reader.ReadLine() is { } line)
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || parts[0].StartsWith('#'))
            {
                continue;
            }

            if (parts[0] == "table")
            {
                table = words[int.Parse(parts[1])] = [];
                continue;
            }

            var value = 0;
            for (var part = 0; part < parts.Length - 1; part++)
            {
                value = value << (parts.Length == 5 ? 1 : 4) | int.Parse(parts[part]);
            }

            var code = parts[^1];
            table.Add(new PrefixCodeWord(Convert.ToUInt32(code, 2), code.Length, value));
        }

        return [.. words.Select(Create)];
    }

    private static PrefixCode Create(List<PrefixCodeWord> words) =>
        words == null ? default :
        PrefixCode.TryCreate(words.ToArray(), PrefixBitOrder.HighFirst, out var code) ? code :
        throw new InvalidDataException("A Huffman table of Layer III is no prefix code.");
}
