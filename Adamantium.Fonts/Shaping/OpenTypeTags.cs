using System;
using System.Collections.Generic;
using Adamantium.Fonts.Common;

namespace Adamantium.Fonts.Shaping;

internal static class OpenTypeTags
{
    private static readonly Lazy<Dictionary<string, string>> TwoLetterLanguages = new(LoadTwoLetterLanguages);

    // The tags a font may have the script under, in the order HarfBuzz tries them: the Indic scripts and Myanmar under
    // their newer tags first ('dev3', 'dev2', then 'deva'; 'mym2', then 'mymr').
    public static string[] ScriptTags(string script)
    {
        var tag = ScriptTag(script);
        var newer = script switch
        {
            "Beng" => "bng2",
            "Deva" => "dev2",
            "Gujr" => "gjr2",
            "Guru" => "gur2",
            "Knda" => "knd2",
            "Mlym" => "mlm2",
            "Orya" => "ory2",
            "Taml" => "tml2",
            "Telu" => "tel2",
            "Mymr" => "mym2",
            _ => null,
        };

        if (newer == null)
        {
            return [tag];
        }

        return newer == "mym2" ? [newer, tag] : [newer.Substring(0, 3) + "3", newer, tag];
    }

    private static string ScriptTag(string script)
    {
        switch (script)
        {
            case null:
                return "DFLT";
            case "Hira":
            case "Kana":
            case "Hrkt":
                return "kana";
            case "Laoo":
                return "lao ";
            case "Yiii":
                return "yi  ";
            case "Nkoo":
                return "nko ";
            case "Vaii":
                return "vai ";
        }

        return char.ToLowerInvariant(script[0]) + script.Substring(1);
    }

    public static string LanguageTag(string language)
    {
        if (string.IsNullOrEmpty(language))
        {
            return null;
        }

        var subtags = language.ToLowerInvariant().Split('-', '_');
        var primary = subtags[0];
        if (primary is "zh" or "zho" or "cmn")
        {
            return ChineseTag(subtags);
        }

        if (primary.Length == 2 && TwoLetterLanguages.Value.TryGetValue(primary, out var threeLetter))
        {
            primary = threeLetter;
        }

        return LanguageTags.TryGetIsoLanguage(primary, out var tag) ? tag.Tag : null;
    }

    private static string ChineseTag(string[] subtags)
    {
        string script = null;
        string region = null;
        for (var i = 1; i < subtags.Length; i++)
        {
            if (subtags[i].Length == 4)
            {
                script = subtags[i];
            }
            else if (subtags[i].Length is 2 or 3)
            {
                region = subtags[i];
            }
        }

        if (script == "hans")
        {
            return "ZHS ";
        }

        switch (region)
        {
            case "hk":
                return "ZHH ";
            case "mo":
                return "ZHTM";
            case "tw":
                return "ZHT ";
        }

        return script == "hant" ? "ZHT " : "ZHS ";
    }

    private static Dictionary<string, string> LoadTwoLetterLanguages()
    {
        var map = new Dictionary<string, string>();
        foreach (var line in DataFile.ReadLines("Iso639.dat"))
        {
            var fields = line.Split(';');
            map[fields[0]] = fields[1];
        }

        return map;
    }
}
