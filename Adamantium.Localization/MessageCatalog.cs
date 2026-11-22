using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Adamantium.Localization;

/// <summary>The messages of a tool - the build, the language server - from the language files
/// <c>Localization/{name}.{language}.alang</c> embedded in an assembly, in the language of
/// <see cref="CultureInfo.CurrentUICulture"/>; a message that language lacks is said in English. A message's
/// {placeholders} take the values given by name; {{ and }} are braces.</summary>
public sealed class MessageCatalog
{
    private const string Fallback = "en";

    private readonly Assembly _assembly;
    private readonly string _name;
    private readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, string>> _languages = new();

    public MessageCatalog(Assembly assembly, string name)
    {
        _assembly = assembly;
        _name = name;
    }

    /// <summary>The message <paramref name="key"/> with its placeholders filled; the key itself when no language has
    /// it.</summary>
    public string Say(string key, params PhraseArgument[] values)
    {
        var culture = CultureInfo.CurrentUICulture;
        string[] languages = [culture.Name, culture.TwoLetterISOLanguageName, Fallback];
        foreach (var language in languages)
        {
            if (Phrases(language).TryGetValue(key, out var text) && text.Length > 0)
            {
                return Fill(text, values);
            }
        }

        return key;
    }

    /// <summary>The messages of <paramref name="language"/> by key; empty for a language the catalog has no file
    /// of.</summary>
    public IReadOnlyDictionary<string, string> Phrases(string language) => _languages.GetOrAdd(language, Load);

    private IReadOnlyDictionary<string, string> Load(string language)
    {
        using var stream = _assembly.GetManifestResourceStream($"Localization/{_name}.{language}.alang");
        if (stream == null)
        {
            return new Dictionary<string, string>();
        }

        try
        {
            return XDocument.Load(stream).Root.Elements("Phrase").ToDictionary(
                phrase => (string)phrase.Attribute("Key"),
                phrase => (string)phrase.Attribute("Text") ?? phrase.Value);
        }
        catch (XmlException)
        {
            return new Dictionary<string, string>();
        }
    }

    private static string Fill(string text, PhraseArgument[] values)
    {
        var result = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var character = text[i];
            if ((character == '{' || character == '}') && i + 1 < text.Length && text[i + 1] == character)
            {
                result.Append(character);
                i++;
                continue;
            }

            var end = character == '{' ? text.IndexOf('}', i + 1) : -1;
            if (end < 0)
            {
                result.Append(character);
                continue;
            }

            var name = text.Substring(i + 1, end - i - 1);
            var found = values.FirstOrDefault(value => value.Name == name);
            result.Append(found.Name != null ? Convert.ToString(found.Value, CultureInfo.CurrentCulture) : text.Substring(i, end - i + 1));
            i = end;
        }

        return result.ToString();
    }
}
