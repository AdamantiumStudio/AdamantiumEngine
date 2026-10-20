using System;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.MathTests;

/// <summary>A color as markup writes it: a name, or a "#" and three, four, six or eight hex digits, the alpha first.</summary>
[TestFixture]
public class ColorsParseTests
{
    [TestCase("Tomato", 255, 99, 71, 255)]
    [TestCase("tomato", 255, 99, 71, 255)]
    [TestCase(" #F80 ", 255, 136, 0, 255)]
    [TestCase("#8F80", 255, 136, 0, 136)]
    [TestCase("#FF8000", 255, 128, 0, 255)]
    [TestCase("#ff8000", 255, 128, 0, 255)]
    [TestCase("#80FF8000", 255, 128, 0, 128)]
    [TestCase("#00000000", 0, 0, 0, 0)]
    public void AMarkupColor_IsRead(string text, int r, int g, int b, int a)
    {
        Assert.That(Colors.TryParse(text, out var color), Is.True);
        Assert.That((color.R, color.G, color.B, color.A), Is.EqualTo(((byte)r, (byte)g, (byte)b, (byte)a)));
        Assert.That(Colors.Get(text), Is.EqualTo(color));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("#")]
    [TestCase("#12")]
    [TestCase("#12345")]
    [TestCase("#1234567")]
    [TestCase("#123456789")]
    [TestCase("#GG0000")]
    [TestCase("# F00")]
    [TestCase("notacolor")]
    public void WhatIsNoMarkupColor_IsNotRead_AndGetSaysSo(string text)
    {
        Assert.That(Colors.TryParse(text, out _), Is.False);
        Assert.That(() => Colors.Get(text), Throws.TypeOf<FormatException>());
    }
}
