using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.MathTests;

/// <summary>What a sweep covers: within a turn as it is, past one what is left over whole turns, whole turns - and
/// rounding past them - a turn.</summary>
[TestFixture]
public class SweepTests
{
    [TestCase(-90, -90)]
    [TestCase(360, 360)]
    [TestCase(-360, -360)]
    [TestCase(485, 125)]
    [TestCase(-485, -125)]
    [TestCase(1000, 280)]
    [TestCase(720, 360)]
    [TestCase(-720, -360)]
    [TestCase(1080, 360)]
    [TestCase(3 * 120.00000000000001, 360)]
    [TestCase(-720.0000000001, -360)]
    [TestCase(360.000004, 360)]
    [TestCase(360.001, 0.001)]
    [TestCase(double.PositiveInfinity, 360)]
    [TestCase(double.NegativeInfinity, -360)]
    [TestCase(double.NaN, 0)]
    public void ASweep_CoversWhatIsLeftOverWholeTurns_AndWholeTurnsAreATurn(double degrees, double covered) =>
        Assert.That(MathHelper.Arc.ReduceSweep(degrees), Is.EqualTo(covered).Within(1e-7));

    // Well inside the setting is rounding, well past it an arc - not on its edge, where floating point decides.
    [TestCase(720)]
    [TestCase(-3600)]
    public void RoundingPastWholeTurns_IsWhatTheSettingSays(double wholeTurns)
    {
        var rounding = MathHelper.Arc.SweepRounding;
        var way = System.Math.Sign(wholeTurns);

        Assert.Multiple(() =>
        {
            Assert.That(MathHelper.Arc.ReduceSweep(wholeTurns + way * rounding / 2), Is.EqualTo(way * 360));
            Assert.That(MathHelper.Arc.ReduceSweep(wholeTurns + way * rounding * 2),
                Is.EqualTo(way * rounding * 2).Within(rounding / 100));
        });
    }
}
