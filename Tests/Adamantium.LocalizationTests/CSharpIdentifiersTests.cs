using System.Linq;
using Adamantium.Localization;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;

namespace Adamantium.LocalizationTests;

// The language files are read without the compiler; these hold its reading of names to the compiler's.
public class CSharpIdentifiersTests
{
    [Test]
    public void EveryCharacter_StartsAndContinuesANameAsTheCompilerSays()
    {
        for (var code = 0; code <= char.MaxValue; code++)
        {
            var character = (char)code;
            var alone = character.ToString();
            var after = "a" + character;
            Assert.That(CSharpIdentifiers.IsValid(alone), Is.EqualTo(SyntaxFacts.IsValidIdentifier(alone)), $"U+{code:X4} first");
            Assert.That(CSharpIdentifiers.IsValid(after), Is.EqualTo(SyntaxFacts.IsValidIdentifier(after)), $"U+{code:X4} after");
        }
    }

    [TestCase("")]
    [TestCase(null)]
    [TestCase("@class")]
    [TestCase("Close")]
    [TestCase("_count2")]
    [TestCase("2count")]
    [TestCase("first name")]
    [TestCase("имя")]
    public void AName_IsValidAsTheCompilerSays(string name) =>
        Assert.That(CSharpIdentifiers.IsValid(name), Is.EqualTo(SyntaxFacts.IsValidIdentifier(name)));

    [Test]
    public void TheKeywords_AreTheCompilersReservedOnes()
    {
        var reserved = SyntaxFacts.GetReservedKeywordKinds().Select(SyntaxFacts.GetText);
        Assert.That(CSharpIdentifiers.Keywords, Is.EquivalentTo(reserved));
    }

    [Test]
    public void AContextualKeyword_IsNoKeyword()
    {
        var contextual = SyntaxFacts.GetContextualKeywordKinds().Select(SyntaxFacts.GetText).ToList();
        Assert.That(contextual, Is.Not.Empty);
        Assert.That(contextual.Where(CSharpIdentifiers.IsKeyword), Is.Empty);
    }
}
