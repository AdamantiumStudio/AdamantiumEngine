using System.Globalization;

namespace Adamantium.Localization;

/// <summary>What C# takes as a name, as the language specification gives it: a table's, a key's or a placeholder's name
/// becomes a member or a parameter of a generated table. Read without the compiler, so the running application can
/// read language files too.</summary>
internal static class CSharpIdentifiers
{
    // The reserved keywords, which have not changed since C# 1; every keyword added later is contextual and may name a
    // member. The four starting with "__" are the compiler's own.
    internal static readonly HashSet<string> Keywords =
    [
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class", "const",
        "continue", "decimal", "default", "delegate", "do", "double", "else", "enum", "event", "explicit", "extern",
        "false", "finally", "fixed", "float", "for", "foreach", "goto", "if", "implicit", "in", "int", "interface",
        "internal", "is", "lock", "long", "namespace", "new", "null", "object", "operator", "out", "override", "params",
        "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short", "sizeof",
        "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true", "try", "typeof", "uint", "ulong",
        "unchecked", "unsafe", "ushort", "using", "virtual", "void", "volatile", "while",
        "__arglist", "__makeref", "__reftype", "__refvalue",
    ];

    /// <summary>True when <paramref name="name"/> is spelled as an identifier: a letter or an underscore, then letters,
    /// digits, connectors, combining and formatting characters. A keyword is spelled so too.</summary>
    public static bool IsValid(string name)
    {
        if (string.IsNullOrEmpty(name) || !StartsIdentifier(name[0]))
        {
            return false;
        }

        for (var i = 1; i < name.Length; i++)
        {
            if (!ContinuesIdentifier(name[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>True when <paramref name="name"/> is a reserved keyword, which names nothing without an @.</summary>
    public static bool IsKeyword(string name) => Keywords.Contains(name);

    private static bool StartsIdentifier(char character) =>
        character == '_' || IsLetter(CharUnicodeInfo.GetUnicodeCategory(character));

    private static bool ContinuesIdentifier(char character)
    {
        var category = CharUnicodeInfo.GetUnicodeCategory(character);
        return IsLetter(category) || category is UnicodeCategory.DecimalDigitNumber or UnicodeCategory.ConnectorPunctuation
            or UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.Format;
    }

    private static bool IsLetter(UnicodeCategory category) => category is UnicodeCategory.UppercaseLetter
        or UnicodeCategory.LowercaseLetter or UnicodeCategory.TitlecaseLetter or UnicodeCategory.ModifierLetter
        or UnicodeCategory.OtherLetter or UnicodeCategory.LetterNumber;
}
