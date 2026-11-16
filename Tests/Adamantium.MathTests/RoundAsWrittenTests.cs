using System;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.MathTests;

// A value is rounded as its shortest decimal form reads, the way a reader sees it: a half in the decimals goes as the
// rounding says, though the double's binary value is a little under or over the half.
[TestFixture]
public class RoundAsWrittenTests
{
    [TestCase(2.675, 2, MidpointRounding.AwayFromZero, 2.68)]
    [TestCase(-2.675, 2, MidpointRounding.AwayFromZero, -2.68)]
    [TestCase(1234567.8915, 3, MidpointRounding.AwayFromZero, 1234567.892)]
    [TestCase(2.665, 2, MidpointRounding.ToEven, 2.66)]
    [TestCase(2.675, 2, MidpointRounding.ToEven, 2.68)]
    [TestCase(0.125, 2, MidpointRounding.ToEven, 0.12)]
    [TestCase(1e-30, 4, MidpointRounding.AwayFromZero, 0)]
    [TestCase(5.5, 0, MidpointRounding.AwayFromZero, 6)]
    public void AHalfInTheDecimals_GoesAsTheRoundingSays(double value, int decimals, MidpointRounding mode, double rounded)
    {
        Assert.That(MathHelper.RoundAsWritten(value, decimals, mode), Is.EqualTo(rounded));
    }

    [Test]
    public void OneTooBigForADecimal_OrNotFinite_IsRoundedByItsBinaryValue()
    {
        Assert.Multiple(() =>
        {
            Assert.That(MathHelper.RoundAsWritten(3.5e30, 2), Is.EqualTo(3.5e30));
            Assert.That(MathHelper.RoundAsWritten(double.NaN, 2), Is.NaN);
            Assert.That(MathHelper.RoundAsWritten(double.NegativeInfinity, 2), Is.EqualTo(double.NegativeInfinity));
        });
    }
}
