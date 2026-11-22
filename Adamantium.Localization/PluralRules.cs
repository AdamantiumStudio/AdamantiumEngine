using System.Globalization;

namespace Adamantium.Localization;

/// <summary>Which form of a phrase a number takes in a language, by the Unicode CLDR plural rules: "1 file", "2 files" in
/// English, "1 файл", "2 файла", "5 файлов" in Russian. The build asks which forms a language has; the running
/// application asks which one a number takes.</summary>
public static class PluralRules
{
    private enum Rule
    {
        OtherOnly,
        OneIfIntegerOne,
        OneIfOne,
        OneIfZeroOrOne,
        OneIfZeroOrOneAtAll,
        EastSlavic,
        Polish,
        Czech,
        Lithuanian,
        Latvian,
        Romanian,
        Slovenian,
        SerboCroatian,
        Arabic,
        Hebrew
    }

    private static readonly Dictionary<string, Rule> Rules = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ja"] = Rule.OtherOnly, ["zh"] = Rule.OtherOnly, ["ko"] = Rule.OtherOnly, ["vi"] = Rule.OtherOnly,
        ["th"] = Rule.OtherOnly, ["id"] = Rule.OtherOnly, ["ms"] = Rule.OtherOnly, ["lo"] = Rule.OtherOnly,
        ["my"] = Rule.OtherOnly, ["km"] = Rule.OtherOnly, ["jv"] = Rule.OtherOnly, ["yue"] = Rule.OtherOnly,

        ["en"] = Rule.OneIfIntegerOne, ["de"] = Rule.OneIfIntegerOne, ["nl"] = Rule.OneIfIntegerOne,
        ["sv"] = Rule.OneIfIntegerOne, ["it"] = Rule.OneIfIntegerOne, ["ca"] = Rule.OneIfIntegerOne,
        ["gl"] = Rule.OneIfIntegerOne, ["fi"] = Rule.OneIfIntegerOne, ["et"] = Rule.OneIfIntegerOne,
        ["ur"] = Rule.OneIfIntegerOne, ["sw"] = Rule.OneIfIntegerOne, ["pt-PT"] = Rule.OneIfIntegerOne,

        ["es"] = Rule.OneIfOne, ["el"] = Rule.OneIfOne, ["hu"] = Rule.OneIfOne, ["tr"] = Rule.OneIfOne,
        ["bg"] = Rule.OneIfOne, ["nb"] = Rule.OneIfOne, ["nn"] = Rule.OneIfOne, ["no"] = Rule.OneIfOne,
        ["da"] = Rule.OneIfOne, ["az"] = Rule.OneIfOne, ["kk"] = Rule.OneIfOne, ["ka"] = Rule.OneIfOne,
        ["af"] = Rule.OneIfOne, ["eu"] = Rule.OneIfOne, ["sq"] = Rule.OneIfOne, ["ta"] = Rule.OneIfOne,
        ["te"] = Rule.OneIfOne, ["ml"] = Rule.OneIfOne, ["mn"] = Rule.OneIfOne, ["uz"] = Rule.OneIfOne,

        ["fr"] = Rule.OneIfZeroOrOne, ["pt"] = Rule.OneIfZeroOrOne, ["hy"] = Rule.OneIfZeroOrOne,

        ["hi"] = Rule.OneIfZeroOrOneAtAll, ["bn"] = Rule.OneIfZeroOrOneAtAll, ["fa"] = Rule.OneIfZeroOrOneAtAll,
        ["gu"] = Rule.OneIfZeroOrOneAtAll, ["kn"] = Rule.OneIfZeroOrOneAtAll, ["am"] = Rule.OneIfZeroOrOneAtAll,
        ["zu"] = Rule.OneIfZeroOrOneAtAll,

        ["ru"] = Rule.EastSlavic, ["be"] = Rule.EastSlavic,
        ["pl"] = Rule.Polish,
        ["cs"] = Rule.Czech, ["sk"] = Rule.Czech,
        ["lt"] = Rule.Lithuanian,
        ["lv"] = Rule.Latvian,
        ["ro"] = Rule.Romanian,
        ["sl"] = Rule.Slovenian,
        ["hr"] = Rule.SerboCroatian, ["sr"] = Rule.SerboCroatian, ["bs"] = Rule.SerboCroatian,
        ["ar"] = Rule.Arabic,
        ["he"] = Rule.Hebrew,
    };

    /// <summary>Whether the rules of <paramref name="language"/> are known. One that is not is taken as English is:
    /// One for 1, Other for everything else.</summary>
    public static bool Knows(string language) => TryRule(language, out _);

    /// <summary>The forms <paramref name="language"/> uses, in the order of <see cref="PluralForm"/>. Other is always
    /// among them.</summary>
    public static IReadOnlyList<PluralForm> FormsOf(string language) => (TryRule(language, out var rule) ? rule : Rule.OneIfIntegerOne) switch
    {
        Rule.OtherOnly => [PluralForm.Other],
        Rule.EastSlavic or Rule.Polish or Rule.Czech or Rule.Lithuanian =>
            [PluralForm.One, PluralForm.Few, PluralForm.Many, PluralForm.Other],
        Rule.Latvian => [PluralForm.Zero, PluralForm.One, PluralForm.Other],
        Rule.Romanian or Rule.SerboCroatian => [PluralForm.One, PluralForm.Few, PluralForm.Other],
        Rule.Slovenian => [PluralForm.One, PluralForm.Two, PluralForm.Few, PluralForm.Other],
        Rule.Arabic => [PluralForm.Zero, PluralForm.One, PluralForm.Two, PluralForm.Few, PluralForm.Many, PluralForm.Other],
        Rule.Hebrew => [PluralForm.One, PluralForm.Two, PluralForm.Other],
        _ => [PluralForm.One, PluralForm.Other],
    };

    /// <summary>The form <paramref name="number"/> takes in <paramref name="language"/>. A whole number and one with
    /// visible decimals can differ ("1 file", "1.0 files"), so a <see cref="decimal"/> keeps the decimals it was given;
    /// anything that is not a number takes Other.</summary>
    public static PluralForm FormOf(string language, object number)
    {
        if (!TryOperands(number, out var n, out var i, out var v, out var f))
        {
            return PluralForm.Other;
        }

        var rule = TryRule(language, out var known) ? known : Rule.OneIfIntegerOne;
        return rule switch
        {
            Rule.OtherOnly => PluralForm.Other,
            Rule.OneIfIntegerOne => i == 1 && v == 0 ? PluralForm.One : PluralForm.Other,
            Rule.OneIfOne => n == 1 ? PluralForm.One : PluralForm.Other,
            Rule.OneIfZeroOrOne => i is 0 or 1 ? PluralForm.One : PluralForm.Other,
            Rule.OneIfZeroOrOneAtAll => i == 0 || n == 1 ? PluralForm.One : PluralForm.Other,
            Rule.EastSlavic => v != 0 ? PluralForm.Other
                : i % 10 == 1 && i % 100 != 11 ? PluralForm.One
                : i % 10 is >= 2 and <= 4 && i % 100 is < 12 or > 14 ? PluralForm.Few
                : PluralForm.Many,
            Rule.Polish => v != 0 ? PluralForm.Other
                : i == 1 ? PluralForm.One
                : i % 10 is >= 2 and <= 4 && i % 100 is < 12 or > 14 ? PluralForm.Few
                : PluralForm.Many,
            Rule.Czech => v != 0 ? PluralForm.Many
                : i == 1 ? PluralForm.One
                : i is >= 2 and <= 4 ? PluralForm.Few
                : PluralForm.Other,
            Rule.Lithuanian => f != 0 ? PluralForm.Many
                : n % 10 == 1 && n % 100 is < 11 or > 19 ? PluralForm.One
                : n % 10 is >= 2 and <= 9 && n % 100 is < 11 or > 19 ? PluralForm.Few
                : PluralForm.Other,
            Rule.Latvian => n % 10 == 0 || n % 100 is >= 11 and <= 19 || v == 2 && f % 100 is >= 11 and <= 19 ? PluralForm.Zero
                : n % 10 == 1 && n % 100 != 11 || v == 2 && f % 10 == 1 && f % 100 != 11 || v != 2 && f % 10 == 1 ? PluralForm.One
                : PluralForm.Other,
            Rule.Romanian => i == 1 && v == 0 ? PluralForm.One
                : v != 0 || n == 0 || n % 100 is >= 2 and <= 19 ? PluralForm.Few
                : PluralForm.Other,
            Rule.Slovenian => v != 0 ? PluralForm.Few
                : i % 100 == 1 ? PluralForm.One
                : i % 100 == 2 ? PluralForm.Two
                : i % 100 is 3 or 4 ? PluralForm.Few
                : PluralForm.Other,
            Rule.SerboCroatian => v == 0 && i % 10 == 1 && i % 100 != 11 || f % 10 == 1 && f % 100 != 11 ? PluralForm.One
                : v == 0 && i % 10 is >= 2 and <= 4 && i % 100 is < 12 or > 14 ||
                  f % 10 is >= 2 and <= 4 && f % 100 is < 12 or > 14 ? PluralForm.Few
                : PluralForm.Other,
            Rule.Arabic => n == 0 ? PluralForm.Zero
                : n == 1 ? PluralForm.One
                : n == 2 ? PluralForm.Two
                : n % 100 is >= 3 and <= 10 ? PluralForm.Few
                : n % 100 is >= 11 and <= 99 ? PluralForm.Many
                : PluralForm.Other,
            Rule.Hebrew => i == 1 && v == 0 ? PluralForm.One
                : i == 2 && v == 0 ? PluralForm.Two
                : PluralForm.Other,
            _ => PluralForm.Other,
        };
    }

    // The language's own rules, else those of the language it is a variant of: "pt-PT" has its own, "ru-RU" is "ru".
    private static bool TryRule(string language, out Rule rule)
    {
        for (var name = language; !string.IsNullOrEmpty(name); name = ParentOf(name))
        {
            if (Rules.TryGetValue(name, out rule))
            {
                return true;
            }
        }

        rule = Rule.OtherOnly;
        return false;
    }

    private static string ParentOf(string language)
    {
        var dash = language.LastIndexOfAny(['-', '_']);
        return dash > 0 ? language.Substring(0, dash) : null;
    }

    // The CLDR operands: n the absolute value, i its whole part, v how many decimals are shown, f those decimals.
    private static bool TryOperands(object number, out decimal n, out decimal i, out int v, out decimal f)
    {
        decimal value;
        switch (number)
        {
            case sbyte or byte or short or ushort or int or uint or long:
                value = Convert.ToDecimal(number, CultureInfo.InvariantCulture);
                break;
            case ulong whole:
                value = whole;
                break;
            case decimal exact:
                value = exact;
                break;
            case double or float:
                var real = Convert.ToDouble(number, CultureInfo.InvariantCulture);
                if (double.IsNaN(real) || double.IsInfinity(real) ||
                    !decimal.TryParse(real.ToString("R", CultureInfo.InvariantCulture), NumberStyles.Float,
                        CultureInfo.InvariantCulture, out value))
                {
                    n = i = f = 0;
                    v = 0;
                    return false;
                }

                break;
            case string text when decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var written):
                value = written;
                break;
            default:
                n = i = f = 0;
                v = 0;
                return false;
        }

        n = Math.Abs(value);
        i = decimal.Truncate(n);
        v = (decimal.GetBits(n)[3] >> 16) & 0xFF;
        f = n - i;
        for (var digit = 0; digit < v; digit++)
        {
            f *= 10;
        }

        return true;
    }
}
