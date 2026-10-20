using System;
using System.Linq;
using Adamantium.Fonts;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.FontTests;

/// <summary>CSS values as SVG glyphs and HTML styles write them: a style's declarations, colors, and the axis values
/// of a variable font.</summary>
[TestFixture]
public class CssTests
{
    [Test]
    public void AStylesDeclarations_AreCutAtSemicolonsOutsideQuotes_TheirImportanceToldApart()
    {
        var declarations = Css.Declarations("color: red; font-family: 'A;B', serif !important;; nothing; fill:url(#a;b)");

        Assert.That(declarations.Select(d => (d.Name, d.Value, d.Important)), Is.EqualTo(new[]
        {
            ("color", "red", false),
            ("font-family", "'A;B', serif", true),
            ("fill", "url(#a;b)", false),
        }));
    }

    [TestCase("#f80", 255, 136, 0, 255)]
    [TestCase("#f808", 255, 136, 0, 136)]
    [TestCase("#FF8000", 255, 128, 0, 255)]
    [TestCase("#ff800080", 255, 128, 0, 128)]
    [TestCase("rgb(255, 128, 0)", 255, 128, 0, 255)]
    [TestCase("rgba(100%, 50%, 0%, 0.5)", 255, 128, 0, 128)]
    [TestCase("rgb(255 128 0 / 50%)", 255, 128, 0, 128)]
    [TestCase("Tomato", 255, 99, 71, 255)]
    [TestCase("darkgrey", 169, 169, 169, 255)]
    [TestCase("SlateGrey", 112, 128, 144, 255)]
    [TestCase("rebeccapurple", 102, 51, 153, 255)]
    [TestCase("Transparent", 0, 0, 0, 0)]
    public void ACssColor_IsRead(string text, int r, int g, int b, int a)
    {
        Assert.That(Css.TryParseColor(text, out var color), Is.True);
        Assert.That((color.R, color.G, color.B, color.A), Is.EqualTo(((byte)r, (byte)g, (byte)b, (byte)a)));
    }

    [TestCase("")]
    [TestCase("#12")]
    [TestCase("#zzzzzz")]
    [TestCase("rgb(1, 2)")]
    [TestCase("notacolor")]
    public void WhatIsNoCssColor_IsNotRead(string text)
    {
        Assert.That(Css.TryParseColor(text, out _), Is.False);
    }

    [Test]
    public void AxisValues_AreRead_AnAutomaticOpticalSizeSettingNone()
    {
        var variations = FontVariation.ParseList("wght=650, \"wdth\"=87.5, opsz=auto");

        Assert.That(variations, Is.EqualTo(new[] { new FontVariation("wght", 650), new FontVariation("wdth", 87.5f) }));
        Assert.That(FontVariation.ParseList(null), Is.Empty);
    }

    [TestCase("wght")]
    [TestCase("weight=700")]
    [TestCase("wght=heavy")]
    [TestCase("wght=Infinity")]
    public void WhatIsNoAxisValue_IsRefused(string text)
    {
        Assert.That(() => FontVariation.ParseList(text), Throws.TypeOf<FormatException>());
    }
}
