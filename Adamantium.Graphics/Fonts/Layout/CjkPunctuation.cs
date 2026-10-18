using System.Globalization;

namespace Adamantium.Graphics.Fonts.Layout;

// The classes of CJK punctuation CSS's text-spacing-trim works with, and the letters CJK lines justify between.
internal enum CjkPunctuationClass
{
    None,
    Opening,
    Closing,
    MiddleDot,
    IdeographicSpace,
}

internal static class CjkPunctuation
{
    // Opening and closing: brackets of the CJK block or fullwidth, and the curly quotes. A comma or full stop is closing,
    // drawn at one side, except in Traditional Chinese, where it is centered; a colon is a middle dot, except in
    // Simplified Chinese.
    public static CjkPunctuationClass Of(int codepoint, string language)
    {
        switch (codepoint)
        {
            case 0x3000:
                return CjkPunctuationClass.IdeographicSpace;
            case 0x2018 or 0x201C:
                return CjkPunctuationClass.Opening;
            case 0x2019 or 0x201D:
                return CjkPunctuationClass.Closing;
            case 0x00B7 or 0x2027 or 0x30FB:
                return CjkPunctuationClass.MiddleDot;
            case 0x3001 or 0x3002 or 0xFF0C or 0xFF0E:
                return IsTraditionalChinese(language) ? CjkPunctuationClass.MiddleDot : CjkPunctuationClass.Closing;
            case 0xFF1A or 0xFF1B:
                return IsSimplifiedChinese(language) ? CjkPunctuationClass.Closing : CjkPunctuationClass.MiddleDot;
        }

        var wide = codepoint is >= 0x3000 and <= 0x303F or >= 0xFF00 and <= 0xFF60 or >= 0xFFE0 and <= 0xFFE6;
        if (!wide)
        {
            return CjkPunctuationClass.None;
        }

        return CharUnicodeInfo.GetUnicodeCategory(codepoint) switch
        {
            UnicodeCategory.OpenPunctuation => CjkPunctuationClass.Opening,
            UnicodeCategory.ClosePunctuation => CjkPunctuationClass.Closing,
            _ => CjkPunctuationClass.None,
        };
    }

    // Whether the text holds any punctuation these rules reach: what a text without it skips them for.
    public static bool Appears(string text)
    {
        foreach (var c in text)
        {
            if (c is >= '　' and <= '〿' or >= '＀' and <= '｠' or '‘' or '’' or '“'
                or '”' or '·' or '‧' or '・')
            {
                return true;
            }
        }

        return false;
    }

    // An ideographic comma or full stop: what hangs at the end of a line.
    public static bool IsStopOrComma(int codepoint) => codepoint is 0x3001 or 0x3002 or 0xFF0C or 0xFF0E;

    // Han, kana, CJK punctuation and fullwidth forms: the letters a line of them is justified between.
    public static bool IsCjk(int codepoint) =>
        codepoint is >= 0x3000 and <= 0x30FF or >= 0x31F0 and <= 0x31FF or >= 0x3400 and <= 0x4DBF
            or >= 0x4E00 and <= 0x9FFF or >= 0xF900 and <= 0xFAFF or >= 0xFF00 and <= 0xFFEF or >= 0x20000 and <= 0x3FFFF;

    private static bool IsSimplifiedChinese(string language) =>
        language != null && (language.StartsWith("zh-Hans") || language is "zh" or "zh-CN" or "zh-SG");

    private static bool IsTraditionalChinese(string language) =>
        language != null && (language.StartsWith("zh-Hant") || language is "zh-TW" or "zh-HK" or "zh-MO");
}
