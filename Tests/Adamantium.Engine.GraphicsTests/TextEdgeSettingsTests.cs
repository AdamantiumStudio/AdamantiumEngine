using System;
using Adamantium.Graphics.Core;
using NUnit.Framework;

namespace Adamantium.Engine.GraphicsTests;

// The edge curve's numbers go straight into the glyph shader, where a gamma of zero or a negative width blends NaN or
// infinity: they are refused when set, and the defaults are what text was tuned with.
[TestFixture]
public class TextEdgeSettingsTests
{
    [TestCase(0f)]
    [TestCase(-1f)]
    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    public void AGammaOrAWidthNotAboveZero_IsRefused(float value)
    {
        var edges = new TextEdgeSettings();

        Assert.Multiple(() =>
        {
            Assert.That(() => edges.LightInkGamma = value, Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(() => edges.SmallDarkInkGamma = value, Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(() => edges.LargeDarkInkGamma = value, Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(() => edges.EdgeWidth = value, Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(edges.LightInkGamma, Is.EqualTo(2.2f), "left as it was");
        });
    }

    [Test]
    public void ASizeBelowZero_IsRefused_AndZeroIsTaken()
    {
        var edges = new TextEdgeSettings();

        edges.LargeTextFrom = 0;

        Assert.Multiple(() =>
        {
            Assert.That(edges.LargeTextFrom, Is.Zero);
            Assert.That(() => edges.LargeTextAt = -1, Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(() => edges.LargeTextFrom = float.NaN, Throws.InstanceOf<ArgumentOutOfRangeException>());
        });
    }

    [Test]
    public void TheDefaults_AreTheTunedCurve()
    {
        var edges = GraphicsSettings.CreateDefault().TextEdges;

        Assert.That((edges.LightInkGamma, edges.SmallDarkInkGamma, edges.LargeDarkInkGamma, edges.LargeTextFrom,
            edges.LargeTextAt, edges.EdgeWidth), Is.EqualTo((2.2f, 1.9f, 1.6f, 3f, 7f, 0.5f)));
    }
}
