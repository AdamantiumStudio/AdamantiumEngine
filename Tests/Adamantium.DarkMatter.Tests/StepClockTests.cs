using NUnit.Framework;

namespace Adamantium.DarkMatter.Tests;

[TestFixture]
public class StepClockTests
{
    [Test]
    public void Frame_GivesTheStepsDueAndKeepsTheRest()
    {
        var clock = new StepClock(120, 8);

        Assert.That(clock.Advance(1.0 / 60), Is.EqualTo(2));
        Assert.That(clock.Advance(1.0 / 240), Is.EqualTo(0));
        Assert.That(clock.Blend, Is.EqualTo(0.5f).Within(1e-4f));
        Assert.That(clock.Advance(1.0 / 240), Is.EqualTo(1));
        Assert.That(clock.Blend, Is.EqualTo(0f).Within(1e-4f));
    }

    [TestCase(double.NaN)]
    [TestCase(double.PositiveInfinity)]
    [TestCase(-0.01)]
    public void BadTime_IsRefused(double elapsed)
    {
        var clock = new StepClock(120, 8);

        Assert.Throws<System.ArgumentOutOfRangeException>(() => clock.Advance(elapsed));
        Assert.That(clock.Advance(1.0 / 120), Is.EqualTo(1));
    }

    [Test]
    public void FallingBehind_GivesAtMostTheLimitAndDropsTheRest()
    {
        var clock = new StepClock(120, maxStepsPerAdvance: 4);

        Assert.That(clock.Advance(1.0), Is.EqualTo(4));
        Assert.That(clock.Blend, Is.EqualTo(0f));
        Assert.That(clock.Advance(1.0 / 120), Is.EqualTo(1));
    }

    [Test]
    public void Reset_ForgetsTheTimeNotYetStepped()
    {
        var clock = new StepClock(120, 8);
        clock.Advance(1.0 / 240);

        clock.Reset();

        Assert.That(clock.Blend, Is.Zero);
        Assert.That(clock.Advance(1.0 / 240), Is.EqualTo(0));
    }
}
