using System.Linq;
using Adamantium.Localization;
using NUnit.Framework;

namespace Adamantium.LocalizationTests;

/// <summary>The form a number takes is the language's own rule, as Unicode CLDR states it.</summary>
[TestFixture]
public class PluralRulesTests
{
    [TestCase("en", new object[] { 0, 1, 2, 1.5, 21 }, new[] { "Other", "One", "Other", "Other", "Other" })]
    [TestCase("ru", new object[] { 1, 2, 4, 5, 11, 12, 14, 21, 22, 25, 101, 111, 1.5 },
        new[] { "One", "Few", "Few", "Many", "Many", "Many", "Many", "One", "Few", "Many", "One", "Many", "Other" })]
    [TestCase("pl", new object[] { 1, 2, 5, 12, 21, 22, 1.5 }, new[] { "One", "Few", "Many", "Many", "Many", "Few", "Other" })]
    [TestCase("cs", new object[] { 1, 2, 4, 5, 1.5 }, new[] { "One", "Few", "Few", "Other", "Many" })]
    [TestCase("fr", new object[] { 0, 1, 1.5, 2 }, new[] { "One", "One", "One", "Other" })]
    [TestCase("ja", new object[] { 1, 2 }, new[] { "Other", "Other" })]
    [TestCase("ar", new object[] { 0, 1, 2, 3, 11, 100 }, new[] { "Zero", "One", "Two", "Few", "Many", "Other" })]
    [TestCase("ru-RU", new object[] { 1, 3, 5 }, new[] { "One", "Few", "Many" })]
    public void ANumber_TakesTheFormOfItsLanguage(string language, object[] numbers, string[] forms) =>
        Assert.That(numbers.Select(n => PluralRules.FormOf(language, n).ToString()), Is.EqualTo(forms));

    [Test]
    public void VisibleDecimals_ChangeTheForm()
    {
        Assert.Multiple(() =>
        {
            Assert.That(PluralRules.FormOf("en", 1m), Is.EqualTo(PluralForm.One));
            Assert.That(PluralRules.FormOf("en", 1.0m), Is.EqualTo(PluralForm.Other), "\"1.0 files\"");
            Assert.That(PluralRules.FormOf("ru", "five"), Is.EqualTo(PluralForm.Other), "not a number");
        });
    }

    // A number written in markup arrives as its text, and CLDR reads the operands from exactly that.
    [Test]
    public void ANumberWrittenOut_TakesItsFormToo()
    {
        Assert.Multiple(() =>
        {
            Assert.That(PluralRules.FormOf("ru", "5"), Is.EqualTo(PluralForm.Many));
            Assert.That(PluralRules.FormOf("ru", "22"), Is.EqualTo(PluralForm.Few));
            Assert.That(PluralRules.FormOf("en", "1"), Is.EqualTo(PluralForm.One));
            Assert.That(PluralRules.FormOf("en", "1.0"), Is.EqualTo(PluralForm.Other), "\"1.0 files\"");
        });
    }

    [Test]
    public void ALanguage_SaysWhichFormsItHas()
    {
        Assert.Multiple(() =>
        {
            Assert.That(PluralRules.FormsOf("en"), Is.EqualTo(new[] { PluralForm.One, PluralForm.Other }));
            Assert.That(PluralRules.FormsOf("ru"), Is.EqualTo(new[] { PluralForm.One, PluralForm.Few, PluralForm.Many, PluralForm.Other }));
            Assert.That(PluralRules.FormsOf("ja"), Is.EqualTo(new[] { PluralForm.Other }));
            Assert.That(PluralRules.Knows("xx"), Is.False);
            Assert.That(PluralRules.FormsOf("xx"), Is.EqualTo(new[] { PluralForm.One, PluralForm.Other }), "taken as English is");
        });
    }
}
