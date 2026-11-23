using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Adamantium.Core.Shared;

namespace Adamantium.Localization;

/// <summary>Reads a language file (<c>Table.language.alang</c>) - the build and the language server alike.</summary>
public static class LanguageFileParser
{
    public const string Extension = ".alang";

    /// <summary>The root element of a language file.</summary>
    public const string RootElement = "Language";

    /// <summary>The element of one string of the table: <c>&lt;Phrase Key="Close" Text="Close"/&gt;</c>, or with the
    /// text inside the tag when it runs over lines.</summary>
    public const string PhraseElement = "Phrase";

    /// <summary>The attribute of a phrase's text in the self-closing form.</summary>
    public const string TextAttribute = "Text";

    /// <summary>The attribute that makes a phrase change with a number: the placeholder whose number picks the form.
    /// Each form is then an attribute of its own, named as <see cref="PluralForm"/> names it:
    /// <c>&lt;Phrase Key="Files" Count="count" One="{count} file" Other="{count} files"/&gt;</c>.</summary>
    public const string CountAttribute = "Count";

    /// <summary>The attribute that makes a phrase change with a value: the placeholder whose value picks the case named
    /// after it - True or False, a member of an enum - and Other for any other value:
    /// <c>&lt;Phrase Key="Actions" Select="enabled" True="Actions enabled" False="Actions disabled"/&gt;</c>.</summary>
    public const string SelectAttribute = "Select";

    /// <summary>The case a value no other case names takes.</summary>
    public const string OtherCase = "Other";

    /// <summary>The element that sets how the language writes dates and numbers.</summary>
    public const string FormatElement = "Language.Format";

    /// <summary>The attributes <c>&lt;Language.Format&gt;</c> takes.</summary>
    public static readonly string[] FormatNames =
        ["ShortDate", "LongDate", "ShortTime", "LongTime", "DecimalSeparator", "GroupSeparator", "FirstDayOfWeek"];

    /// <summary>The names of the forms a phrase that changes with a number can take, as attributes.</summary>
    public static readonly string[] FormNames = Enum.GetNames(typeof(PluralForm));

    /// <summary>Parses <paramref name="content"/> of the file at <paramref name="path"/>; the folder is taken relative to
    /// <paramref name="projectDir"/>.</summary>
    public static LanguageFile Parse(string path, string content, string projectDir)
    {
        var problems = new List<LanguageProblem>();
        var entries = new List<LanguageEntry>();
        var format = new List<LanguageFormatValue>();
        var formatLine = 0;

        var relative = RelativePath(path, projectDir);
        var folder = (System.IO.Path.GetDirectoryName(relative) ?? string.Empty).Replace('\\', '/');
        var name = System.IO.Path.GetFileNameWithoutExtension(relative);
        string table = null;
        string language = null;

        var dot = name.LastIndexOf('.');
        if (dot <= 0 || dot == name.Length - 1)
        {
            problems.Add(new LanguageProblem("AUL001",
                LanguageMessages.LanguageFileUnnamed(System.IO.Path.GetFileName(relative), Extension),
                1, 1));
        }
        else
        {
            table = name.Substring(0, dot);
            language = name.Substring(dot + 1);
            if (!CSharpIdentifiers.IsValid(table) || CSharpIdentifiers.IsKeyword(table))
            {
                problems.Add(new LanguageProblem("AUL003", LanguageMessages.LanguageTableNameInvalid(table), 1, 1));
                table = null;
            }

            if (!IsLanguage(language))
            {
                problems.Add(new LanguageProblem("AUL002", LanguageMessages.LanguageNameInvalid(language), 1, 1));
                language = null;
            }
        }

        XDocument document;
        try
        {
            document = XDocument.Parse(content, LoadOptions.SetLineInfo);
        }
        catch (XmlException e)
        {
            problems.Add(new LanguageProblem("AUL004", e.Message, Math.Max(1, e.LineNumber), Math.Max(1, e.LinePosition)));
            return new LanguageFile(path, folder, table, language, entries, format, formatLine, problems);
        }

        var root = document.Root;
        if (root == null || root.Name.LocalName != RootElement)
        {
            problems.Add(Problem("AUL004", LanguageMessages.LanguageFileRoot(RootElement, PhraseElement), root));
            return new LanguageFile(path, folder, table, language, entries, format, formatLine, problems);
        }

        foreach (var attribute in root.Attributes().Where(a => !a.IsNamespaceDeclaration))
        {
            problems.Add(Problem("AUL004", LanguageMessages.LanguageRootAttribute(RootElement, attribute.Name.LocalName), attribute));
        }

        foreach (var element in root.Elements())
        {
            switch (element.Name.LocalName)
            {
                case PhraseElement:
                    ReadEntry(element, language, entries, problems);
                    break;
                case FormatElement:
                    if (formatLine != 0)
                    {
                        problems.Add(Problem("AUL009", LanguageMessages.LanguageFormatTwice(FormatElement), element));
                        break;
                    }

                    formatLine = Line(element);
                    ReadFormat(element, format, problems);
                    break;
                default:
                    problems.Add(Problem("AUL004", LanguageMessages.LanguageStrayElement(element.Name.LocalName, PhraseElement, FormatElement), element));
                    break;
            }
        }

        return new LanguageFile(path, folder, table, language, entries, format, formatLine, problems);
    }

    /// <summary>The namespace a table in <paramref name="folder"/> is generated under: the root namespace and the folder,
    /// as for a view.</summary>
    public static string TableNamespace(string folder, string rootNamespace) =>
        string.IsNullOrEmpty(folder) ? rootNamespace : $"{rootNamespace}.{folder.ToNamespace()}";

    /// <summary>The base language of a project whose NeutralLanguage is <paramref name="neutralLanguage"/>: English when it
    /// sets none. The tables are generated from the files in it.</summary>
    public static string BaseLanguage(string neutralLanguage) =>
        string.IsNullOrWhiteSpace(neutralLanguage) ? "en" : neutralLanguage.Trim();

    /// <summary>The names of the placeholders <paramref name="entry"/> fills, in the order its table's string takes them;
    /// empty for a phrase whose placeholders cannot be read.</summary>
    public static IReadOnlyList<string> PlaceholdersOf(LanguageEntry entry) =>
        Placeholders.TryRead(entry, out var names, out _) ? names : [];

    private static void ReadEntry(XElement element, string language, List<LanguageEntry> entries, List<LanguageProblem> problems)
    {
        var key = element.Attribute("Key")?.Value;
        if (string.IsNullOrEmpty(key))
        {
            problems.Add(Problem("AUL004", LanguageMessages.PhraseNeedsKey(PhraseElement), element));
            return;
        }

        if (!CSharpIdentifiers.IsValid(key) || CSharpIdentifiers.IsKeyword(key))
        {
            problems.Add(Problem("AUL003", LanguageMessages.PhraseKeyInvalid(key), element));
            return;
        }

        if (entries.Any(e => e.Key == key))
        {
            problems.Add(Problem("AUL004", LanguageMessages.PhraseKeyTwice(key), element));
            return;
        }

        if (element.HasElements)
        {
            problems.Add(Problem("AUL004", LanguageMessages.PhraseHoldsElements(key), element));
            return;
        }

        // A phrase chosen by a value names its cases after the values, and beside Select neither Text nor Count means
        // anything else - so there they are cases, as an enum may have members of those names.
        var select = element.Attribute(SelectAttribute);
        var count = select == null ? element.Attribute(CountAttribute) : null;

        // The text in Text, the short form of a self-closing tag, or inside the tag, where it may run over lines.
        var text = select == null ? element.Attribute(TextAttribute)?.Value : null;
        if (text != null && !string.IsNullOrWhiteSpace(element.Value))
        {
            problems.Add(Problem("AUL004", LanguageMessages.PhraseTextTwice(key, TextAttribute), element));
            return;
        }

        var cases = element.Attributes()
            .Where(a => select != null
                ? a.Name.LocalName is not ("Key" or SelectAttribute)
                : a.Name.LocalName is not ("Key" or TextAttribute or CountAttribute or SelectAttribute))
            .ToList();
        if (count == null && select == null)
        {
            foreach (var attribute in cases)
            {
                problems.Add(Enum.TryParse<PluralForm>(attribute.Name.LocalName, out _)
                    ? Problem("AUL010", LanguageMessages.PhraseCasesNeedChooser(key, CountAttribute, SelectAttribute), attribute)
                    : Problem("AUL004", LanguageMessages.PhraseStrayAttribute(PhraseElement, TextAttribute, CountAttribute,
                        SelectAttribute, attribute.Name.LocalName), attribute));
            }

            if (cases.Count == 0)
            {
                entries.Add(new LanguageEntry(key, text ?? element.Value, Line(element), Column(element)));
            }

            return;
        }

        if (text != null || !string.IsNullOrWhiteSpace(element.Value))
        {
            problems.Add(Problem("AUL004", LanguageMessages.PhraseTextAndCases(key), element));
            return;
        }

        var chooser = count ?? select;
        if (!CSharpIdentifiers.IsValid(chooser.Value))
        {
            problems.Add(Problem("AUL010", LanguageMessages.ChooserInvalid(chooser.Name.LocalName, count != null ? "count" : "state"), chooser));
            return;
        }

        if (select != null)
        {
            ReadSelected(element, key, select.Value, cases, entries, problems);
            return;
        }

        foreach (var stray in cases.Where(c => !Enum.TryParse<PluralForm>(c.Name.LocalName, out _)))
        {
            problems.Add(Problem("AUL010", LanguageMessages.NotAPluralForm(stray.Name.LocalName, string.Join(", ", FormNames)), stray));
        }

        var forms = cases
            .Select(a => (Attribute: a, IsForm: Enum.TryParse<PluralForm>(a.Name.LocalName, out var form), Form: form))
            .Where(a => a.IsForm)
            .ToList();

        // An empty form is one not written yet, like an empty text; a phrase with none written is not translated yet.
        var given = forms.Where(f => !string.IsNullOrEmpty(f.Attribute.Value)).OrderBy(f => f.Form).ToList();
        if (given.Count > 0)
        {
            if (given.All(f => f.Form != PluralForm.Other))
            {
                problems.Add(Problem("AUL010", LanguageMessages.PhraseNeedsOther(key, nameof(PluralForm.Other)), element));
                return;
            }

            if (language != null)
            {
                var spoken = PluralRules.FormsOf(language);
                foreach (var stray in given.Where(f => !spoken.Contains(f.Form)))
                {
                    problems.Add(Problem("AUL010", LanguageMessages.LanguageHasNoForm(NameOf(language), stray.Form.ToString(), string.Join(", ", spoken)), stray.Attribute));
                }

                var lacking = spoken.Where(s => given.All(f => f.Form != s)).ToList();
                if (lacking.Count > 0)
                {
                    problems.Add(new LanguageProblem("AUL010",
                        LanguageMessages.PhraseLacksForms(key, lacking.Select(form => form.ToString()).ToList(), NameOf(language),
                            nameof(PluralForm.Other)),
                        Line(element), Column(element), false));
                }

                if (!PluralRules.Knows(language))
                {
                    problems.Add(new LanguageProblem("AUL010",
                        LanguageMessages.PluralRulesUnknown(language, nameof(PluralForm.One), nameof(PluralForm.Other)),
                        Line(element), Column(element), false));
                }
            }
        }

        var other = given.FirstOrDefault(f => f.Form == PluralForm.Other).Attribute?.Value ?? string.Empty;
        entries.Add(new LanguageEntry(key, other, count.Value, true,
            given.Select(f => new LanguageCase(f.Form.ToString(), f.Attribute.Value)).ToList(), Line(element), Column(element)));
    }

    // A case per value: named after it, so a case's name is a member's name - and Other for any value no case names.
    private static void ReadSelected(XElement element, string key, string chooser, List<XAttribute> cases,
        List<LanguageEntry> entries, List<LanguageProblem> problems)
    {
        if (cases.FirstOrDefault(c => !CSharpIdentifiers.IsValid(c.Name.LocalName) || CSharpIdentifiers.IsKeyword(c.Name.LocalName) ||
                                      c.Name.LocalName == key) is { } bad)
        {
            problems.Add(Problem("AUL010", LanguageMessages.CaseNameInvalid(bad.Name.LocalName, key), bad));
            return;
        }

        if (cases.Count == 0)
        {
            problems.Add(Problem("AUL010", LanguageMessages.PhraseNoCases(key, chooser), element));
            return;
        }

        var given = cases.Where(c => !string.IsNullOrEmpty(c.Value)).ToList();
        var other = given.FirstOrDefault(c => c.Name.LocalName == OtherCase)?.Value ?? given.FirstOrDefault()?.Value ?? string.Empty;
        entries.Add(new LanguageEntry(key, other, chooser, false,
            given.Select(c => new LanguageCase(c.Name.LocalName, c.Value)).ToList(), Line(element), Column(element)));
    }

    private static string NameOf(string language)
    {
        try
        {
            return CultureInfo.GetCultureInfo(language).EnglishName;
        }
        catch (CultureNotFoundException)
        {
            return language;
        }
    }

    private static void ReadFormat(XElement element, List<LanguageFormatValue> format, List<LanguageProblem> problems)
    {
        foreach (var attribute in element.Attributes())
        {
            var name = attribute.Name.LocalName;
            if (!FormatNames.Contains(name))
            {
                problems.Add(Problem("AUL009", LanguageMessages.NotAFormat(name, string.Join(", ", FormatNames)), attribute));
                continue;
            }

            if (name == "FirstDayOfWeek" && !Enum.TryParse<DayOfWeek>(attribute.Value, out _))
            {
                problems.Add(Problem("AUL009", LanguageMessages.FirstDayOfWeekInvalid(attribute.Value), attribute));
                continue;
            }

            format.Add(new LanguageFormatValue(name, attribute.Value));
        }
    }

    private static bool IsLanguage(string language)
    {
        try
        {
            return CultureInfo.GetCultureInfo(language).Name.Length > 0;
        }
        catch (CultureNotFoundException)
        {
            return false;
        }
    }

    private static string RelativePath(string path, string projectDir)
    {
        var relative = string.IsNullOrEmpty(projectDir) ? path : path.Replace(projectDir, string.Empty);
        return relative.Replace('\\', '/').TrimStart('/');
    }

    private static LanguageProblem Problem(string id, string message, XObject node) =>
        new(id, message, Line(node), Column(node));

    private static int Line(XObject node) => node is IXmlLineInfo info && info.HasLineInfo() ? info.LineNumber : 1;

    private static int Column(XObject node) => node is IXmlLineInfo info && info.HasLineInfo() ? info.LinePosition : 1;
}
