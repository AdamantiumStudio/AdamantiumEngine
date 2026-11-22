using Adamantium.Localization;

namespace Adamantium.Localization.Generators;

/// <summary>What the build of language files says - its problems - in the language of
/// <see cref="System.Globalization.CultureInfo.CurrentUICulture"/>; each method is one message.</summary>
public static class LanguageMessages
{
    /// <summary>The catalog the messages are read from.</summary>
    public static MessageCatalog Catalog { get; } = new(typeof(LanguageMessages).Assembly, nameof(LanguageMessages));

    public static string NoRootNamespace() => Say(nameof(NoRootNamespace));

    public static string LanguageFileUnnamed(string file, string extension) =>
        Say(nameof(LanguageFileUnnamed), new PhraseArgument("file", file), new PhraseArgument("extension", extension));

    public static string LanguageTableNameInvalid(string table) => Say(nameof(LanguageTableNameInvalid), new PhraseArgument("table", table));
    public static string LanguageNameInvalid(string language) => Say(nameof(LanguageNameInvalid), new PhraseArgument("language", language));
    public static string LanguageFileRoot(string root, string phrase) => Say(nameof(LanguageFileRoot), new PhraseArgument("root", root), new PhraseArgument("phrase", phrase));

    public static string LanguageRootAttribute(string root, string attribute) =>
        Say(nameof(LanguageRootAttribute), new PhraseArgument("root", root), new PhraseArgument("attribute", attribute));

    public static string LanguageFormatTwice(string format) => Say(nameof(LanguageFormatTwice), new PhraseArgument("format", format));

    public static string LanguageStrayElement(string element, string phrase, string format) =>
        Say(nameof(LanguageStrayElement), new PhraseArgument("element", element), new PhraseArgument("phrase", phrase), new PhraseArgument("format", format));

    public static string PhraseNeedsKey(string phrase) => Say(nameof(PhraseNeedsKey), new PhraseArgument("phrase", phrase));
    public static string PhraseKeyInvalid(string key) => Say(nameof(PhraseKeyInvalid), new PhraseArgument("key", key));
    public static string PhraseKeyTwice(string key) => Say(nameof(PhraseKeyTwice), new PhraseArgument("key", key));
    public static string PhraseHoldsElements(string key) => Say(nameof(PhraseHoldsElements), new PhraseArgument("key", key));
    public static string PhraseTextTwice(string key, string text) => Say(nameof(PhraseTextTwice), new PhraseArgument("key", key), new PhraseArgument("text", text));

    public static string PhraseCasesNeedChooser(string key, string count, string select) =>
        Say(nameof(PhraseCasesNeedChooser), new PhraseArgument("key", key), new PhraseArgument("count", count), new PhraseArgument("select", select));

    public static string PhraseStrayAttribute(string phrase, string text, string count, string select, string attribute) =>
        Say(nameof(PhraseStrayAttribute), new PhraseArgument("phrase", phrase), new PhraseArgument("text", text), new PhraseArgument("count", count), new PhraseArgument("select", select),
            new PhraseArgument("attribute", attribute));

    public static string PhraseTextAndCases(string key) => Say(nameof(PhraseTextAndCases), new PhraseArgument("key", key));
    public static string ChooserInvalid(string chooser, string example) => Say(nameof(ChooserInvalid), new PhraseArgument("chooser", chooser), new PhraseArgument("example", example));
    public static string NotAPluralForm(string name, string forms) => Say(nameof(NotAPluralForm), new PhraseArgument("name", name), new PhraseArgument("forms", forms));
    public static string PhraseNeedsOther(string key, string other) => Say(nameof(PhraseNeedsOther), new PhraseArgument("key", key), new PhraseArgument("other", other));

    public static string LanguageHasNoForm(string language, string form, string forms) =>
        Say(nameof(LanguageHasNoForm), new PhraseArgument("language", language), new PhraseArgument("form", form), new PhraseArgument("forms", forms));

    public static string PhraseLacksForms(string key, IReadOnlyCollection<string> forms, string language, string other) =>
        Say(forms.Count > 1 ? nameof(PhraseLacksForms) : "PhraseLacksForm", new PhraseArgument("key", key), new PhraseArgument("forms", string.Join(", ", forms)),
            new PhraseArgument("language", language), new PhraseArgument("other", other));

    public static string PluralRulesUnknown(string language, string one, string other) =>
        Say(nameof(PluralRulesUnknown), new PhraseArgument("language", language), new PhraseArgument("one", one), new PhraseArgument("other", other));

    public static string CaseNameInvalid(string name, string key) => Say(nameof(CaseNameInvalid), new PhraseArgument("name", name), new PhraseArgument("key", key));
    public static string PhraseNoCases(string key, string chooser) => Say(nameof(PhraseNoCases), new PhraseArgument("key", key), new PhraseArgument("chooser", chooser));
    public static string NotAFormat(string name, string formats) => Say(nameof(NotAFormat), new PhraseArgument("name", name), new PhraseArgument("formats", formats));
    public static string FirstDayOfWeekInvalid(string value) => Say(nameof(FirstDayOfWeekInvalid), new PhraseArgument("value", value));

    public static string LanguageInAnotherFile(string language, string table) =>
        Say(nameof(LanguageInAnotherFile), new PhraseArgument("language", language), new PhraseArgument("table", table));

    public static string KeyIsTableMember(string key, string table) => Say(nameof(KeyIsTableMember), new PhraseArgument("key", key), new PhraseArgument("table", table));
    public static string KeyNotInBase(string key, string file) => Say(nameof(KeyNotInBase), new PhraseArgument("key", key), new PhraseArgument("file", file));

    public static string ChooserDiffers(string key, string chooser, string other) =>
        Say(nameof(ChooserDiffers), new PhraseArgument("key", key), new PhraseArgument("chooser", chooser), new PhraseArgument("other", other));

    public static string KeyIsCaseName(string key) => Say(nameof(KeyIsCaseName), new PhraseArgument("key", key));

    public static string LacksCases(string key, IReadOnlyCollection<string> cases) =>
        Say(cases.Count > 1 ? nameof(LacksCases) : "LacksCase", new PhraseArgument("key", key), new PhraseArgument("cases", string.Join(", ", cases)));

    public static string TableNotFound(string table, string file, string language) =>
        Say(nameof(TableNotFound), new PhraseArgument("table", table), new PhraseArgument("file", file), new PhraseArgument("language", language));

    public static string TableAmbiguous(string table, string tables) => Say(nameof(TableAmbiguous), new PhraseArgument("table", table), new PhraseArgument("tables", tables));
    public static string TableHasNoKey(string table, string key) => Say(nameof(TableHasNoKey), new PhraseArgument("table", table), new PhraseArgument("key", key));
    public static string FormatsInLibrary() => Say(nameof(FormatsInLibrary));
    public static string FormatsTwice(string language) => Say(nameof(FormatsTwice), new PhraseArgument("language", language));
    public static string PlaceholdersExtra(string names) => Say(nameof(PlaceholdersExtra), new PhraseArgument("names", names));
    public static string PlaceholdersLacking(string names) => Say(nameof(PlaceholdersLacking), new PhraseArgument("names", names));

    public static string PlaceholderProblems(string key, IReadOnlyList<string> problems) => problems.Count > 1
        ? Say(nameof(PlaceholderProblems), new PhraseArgument("key", key), new PhraseArgument("first", problems[0]), new PhraseArgument("second", problems[1]))
        : Say("PlaceholderProblem", new PhraseArgument("key", key), new PhraseArgument("problem", problems[0]));

    public static string LanguageLacksStrings(string file, int count, string table, string shown, string language) =>
        Say(nameof(LanguageLacksStrings), new PhraseArgument("file", file), new PhraseArgument("count", count), new PhraseArgument("table", table), new PhraseArgument("shown", shown),
            new PhraseArgument("language", language));

    public static string AndMore(string shown, int count) => Say(nameof(AndMore), new PhraseArgument("shown", shown), new PhraseArgument("count", count));
    public static string BraceNotClosed() => Say(nameof(BraceNotClosed));
    public static string PlaceholderNoName(string text) => Say(nameof(PlaceholderNoName), new PhraseArgument("text", text));
    public static string BraceNotOpened() => Say(nameof(BraceNotOpened));

    private static string Say(string key, params PhraseArgument[] values) => Catalog.Say(key, values);
}
