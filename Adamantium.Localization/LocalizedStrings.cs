using System.ComponentModel;
using System.Globalization;

namespace Adamantium.Localization;

/// <summary>Base of the classes generated from language files: each string in the application's language, else in a
/// parent language ("ru" for "ru-RU"), else in the table's base language. Tells its bindings when the language
/// changes. A table also implements <see cref="ILanguageTable"/>, which this class reads its strings through.</summary>
public abstract class LocalizedStrings : INotifyPropertyChanged
{
    private PropertyChangedEventHandler _propertyChanged;
    private string _spoken;

    protected LocalizedStrings()
    {
        Languages.Register(this);
    }

    event PropertyChangedEventHandler INotifyPropertyChanged.PropertyChanged
    {
        add => _propertyChanged += value;
        remove => _propertyChanged -= value;
    }

    /// <summary>The string <paramref name="key"/> in the application's language: a translation another assembly gave
    /// the table first, then the table's own.</summary>
    protected string Localized(string key) => Text(key, null);

    /// <summary>The string <paramref name="key"/> with its placeholders filled, in the application's language - in the
    /// case its choosing value takes, for a string written in cases.</summary>
    protected string Localized(string key, params object[] arguments) =>
        string.Format(Languages.Culture, Text(key, ChoiceOf(key, arguments)), arguments);

    /// <summary>The string <paramref name="key"/> with each placeholder filled by the argument of its name - none fills
    /// it with null - written the way <paramref name="culture"/> writes numbers and dates; false for a key the table
    /// lacks.</summary>
    public bool TryText(string key, IReadOnlyList<PhraseArgument> arguments, CultureInfo culture, out string text)
    {
        var table = (ILanguageTable)this;
        if (!table.Keys.Contains(key))
        {
            text = null;
            return false;
        }

        var names = table.PlaceholdersOf(key);
        var values = new object[names.Count];
        for (var i = 0; i < names.Count; i++)
        {
            values[i] = PhraseArgument.ValueOf(arguments, names[i]);
        }

        text = names.Count == 0
            ? Localized(key)
            : string.Format(culture ?? Languages.Culture, Text(key, ChoiceOf(key, values)), values);
        return true;
    }

    private string Text(string key, object choice) =>
        Translated(key, choice) ?? ((ILanguageTable)this).Find(key, _spoken ??= Answering(Languages.Current), choice);

    // The value that chooses the case of a string written in cases: that of its choosing placeholder - the table's, or
    // that of a translation another assembly gave it. The values are in the order of the string's placeholders.
    private object ChoiceOf(string key, IReadOnlyList<object> values)
    {
        var table = (ILanguageTable)this;
        var chooser = table.ChooserOf(key);
        for (var language = Languages.Current; chooser == null && !string.IsNullOrEmpty(language); language = ParentOf(language))
        {
            chooser = Languages.ChooserOf(GetType(), language, key);
        }

        if (chooser == null)
        {
            return null;
        }

        var names = table.PlaceholdersOf(key);
        for (var i = 0; i < names.Count; i++)
        {
            if (names[i] == chooser)
            {
                // The value itself chooses, not the way a format writes it.
                return values[i] is FormattedArgument formatted ? formatted.Value : values[i];
            }
        }

        return null;
    }

    internal void OnLanguageChanged()
    {
        _spoken = null;
        _propertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }

    // The language the table answers in: the application's, else a parent of it, else the base language.
    private string Answering(string language)
    {
        var languages = ((ILanguageTable)this).Languages;
        for (; !string.IsNullOrEmpty(language); language = ParentOf(language))
        {
            var known = languages.FirstOrDefault(l => string.Equals(l, language, StringComparison.OrdinalIgnoreCase));
            if (known != null)
            {
                return known;
            }
        }

        return languages[0];
    }

    private string Translated(string key, object choice)
    {
        for (var language = Languages.Current; !string.IsNullOrEmpty(language); language = ParentOf(language))
        {
            if (Languages.TryTranslate(GetType(), language, key, choice, out var translated))
            {
                return translated;
            }
        }

        return null;
    }

    private static string ParentOf(string language)
    {
        try
        {
            return CultureInfo.GetCultureInfo(language).Parent.Name;
        }
        catch (CultureNotFoundException)
        {
            return null;
        }
    }
}
