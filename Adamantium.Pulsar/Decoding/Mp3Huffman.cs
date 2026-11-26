namespace Adamantium.Pulsar.Decoding;

/// <summary>The Huffman codes of Layer III, table 3-B.7: pairs of 16 tables, the higher ones with linbits, and the
/// quadruples of table A. Each a tree read a bit at a time, made once from the codes.</summary>
internal static class Mp3Huffman
{
    private const int Quadruples = 32;

    // Tables 16 to 23 are 16's codes, 24 to 31 are 24's.
    private static readonly int[] Linbits = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 2, 3, 4, 6, 8, 10, 13, 4, 5, 6, 7, 8, 9, 11, 13];
    private const int FirstBits = 8;

    private static readonly int[][] Trees = ReadTrees();
    // By the next 8 bits: a value and its code's length, the length in the upper half; or, for a longer code, minus one
    // less the node those bits lead to; 0 where they lead to no code.
    private static readonly int[][] Firsts = [.. Trees.Select(FirstsOf)];

    public static bool IsMissing(int table) => table is 4 or 14;

    public static int LinbitsOf(int table) => Linbits[table];

    /// <summary>x in the upper four bits, y in the lower; -1 for no code.</summary>
    public static int DecodePair(ref Mp3Bits bits, int table)
    {
        var codes = CodesOf(table);
        return Decode(ref bits, Trees[codes], Firsts[codes]);
    }

    /// <summary>v, w, x and y, the highest first; -1 for no code.</summary>
    public static int DecodeQuadruple(ref Mp3Bits bits) => Decode(ref bits, Trees[Quadruples], Firsts[Quadruples]);

    private static int CodesOf(int table) => table >= 24 ? 24 : table >= 16 ? 16 : table;

    private static int Decode(ref Mp3Bits bits, int[] tree, int[] firsts)
    {
        var first = firsts[bits.Peek() >> (32 - FirstBits)];
        if (first > 0)
        {
            bits.Position += first >> 16;
            return first & 0xFFFF;
        }

        if (first == 0)
        {
            return -1;
        }

        bits.Position += FirstBits;
        return Walk(ref bits, tree, -1 - first);
    }

    // A node is two entries, its children for 0 and 1: another node's index, or a value as -1 - value; 0 for none.
    private static int Walk(ref Mp3Bits bits, int[] tree, int node)
    {
        for (var depth = 0; depth < 20; depth++)
        {
            var child = tree[node * 2 + bits.ReadBit()];
            if (child < 0)
            {
                return -1 - child;
            }

            if (child == 0)
            {
                return -1;
            }

            node = child;
        }

        return -1;
    }

    private static int[] FirstsOf(int[] tree)
    {
        if (tree == null)
        {
            return null;
        }

        var firsts = new int[1 << FirstBits];
        for (var pattern = 0; pattern < firsts.Length; pattern++)
        {
            var node = 0;
            for (var bit = 0; bit < FirstBits; bit++)
            {
                var child = tree[node * 2 + (pattern >> (FirstBits - 1 - bit) & 1)];
                if (child <= 0)
                {
                    // A value, its length in the upper half; or no code.
                    firsts[pattern] = child < 0 ? (bit + 1) << 16 | (-1 - child) : 0;
                    break;
                }

                node = child;
                if (bit == FirstBits - 1)
                {
                    firsts[pattern] = -1 - node;
                }
            }
        }

        return firsts;
    }

    // Lines "table n", then "x y code", or "v w x y code" for the quadruples' "table 32".
    private static int[][] ReadTrees()
    {
        var built = new List<int>[Quadruples + 1];
        using var resource = typeof(Mp3Huffman).Assembly.GetManifestResourceStream("Mp3Huffman.txt");
        using var reader = new StreamReader(resource);
        List<int> tree = null;
        while (reader.ReadLine() is { } line)
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || parts[0].StartsWith('#'))
            {
                continue;
            }

            if (parts[0] == "table")
            {
                tree = built[int.Parse(parts[1])] = [0, 0];
                continue;
            }

            var value = 0;
            for (var part = 0; part < parts.Length - 1; part++)
            {
                value = value << (parts.Length == 5 ? 1 : 4) | int.Parse(parts[part]);
            }

            Add(tree, parts[^1], value);
        }

        return [.. built.Select(codes => codes?.ToArray())];
    }

    private static void Add(List<int> tree, string code, int value)
    {
        var node = 0;
        for (var at = 0; at < code.Length; at++)
        {
            var slot = node * 2 + (code[at] - '0');
            if (at == code.Length - 1)
            {
                tree[slot] = -1 - value;
                return;
            }

            if (tree[slot] == 0)
            {
                tree[slot] = tree.Count / 2;
                tree.Add(0);
                tree.Add(0);
            }

            node = tree[slot];
        }
    }
}
