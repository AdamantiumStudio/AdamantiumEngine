// Ported from HarfBuzz's Arabic shaper (src/hb-ot-shaper-arabic.cc).
// Copyright © 2010-2022 Google, Inc. and the HarfBuzz authors. Under the "Old MIT" licence, details:
// THIRD-PARTY-NOTICES.md.

using System;
using System.Collections.Generic;
using System.Globalization;

namespace Adamantium.Fonts.Shaping;

internal static class ArabicShaper
{
    private const byte JoinU = 0;
    private const byte JoinL = 1;
    private const byte JoinR = 2;
    private const byte JoinD = 3;
    private const byte Alaph = 4;
    private const byte DalathRish = 5;
    private const byte JoinT = 7;
    private const byte Unlisted = 8;

    private const byte Isol = 0;
    private const byte Fina = 1;
    private const byte Fin2 = 2;
    private const byte Fin3 = 3;
    private const byte Medi = 4;
    private const byte Med2 = 5;
    private const byte Init = 6;
    private const byte NoAction = 7;

    private const int ContextLength = 5;

    public static readonly string[] ActionFeatures = ["isol", "fina", "fin2", "fin3", "medi", "med2", "init"];

    private static readonly Lazy<RangeTable<byte>> JoiningTypes =
        new(() => RangeTable<byte>.Load("JoiningType.ucd", ParseJoiningType, Unlisted));

    private static readonly int[] ModifierCombiningMarks =
        [0x0654, 0x0655, 0x0658, 0x06DC, 0x06E3, 0x06E7, 0x06E8, 0x08CA, 0x08CB, 0x08CD, 0x08CE, 0x08CF, 0x08D3, 0x08F3];

    private static readonly (byte Previous, byte Current, byte Next)[,] States =
    {
        { (NoAction, NoAction, 0), (NoAction, Isol, 2), (NoAction, Isol, 1), (NoAction, Isol, 2), (NoAction, Isol, 1), (NoAction, Isol, 6) },
        { (NoAction, NoAction, 0), (NoAction, Isol, 2), (NoAction, Isol, 1), (NoAction, Isol, 2), (NoAction, Fin2, 5), (NoAction, Isol, 6) },
        { (NoAction, NoAction, 0), (NoAction, Isol, 2), (Init, Fina, 1), (Init, Fina, 3), (Init, Fina, 4), (Init, Fina, 6) },
        { (NoAction, NoAction, 0), (NoAction, Isol, 2), (Medi, Fina, 1), (Medi, Fina, 3), (Medi, Fina, 4), (Medi, Fina, 6) },
        { (NoAction, NoAction, 0), (NoAction, Isol, 2), (Med2, Isol, 1), (Med2, Isol, 2), (Med2, Fin2, 5), (Med2, Isol, 6) },
        { (NoAction, NoAction, 0), (NoAction, Isol, 2), (Isol, Isol, 1), (Isol, Isol, 2), (Isol, Fin2, 5), (Isol, Isol, 6) },
        { (NoAction, NoAction, 0), (NoAction, Isol, 2), (NoAction, Isol, 1), (NoAction, Isol, 2), (NoAction, Fin3, 5), (NoAction, Isol, 6) },
    };

    public static bool Shapes(string script, bool fontHasScript) =>
        (fontHasScript || script == "Arab") && script is "Arab" or "Syrc" or "Mong" or "Nkoo" or "Phag" or "Mand"
            or "Mani" or "Phlp" or "Adlm" or "Rohg" or "Sogd" or "Chrs" or "Ougr";

    public static void SetupMasks(GlyphBuffer buffer, ShapePlan plan, string text, int start, int end)
    {
        var info = buffer.Info;
        var actions = new byte[buffer.Length];
        var state = 0;
        foreach (var codepoint in Before(text, start))
        {
            var type = JoiningType(codepoint, UnicodeData.GetCategory(codepoint));
            if (type != JoinT)
            {
                state = States[state, type].Next;
                break;
            }
        }

        var previous = -1;
        for (var i = 0; i < buffer.Length; i++)
        {
            var type = JoiningType(info[i].Codepoint, info[i].Category);
            if (type == JoinT)
            {
                actions[i] = NoAction;
                continue;
            }

            var entry = States[state, type];
            if (entry.Previous != NoAction && previous >= 0)
            {
                actions[previous] = entry.Previous;
            }

            actions[i] = entry.Current;
            previous = i;
            state = entry.Next;
        }

        foreach (var codepoint in After(text, end))
        {
            var type = JoiningType(codepoint, UnicodeData.GetCategory(codepoint));
            if (type == JoinT)
            {
                continue;
            }

            var entry = States[state, type];
            if (entry.Previous != NoAction && previous >= 0)
            {
                actions[previous] = entry.Previous;
            }

            break;
        }

        for (var i = 1; i < buffer.Length && plan.JoiningScript == "Mong"; i++)
        {
            if (info[i].Codepoint is >= 0x180B and <= 0x180D or 0x180F)
            {
                actions[i] = actions[i - 1];
            }
        }

        var masks = plan.JoiningMasks;
        for (var i = 0; i < buffer.Length; i++)
        {
            info[i].Mask |= actions[i] < masks.Length ? masks[actions[i]] : 0;
        }
    }

    public static uint[] Masks(ShapePlan plan)
    {
        var masks = new uint[ActionFeatures.Length];
        for (var k = 0; k < ActionFeatures.Length; k++)
        {
            var map = plan.GetFeature(ActionFeatures[k]);
            masks[k] = map == null ? 0 : (1u << map.Shift) & map.Mask;
        }

        return masks;
    }

    public static void ReorderMarks(GlyphBuffer buffer, int start, int end)
    {
        var infos = buffer.Info;
        var i = start;
        for (var cc = 220; cc <= 230; cc += 10)
        {
            while (i < end && infos[i].CombiningClass < cc)
            {
                i++;
            }

            if (i == end)
            {
                break;
            }

            if (infos[i].CombiningClass > cc)
            {
                continue;
            }

            var j = i;
            while (j < end && infos[j].CombiningClass == cc && Array.IndexOf(ModifierCombiningMarks, infos[j].Codepoint) >= 0)
            {
                j++;
            }

            if (i == j)
            {
                continue;
            }

            var cluster = int.MaxValue;
            for (var k = start; k < j; k++)
            {
                cluster = Math.Min(cluster, infos[k].Cluster);
            }

            var count = j - i;
            Array.Reverse(infos, start, i - start);
            Array.Reverse(infos, i, count);
            Array.Reverse(infos, start, j - start);
            for (var k = start; k < j; k++)
            {
                infos[k].Cluster = cluster;
                if (k < start + count)
                {
                    infos[k].CombiningClass = (byte)(cc == 220 ? 25 : 26);
                }
            }

            start += count;
            i = j;
        }
    }

    public static bool JoinsNext(string text, int index)
    {
        if (JoiningTypeAt(text, index) is not (JoinD or JoinL))
        {
            return false;
        }

        var next = index + (char.IsHighSurrogate(text[index]) && index + 1 < text.Length ? 2 : 1);
        while (next < text.Length && JoiningTypeAt(text, next) == JoinT)
        {
            next += char.IsHighSurrogate(text[next]) && next + 1 < text.Length ? 2 : 1;
        }

        return next < text.Length && JoiningTypeAt(text, next) is JoinD or JoinR or Alaph or DalathRish;
    }

    private static byte JoiningTypeAt(string text, int index)
    {
        var codepoint = char.IsHighSurrogate(text[index]) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1])
            ? char.ConvertToUtf32(text[index], text[index + 1])
            : text[index];
        return JoiningType(codepoint, UnicodeData.GetCategory(codepoint));
    }

    private static byte JoiningType(int codepoint, UnicodeCategory category)
    {
        var type = JoiningTypes.Value[codepoint];
        if (type != Unlisted)
        {
            return type;
        }

        return category is UnicodeCategory.NonSpacingMark or UnicodeCategory.EnclosingMark or UnicodeCategory.Format
            ? JoinT
            : JoinU;
    }

    private static byte ParseJoiningType(string value) => value switch
    {
        "L" => JoinL,
        "R" => JoinR,
        "D" or "C" => JoinD,
        "A" => Alaph,
        "S" => DalathRish,
        "T" => JoinT,
        _ => JoinU,
    };

    private static IEnumerable<int> Before(string text, int start)
    {
        var i = start - 1;
        for (var count = 0; count < ContextLength && i >= 0; count++)
        {
            if (char.IsLowSurrogate(text[i]) && i > 0 && char.IsHighSurrogate(text[i - 1]))
            {
                yield return char.ConvertToUtf32(text[i - 1], text[i]);
                i -= 2;
                continue;
            }

            yield return text[i];
            i--;
        }
    }

    private static IEnumerable<int> After(string text, int end)
    {
        var i = end;
        for (var count = 0; count < ContextLength && i < text.Length; count++)
        {
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                yield return char.ConvertToUtf32(text[i], text[i + 1]);
                i += 2;
                continue;
            }

            yield return text[i];
            i++;
        }
    }
}
