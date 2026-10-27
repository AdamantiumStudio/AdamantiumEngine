using System;
using System.Linq;
using Adamantium.Core.Fonts.Shaping;
using NUnit.Framework;

namespace Adamantium.FontTests;

[TestFixture]
public class FontFeatureTests
{
    [Test]
    public void AListOfRegisteredFeatures_Parses()
    {
        var features = FontFeature.ParseList("liga=0, ss01, cv05=2, -kern, smcp[2:4]");
        string[] expected = ["liga=0", "ss01", "cv05=2", "kern=0", "smcp[2:4]"];

        Assert.That(features.Select(f => f.ToString()), Is.EqualTo(expected));
    }

    [Test]
    public void AMisspelledTag_IsAnError_WithTheTagItMeant()
    {
        Assert.That(FontFeature.TryParseList("lgia=0", out _, out var error), Is.False);
        Assert.That(error, Does.Contain("'lgia'").And.Contain("Did you mean 'liga'?"));
    }

    [Test]
    public void AStylisticSetPastTwenty_IsNotRegistered()
    {
        Assert.That(FontFeature.TryParseList("ss21", out _, out var error), Is.False);
        Assert.That(error, Does.Contain("'ss21'"));
    }

    [Test]
    public void APrivateTag_NeedsAnUppercaseLetter()
    {
        Assert.That(FontFeature.TryParseList("MYFT=3", out var features, out _), Is.True);
        Assert.That(features.Single().Tag, Is.EqualTo("MYFT"));
        Assert.That(FontFeature.TryParseList("myft", out _, out _), Is.False);
    }

    [Test]
    public void BrokenSyntax_IsAnError()
    {
        Assert.That(FontFeature.TryParseList("liga=on", out _, out var error), Is.False);
        Assert.That(error, Does.Contain("'liga=on'"));
        Assert.Throws<FormatException>(() => FontFeature.ParseList("liga,,kern"));
    }

    [Test]
    public void TypedFeatures_AreTheTagsTheyName()
    {
        Assert.That(FontFeature.Ligatures.Off.ToString(), Is.EqualTo("liga=0"));
        Assert.That(FontFeature.StylisticSet(3).ToString(), Is.EqualTo("ss03"));
        Assert.That(FontFeature.CharacterVariant(5, 2).ToString(), Is.EqualTo("cv05=2"));
        Assert.That(FontFeature.Numerals.Tabular.ToString(), Is.EqualTo("tnum"));
        Assert.Throws<ArgumentOutOfRangeException>(() => FontFeature.StylisticSet(21));
    }
}
