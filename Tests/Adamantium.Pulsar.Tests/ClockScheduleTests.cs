using Adamantium.Pulsar.Outputs;
using NUnit.Framework;

namespace Adamantium.Pulsar.Tests;

/// <summary>What the output without a device asks for, by times told to it.</summary>
[TestFixture]
public class ClockScheduleTests
{
    private ClockSchedule _schedule;

    [SetUp]
    public void CreateSchedule() =>
        _schedule = new ClockSchedule(new AudioFormat(48000, 2), TimeSpan.FromMilliseconds(40));

    [Test]
    public void AtTheStart_ItFillsTheBuffer()
    {
        var periods = _schedule.PeriodsAt(TimeSpan.Zero, out var ranDry);

        Assert.That(periods * _schedule.PeriodFrames, Is.EqualTo(_schedule.BufferFrames));
        Assert.That(ranDry, Is.False);
    }

    [Test]
    public void InTime_ItAsksForAPeriodEachPeriod_AsFastAsItPlays()
    {
        var elapsed = TimeSpan.Zero;
        long asked = 0;
        var dry = 0;
        while (elapsed < TimeSpan.FromSeconds(10))
        {
            asked += _schedule.PeriodsAt(elapsed, out var ranDry) * _schedule.PeriodFrames;
            dry += ranDry ? 1 : 0;
            elapsed += _schedule.WaitAt(elapsed);
        }

        Assert.That(dry, Is.Zero);
        Assert.That(asked, Is.EqualTo(48000 * 10).Within(_schedule.BufferFrames));
    }

    [Test]
    public void LateByMoreThanTheBuffer_ItRanDryOnce_AndFillsAnew()
    {
        _schedule.PeriodsAt(TimeSpan.Zero, out _);

        var periods = _schedule.PeriodsAt(TimeSpan.FromMilliseconds(300), out var ranDry);
        _schedule.PeriodsAt(TimeSpan.FromMilliseconds(320), out var again);

        Assert.Multiple(() =>
        {
            Assert.That(ranDry, Is.True);
            Assert.That(periods, Is.EqualTo(2), "what was missed is silence, not caught up");
            Assert.That(again, Is.False);
        });
    }

    [Test]
    public void LateByLessThanTheBuffer_ItDidNotRunDry()
    {
        _schedule.PeriodsAt(TimeSpan.Zero, out _);

        _schedule.PeriodsAt(TimeSpan.FromMilliseconds(39), out var ranDry);

        Assert.That(ranDry, Is.False);
    }

    [Test]
    public void AWait_IsNeverZero()
    {
        _schedule.PeriodsAt(TimeSpan.Zero, out _);

        Assert.That(_schedule.WaitAt(TimeSpan.FromMilliseconds(19.6)), Is.GreaterThanOrEqualTo(TimeSpan.FromMilliseconds(1)));
        Assert.That(_schedule.WaitAt(TimeSpan.FromSeconds(1)), Is.GreaterThanOrEqualTo(TimeSpan.FromMilliseconds(1)));
    }
}
