using System;
using System.Collections.Generic;
using Adamantium.Core.Compression;

namespace Adamantium.Engine.Compiler.Models.Prc;

/// <summary>The prefix codes a compressed Double starts with (PRC WriteDouble, its table of frequent doubles and
/// exponents): one code for each of 2048 exponents and 29 values frequent in CAD data. The exponents' codes are held as
/// runs of consecutive codes of one length, in exponent order; each frequent value with its code and its bits.</summary>
internal static class PrcDoubleCodes
{
    /// <summary>The longest code in the table.</summary>
    internal const int LongestCode = 22;

    private const int ExponentCount = 2048;

    private static readonly uint[] ExponentRuns =
    [
        22, 0xd1d32, 2, 22, 0xf78d8, 40, 22, 0x3a8300, 810, 18, 0xf787, 1, 18, 0xd1d4, 1, 19, 0x77f9f, 1,
        18, 0x3b8fb, 1, 19, 0x1ef1a, 1, 21, 0x1d4315, 1, 16, 0x3de2, 1, 22, 0x3a862c, 13, 21, 0x1d431d, 1,
        22, 0x3a8639, 1, 22, 0x3a863c, 1, 16, 0x3de0, 1, 18, 0x3a95e, 1, 21, 0x1d431f, 1, 22, 0x3a863d, 1,
        22, 0x3a8640, 1, 20, 0x34749, 1, 20, 0x3474d, 1, 22, 0x3a8641, 29, 21, 0x1d432f, 1, 22, 0x3a8660, 3,
        21, 0x1d4332, 1, 18, 0xd1d5, 1, 18, 0x3bfda, 1, 15, 0x7528, 2, 18, 0x3a95f, 1, 17, 0x1d434, 1,
        19, 0x750cd, 1, 18, 0x3a867, 1, 19, 0x771fd, 1, 15, 0x7764, 1, 20, 0xee3f9, 1, 18, 0x3bfdb, 1,
        16, 0xeff7, 1, 18, 0xd1d6, 1, 22, 0x3a8663, 1, 22, 0x3a8666, 1, 18, 0x3b8fa, 1, 17, 0x1d435, 1,
        17, 0x1dfe4, 1, 19, 0x750d8, 1, 18, 0x3a95b, 1, 19, 0x77f9e, 1, 19, 0x750d9, 1, 18, 0xd1d7, 1,
        18, 0x3b8ff, 1, 17, 0x1dc7c, 1, 19, 0x750da, 1, 17, 0x7bc2, 1, 18, 0x3bfca, 1, 19, 0x1a3a5, 1,
        17, 0x1d4ac, 1, 18, 0x3a86e, 1, 17, 0x1d438, 1, 18, 0x3bfcb, 1, 19, 0x1a3a7, 1, 17, 0x1d4ae, 1,
        18, 0x3bfce, 1, 18, 0xf78c, 1, 17, 0x1dfec, 1, 17, 0x1d439, 1, 17, 0x68e8, 1, 18, 0xf786, 1,
        15, 0x771e, 1, 17, 0x1dfe6, 1, 15, 0x77f8, 1, 14, 0x3bb3, 1, 14, 0x3b8e, 1, 14, 0x3a82, 1,
        14, 0x3b96, 1, 13, 0x68f, 1, 12, 0x3d4, 1, 13, 0x1dca, 1, 12, 0x346, 1, 12, 0xee7, 1,
        12, 0xeea, 1, 11, 0x1ed, 1, 12, 0x3df, 1, 11, 0x1a2, 1, 11, 0x56f, 1, 11, 0xb9, 1,
        13, 0x7b2, 1, 13, 0x1dd8, 1, 13, 0x15ba, 1, 12, 0xee6, 1, 13, 0x15b8, 1, 14, 0xf79, 1,
        14, 0x3a81, 1, 14, 0xd1c, 1, 15, 0x7765, 1, 14, 0xf54, 1, 13, 0x15b9, 1, 13, 0x7ab, 1,
        15, 0x7500, 1, 15, 0x1eaa, 1, 15, 0x7501, 1, 15, 0x1eab, 1, 14, 0x3b97, 1, 15, 0x752a, 1,
        15, 0x77fa, 1, 14, 0x3a93, 1, 13, 0x1dc6, 1, 13, 0x7bd, 1, 13, 0x1dff, 1, 12, 0xefe, 1,
        12, 0xeed, 1, 11, 0xb8, 1, 12, 0x3d8, 1, 11, 0x1eb, 1, 13, 0x1d4b, 1, 13, 0x7b3, 1,
        10, 0x5d, 1, 12, 0xeeb, 1, 11, 0x1ee, 1, 10, 0x5f, 1, 10, 0x2b6, 1, 9, 0x1de, 1,
        8, 0xe8, 1, 8, 0xae, 1, 7, 0x1b, 1, 7, 0x76, 1, 7, 0xa, 1, 6, 0x8, 1,
        6, 0xe, 1, 6, 0x4, 1, 6, 0xc, 1, 5, 0x3, 1, 4, 0x8, 1, 5, 0x13, 1,
        5, 0x1b, 1, 5, 0x17, 1, 5, 0x12, 1, 5, 0x19, 2, 5, 0x18, 1, 5, 0x1c, 1,
        5, 0x14, 1, 5, 0x5, 1, 8, 0x16, 1, 12, 0xee2, 1, 12, 0xee4, 1, 8, 0xac, 1,
        13, 0x15bb, 1, 22, 0x3a8667, 1, 22, 0x3a86d8, 3, 17, 0x1dc7e, 1, 22, 0x3a86db, 5, 22, 0x3a86f0, 16,
        22, 0x3a8740, 25, 15, 0x1a3b, 1, 22, 0x3a8759, 167, 22, 0x3a9000, 768, 22, 0x3a95a0, 16, 22, 0x3b8fe0, 4,
        21, 0x68e90, 2, 21, 0x68e98, 1
    ];

    private static readonly ulong[] FrequentDoubles =
    [
        2, 0x1, 0x0000000000000000,
        10, 0xf4, 0x3e35798ee2308c3a,
        11, 0x77c, 0x3e45798ee2308c3a,
        8, 0xaf, 0x3e8ad7f29abcaf4a,
        9, 0x1d2, 0x3ec0c6f7a0b5ed8e,
        10, 0xd0, 0x3f454c985f06f694,
        6, 0x9, 0x3f4a36e2eb1c432d,
        4, 0xf, 0x3f50624dd2f1a9fc,
        5, 0x16, 0x3f60624dd2f1a9fc,
        11, 0x751, 0x3fbe69ad42c3c9ee,
        11, 0x777, 0x3fe0000000000000,
        9, 0x1d6, 0x3feffffff8000002,
        4, 0x0, 0x3ff0000000000000,
        9, 0x15a, 0x401921fb54442d18,
        11, 0x774, 0x4035ee1480000000,
        9, 0x1d3, 0x404ca5dc1a63c1f8,
        11, 0x77e, 0x405bb32fe0000000,
        10, 0x5e, 0x405c332fe0000000,
        9, 0x1d7, 0x4066800000000000,
        9, 0x1d5, 0x4076800000000000,
        11, 0x77d, 0x408f400000000000,
        10, 0xd2, 0x409233ffffffffff,
        8, 0x3c, 0x4092340000000000,
        11, 0x753, 0x4092340000000001,
        10, 0xd3, 0x4092abffffffffff,
        8, 0x35, 0x4092ac0000000000,
        11, 0x770, 0x4092ac0000000001,
        7, 0x1f, 0x40c81c8000000000,
        6, 0x2a, 0x41cdcd64ff800000
    ];

    private static readonly PrcDoubleCode[] Entries = Build();
    private static readonly PrefixCode Code = CodeOf(Entries);

    /// <summary>Every code of the table, the exponents' and the frequent values'.</summary>
    internal static IReadOnlyCollection<PrcDoubleCode> All => Entries;

    /// <summary>The entry whose code <paramref name="next"/> begins by, the next bit its highest; false for none.
    /// </summary>
    internal static bool TryDecode(ulong next, out PrcDoubleCode entry)
    {
        var index = Code.Decode(next, out _);
        entry = index < 0 ? default : Entries[index];
        return index >= 0;
    }

    private static PrcDoubleCode[] Build()
    {
        var codes = new List<PrcDoubleCode>();
        var exponent = 0;
        for (var run = 0; run < ExponentRuns.Length; run += 3)
        {
            var length = (int)ExponentRuns[run];
            for (var step = 0u; step < ExponentRuns[run + 2]; step++)
            {
                var code = ExponentRuns[run + 1] + step;
                codes.Add(new PrcDoubleCode(length, code, true, exponent, 0));
                exponent++;
            }
        }

        if (exponent != ExponentCount)
        {
            throw new InvalidOperationException("The PRC double table does not give a code for every exponent");
        }

        for (var index = 0; index < FrequentDoubles.Length; index += 3)
        {
            var length = (int)FrequentDoubles[index];
            var code = (uint)FrequentDoubles[index + 1];
            var value = BitConverter.UInt64BitsToDouble(FrequentDoubles[index + 2]);
            codes.Add(new PrcDoubleCode(length, code, false, 0, value));
        }

        return [.. codes];
    }

    private static PrefixCode CodeOf(PrcDoubleCode[] entries)
    {
        var words = new PrefixCodeWord[entries.Length];
        for (var index = 0; index < words.Length; index++)
        {
            words[index] = new PrefixCodeWord(entries[index].Code, entries[index].Length, index);
        }

        return PrefixCode.TryCreate(words, PrefixBitOrder.HighFirst, out var code)
            ? code
            : throw new InvalidOperationException("The PRC double table is no prefix code");
    }
}
