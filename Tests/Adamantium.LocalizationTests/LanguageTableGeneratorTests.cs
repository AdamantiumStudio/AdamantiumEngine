using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Adamantium.Localization;
using NUnit.Framework;

namespace Adamantium.LocalizationTests;

/// <summary>Language files become string tables: one class per table, built from the files of every language together,
/// following the application's language while it runs.</summary>
[TestFixture]
[SetUICulture("en-US")]
public class LanguageTableGeneratorTests
{
    // A value whose members are named as the attributes beside Select: Text and Count.
    public enum Aggregate
    {
        Count,
        Sum,
        Text,
    }

    private const string English = """
        <Language>
            <Phrase Key="Close">Close</Phrase>
            <Phrase Key="PageOf">Page {page} of {count}</Phrase>
            <Phrase Key="Ratio">Ratio {value:N1}</Phrase>
            <Phrase Key="Braces">{{not a placeholder}}</Phrase>
        </Language>
        """;

    private const string Russian = """
        <Language>
            <Phrase Key="Close">Закрыть</Phrase>
            <Phrase Key="PageOf">{count}: страница {page}</Phrase>
            <Phrase Key="Ratio">Доля {value:N1}</Phrase>
            <Phrase Key="Braces">{{не подстановка}}</Phrase>
        </Language>
        """;

    [TearDown]
    public void BackToBase() => Languages.Current = null;

    [Test]
    public void ATable_FollowsTheApplicationsLanguage_AndFallsBackToItsBase()
    {
        var table = Table(Generate(new() { ["Localization/Strings.en.alang"] = English, ["Localization/Strings.ru.alang"] = Russian }),
            "Test.App.Localization.Strings");

        Assert.Multiple(() =>
        {
            Assert.That(Read(table, "Close"), Is.EqualTo("Close"), "no language set: the base language");
            Languages.Current = "ru";
            Assert.That(Read(table, "Close"), Is.EqualTo("Закрыть"));
            Languages.Current = "ru-RU";
            Assert.That(Read(table, "Close"), Is.EqualTo("Закрыть"), "a regional language falls back to its parent");
            Languages.Current = "de";
            Assert.That(Read(table, "Close"), Is.EqualTo("Close"), "a language the table lacks falls back to the base");
        });
    }

    [Test]
    public void APlaceholder_BecomesAParameter_AndATranslationMayReorderThem()
    {
        var table = Table(Generate(new() { ["Strings.en.alang"] = English, ["Strings.ru.alang"] = Russian }), "Test.App.Strings");

        Assert.Multiple(() =>
        {
            Assert.That(Call(table, "PageOf", 2, 5), Is.EqualTo("Page 2 of 5"));
            Assert.That(Read(table, "Braces"), Is.EqualTo("{not a placeholder}"), "doubled braces are a brace");
            Languages.Current = "ru";
            Assert.That(Call(table, "PageOf", 2, 5), Is.EqualTo("5: страница 2"));
            Assert.That(Call(table, "Ratio", 1.5), Is.EqualTo("Доля 1,5"), "a format follows the application's language");
            Assert.That(Read(table, "Braces"), Is.EqualTo("{не подстановка}"));
        });
    }

    // A short phrase is a self-closing tag with its text in Text; each language writes its own format inside the text.
    [Test]
    public void APhrase_MayCloseItself_AndEachLanguageFormatsItsOwnWay()
    {
        var table = Table(Generate(new()
        {
            ["Saved.en.alang"] = """<Language><Phrase Key="At" Text="Saved at {time:h:mm tt}"/></Language>""",
            ["Saved.ru.alang"] = """<Language><Phrase Key="At" Text="Сохранено в {time:HH:mm}"/></Language>""",
        }), "Test.App.Saved");
        var evening = new DateTime(2026, 10, 3, 17, 45, 0);

        Assert.Multiple(() =>
        {
            Languages.Current = "en";
            Assert.That(Call(table, "At", evening), Is.EqualTo("Saved at 5:45 PM"));
            Languages.Current = "ru";
            Assert.That(Call(table, "At", evening), Is.EqualTo("Сохранено в 17:45"));
        });
    }

    [Test]
    public void ChangingTheLanguage_TellsTheTablesBindings()
    {
        var table = Table(Generate(new() { ["Strings.en.alang"] = English, ["Strings.ru.alang"] = Russian }), "Test.App.Strings");
        var changed = new List<string>();
        ((INotifyPropertyChanged)table).PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        Languages.Current = "ru";

        Assert.That(changed, Is.EqualTo(new[] { string.Empty }), "one notice, for every property at once");
    }

    [Test]
    public void AMissingTranslation_WarnsAndShowsTheBase()
    {
        var project = Generate(new()
        {
            ["Strings.en.alang"] = English,
            ["Strings.ru.alang"] = """<Language><Phrase Key="Close">Закрыть</Phrase></Language>""",
        });
        var table = Table(project, "Test.App.Strings");
        Languages.Current = "ru";

        Assert.Multiple(() =>
        {
            Assert.That(project.Warnings.Select(d => d.Id), Does.Contain("AUL007"));
            Assert.That(Call(table, "PageOf", 1, 2), Is.EqualTo("Page 1 of 2"));
        });
    }

    // Code a person reads: each language a class of named strings in a file of its own, the table choosing among them
    // in one place, and placeholders listed only for the strings that have them.
    [Test]
    public void ATable_IsAClassOfStringsPerLanguage()
    {
        var project = Generate(new()
        {
            ["Strings.en.alang"] = English,
            ["Strings.ru.alang"] = Russian,
            ["Plain.en.alang"] = """<Language><Phrase Key="Close">Close</Phrase><Phrase Key="Open">Open</Phrase><Phrase Key="Find">Find</Phrase><Phrase Key="Text">Text</Phrase></Language>""",
            ["Plain.ru.alang"] = """<Language><Phrase Key="Close">Закрыть</Phrase></Language>""",
        });
        Assert.That(project.Errors, Is.Empty, "Find and Text are keys like any other");
        var files = project.Compilation.SyntaxTrees.Select(t => System.IO.Path.GetFileName(t.FilePath)).ToList();
        var plain = project.Source[project.Source.IndexOf("public sealed partial class Plain", StringComparison.Ordinal)..];

        Assert.Multiple(() =>
        {
            Assert.That(files, Is.SupersetOf(new[] { "Test.App.Strings.g.cs", "Test.App.Strings.en.g.cs", "Test.App.Strings.ru.g.cs" }));
            Assert.That(project.Source, Does.Contain("private static class Ru"));
            Assert.That(project.Source, Does.Contain("public const string Close = \"Закрыть\";"));
            Assert.That(project.Source, Does.Contain("public static string Close => Current.Localized(nameof(Close));"));
            Assert.That(project.Source, Does.Contain("public static string PageOf(object page, object count) => Current.Localized(nameof(PageOf), page, count);"));
            Assert.That(project.Source, Does.Contain("\"ru\" => Ru.Close,"));
            Assert.That(project.Source, Does.Contain("nameof(PageOf) => [\"page\", \"count\"],"));
            Assert.That(plain, Does.Contain("nameof(Open) => En.Open,"), "a string not translated is the base one");
            Assert.That(plain, Does.Contain("ILanguageTable.PlaceholdersOf(string key) => [];"));
        });
    }

    // A translator gets the file with every key and fills them in; what is still empty is not translated yet.
    [Test]
    public void AnEmptyTranslation_IsNotTranslatedYet()
    {
        var project = Generate(new()
        {
            ["Strings.en.alang"] = English,
            ["Strings.ru.alang"] = """
                <Language>
                    <Phrase Key="Close"></Phrase>
                    <Phrase Key="PageOf">
                    </Phrase>
                    <Phrase Key="Ratio">Доля {value:N1}</Phrase>
                    <Phrase Key="Braces">{{не подстановка}}</Phrase>
                </Language>
                """,
        });
        var table = Table(project, "Test.App.Strings");
        Languages.Current = "ru";

        Assert.Multiple(() =>
        {
            Assert.That(project.Warnings.Select(d => d.GetMessage()), Has.Some.Contains("lacks 2 of Strings's strings: Close, PageOf"));
            Assert.That(Read(table, "Close"), Is.EqualTo("Close"));
            Assert.That(Call(table, "PageOf", 1, 2), Is.EqualTo("Page 1 of 2"));
        });
    }

    [TestCase("""<Language><Phrase Key="Open">Открыть</Phrase></Language>""", "AUL005", TestName = "A key the base lacks")]
    [TestCase("""<Language><Phrase Key="PageOf">Страница {page}</Phrase></Language>""", "AUL006", TestName = "A placeholder the translation lacks")]
    [TestCase("""<Language><Phrase Key="Close">Закрыть {now}</Phrase></Language>""", "AUL006", TestName = "A placeholder the base lacks")]
    [TestCase("""<Language><Phrase Key="Close">Закрыть</Phrase><Phrase Key="Close">Ещё раз</Phrase></Language>""", "AUL004", TestName = "A key twice")]
    [TestCase("""<Language><Text Key="Close">Закрыть</Text></Language>""", "AUL004", TestName = "An element that is not a string")]
    [TestCase("""<Language><Phrase Key="Close" Text="Закрыть">Закрыть</Phrase></Language>""", "AUL004", TestName = "A text given twice")]
    [TestCase("""<Language><Phrase Key="Close">Закрыть</Language>""", "AUL004", TestName = "Broken XML")]
    public void AWrongTranslation_IsABuildError(string russian, string id)
    {
        var project = Generate(new() { ["Strings.en.alang"] = English, ["Strings.ru.alang"] = russian });

        Assert.That(project.Errors.Select(d => d.Id), Does.Contain(id));
    }

    [TestCase("Strings.alang", "AUL001", TestName = "A file that names no language")]
    [TestCase("Strings.e n.alang", "AUL002", TestName = "A name that is no language")]
    [TestCase("event.en.alang", "AUL003", TestName = "A table named as a keyword")]
    public void AFileName_StatesTableAndLanguage(string name, string id)
    {
        var project = Generate(new() { [name] = English });

        Assert.That(project.Errors.Select(d => d.Id), Does.Contain(id));
    }

    [TestCase("Current")]
    [TestCase("TryText")]
    public void AKeyTheTableClassAlreadyHas_IsABuildError(string key)
    {
        var project = Generate(new() { ["Strings.en.alang"] = $"""<Language><Phrase Key="{key}">Now</Phrase></Language>""" });

        Assert.That(project.Errors.Select(d => d.Id), Does.Contain("AUL003"));
    }

    [Test]
    public void AnApplication_SetsHowALanguageWritesTime()
    {
        var project = Generate(new()
        {
            ["Strings.en.alang"] = """
                <Language>
                    <Language.Format ShortTime="HH:mm" DecimalSeparator="." FirstDayOfWeek="Monday"/>
                    <Phrase Key="Close">Close</Phrase>
                </Language>
                """,
        }, new() { ["OutputType"] = "WinExe" });
        RuntimeHelpers.RunModuleConstructor(project.Load().ManifestModule.ModuleHandle);

        var culture = Languages.CultureOf("en");
        Assert.Multiple(() =>
        {
            Assert.That(culture.DateTimeFormat.ShortTimePattern, Is.EqualTo("HH:mm"));
            Assert.That(culture.NumberFormat.NumberDecimalSeparator, Is.EqualTo("."));
            Assert.That(culture.DateTimeFormat.FirstDayOfWeek, Is.EqualTo(System.DayOfWeek.Monday));
        });
    }

    [Test]
    public void ALibrary_CannotSetALanguagesFormats()
    {
        var project = Generate(new()
        {
            ["Strings.en.alang"] = """<Language><Language.Format ShortTime="HH:mm"/><Phrase Key="Close">Close</Phrase></Language>""",
        }, new() { ["OutputType"] = "Library" });

        Assert.That(project.Errors.Select(d => d.Id), Does.Contain("AUL009"));
    }

    [Test]
    public void AnApplication_TranslatesALibrarysTable_WithAFileOfItsOwn()
    {
        var library = LanguageTableHarness.Project(new Dictionary<string, string>
        {
            ["Text/LibraryStrings.en.alang"] = """<Language><Phrase Key="Close">Close</Phrase><Phrase Key="Open">Open</Phrase><Phrase Key="PageOf">Page {page} of {count}</Phrase></Language>""",
            ["Text/LibraryStrings.ru.alang"] = """<Language><Phrase Key="Close">Закрыть</Phrase><Phrase Key="Open">Открыть</Phrase><Phrase Key="PageOf">Страница {page} из {count}</Phrase></Language>""",
        }, new Dictionary<string, string> { ["RootNamespace"] = "Test.Library" });
        Assert.That(library.Errors, Is.Empty, LanguageTableHarness.Errors(library.Errors));

        var application = LanguageTableHarness.Project(new Dictionary<string, string>
        {
            ["LibraryStrings.de.alang"] = """<Language><Phrase Key="Close">Schließen</Phrase><Phrase Key="PageOf">{count}: Seite {page}</Phrase></Language>""",
            ["LibraryStrings.ru.alang"] = """<Language><Phrase Key="Close">Закрыть всё</Phrase></Language>""",
        }, new Dictionary<string, string> { ["OutputType"] = "WinExe" }, [library.Reference]);
        Assert.That(application.Errors, Is.Empty, LanguageTableHarness.Errors(application.Errors));

        var table = Table(library, "Test.Library.Text.LibraryStrings");
        RuntimeHelpers.RunModuleConstructor(application.Load().ManifestModule.ModuleHandle);

        Assert.Multiple(() =>
        {
            Assert.That(application.Warnings.Select(d => d.Id), Does.Contain("AUL007"), "the new German lacks Open");
            Assert.That(application.Warnings.Count(d => d.Id == "AUL007"), Is.EqualTo(1), "a partial Russian is an override, not a gap");
            Languages.Current = "de";
            Assert.That(Read(table, "Close"), Is.EqualTo("Schließen"));
            Assert.That(Call(table, "PageOf", 2, 5), Is.EqualTo("5: Seite 2"));
            Assert.That(Read(table, "Open"), Is.EqualTo("Open"), "untranslated: the library's base language");
            Languages.Current = "ru";
            Assert.That(Read(table, "Close"), Is.EqualTo("Закрыть всё"), "the application's word wins");
            Assert.That(Read(table, "Open"), Is.EqualTo("Открыть"), "the rest stays the library's");
        });
    }

    private const string EnglishFiles = """
        <Language>
            <Phrase Key="Files" Count="count" One="{count} file" Other="{count} files"/>
            <Phrase Key="Rows" Text="Rows: {count}"/>
        </Language>
        """;

    private const string RussianFiles = """
        <Language>
            <Phrase Key="Files" Count="count" One="{count} файл" Few="{count} файла" Many="{count} файлов" Other="{count} файла"/>
            <Phrase Key="Rows" Count="count" One="{count} строка" Few="{count} строки" Many="{count} строк" Other="{count} строки"/>
        </Language>
        """;

    // A phrase that changes with a number has a text per form, and each language's own rules pick the form - a language
    // may count a phrase its base language writes once.
    [Test]
    public void APhraseThatChangesWithANumber_TakesTheFormItsLanguagePicks()
    {
        var project = Generate(new() { ["Counts.en.alang"] = EnglishFiles, ["Counts.ru.alang"] = RussianFiles });
        var table = Table(project, "Test.App.Counts");

        Assert.Multiple(() =>
        {
            Assert.That(project.Warnings, Is.Empty, LanguageTableHarness.Errors(project.Warnings));
            Languages.Current = "en";
            Assert.That(Call(table, "Files", 1), Is.EqualTo("1 file"));
            Assert.That(Call(table, "Files", 3), Is.EqualTo("3 files"));
            Assert.That(Call(table, "Files", 1.0m), Is.EqualTo("1.0 files"), "a one with decimals shown is not One");
            Assert.That(Call(table, "Rows", 1), Is.EqualTo("Rows: 1"));
            Languages.Current = "ru";
            Assert.That(new[] { 1, 3, 5, 11, 21, 22 }.Select(n => Call(table, "Files", n)),
                Is.EqualTo(new[] { "1 файл", "3 файла", "5 файлов", "11 файлов", "21 файл", "22 файла" }));
            Assert.That(Call(table, "Files", 1.5), Is.EqualTo("1,5 файла"));
            Assert.That(Call(table, "Rows", 5), Is.EqualTo("5 строк"), "counted here, though the base says it once");
            Assert.That(project.Source, Does.Contain("public static class Files"));
            Assert.That(project.Source, Does.Contain("global::Adamantium.Localization.PluralRules.FormOf(\"ru\", choice) switch"));
        });
    }

    [TestCase("""<Phrase Key="Files" Count="count" One="{count} file" Few="{count} files" Other="{count} files"/>""", "AUL010",
        TestName = "A form the language has not")]
    [TestCase("""<Phrase Key="Files" Count="count" One="{count} file"/>""", "AUL010", TestName = "No Other form")]
    [TestCase("""<Phrase Key="Files" One="{count} file" Other="{count} files"/>""", "AUL010", TestName = "Forms without Count")]
    [TestCase("""<Phrase Key="Files" Count="count" Text="{count} files" Other="{count} files"/>""", "AUL004",
        TestName = "A text and forms")]
    [TestCase("""<Phrase Key="Other" Count="count" One="{count} file" Other="{count} files"/>""", "AUL003",
        TestName = "A counted phrase named as a form")]
    public void AWrongCountedPhrase_IsABuildError(string phrase, string id)
    {
        var project = Generate(new() { ["Counts.en.alang"] = $"<Language>{phrase}</Language>" });

        Assert.That(project.Errors.Select(d => d.Id), Does.Contain(id));
    }

    [Test]
    public void ACountedPhraseLackingAFormOfItsLanguage_WarnsAndSaysOther()
    {
        var project = Generate(new()
        {
            ["Counts.en.alang"] = EnglishFiles,
            ["Counts.ru.alang"] = """<Language><Phrase Key="Files" Count="count" One="{count} файл" Other="{count} файла"/></Language>""",
        });
        var table = Table(project, "Test.App.Counts");
        Languages.Current = "ru";

        Assert.Multiple(() =>
        {
            Assert.That(project.Warnings.Select(d => d.GetMessage()), Has.Some.Contains("lacks the Few, Many forms of Russian"));
            Assert.That(Call(table, "Files", 5), Is.EqualTo("5 файла"));
        });
    }

    private const string EnglishStates = """
        <Language>
            <Phrase Key="Actions" Select="enabled" True="Actions enabled" False="Actions disabled"/>
            <Phrase Key="Terms" Select="state" True="accepted" False="declined" Other="undecided"/>
            <Phrase Key="Day" Select="day" Saturday="Weekend: {day}" Sunday="Weekend: {day}" Other="Weekday: {day}"/>
            <Phrase Key="City" Select="city" None="No city chosen" Other="City: {city}"/>
        </Language>
        """;

    private const string RussianStates = """
        <Language>
            <Phrase Key="Actions" Select="enabled" True="Действия включены" False="Действия выключены"/>
            <Phrase Key="Terms" Select="state" True="приняты" False="отклонены" Other="не решено"/>
        </Language>
        """;

    // A phrase whose words depend on a value: a case named after each value - True, False, a member of an enum - and
    // Other for the rest, null included.
    [Test]
    public void APhraseChosenByAValue_TakesTheCaseNamedAfterIt()
    {
        var project = Generate(new() { ["States.en.alang"] = EnglishStates, ["States.ru.alang"] = RussianStates });
        var table = Table(project, "Test.App.States");

        Assert.Multiple(() =>
        {
            Languages.Current = "en";
            Assert.That(Call(table, "Actions", true), Is.EqualTo("Actions enabled"));
            Assert.That(Call(table, "Actions", false), Is.EqualTo("Actions disabled"));
            Assert.That(Call(table, "Terms", new object[] { null }), Is.EqualTo("undecided"));
            Assert.That(Call(table, "Day", DayOfWeek.Sunday), Is.EqualTo("Weekend: Sunday"));
            Assert.That(Call(table, "Day", DayOfWeek.Monday), Is.EqualTo("Weekday: Monday"));
            Assert.That(Call(table, "City", new object[] { null }), Is.EqualTo("No city chosen"));
            Assert.That(Call(table, "City", "Paris"), Is.EqualTo("City: Paris"));
            Languages.Current = "ru";
            Assert.That(Call(table, "Actions", false), Is.EqualTo("Действия выключены"));
            Assert.That(Call(table, "Terms", true), Is.EqualTo("приняты"));
            Assert.That(project.Source, Does.Contain("global::Adamantium.Localization.PhraseCases.Of(choice) switch"));
        });
    }

    private const string EnglishShown = """
        <Language><Phrase Key="Shown" Select="as" Text="as text" Count="as a count" Other="as is"/></Language>
        """;

    // An enum may have a member named Text or Count, and beside Select neither attribute means anything else.
    [Test]
    public void AValueNamedTextOrCount_HasACaseToo()
    {
        var project = Generate(new() { ["Totals.en.alang"] = EnglishShown });
        Assert.That(project.Errors, Is.Empty, LanguageTableHarness.Errors(project.Errors));
        var table = Table(project, "Test.App.Totals");

        Assert.Multiple(() =>
        {
            Assert.That(Call(table, "Shown", Aggregate.Count), Is.EqualTo("as a count"));
            Assert.That(Call(table, "Shown", "Text"), Is.EqualTo("as text"));
            Assert.That(Call(table, "Shown", Aggregate.Sum), Is.EqualTo("as is"));
        });
    }

    [Test]
    public void AnApplication_ChoosesALibrarysCaseNamedCount_InItsOwnLanguage()
    {
        var library = LanguageTableHarness.Project(new Dictionary<string, string>
        {
            ["Totals.en.alang"] = EnglishShown,
        }, new Dictionary<string, string> { ["RootNamespace"] = "Test.Showing" });
        Assert.That(library.Errors, Is.Empty, LanguageTableHarness.Errors(library.Errors));

        var application = LanguageTableHarness.Project(new Dictionary<string, string>
        {
            ["Totals.pl.alang"] = """
                <Language><Phrase Key="Shown" Select="as" Text="jako tekst" Count="jako liczba" Other="tak jak jest"/></Language>
                """,
        }, new Dictionary<string, string> { ["OutputType"] = "WinExe" }, [library.Reference]);
        Assert.That(application.Errors, Is.Empty, LanguageTableHarness.Errors(application.Errors));

        var table = Table(library, "Test.Showing.Totals");
        RuntimeHelpers.RunModuleConstructor(application.Load().ManifestModule.ModuleHandle);
        Languages.Current = "pl";

        Assert.Multiple(() =>
        {
            Assert.That(Call(table, "Shown", Aggregate.Count), Is.EqualTo("jako liczba"));
            Assert.That(Call(table, "Shown", "Text"), Is.EqualTo("jako tekst"));
            Assert.That(Call(table, "Shown", Aggregate.Sum), Is.EqualTo("tak jak jest"));
        });
    }

    [TestCase("""<Phrase Key="Actions" Select="on"/>""", "AUL010", TestName = "A value that chooses nothing")]
    [TestCase("""<Phrase Key="Actions" Select="on" Actions="on" Other="off"/>""", "AUL010", TestName = "A case named as its phrase")]
    [TestCase("""<Phrase Key="Door" Select="state" class="open" Other="shut"/>""", "AUL010", TestName = "A case named as a keyword")]
    [TestCase("""<Phrase Key="Actions" True="on" False="off"/>""", "AUL004", TestName = "Cases without a choosing value")]
    public void AWrongChosenPhrase_IsABuildError(string phrase, string id)
    {
        var project = Generate(new() { ["States.en.alang"] = $"<Language>{phrase}</Language>" });

        Assert.That(project.Errors.Select(d => d.Id), Does.Contain(id));
    }

    [Test]
    public void ATranslationLackingACaseOfTheBase_Warns()
    {
        var project = Generate(new()
        {
            ["States.en.alang"] = EnglishStates,
            ["States.ru.alang"] = """<Language><Phrase Key="Terms" Select="state" True="приняты" Other="не решено"/></Language>""",
        });

        Assert.That(project.Warnings.Select(d => d.GetMessage()), Has.Some.Contains("Terms lacks the case False the base text gives"));
    }

    [Test]
    public void AnApplication_CountsALibrarysPhrase_InItsOwnLanguage()
    {
        var library = LanguageTableHarness.Project(new Dictionary<string, string>
        {
            ["Counts.en.alang"] = EnglishFiles,
        }, new Dictionary<string, string> { ["RootNamespace"] = "Test.Counting" });
        Assert.That(library.Errors, Is.Empty, LanguageTableHarness.Errors(library.Errors));

        var application = LanguageTableHarness.Project(new Dictionary<string, string>
        {
            ["Counts.pl.alang"] = """
                <Language>
                    <Phrase Key="Files" Count="count" One="{count} plik" Few="{count} pliki" Many="{count} plików" Other="{count} pliku"/>
                    <Phrase Key="Rows" Count="count" One="{count} wiersz" Few="{count} wiersze" Many="{count} wierszy" Other="{count} wiersza"/>
                </Language>
                """,
        }, new Dictionary<string, string> { ["OutputType"] = "WinExe" }, [library.Reference]);
        Assert.That(application.Errors, Is.Empty, LanguageTableHarness.Errors(application.Errors));

        var table = Table(library, "Test.Counting.Counts");
        RuntimeHelpers.RunModuleConstructor(application.Load().ManifestModule.ModuleHandle);
        Languages.Current = "pl";

        Assert.Multiple(() =>
        {
            Assert.That(new[] { 1, 2, 5, 22 }.Select(n => Call(table, "Files", n)),
                Is.EqualTo(new[] { "1 plik", "2 pliki", "5 plików", "22 pliki" }));
            Assert.That(Call(table, "Rows", 5), Is.EqualTo("5 wierszy"), "counted by the translation alone");
        });
    }

    [Test]
    public void ATranslationOfATableNobodyHas_IsABuildError()
    {
        var project = Generate(new() { ["Missing.de.alang"] = """<Language><Phrase Key="Close">Schließen</Phrase></Language>""" });

        Assert.That(project.Errors.Select(d => d.Id), Does.Contain("AUL008"));
    }

    private static GeneratedProject Generate(Dictionary<string, string> files, Dictionary<string, string> properties = null) =>
        LanguageTableHarness.Project(files, properties);

    private static object Table(GeneratedProject project, string fullName)
    {
        Assert.That(project.Errors, Is.Empty, LanguageTableHarness.Errors(project.Errors));
        var type = project.Load().GetType(fullName, throwOnError: true);
        return type.GetProperty("Current", BindingFlags.Public | BindingFlags.Static).GetValue(null);
    }

    private static string Read(object table, string key) => (string)table.GetType().GetProperty(key).GetValue(table);

    private static string Call(object table, string key, params object[] arguments) =>
        (string)table.GetType().GetMethod(key).Invoke(table, arguments);
}
