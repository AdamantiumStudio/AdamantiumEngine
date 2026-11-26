using System;
using System.Collections.Generic;
using Adamantium.Core.Compression;

namespace Adamantium.Imaging.Ccitt;

/// <summary>The codes of CCITT fax (ITU-T T.4): the run lengths of tables 1 to 3 - terminating codes for runs of 0 to
/// 63, make-up codes for multiples of 64, and the extended make-up codes both colors share - each standing for its
/// run; and the two-dimensional modes of table 4.</summary>
internal static class CcittCodes
{
    /// <summary>The bits the next code is looked for in - more than the longest.</summary>
    public const int Bits = 24;

    private static readonly string[] Extended =
    [
        "00000001000", "00000001100", "00000001101", "000000010010", "000000010011", "000000010100", "000000010101",
        "000000010110", "000000010111", "000000011100", "000000011101", "000000011110", "000000011111"
    ];

    /// <summary>White runs.</summary>
    public static readonly PrefixCode White = Build(
    [
        "00110101", "000111", "0111", "1000", "1011", "1100", "1110", "1111", "10011", "10100", "00111", "01000",
        "001000", "000011", "110100", "110101", "101010", "101011", "0100111", "0001100", "0001000", "0010111",
        "0000011", "0000100", "0101000", "0101011", "0010011", "0100100", "0011000", "00000010", "00000011",
        "00011010", "00011011", "00010010", "00010011", "00010100", "00010101", "00010110", "00010111", "00101000",
        "00101001", "00101010", "00101011", "00101100", "00101101", "00000100", "00000101", "00001010", "00001011",
        "01010010", "01010011", "01010100", "01010101", "00100100", "00100101", "01011000", "01011001", "01011010",
        "01011011", "01001010", "01001011", "00110010", "00110011", "00110100"
    ],
    [
        "11011", "10010", "010111", "0110111", "00110110", "00110111", "01100100", "01100101", "01101000", "01100111",
        "011001100", "011001101", "011010010", "011010011", "011010100", "011010101", "011010110", "011010111",
        "011011000", "011011001", "011011010", "011011011", "010011000", "010011001", "010011010", "011000",
        "010011011"
    ]);

    /// <summary>Black runs.</summary>
    public static readonly PrefixCode Black = Build(
    [
        "0000110111", "010", "11", "10", "011", "0011", "0010", "00011", "000101", "000100", "0000100", "0000101",
        "0000111", "00000100", "00000111", "000011000", "0000010111", "0000011000", "0000001000", "00001100111",
        "00001101000", "00001101100", "00000110111", "00000101000", "00000010111", "00000011000", "000011001010",
        "000011001011", "000011001100", "000011001101", "000001101000", "000001101001", "000001101010",
        "000001101011", "000011010010", "000011010011", "000011010100", "000011010101", "000011010110",
        "000011010111", "000001101100", "000001101101", "000011011010", "000011011011", "000001010100",
        "000001010101", "000001010110", "000001010111", "000001100100", "000001100101", "000001010010",
        "000001010011", "000000100100", "000000110111", "000000111000", "000000100111", "000000101000",
        "000001011000", "000001011001", "000000101011", "000000101100", "000001011010", "000001100110",
        "000001100111"
    ],
    [
        "0000001111", "000011001000", "000011001001", "000001011011", "000000110011", "000000110100",
        "000000110101", "0000001101100", "0000001101101", "0000001001010", "0000001001011", "0000001001100",
        "0000001001101", "0000001110010", "0000001110011", "0000001110100", "0000001110101", "0000001110110",
        "0000001110111", "0000001010010", "0000001010011", "0000001010100", "0000001010101", "0000001011010",
        "0000001011011", "0000001100100", "0000001100101"
    ]);

    /// <summary>The two-dimensional modes, by <see cref="CcittMode"/>; the extensions are not read.</summary>
    public static readonly PrefixCode Modes = CodeOf(
    [
        Word("1", CcittMode.Vertical0), Word("011", CcittMode.VerticalRight1), Word("010", CcittMode.VerticalLeft1),
        Word("001", CcittMode.Horizontal), Word("0001", CcittMode.Pass), Word("000011", CcittMode.VerticalRight2),
        Word("000010", CcittMode.VerticalLeft2), Word("0000011", CcittMode.VerticalRight3),
        Word("0000010", CcittMode.VerticalLeft3),
    ]);

    /// <summary>The symbol of the code the bits that come next begin by, and its length; -1 for none.</summary>
    public static int Decode(PrefixCode code, int next, out int length) =>
        code.Decode((ulong)(uint)next << (64 - Bits), out length);

    private static PrefixCode Build(string[] terminating, string[] makeUp)
    {
        var words = new List<PrefixCodeWord>();
        for (var run = 0; run < terminating.Length; run++)
        {
            words.Add(Word(terminating[run], run));
        }

        for (var index = 0; index < makeUp.Length; index++)
        {
            words.Add(Word(makeUp[index], (index + 1) * 64));
        }

        for (var index = 0; index < Extended.Length; index++)
        {
            words.Add(Word(Extended[index], 1792 + index * 64));
        }

        return CodeOf([.. words]);
    }

    private static PrefixCodeWord Word(string code, CcittMode mode) => Word(code, (int)mode);

    private static PrefixCodeWord Word(string code, int symbol) =>
        new(Convert.ToUInt32(code, 2), code.Length, symbol);

    private static PrefixCode CodeOf(PrefixCodeWord[] words) =>
        PrefixCode.TryCreate(words, PrefixBitOrder.HighFirst, out var code)
            ? code
            : throw new InvalidOperationException("A CCITT code table is no prefix code.");
}
