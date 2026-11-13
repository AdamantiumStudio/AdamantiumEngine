using System;
using System.Globalization;
using System.Threading;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.MathTests;

/// <summary>Vectors and quaternions read from text as markup and text files write them: numbers in the invariant
/// culture, separated by spaces, commas or both.</summary>
[TestFixture]
public class TextParseTests
{
    [TestCase("1 2 3")]
    [TestCase("1,2,3")]
    [TestCase("1, 2, 3")]
    [TestCase("  1 ,2  3 ")]
    public void AVector3_IsReadWhateverSeparatesItsNumbers(string text)
    {
        Assert.That(Vector3<double>.Parse(text), Is.EqualTo(new Vector3<double>(1, 2, 3)));
    }

    [Test]
    public void TheNumbers_AreReadInTheInvariantCulture_WhateverTheCurrentOne()
    {
        var culture = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU");
        try
        {
            Assert.Multiple(() =>
            {
                Assert.That(Vector3<float>.Parse("0.5, -1.25, 2"), Is.EqualTo(new Vector3<float>(0.5f, -1.25f, 2)));
                Assert.That(Vector4<float>.Parse("0.5 1 1.5 2"), Is.EqualTo(new Vector4<float>(0.5f, 1, 1.5f, 2)));
                Assert.That(Quaternion<float>.Parse("0, 0.7071, 0, 0.7071"),
                    Is.EqualTo(new Quaternion<float>(0, 0.7071f, 0, 0.7071f)));
                Assert.That(Vector2<double>.Parse("0.25,4"), Is.EqualTo(new Vector2<double>(0.25, 4)));
            });
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = culture;
        }
    }

    [Test]
    public void TooFewOrTooManyNumbers_AreRefused()
    {
        Assert.Multiple(() =>
        {
            Assert.Throws<FormatException>(() => Vector3<double>.Parse("1 2"));
            Assert.Throws<FormatException>(() => Vector4<float>.Parse("1 2 3 4 5"));
            Assert.Throws<FormatException>(() => Quaternion<float>.Parse(""));
            Assert.Throws<FormatException>(() => Vector2<float>.Parse("1"));
        });
    }

    [Test]
    public void SomethingNotANumber_IsRefused()
    {
        Assert.Throws<FormatException>(() => Vector3<float>.Parse("1 two 3"));
    }
}
