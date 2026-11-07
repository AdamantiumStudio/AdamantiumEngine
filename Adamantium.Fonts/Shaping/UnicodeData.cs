using System;
using System.Globalization;

namespace Adamantium.Fonts.Shaping;

internal static class UnicodeData
{
    private const int HangulSBase = 0xAC00;
    private const int HangulLBase = 0x1100;
    private const int HangulVBase = 0x1161;
    private const int HangulTBase = 0x11A7;
    private const int HangulLCount = 19;
    private const int HangulVCount = 21;
    private const int HangulTCount = 28;
    private const int HangulNCount = HangulVCount * HangulTCount;
    private const int HangulSCount = HangulLCount * HangulNCount;

    private static readonly Lazy<RangeTable<string>> Scripts =
        new(() => RangeTable<string>.Load("Scripts.ucd", v => v, "Zzzz"));

    private static readonly Lazy<RangeTable<byte>> CombiningClasses =
        new(() => RangeTable<byte>.Load("CombiningClass.ucd", v => byte.Parse(v, CultureInfo.InvariantCulture), 0));

    private static readonly Lazy<RangeTable<bool>> Pictographic =
        new(() => RangeTable<bool>.Load("ExtendedPictographic.ucd", _ => true, false));

    private static readonly Lazy<RangeTable<bool>> EmojiByDefault =
        new(() => RangeTable<bool>.Load("EmojiPresentation.ucd", _ => true, false));

    private static readonly Lazy<RangeTable<bool>> Wide =
        new(() => RangeTable<bool>.Load("EastAsianWidth.ucd", v => v is "W" or "F", false));

    private static readonly Lazy<CanonicalDecompositions> Decompositions = new(CanonicalDecompositions.Load);

    public static string GetScript(int codepoint) => Scripts.Value[codepoint];

    public static byte GetCombiningClass(int codepoint) => CombiningClasses.Value[codepoint];

    // Ported from HarfBuzz (src/hb-unicode.hh), Copyright © 2010-2022 Google, Inc. and the HarfBuzz authors. Under the
    // "Old MIT" licence, details: THIRD-PARTY-NOTICES.md.
    public static byte GetModifiedCombiningClass(int codepoint) => codepoint switch
    {
        0x1A60 or 0x0FC6 => 254,
        0x0F39 => 127,
        _ => GetCombiningClass(codepoint) switch
        {
            10 => 22,
            11 => 15,
            12 => 16,
            13 => 17,
            14 => 23,
            15 => 18,
            16 => 19,
            17 => 20,
            18 => 21,
            19 => 14,
            20 => 24,
            21 => 12,
            22 => 25,
            23 => 13,
            24 => 10,
            25 => 11,
            27 => 28,
            28 => 29,
            29 => 30,
            30 => 31,
            31 => 32,
            32 => 33,
            33 => 27,
            103 => 3,
            130 => 132,
            132 => 131,
            var other => other
        }
    };

    public static bool IsExtendedPictographic(int codepoint) => Pictographic.Value[codepoint];

    public static bool IsEmojiPresentation(int codepoint) => EmojiByDefault.Value[codepoint];

    /// <summary>East_Asian_Width is Wide or Fullwidth.</summary>
    public static bool IsWide(int codepoint) => Wide.Value[codepoint];

    public static UnicodeCategory GetCategory(int codepoint)
    {
        if (codepoint < 0x10000)
        {
            return CharUnicodeInfo.GetUnicodeCategory((char)codepoint);
        }

        return CharUnicodeInfo.GetUnicodeCategory(char.ConvertFromUtf32(codepoint), 0);
    }

    public static bool IsMark(UnicodeCategory category)
    {
        return category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark
            or UnicodeCategory.EnclosingMark;
    }

    public static bool IsDefaultIgnorable(int codepoint)
    {
        switch (codepoint >> 8)
        {
            case 0x00:
                return codepoint == 0x00AD;
            case 0x03:
                return codepoint == 0x034F;
            case 0x06:
                return codepoint == 0x061C;
            case 0x17:
                return codepoint is >= 0x17B4 and <= 0x17B5;
            case 0x18:
                return codepoint is >= 0x180B and <= 0x180F;
            case 0x20:
                return codepoint is >= 0x200B and <= 0x200F or >= 0x202A and <= 0x202E or >= 0x2060 and <= 0x206F;
            case 0xFE:
                return codepoint is >= 0xFE00 and <= 0xFE0F or 0xFEFF;
            case 0xFF:
                return codepoint is >= 0xFFF0 and <= 0xFFF8;
            case 0x1D1:
                return codepoint is >= 0x1D173 and <= 0x1D17A;
        }

        return codepoint is >= 0xE0000 and <= 0xE0FFF;
    }

    public static bool TryDecompose(int codepoint, out int first, out int second)
    {
        var index = codepoint - HangulSBase;
        if (index is >= 0 and < HangulSCount)
        {
            var trailing = index % HangulTCount;
            if (trailing != 0)
            {
                first = codepoint - trailing;
                second = HangulTBase + trailing;
            }
            else
            {
                first = HangulLBase + index / HangulNCount;
                second = HangulVBase + index % HangulNCount / HangulTCount;
            }

            return true;
        }

        return Decompositions.Value.TryDecompose(codepoint, out first, out second);
    }

    public static bool TryCompose(int first, int second, out int composed)
    {
        if (first is >= HangulLBase and < HangulLBase + HangulLCount
            && second is >= HangulVBase and < HangulVBase + HangulVCount)
        {
            composed = HangulSBase + ((first - HangulLBase) * HangulVCount + second - HangulVBase) * HangulTCount;
            return true;
        }

        var index = first - HangulSBase;
        if (index is >= 0 and < HangulSCount && index % HangulTCount == 0
            && second is > HangulTBase and < HangulTBase + HangulTCount)
        {
            composed = first + second - HangulTBase;
            return true;
        }

        return Decompositions.Value.TryCompose(first, second, out composed);
    }
}
