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
        var differing = Enumerable.Range(0, char.MaxValue + 1)
            .Select(code => (char)code)
            .Where(character => Differs(character.ToString()) || Differs("a" + character))
            .Select(character => $"U+{(int)character:X4}")
            .ToList();

        Assert.That(differing, Is.Empty);

        static bool Differs(string name) => CSharpIdentifiers.IsValid(name) != SyntaxFacts.IsValidIdentifier(name);
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
