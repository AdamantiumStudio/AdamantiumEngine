using System.Globalization;
using System.Reflection;

namespace Adamantium.Localization;

/// <summary>The application's language: the one its string tables speak and whose rules write its dates and numbers.
/// It is the application's own setting - the operating system's language and region are never read.</summary>
public static class Languages
{
    private static readonly object Gate = new();
    private static readonly List<LocalizedStrings> Tables = [];
    private static readonly List<(Assembly Assembly, string Language)> Declared = [];
    private static readonly Dictionary<string, LanguageFormat> Formats = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, CultureInfo> Cultures = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<(Type Table, string Language), IReadOnlyDictionary<string, string>> Translations = new();
    private static string _current;

    /// <summary>Raised after <see cref="Current"/> changes, once every string table has told its bindings: what follows
    /// the language without a table - text written in the language, numbers and dates in its culture - writes itself
    /// again.</summary>
    public static event EventHandler Changed;

    /// <summary>The language the application shows, by name ("en", "ru"); null shows every table in its base
    /// language. Setting it switches every table at once, while the application runs.</summary>
    public static string Current
    {
        get => _current;
        set
        {
            var language = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            LocalizedStrings[] tables;
            lock (Gate)
            {
                if (string.Equals(_current, language, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                _current = language;
                tables = Tables.ToArray();
            }

            foreach (var table in tables)
            {
                table.OnLanguageChanged();
            }

            Changed?.Invoke(null, EventArgs.Empty);
        }
    }

    /// <summary>How the current language writes dates and numbers, with the application's formats for it applied.</summary>
    public static CultureInfo Culture => CultureOf(Current);

    /// <summary>The languages the application is written in, its base language first: those of the entry assembly's
    /// string tables, or of every table when it has none.</summary>
    public static IReadOnlyList<string> Available
    {
        get
        {
            lock (Gate)
            {
                var entry = Assembly.GetEntryAssembly();
                var own = Declared.Where(d => d.Assembly == entry).Select(d => d.Language).ToList();
                var languages = own.Count > 0 ? own : Declared.Select(d => d.Language).ToList();
                return languages.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            }
        }
    }

    /// <summary>How <paramref name="language"/> writes dates and numbers in this application; the invariant culture
    /// for null.</summary>
    public static CultureInfo CultureOf(string language)
    {
        if (string.IsNullOrEmpty(language))
        {
            return CultureInfo.InvariantCulture;
        }

        lock (Gate)
        {
            if (Cultures.TryGetValue(language, out var cached))
            {
                return cached;
            }

            var culture = new CultureInfo(language);
            if (Formats.TryGetValue(language, out var format))
            {
                format.ApplyTo(culture);
            }

            cached = CultureInfo.ReadOnly(culture);
            Cultures[language] = cached;
            return cached;
        }
    }

    /// <summary>The culture a binding or a control writes with: <see cref="Culture"/> when <paramref name="culture"/> is
    /// unset, the invariant culture for "Invariant", else the named culture with the application's formats.</summary>
    public static CultureInfo CultureFor(string culture)
    {
        if (string.IsNullOrEmpty(culture))
        {
            return Culture;
        }

        return string.Equals(culture, "Invariant", StringComparison.OrdinalIgnoreCase)
            ? CultureInfo.InvariantCulture
            : CultureOf(culture);
    }

    /// <summary>The application's formats for <paramref name="language"/>. Called by the code generated from its
    /// language files.</summary>
    public static void SetFormat(string language, LanguageFormat format)
    {
        lock (Gate)
        {
            Formats[language] = format;
            Cultures.Remove(language);
        }
    }

    /// <summary>The languages an assembly's string tables are written in. Called by the generated code when the
    /// assembly loads.</summary>
    public static void Declare(Assembly assembly, params string[] languages)
    {
        lock (Gate)
        {
            foreach (var language in languages)
            {
                if (!Declared.Contains((assembly, language)))
                {
                    Declared.Add((assembly, language));
                }
            }
        }
    }

    /// <summary>A translation of another assembly's table into <paramref name="language"/>, by key. Called by the code
    /// generated from an application's language file for that table; it wins over the table's own strings. A string
    /// written in cases is given per case, under its key and the case - <c>Files.Few</c>, <c>Actions.True</c> - and the
    /// placeholder that chooses under <c>Files.Count</c> for a number or <c>Actions.Select</c> for any other value.
    /// </summary>
    public static void AddTranslation(Type table, string language, IReadOnlyDictionary<string, string> strings)
    {
        lock (Gate)
        {
            Translations[(table, language.ToLowerInvariant())] = strings;
        }
    }

    /// <summary>The phrase <paramref name="key"/> of <paramref name="table"/> in the application's language, its
    /// placeholders filled by name - for code that names a thing once, as a new socket is named; what is shown follows
    /// the language with <c>{Localize}</c>. A key the table lacks is given back as it is.</summary>
    public static string Say(LocalizedStrings table, string key, params PhraseArgument[] arguments)
    {
        if (table == null || string.IsNullOrEmpty(key))
        {
            return key;
        }

        return table.TryText(key, arguments, Culture, out var text) ? text : key;
    }

    internal static void Register(LocalizedStrings table)
    {
        lock (Gate)
        {
            Tables.Add(table);
        }
    }

    /// <summary>The placeholder a translation of <paramref name="table"/> into <paramref name="language"/> counts
    /// <paramref name="key"/> by, where the table itself does not.</summary>
    internal static string ChooserOf(Type table, string language, string key)
    {
        lock (Gate)
        {
            if (!Translations.TryGetValue((table, language.ToLowerInvariant()), out var strings))
            {
                return null;
            }

            return strings.TryGetValue($"{key}.{SelectedBy}", out var select) ? select
                : strings.TryGetValue($"{key}.{CountedBy}", out var count) ? count
                : null;
        }
    }

    internal static bool TryTranslate(Type table, string language, string key, object choice, out string value)
    {
        lock (Gate)
        {
            if (Translations.TryGetValue((table, language.ToLowerInvariant()), out var strings) &&
                (strings.TryGetValue(key, out value) ||
                 CaseOf(strings, language, key, choice) is { } chosen && strings.TryGetValue($"{key}.{chosen}", out value) ||
                 strings.TryGetValue($"{key}.{PhraseCases.Other}", out value)))
            {
                return true;
            }
        }

        value = null;
        return false;
    }

    // Select is asked first: a phrase chosen by a value may have a case named Count.
    private static string CaseOf(IReadOnlyDictionary<string, string> strings, string language, string key, object choice) =>
        strings.ContainsKey($"{key}.{SelectedBy}") ? PhraseCases.Of(choice)
        : strings.ContainsKey($"{key}.{CountedBy}") ? PluralRules.FormOf(language, choice).ToString()
        : null;

    // How a translation's dictionary says what chooses a string's case: by a number, or by a value.
    private const string CountedBy = "Count";
    private const string SelectedBy = "Select";
}
