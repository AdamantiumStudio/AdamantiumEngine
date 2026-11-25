using NUnit.Framework;

namespace Adamantium.Pulsar.Tests;

[TestFixture]
public class AudioFormatTests
{
    [Test]
    public void YearsOfSound_AreCountedToTheFrame()
    {
        var format = new AudioFormat(48000, 2);
        var years = TimeSpan.FromDays(3650);

        var frames = format.FramesIn(years);

        Assert.Multiple(() =>
        {
            Assert.That(frames, Is.EqualTo(3650L * 24 * 3600 * 48000));
            Assert.That(format.DurationOf(frames), Is.EqualTo(years));
        });
    }
}
