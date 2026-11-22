using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Adamantium.Localization;
using Adamantium.Localization.Generators;
using Adamantium.LocalizationTests.Localization;
using NUnit.Framework;

namespace Adamantium.LocalizationTests;

/// <summary>Localization with no UI: a project's own language files built into tables, followed as the application's
/// language changes; and the build's own messages in English and in Russian.</summary>
[TestFixture]
public class LanguagesTests
{
    [TearDown]
    public void BackToBase() => Languages.Current = null;

    [Test]
    public void AProjectsOwnTable_IsBuiltFromItsFiles_AndFollowsTheLanguage()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ProbeStrings.Close, Is.EqualTo("Close"));
            Assert.That(ProbeStrings.Files(1), Is.EqualTo("1 file"));
            Languages.Current = "ru";
            Assert.That(ProbeStrings.Close, Is.EqualTo("Закрыть"));
            Assert.That(ProbeStrings.Files(5), Is.EqualTo("5 файлов"));
            Assert.That(ProbeStrings.Files(22), Is.EqualTo("22 файла"));
        });
    }

    [Test]
    public void APhrase_IsSaidByKey_WithItsPlaceholdersByName()
    {
        Languages.Current = "ru";

        Assert.Multiple(() =>
        {
            Assert.That(Languages.Say(ProbeStrings.Current, nameof(ProbeStrings.Files), new PhraseArgument("count", 21)), Is.EqualTo("21 файл"));
            Assert.That(Languages.Say(ProbeStrings.Current, "Missing"), Is.EqualTo("Missing"), "a key the table lacks");
        });
    }

    [Test]
    public void Changed_IsRaised_OnceTheTablesHaveToldTheirBindings()
    {
        var order = new List<string>();
        PropertyChangedEventHandler told = (_, _) => order.Add("table");
        EventHandler changed = (_, _) => order.Add("changed");
        ((INotifyPropertyChanged)ProbeStrings.Current).PropertyChanged += told;
        Languages.Changed += changed;
        try
        {
            Languages.Current = "ru";
            Languages.Current = "ru";
        }
        finally
        {
            ((INotifyPropertyChanged)ProbeStrings.Current).PropertyChanged -= told;
            Languages.Changed -= changed;
        }

        Assert.That(order, Is.EqualTo(new[] { "table", "changed" }), "once, and not again for the same language");
    }

    [Test]
    public void EveryMessage_IsInBothLanguages_WithTheSamePlaceholders()
    {
        var english = LanguageMessages.Catalog.Phrases("en");
        var russian = LanguageMessages.Catalog.Phrases("ru");

        Assert.Multiple(() =>
        {
            Assert.That(english, Is.Not.Empty);
            Assert.That(russian.Keys, Is.EquivalentTo(english.Keys));
            foreach (var (key, text) in english)
            {
                if (russian.TryGetValue(key, out var translated))
                {
                    Assert.That(Placeholders(translated), Is.EquivalentTo(Placeholders(text)), key);
                }
            }
        });
    }

    [TestCase("en-US")]
    [TestCase("ru-RU")]
    public void EveryMethod_SaysAMessageOfItsCatalog(string language)
    {
        var culture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
        try
        {
            Assert.Multiple(() =>
            {
                foreach (var method in typeof(LanguageMessages).GetMethods(BindingFlags.Public | BindingFlags.Static)
                             .Where(m => m.ReturnType == typeof(string)))
                {
                    var said = (string)method.Invoke(null, method.GetParameters().Select(Sample).ToArray());
                    Assert.That(said, Does.Not.Match("^[A-Z][A-Za-z]+$"), method.Name);
                }
            });
        }
        finally
        {
            CultureInfo.CurrentUICulture = culture;
        }
    }

    [Test]
    public void AMessage_IsSaidInTheUICulturesLanguage_AndInEnglishForOneWithoutAFile()
    {
        var culture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ru-RU");
            var russian = LanguageMessages.PhraseKeyTwice("Close");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de-DE");
            var german = LanguageMessages.PhraseKeyTwice("Close");

            Assert.That(russian, Is.EqualTo("'Close' уже есть в этом файле"));
            Assert.That(german, Is.EqualTo("'Close' is already in this file"));
        }
        finally
        {
            CultureInfo.CurrentUICulture = culture;
        }
    }

    private static object Sample(ParameterInfo parameter) => parameter.ParameterType switch
    {
        var type when type == typeof(int) => 2,
        var type when type == typeof(IReadOnlyCollection<string>) || type == typeof(IReadOnlyList<string>) => new List<string> { "a", "b" },
        _ => "x"
    };

    private static IEnumerable<string> Placeholders(string text) =>
        Regex.Matches(text.Replace("{{", string.Empty).Replace("}}", string.Empty), @"\{(\w+)\}").Select(m => m.Groups[1].Value).Distinct();
}
